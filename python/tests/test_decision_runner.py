"""AGT-04 / AGT-06: decision runs go through the shared runner, then grade offline."""

from __future__ import annotations

import asyncio
import functools
import hashlib
import json
from collections.abc import Callable
from decimal import Decimal
from pathlib import Path
from typing import Any

import httpx2
import pytest
from pydantic import ValidationError
from typer.testing import CliRunner

from carimbo_evals import cli, runner
from carimbo_evals.cli import app
from carimbo_evals.decision_contract import DecisionRequest, DecisionResponse
from carimbo_evals.runner import (
    DECISION_SPEC,
    EXTRACTION_SPEC,
    DecisionCaseRef,
    RunConfigMismatchError,
    RunReport,
    build_decision_payload,
    discover_decision_cases,
    run_cases,
)

API_KEY = "test-eval-key-do-not-leak-0123456789"
TRACE_ID = "0" * 31 + "1"

EXPECTED_LABELS: dict[str, tuple[str, str, int]] = {
    "dc-001": ("approve", "none", 5),
    "dc-002": ("reject", "none", 2),
    "dc-003": ("escalate", "policy_warning", 3),
    "dc-004": ("reject", "none", 2),
}


def _make_decision_cases(directory: Path, count: int = 1) -> list[DecisionCaseRef]:
    """A tmp ``data/decision``-shaped directory: manifest, cases/<id>.json and expected.json."""
    (directory / "cases").mkdir(parents=True, exist_ok=True)
    ids = [f"dc-{index:03d}" for index in range(1, count + 1)]
    for case_id in ids:
        (directory / "cases" / f"{case_id}.json").write_text(
            json.dumps({"case_id": case_id, "synthetic": True}), encoding="utf-8"
        )
    manifest = {
        "dataset": "decision",
        "dataset_version": "decision-001",
        "labelling_version": "decision-labels-1",
        "cases": [{"case_id": case_id} for case_id in ids],
    }
    (directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    labels = [
        {
            "case_id": case_id,
            "decision": EXPECTED_LABELS.get(case_id, ("approve", "none", 5))[0],
            "cause": EXPECTED_LABELS.get(case_id, ("approve", "none", 5))[1],
            "rule": EXPECTED_LABELS.get(case_id, ("approve", "none", 5))[2],
        }
        for case_id in ids
    ]
    (directory / "expected.json").write_text(
        json.dumps(
            {"labelling_version": "decision-labels-1", "decision_record": "D-34", "cases": labels}
        ),
        encoding="utf-8",
    )
    return discover_decision_cases(directory)


def _tool_call(name: str, source: str = "model", outcome: str = "ok") -> dict[str, Any]:
    return {
        "id": None if source == "guard" else f"toolu_{name}",
        "name": name,
        "arguments": {},
        "source": source,
        "outcome": outcome,
        "error_kind": None,
        "error_message": None,
        "latency_ms": 3,
    }


def _usage() -> dict[str, int]:
    return {
        "input_tokens": 100,
        "output_tokens": 20,
        "cache_read_tokens": 0,
        "cache_write_5m_tokens": 0,
        "cache_write_1h_tokens": 0,
    }


def _step(
    index: int,
    stop_reason: str,
    tool_calls: list[dict[str, Any]],
    *,
    cost: str,
    incurred: str | None,
    cache_status: str = "miss",
) -> dict[str, Any]:
    return {
        "index": index,
        "usage": _usage(),
        "cost_usd": cost,
        "cost_warning": None,
        "incurred_cost_usd": incurred,
        "latency_ms": 40,
        "stop_reason": stop_reason,
        "model_returned": "claude-haiku-4-5",
        "provider_message_id": f"msg_{index}",
        "http_attempts": 1,
        "cache": {
            "mode": "read_write",
            "status": cache_status,
            "key": f"{index:064x}",
            "stored": True,
        },
        "failure": None,
        "tool_calls": tool_calls,
    }


def _decision(outcome: str, cause: str) -> dict[str, Any]:
    return {
        "evidence": [
            {"source": "get_invoice", "ref": "recipient.tax_id", "value": "12345678000195"}
        ],
        "reason": "Recipient is the company and totals are within the limit.",
        "outcome": outcome,
        "cause": cause,
    }


def decision_body(
    case_id: str,
    *,
    cost: str = "0.01000000",
    incurred: str | None = "0.01000000",
    outcome: str = "approve",
    cause: str = "none",
) -> dict[str, Any]:
    """A decision-1 body: two steps (tool_use with two model calls, then end_turn)."""
    half = "0.00500000"
    return {
        "contract_version": "decision-1",
        "case_id": case_id,
        "trace_id": TRACE_ID,
        "effective": {
            "model": "claude-haiku-4-5",
            "prompt_version": "agent-001",
            "max_steps": 6,
            "schema_sha256": "a" * 64,
            "tools_sha256": "b" * 64,
            "pricing_version": "pricing-001",
            "cache_mode": "read_write",
            "replicate_salt": None,
        },
        "outcome": {
            "status": "decided",
            "decision": _decision(outcome, cause),
            "model_decision": _decision(outcome, cause),
            "decided_by": "model",
            "guard": {
                "extraction_status": "success",
                "overridden": False,
                "tool_call": _tool_call("get_extraction_status", source="guard"),
            },
            "tool_failure": None,
            "failure": None,
            "raw_output": None,
        },
        "steps": [
            _step(
                0,
                "tool_use",
                [_tool_call("get_invoice"), _tool_call("get_policy")],
                cost=half,
                incurred=half if incurred is not None else None,
            ),
            _step(1, "end_turn", [], cost=half, incurred=half if incurred is not None else None),
        ],
        "usage": {
            "input_tokens": 200,
            "output_tokens": 40,
            "cache_read_tokens": 0,
            "cache_write_5m_tokens": 0,
            "cache_write_1h_tokens": 0,
        },
        "cost_usd": cost,
        "cost_warning": None,
        "incurred_cost_usd": incurred,
        "latency_ms": 90,
    }


def _transport(
    bodies: Callable[[str], dict[str, Any]] | None = None,
    *,
    status: Callable[[str], int] | None = None,
    seen: list[dict[str, Any]] | None = None,
) -> httpx2.MockTransport:
    async def handler(request: httpx2.Request) -> httpx2.Response:
        payload = json.loads(request.content)
        if seen is not None:
            seen.append({"path": request.url.path, "headers": request.headers, "body": payload})
        code = status(payload["case_id"]) if status is not None else 200
        if code != 200:
            return httpx2.Response(code, json={"error": "nope"})
        body = (
            bodies(payload["case_id"]) if bodies is not None else decision_body(payload["case_id"])
        )
        return httpx2.Response(200, json=body)

    return httpx2.MockTransport(handler)


async def _run(
    tmp_path: Path,
    cases: list[DecisionCaseRef],
    transport: httpx2.MockTransport,
    **kwargs: Any,
) -> RunReport:
    return await run_cases(
        cases,
        spec=DECISION_SPEC,
        base_url="http://eval.test",
        api_key=API_KEY,
        out_dir=tmp_path / "run",
        run_id="run-test",
        transport=transport,
        **kwargs,
    )


def _records(run_dir: Path) -> list[dict[str, Any]]:
    text = (run_dir / "cases.jsonl").read_text(encoding="utf-8")
    return [json.loads(line) for line in text.splitlines() if line.strip()]


# --- discovery and payload --------------------------------------------------------------------


def test_discover_reads_manifest_order(tmp_path: Path) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 3)
    assert [c.case_id for c in cases] == ["dc-001", "dc-002", "dc-003"]
    assert cases[0].case_path == tmp_path / "decision" / "cases" / "dc-001.json"


def test_discover_names_the_missing_manifest(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match=r"manifest\.json"):
        discover_decision_cases(tmp_path)


def test_discover_names_the_missing_case_file(tmp_path: Path) -> None:
    _make_decision_cases(tmp_path / "decision", 2)
    (tmp_path / "decision" / "cases" / "dc-002.json").unlink()
    with pytest.raises(ValueError, match=r"dc-002\.json"):
        discover_decision_cases(tmp_path / "decision")


def test_decision_payload_has_no_overrides_member_when_none() -> None:
    assert build_decision_payload("dc-001", None) == {
        "contract_version": "decision-1",
        "case_id": "dc-001",
    }
    assert build_decision_payload("dc-001", {}) == {
        "contract_version": "decision-1",
        "case_id": "dc-001",
    }
    assert build_decision_payload("dc-001", {"model": "claude-haiku-4-5"}) == {
        "contract_version": "decision-1",
        "case_id": "dc-001",
        "overrides": {"model": "claude-haiku-4-5"},
    }


def test_specs_describe_both_endpoints() -> None:
    assert EXTRACTION_SPEC.path == "/eval/extractions"
    assert EXTRACTION_SPEC.record_version == 3
    assert EXTRACTION_SPEC.record_kind is None
    assert DECISION_SPEC.path == "/eval/decisions"
    assert DECISION_SPEC.record_version == 1
    assert DECISION_SPEC.record_kind == "decision"
    assert DECISION_SPEC.timeout_s == 5400.0


# --- tracer: one case runs, records, grades -----------------------------------------------------


async def test_one_case_run_writes_a_decision_record(tmp_path: Path) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 1)
    seen: list[dict[str, Any]] = []
    report = await _run(tmp_path, cases, _transport(seen=seen))

    assert report.completed == 1 and report.harness_errors == 0
    assert seen[0]["path"] == "/eval/decisions"
    assert seen[0]["body"] == {"contract_version": "decision-1", "case_id": "dc-001"}
    assert seen[0]["headers"]["x-api-key"] == API_KEY
    assert seen[0]["headers"]["traceparent"].startswith("00-")

    (record,) = _records(tmp_path / "run")
    assert record["record_version"] == 1
    assert record["record_kind"] == "decision"
    assert record["status"] == "completed"
    assert record["case_id"] == "dc-001"
    expected_sha = hashlib.sha256(cases[0].case_path.read_bytes()).hexdigest()
    assert record["request"]["case_sha256"] == expected_sha
    assert record["request"]["overrides"] is None
    assert "reference_date" not in record["request"]
    assert "pdf_sha256" not in record["request"]
    assert record["request"]["trace_id"] == seen[0]["headers"]["traceparent"].split("-")[1]
    assert record["http"]["status"] == 200
    assert record["response"]["outcome"]["status"] == "decided"
    assert API_KEY not in (tmp_path / "run" / "cases.jsonl").read_text(encoding="utf-8")

    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["spent_usd"] == "0.01000000"
    assert run_json["cases_dir"] == str(tmp_path / "decision")
    assert "reference_date" in run_json  # shared run.json shape; null for decisions
    assert run_json["reference_date"] is None


async def test_extraction_records_keep_their_shape(tmp_path: Path) -> None:
    """The extraction spec is the default and writes no record_kind (record_version 3)."""
    from carimbo_evals.runner import CaseRef

    directory = tmp_path / "cases"
    directory.mkdir()
    (directory / "case-001.pdf").write_bytes(b"%PDF-1.4\n%%EOF\n")
    (directory / "case-001.xml").write_text("<NFe/>", encoding="utf-8")
    cases = [CaseRef("case-001", directory / "case-001.pdf", directory / "case-001.xml")]

    async def handler(request: httpx2.Request) -> httpx2.Response:
        assert request.url.path == "/eval/extractions"
        return httpx2.Response(200, json={"cost_usd": "0.01", "outcome": {"status": "success"}})

    report = await run_cases(
        cases,
        base_url="http://eval.test",
        api_key=API_KEY,
        out_dir=tmp_path / "xrun",
        run_id="x",
        transport=httpx2.MockTransport(handler),
    )
    assert report.completed == 1
    (record,) = _records(tmp_path / "xrun")
    assert record["record_version"] == 3
    assert "record_kind" not in record
    assert set(record["request"]) == {
        "pdf_sha256",
        "traceparent",
        "trace_id",
        "reference_date",
        "overrides",
    }


# --- strict contract models ---------------------------------------------------------------------


def test_decision_response_accepts_the_mock_body_and_rejects_extras() -> None:
    body = decision_body("dc-001")
    parsed = DecisionResponse.model_validate(body)
    assert parsed.outcome.decided_by == "model"
    assert parsed.steps[0].tool_calls[0].name == "get_invoice"

    with pytest.raises(ValidationError):
        DecisionResponse.model_validate({**body, "surprise": 1})
    nested = json.loads(json.dumps(body))
    nested["steps"][0]["tool_calls"][0]["extra"] = True
    with pytest.raises(ValidationError):
        DecisionResponse.model_validate(nested)
    bad_status = json.loads(json.dumps(body))
    bad_status["outcome"]["status"] = "maybe"
    with pytest.raises(ValidationError):
        DecisionResponse.model_validate(bad_status)


def test_decision_request_is_strict() -> None:
    assert (
        DecisionRequest.model_validate(
            {"contract_version": "decision-1", "case_id": "dc-001"}
        ).overrides
        is None
    )
    DecisionRequest.model_validate(
        {
            "contract_version": "decision-1",
            "case_id": "dc-001",
            "overrides": {"model": "claude-haiku-4-5", "cache_mode": "read_only"},
        }
    )
    with pytest.raises(ValidationError):
        DecisionRequest.model_validate(
            {"contract_version": "decision-1", "case_id": "dc-001", "overrides": {"x": 1}}
        )


# --- CLI: run then grade (the tracer end to end) --------------------------------------------------


def test_grade_decisions_cli_grades_one_case(tmp_path: Path) -> None:
    import asyncio

    cases_dir = tmp_path / "decision"
    cases = _make_decision_cases(cases_dir, 1)
    asyncio.run(_run(tmp_path, cases, _transport()))

    result = CliRunner().invoke(
        app, ["grade-decisions", "--run", str(tmp_path / "run"), "--cases", str(cases_dir)]
    )
    assert result.exit_code == 0, result.output
    summary = json.loads((tmp_path / "run" / "summary.json").read_text(encoding="utf-8"))
    assert summary["accuracy"] == {"n": 1, "correct": 1, "rate": 1.0}
    assert summary["citations"]["n"] == 1 and summary["citations"]["valid"] == 1
    assert summary["guard"]["checked"] == 1 and summary["guard"]["overridden"] == 0
    markdown = (tmp_path / "run" / "summary.md").read_text(encoding="utf-8")
    assert "dc-001" in markdown
    assert "dc-001" in result.output


def test_grade_decisions_cli_exits_2_without_records(tmp_path: Path) -> None:
    cases_dir = tmp_path / "decision"
    _make_decision_cases(cases_dir, 1)
    (tmp_path / "run").mkdir()
    result = CliRunner().invoke(
        app, ["grade-decisions", "--run", str(tmp_path / "run"), "--cases", str(cases_dir)]
    )
    assert result.exit_code == 2


def test_grade_decisions_cli_exits_2_without_expected(tmp_path: Path) -> None:
    import asyncio

    cases_dir = tmp_path / "decision"
    cases = _make_decision_cases(cases_dir, 1)
    asyncio.run(_run(tmp_path, cases, _transport()))
    (cases_dir / "expected.json").unlink()
    result = CliRunner().invoke(
        app, ["grade-decisions", "--run", str(tmp_path / "run"), "--cases", str(cases_dir)]
    )
    assert result.exit_code == 2


# --- shared runner guarantees under DECISION_SPEC ------------------------------------------------


def _priced(cost: str, incurred: str | None) -> Callable[[str], dict[str, Any]]:
    return lambda case_id: decision_body(case_id, cost=cost, incurred=incurred)


async def test_concurrency_two_over_four_cases_gives_one_record_per_case(tmp_path: Path) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 4)
    in_flight = 0
    peak = 0

    async def handler(request: httpx2.Request) -> httpx2.Response:
        nonlocal in_flight, peak
        case_id = json.loads(request.content)["case_id"]
        in_flight += 1
        peak = max(peak, in_flight)
        try:
            # The first case is the slowest, so records are written out of manifest order.
            await asyncio.sleep(0.08 if case_id == "dc-001" else 0.01)
        finally:
            in_flight -= 1
        return httpx2.Response(200, json=decision_body(case_id))

    report = await _run(tmp_path, cases, httpx2.MockTransport(handler), concurrency=2)
    assert report.completed == 4 and report.harness_errors == 0
    assert peak == 2
    ids = [record["case_id"] for record in _records(tmp_path / "run")]
    assert sorted(ids) == ["dc-001", "dc-002", "dc-003", "dc-004"]
    assert ids != sorted(ids)  # completion order, not manifest order


async def test_dispatch_continues_when_spent_plus_reserve_equals_the_cap(tmp_path: Path) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 3)
    seen: list[dict[str, Any]] = []
    report = await _run(
        tmp_path,
        cases,
        _transport(_priced("0.25000000", "0.25000000"), seen=seen),
        concurrency=1,
        max_cost_usd=Decimal("0.50"),
        reserve_usd=Decimal("0.25"),
    )
    # case 1: 0 + 0.25 <= 0.50; case 2: 0.25 + 0.25 == 0.50 still dispatches; case 3: 0.75 > 0.50.
    assert [s["body"]["case_id"] for s in seen] == ["dc-001", "dc-002"]
    assert report.completed == 2
    assert report.stopped_reason == "cost_cap"
    assert report.spent_usd == Decimal("0.50")


async def test_dispatch_stops_when_spent_plus_reserve_is_one_cent_over_the_cap(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 3)
    seen: list[dict[str, Any]] = []
    report = await _run(
        tmp_path,
        cases,
        _transport(_priced("0.25000000", "0.25000000"), seen=seen),
        concurrency=1,
        max_cost_usd=Decimal("0.49"),
        reserve_usd=Decimal("0.25"),
    )
    assert [s["body"]["case_id"] for s in seen] == ["dc-001"]
    assert report.stopped_reason == "cost_cap"


async def test_replayed_cases_never_trip_the_cap_and_report_notional_cost(tmp_path: Path) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 4)
    report = await _run(
        tmp_path,
        cases,
        _transport(_priced("0.03000000", "0.00000000")),
        concurrency=1,
        max_cost_usd=Decimal("0.30"),
        reserve_usd=Decimal("0.25"),
    )
    assert report.completed == 4
    assert report.stopped_reason is None
    assert report.spent_usd == Decimal(0)
    assert report.notional_usd == Decimal("0.12")
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["spent_usd"] == "0.00000000"
    assert run_json["notional_usd"] == "0.12000000"
    assert "E" not in run_json["spent_usd"]


async def test_a_run_with_no_dispatch_renders_zero_money_without_an_exponent(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 1)
    report = await _run(
        tmp_path,
        cases,
        _transport(),
        max_cost_usd=Decimal("0.10"),
        reserve_usd=Decimal("0.25"),
    )
    assert report.stopped_reason == "cost_cap" and report.completed == 0
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    for key in ("spent_usd", "notional_usd", "assumed_usd"):
        assert "E" not in run_json[key]
        assert float(run_json[key]) == 0


async def test_a_null_incurred_cost_charges_the_reserve_and_counts_unpriced(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 2)
    report = await _run(
        tmp_path,
        cases,
        _transport(_priced("0.01000000", None)),
        concurrency=1,
        reserve_usd=Decimal("0.25"),
    )
    assert report.completed == 2
    assert report.unpriced_cases == 2
    assert report.assumed_usd == Decimal("0.50")
    assert report.spent_usd == Decimal(0)


async def test_a_server_error_is_charged_the_reserve_and_a_rejection_is_not(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 2)
    report = await _run(
        tmp_path,
        cases,
        _transport(status=lambda case_id: 500 if case_id == "dc-001" else 400),
        concurrency=1,
        reserve_usd=Decimal("0.25"),
    )
    assert report.harness_errors == 2
    assert report.assumed_usd == Decimal("0.25")
    records = _records(tmp_path / "run")
    assert {r["http"]["status"] for r in records} == {400, 500}
    assert all(r["status"] == "harness_error" and r["record_kind"] == "decision" for r in records)


async def test_resume_skips_completed_reruns_harness_errors_and_charges_prior_records(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 3)
    first = _transport(
        _priced("0.10000000", "0.10000000"),
        status=lambda case_id: 500 if case_id == "dc-002" else 200,
    )
    await _run(tmp_path, cases, first, concurrency=1, reserve_usd=Decimal("0.25"))

    seen: list[dict[str, Any]] = []
    second = _transport(_priced("0.10000000", "0.10000000"), seen=seen)
    report = await _run(
        tmp_path, cases, second, concurrency=1, resume=True, reserve_usd=Decimal("0.25")
    )
    assert [s["body"]["case_id"] for s in seen] == ["dc-002"]
    assert report.skipped == 2 and report.completed == 1
    # two earlier completions and the new one are spent; the earlier 500 stays charged.
    assert report.spent_usd == Decimal("0.30")
    assert report.assumed_usd == Decimal("0.25")
    completed = [r for r in _records(tmp_path / "run") if r["status"] == "completed"]
    assert sorted(r["case_id"] for r in completed) == ["dc-001", "dc-002", "dc-003"]


async def test_resume_with_different_overrides_is_refused_and_leaves_the_file_unchanged(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 2)
    await _run(tmp_path, cases[:1], _transport(), overrides={"cache_mode": "read_only"})
    path = tmp_path / "run" / "cases.jsonl"
    before = path.read_bytes()
    with pytest.raises(RunConfigMismatchError):
        await _run(
            tmp_path,
            cases,
            _transport(),
            resume=True,
            overrides={"cache_mode": "refresh"},
        )
    assert path.read_bytes() == before


async def test_a_second_run_into_a_used_directory_without_resume_is_refused(
    tmp_path: Path,
) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 1)
    await _run(tmp_path, cases, _transport())
    with pytest.raises(FileExistsError):
        await _run(tmp_path, cases, _transport())


async def test_overrides_are_sent_and_recorded(tmp_path: Path) -> None:
    cases = _make_decision_cases(tmp_path / "decision", 1)
    seen: list[dict[str, Any]] = []
    overrides = {"model": "claude-haiku-4-5", "cache_mode": "read_only", "replicate_salt": "r1"}
    await _run(tmp_path, cases, _transport(seen=seen), overrides=overrides)
    assert seen[0]["body"]["overrides"] == overrides
    (record,) = _records(tmp_path / "run")
    assert record["request"]["overrides"] == overrides
    DecisionRequest.model_validate(
        seen[0]["body"]
    )  # the runner's body is a valid decision-1 request


async def test_the_client_read_timeout_covers_a_full_agent_loop(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    captured: list[httpx2.Timeout] = []
    real_client = httpx2.AsyncClient

    def spy(*args: Any, **kwargs: Any) -> httpx2.AsyncClient:
        captured.append(kwargs["timeout"])
        return real_client(*args, **kwargs)

    monkeypatch.setattr(runner.httpx2, "AsyncClient", spy)
    await _run(tmp_path, _make_decision_cases(tmp_path / "decision", 1), _transport())
    assert captured[0].read == 6 * 3 * 300.0
    assert captured[0].connect == 5.0


# --- CLI: carimbo-evals decisions ----------------------------------------------------------


@pytest.fixture
def decisions_cli(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Callable[..., Any]:
    monkeypatch.setenv("CARIMBO_EVAL_API_KEY", API_KEY)
    _make_decision_cases(tmp_path / "decision", 4)

    def invoke(transport: httpx2.MockTransport, *args: str) -> Any:
        monkeypatch.setattr(cli, "run_cases", functools.partial(run_cases, transport=transport))
        return CliRunner().invoke(
            app,
            [
                "decisions",
                "--cases",
                str(tmp_path / "decision"),
                "--out",
                str(tmp_path / "out"),
                "--run-id",
                "run-cli",
                *args,
            ],
        )

    return invoke


def test_cli_exits_0_and_prints_one_line_per_case_and_the_totals(
    decisions_cli: Callable[..., Any], tmp_path: Path
) -> None:
    result = decisions_cli(_transport(_priced("0.01000000", "0.00000000")))
    assert result.exit_code == 0, result.output
    assert "dc-001  completed  decided  approve/none  steps=2 cost=0.01000000" in result.output
    assert "incurred=0.00000000" in result.output
    assert "hits=0/2" in result.output
    assert "spent=0.00000000" in result.output and "notional=0.04000000" in result.output
    assert "E-" not in result.output


def test_cli_exits_2_without_the_key_and_sends_nothing(
    decisions_cli: Callable[..., Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.delenv("CARIMBO_EVAL_API_KEY")
    seen: list[dict[str, Any]] = []
    result = decisions_cli(_transport(seen=seen))
    assert result.exit_code == 2
    assert "CARIMBO_EVAL_API_KEY" in result.output
    assert seen == []


def test_cli_rejects_an_unknown_case_id_before_any_request(
    decisions_cli: Callable[..., Any],
) -> None:
    seen: list[dict[str, Any]] = []
    result = decisions_cli(_transport(seen=seen), "--case", "dc-999")
    assert result.exit_code == 2
    assert "dc-999" in result.output
    assert seen == []


def test_cli_case_filter_runs_only_the_named_cases(decisions_cli: Callable[..., Any]) -> None:
    seen: list[dict[str, Any]] = []
    result = decisions_cli(_transport(seen=seen), "--case", "dc-003", "--case", "dc-001")
    assert result.exit_code == 0, result.output
    assert sorted(s["body"]["case_id"] for s in seen) == ["dc-001", "dc-003"]


def test_cli_sends_model_cache_mode_and_salt_as_overrides(
    decisions_cli: Callable[..., Any],
) -> None:
    seen: list[dict[str, Any]] = []
    result = decisions_cli(
        _transport(seen=seen),
        "--case",
        "dc-001",
        "--model",
        "claude-sonnet-5-5",
        "--cache-mode",
        "read_only",
        "--replicate-salt",
        "rep-1",
    )
    assert result.exit_code == 0, result.output
    assert seen[0]["body"]["overrides"] == {
        "model": "claude-sonnet-5-5",
        "cache_mode": "read_only",
        "replicate_salt": "rep-1",
    }


def test_cli_without_override_flags_sends_no_overrides_member(
    decisions_cli: Callable[..., Any],
) -> None:
    seen: list[dict[str, Any]] = []
    decisions_cli(_transport(seen=seen), "--case", "dc-001")
    assert "overrides" not in seen[0]["body"]


@pytest.mark.parametrize(
    "args",
    [["--cache-mode", "sometimes"], ["--replicate-salt", "bad salt"], ["--max-cost-usd", "x"]],
)
def test_cli_rejects_a_bad_option_before_any_request(
    decisions_cli: Callable[..., Any], args: list[str]
) -> None:
    seen: list[dict[str, Any]] = []
    result = decisions_cli(_transport(seen=seen), *args)
    assert result.exit_code == 2
    assert seen == []


def test_cli_has_no_prompt_version_or_max_repairs_options() -> None:
    env = {"NO_COLOR": "1", "COLUMNS": "200", "TERM": "dumb"}
    result = CliRunner().invoke(app, ["decisions", "--help"], env=env)
    assert result.exit_code == 0
    assert "--prompt-version" not in result.output
    assert "--max-repairs" not in result.output
    for option in ("--cache-mode", "--replicate-salt", "--model", "--case", "--resume"):
        assert option in result.output


def test_cli_exits_3_when_stopped_at_the_cost_cap(decisions_cli: Callable[..., Any]) -> None:
    result = decisions_cli(
        _transport(_priced("0.25000000", "0.25000000")),
        "--concurrency",
        "1",
        "--max-cost-usd",
        "0.50",
    )
    assert result.exit_code == 3
    assert "stopped_reason=cost_cap" in result.output


def test_cli_exits_4_when_harness_errors_remain(decisions_cli: Callable[..., Any]) -> None:
    result = decisions_cli(_transport(status=lambda case_id: 400 if case_id == "dc-002" else 200))
    assert result.exit_code == 4


def test_cli_resume_with_different_overrides_exits_2(
    decisions_cli: Callable[..., Any],
) -> None:
    first = decisions_cli(_transport(), "--cache-mode", "read_only", "--case", "dc-001")
    assert first.exit_code == 0, first.output
    second = decisions_cli(_transport(), "--resume", "--cache-mode", "refresh")
    assert second.exit_code == 2
    assert "overrides" in second.output


def test_cli_a_used_run_directory_without_resume_exits_2(
    decisions_cli: Callable[..., Any],
) -> None:
    assert decisions_cli(_transport(), "--case", "dc-001").exit_code == 0
    assert decisions_cli(_transport(), "--case", "dc-001").exit_code == 2


def test_cli_a_missing_manifest_exits_2(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("CARIMBO_EVAL_API_KEY", API_KEY)
    result = CliRunner().invoke(app, ["decisions", "--cases", str(tmp_path / "nothing")])
    assert result.exit_code == 2
    assert "manifest.json" in result.output
