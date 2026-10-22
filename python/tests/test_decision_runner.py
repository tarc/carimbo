"""AGT-04 / AGT-06: decision runs go through the shared runner, then grade offline."""

from __future__ import annotations

import hashlib
import json
from collections.abc import Callable
from pathlib import Path
from typing import Any

import httpx2
import pytest
from pydantic import ValidationError
from typer.testing import CliRunner

from carimbo_evals.cli import app
from carimbo_evals.decision_contract import DecisionRequest, DecisionResponse
from carimbo_evals.runner import (
    DECISION_SPEC,
    EXTRACTION_SPEC,
    DecisionCaseRef,
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
        "evidence": [{"source": "get_invoice", "ref": "recipient.tax_id", "value": "12345678000195"}],
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
        body = bodies(payload["case_id"]) if bodies is not None else decision_body(payload["case_id"])
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
    with pytest.raises(ValueError, match="manifest.json"):
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
    assert DecisionRequest.model_validate(
        {"contract_version": "decision-1", "case_id": "dc-001"}
    ).overrides is None
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
