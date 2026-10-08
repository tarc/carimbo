"""EVAL-01: bounded concurrency, a tested cost cap, resume, and a CLI with defined exit codes."""

from __future__ import annotations

import asyncio
import functools
import json
import re
from collections.abc import Callable
from decimal import Decimal
from pathlib import Path
from typing import Any

import httpx2
import pytest
from typer.testing import CliRunner

from carimbo_evals import cli
from carimbo_evals.cli import app
from carimbo_evals.runner import CaseRef, RunReport, discover_cases, run_cases

API_KEY = "test-eval-key-do-not-leak-0123456789"
TRACEPARENT_RE = re.compile(r"^00-[0-9a-f]{32}-[0-9a-f]{16}-01$")


def _make_cases(directory: Path, count: int) -> list[CaseRef]:
    directory.mkdir(parents=True, exist_ok=True)
    cases: list[CaseRef] = []
    for index in range(1, count + 1):
        case_id = f"case-{index:03d}"
        pdf = directory / f"{case_id}.pdf"
        xml = directory / f"{case_id}.xml"
        pdf.write_bytes(f"%PDF-1.4\n% synthetic {case_id}\n%%EOF\n".encode())
        xml.write_text("<NFe/>", encoding="utf-8")
        cases.append(CaseRef(case_id, pdf, xml))
    return cases


def _body(case_id: str, cost: str | None) -> dict[str, Any]:
    return {
        "contract_version": "1",
        "case_id": case_id,
        "trace_id": "0" * 31 + "1",
        "outcome": {"status": "success", "invoice": {}, "failure": None, "raw_output": "{}"},
        "usage": {"input_tokens": 1, "output_tokens": 1},
        "cost_usd": cost,
        "latency_ms": 5,
    }


def _transport(
    costs: dict[str, str | None] | str | None = "0.01",
    *,
    on_request: Callable[[httpx2.Request], None] | None = None,
    status: Callable[[str], int] | None = None,
    delay: float = 0.0,
) -> tuple[httpx2.MockTransport, list[str]]:
    """A mock endpoint. Returns the transport and the list of case ids it was asked for."""
    seen: list[str] = []

    async def handler(request: httpx2.Request) -> httpx2.Response:
        payload = json.loads(request.content)
        case_id = payload["case_id"]
        seen.append(case_id)
        if on_request is not None:
            on_request(request)
        if delay:
            await asyncio.sleep(delay)
        code = status(case_id) if status is not None else 200
        if code != 200:
            return httpx2.Response(code, json={"error": "nope"})
        cost = costs.get(case_id) if isinstance(costs, dict) else costs
        return httpx2.Response(200, json=_body(case_id, cost))

    return httpx2.MockTransport(handler), seen


async def _run(
    tmp_path: Path,
    cases: list[CaseRef],
    transport: httpx2.MockTransport,
    **kwargs: Any,
) -> RunReport:
    return await run_cases(
        cases,
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


# --- concurrency ----------------------------------------------------------------------------


async def test_in_flight_requests_never_exceed_concurrency(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 6)
    in_flight = 0
    peak = 0

    async def handler(request: httpx2.Request) -> httpx2.Response:
        nonlocal in_flight, peak
        in_flight += 1
        peak = max(peak, in_flight)
        try:
            await asyncio.sleep(0.02)
        finally:
            in_flight -= 1
        case_id = json.loads(request.content)["case_id"]
        return httpx2.Response(200, json=_body(case_id, "0.01"))

    report = await _run(tmp_path, cases, httpx2.MockTransport(handler), concurrency=2)
    assert report.completed == 6
    assert peak == 2


# --- cost cap -------------------------------------------------------------------------------


async def test_dispatch_continues_when_spent_plus_reserve_equals_the_cap(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 6)
    transport, seen = _transport("0.10")
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.30"),
        reserve_usd=Decimal("0.05"),
    )
    # Third dispatch sees spent 0.20 + reserve 0.10 (the largest cost seen) == cap 0.30: allowed.
    assert seen == ["case-001", "case-002", "case-003"]
    assert report.spent_usd == Decimal("0.30")
    assert report.stopped_reason == "cost_cap"


async def test_dispatch_stops_when_spent_plus_reserve_is_one_cent_over_the_cap(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 6)
    transport, seen = _transport("0.10")
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.29"),
        reserve_usd=Decimal("0.05"),
    )
    # Third dispatch would need 0.20 + 0.10 = 0.30 > 0.29: stop before it.
    assert seen == ["case-001", "case-002"]
    assert report.spent_usd == Decimal("0.20")
    assert report.stopped_reason == "cost_cap"
    assert report.completed == 2
    assert len(_records(tmp_path / "run")) == 2


async def test_a_run_that_fits_under_the_cap_is_not_stopped(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 3)
    transport, seen = _transport("0.01")
    report = await _run(tmp_path, cases, transport, max_cost_usd=Decimal("1.00"))
    assert len(seen) == 3
    assert report.stopped_reason is None


async def test_costs_are_summed_as_decimal_never_float(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    transport, _ = _transport({"case-001": "0.1", "case-002": "0.2"})
    report = await _run(tmp_path, cases, transport, concurrency=1)
    assert report.spent_usd == Decimal("0.3")
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["spent_usd"] == "0.3"
    assert isinstance(run_json["max_cost_usd"], str)


async def test_a_null_cost_counts_as_the_reserve_for_the_cap_and_is_reported(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 5)
    transport, seen = _transport(None)
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.10"),
        reserve_usd=Decimal("0.05"),
    )
    # Unpriced cases are assumed to cost the reserve: 0+.05 and .05+.05 pass, .10+.05 does not.
    assert seen == ["case-001", "case-002"]
    assert report.unpriced_cases == 2
    assert report.spent_usd == Decimal("0")
    assert report.stopped_reason == "cost_cap"


# --- cost cap: harness errors that may have been paid for (WR-02) ---------------------------


def _raising_transport(
    make_error: Callable[[httpx2.Request], Exception],
) -> tuple[httpx2.MockTransport, list[str]]:
    """A mock endpoint whose every request fails with a transport-level exception."""
    seen: list[str] = []

    async def handler(request: httpx2.Request) -> httpx2.Response:
        seen.append(json.loads(request.content)["case_id"])
        raise make_error(request)

    return httpx2.MockTransport(handler), seen


async def test_a_server_error_is_charged_the_reserve_against_the_cap(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 4)
    transport, seen = _transport(status=lambda _case_id: 500)
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.10"),
        reserve_usd=Decimal("0.05"),
    )
    # A 500 may have come after the provider call: each is charged the reserve (0.05 + 0.05 = cap).
    assert seen == ["case-001", "case-002"]
    assert report.harness_errors == 2
    assert report.stopped_reason == "cost_cap"
    assert report.assumed_usd == Decimal("0.10")
    assert report.spent_usd == Decimal("0")


@pytest.mark.parametrize("status_code", [400, 401, 404, 413, 415])
async def test_rejections_before_any_provider_call_are_not_charged(
    tmp_path: Path, status_code: int
) -> None:
    cases = _make_cases(tmp_path / "cases", 4)
    transport, seen = _transport(status=lambda _case_id: status_code)
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.10"),
        reserve_usd=Decimal("0.05"),
    )
    assert len(seen) == 4
    assert report.harness_errors == 4
    assert report.stopped_reason is None
    assert report.assumed_usd == Decimal("0")
    for record in _records(tmp_path / "run"):
        assert record["http"]["status"] == status_code
        assert record["http"]["error_type"] is None


@pytest.mark.parametrize(
    ("error_class", "expected_dispatches"),
    [
        (httpx2.ReadTimeout, 2),
        (httpx2.WriteTimeout, 2),
        (httpx2.RemoteProtocolError, 2),
        (httpx2.ConnectError, 4),
        (httpx2.ConnectTimeout, 4),
        (httpx2.PoolTimeout, 4),
    ],
)
async def test_transport_errors_are_charged_unless_the_request_never_left_the_client(
    tmp_path: Path, error_class: type[httpx2.TransportError], expected_dispatches: int
) -> None:
    cases = _make_cases(tmp_path / "cases", 4)
    transport, seen = _raising_transport(lambda request: error_class("boom", request=request))
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.10"),
        reserve_usd=Decimal("0.05"),
    )
    assert len(seen) == expected_dispatches
    expect_stop = expected_dispatches < 4
    assert (report.stopped_reason == "cost_cap") is expect_stop
    for record in _records(tmp_path / "run"):
        assert record["http"]["error_type"] == error_class.__name__
        assert record["http"]["status"] is None


async def test_resume_charges_every_prior_harness_error_not_only_the_last(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 1)
    for attempt in range(2):
        failing, _ = _transport(status=lambda _case_id: 500)
        report = await _run(tmp_path, cases, failing, resume=attempt > 0)
        assert report.harness_errors == 1
    assert len(_records(tmp_path / "run")) == 2

    healthy, seen = _transport()
    report = await _run(
        tmp_path,
        cases,
        healthy,
        resume=True,
        max_cost_usd=Decimal("0.14"),
        reserve_usd=Decimal("0.05"),
    )
    # Both prior 500s were possibly paid: 0.05 + 0.05 + the next reserve 0.05 > 0.14.
    # Counting only the last record per case would allow the dispatch (0.05 + 0.05 <= 0.14).
    assert seen == []
    assert report.stopped_reason == "cost_cap"
    assert report.assumed_usd == Decimal("0.10")


# --- resume ---------------------------------------------------------------------------------


async def test_resume_reruns_only_harness_errors_and_keeps_one_completed_record(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 3)
    flaky, first_seen = _transport(status=lambda case_id: 503 if case_id == "case-002" else 200)
    first = await _run(tmp_path, cases, flaky)
    assert (first.completed, first.harness_errors) == (2, 1)
    assert sorted(first_seen) == ["case-001", "case-002", "case-003"]

    healthy, second_seen = _transport()
    second = await _run(tmp_path, cases, healthy, resume=True)
    assert second_seen == ["case-002"]
    assert (second.completed, second.harness_errors, second.skipped) == (1, 0, 2)

    records = _records(tmp_path / "run")
    completed = [r["case_id"] for r in records if r["status"] == "completed"]
    assert sorted(completed) == ["case-001", "case-002", "case-003"]
    assert len(completed) == len(set(completed))


async def test_resume_counts_prior_spend_against_the_cap(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 4)
    transport, _ = _transport("0.10")
    await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.20"),
        reserve_usd=Decimal("0.05"),
    )
    again, seen = _transport("0.10")
    report = await _run(
        tmp_path,
        cases,
        again,
        concurrency=1,
        max_cost_usd=Decimal("0.20"),
        reserve_usd=Decimal("0.05"),
        resume=True,
    )
    assert seen == []
    assert report.skipped == 2
    assert report.spent_usd == Decimal("0.20")
    assert report.stopped_reason == "cost_cap"


async def test_a_second_run_into_a_used_directory_without_resume_is_refused(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 1)
    transport, _ = _transport()
    await _run(tmp_path, cases, transport)
    again, seen = _transport()
    with pytest.raises(FileExistsError, match="--resume"):
        await _run(tmp_path, cases, again)
    assert seen == []
    assert len(_records(tmp_path / "run")) == 1


async def test_resume_repairs_a_torn_final_line(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    transport, _ = _transport()
    await _run(tmp_path, cases[:1], transport)
    sink = tmp_path / "run" / "cases.jsonl"
    with sink.open("a", encoding="utf-8") as handle:
        handle.write('{"record_version": 1, "case_id": "case-0')  # killed mid-write
    again, seen = _transport()
    report = await _run(tmp_path, cases, again, resume=True)
    assert seen == ["case-002"]
    assert report.completed == 1
    assert {r["case_id"] for r in _records(tmp_path / "run")} == {"case-001", "case-002"}


# --- interruption ---------------------------------------------------------------------------


async def test_cancelling_a_run_leaves_only_complete_json_lines(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 8)
    transport, _ = _transport(delay=0.03)
    sink = tmp_path / "run" / "cases.jsonl"
    task = asyncio.create_task(_run(tmp_path, cases, transport, concurrency=1))
    for _ in range(400):
        if sink.exists() and len(sink.read_text(encoding="utf-8").splitlines()) >= 2:
            break
        await asyncio.sleep(0.01)
    task.cancel()
    with pytest.raises(asyncio.CancelledError):
        await task
    text = sink.read_text(encoding="utf-8")
    assert text.endswith("\n")
    lines = text.splitlines()
    assert 2 <= len(lines) < 8
    for line in lines:
        assert json.loads(line)["case_id"].startswith("case-")


# --- secrets and tracing --------------------------------------------------------------------


async def test_key_never_reaches_disk_and_traceparents_are_unique(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 4)
    keys_seen: list[str] = []
    transport, _ = _transport(on_request=lambda r: keys_seen.append(r.headers["x-api-key"]))
    await _run(tmp_path, cases, transport, concurrency=2)
    assert keys_seen == [API_KEY] * 4  # it is sent, but only as a header
    run_dir = tmp_path / "run"
    for name in ("cases.jsonl", "run.json"):
        assert API_KEY not in (run_dir / name).read_text(encoding="utf-8")
    traceparents = [r["request"]["traceparent"] for r in _records(run_dir)]
    assert all(TRACEPARENT_RE.match(t) for t in traceparents)
    assert len(set(traceparents)) == 4
    for record in _records(run_dir):
        assert record["request"]["traceparent"].split("-")[1] == record["request"]["trace_id"]


async def test_run_json_describes_the_run(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    transport, _ = _transport("0.02")
    await _run(tmp_path, cases, transport, concurrency=1)
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["run_id"] == "run-test"
    assert run_json["base_url"] == "http://eval.test"
    assert run_json["case_ids"] == ["case-001", "case-002"]
    assert run_json["concurrency"] == 1
    assert run_json["max_cost_usd"] == "1.00"
    assert run_json["spent_usd"] == "0.04"
    assert run_json["assumed_usd"] == "0"
    assert run_json["unpriced_cases"] == 0
    assert run_json["stopped_reason"] is None
    assert (run_json["completed"], run_json["harness_errors"], run_json["skipped"]) == (2, 0, 0)
    assert run_json["started_at"] <= run_json["finished_at"]


# --- harness errors -------------------------------------------------------------------------


async def test_transport_errors_and_non_200_statuses_become_harness_error_records(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 2)

    async def handler(request: httpx2.Request) -> httpx2.Response:
        case_id = json.loads(request.content)["case_id"]
        if case_id == "case-001":
            raise httpx2.ConnectError("connection refused", request=request)
        return httpx2.Response(500, json={"error": "boom"})

    report = await _run(tmp_path, cases, httpx2.MockTransport(handler))
    assert (report.completed, report.harness_errors) == (0, 2)
    by_case = {r["case_id"]: r for r in _records(tmp_path / "run")}
    for record in by_case.values():
        assert record["status"] == "harness_error"
        assert record["http"]["error"]
    assert by_case["case-001"]["http"]["status"] is None
    assert "ConnectError" in by_case["case-001"]["http"]["error"]
    assert by_case["case-002"]["http"]["status"] == 500


# --- CLI ------------------------------------------------------------------------------------


@pytest.fixture
def cli_env(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Callable[..., Any]:
    """Return an invoker for ``carimbo-evals run`` whose HTTP layer is a mock transport."""
    monkeypatch.setenv("CARIMBO_EVAL_API_KEY", API_KEY)
    _make_cases(tmp_path / "cases", 4)

    def invoke(
        transport: httpx2.MockTransport, *args: str, env: dict[str, str] | None = None
    ) -> Any:
        monkeypatch.setattr(cli, "run_cases", functools.partial(run_cases, transport=transport))
        return CliRunner().invoke(
            app,
            [
                "run",
                "--cases",
                str(tmp_path / "cases"),
                "--out",
                str(tmp_path / "out"),
                "--run-id",
                "run-cli",
                *args,
            ],
            env=env,
        )

    return invoke


def test_cli_exits_0_when_every_case_completed(cli_env: Callable[..., Any], tmp_path: Path) -> None:
    transport, _ = _transport("0.01")
    result = cli_env(transport)
    assert result.exit_code == 0, result.output
    assert "case-001" in result.output
    assert "spent" in result.output
    assert "assumed=" in result.output
    assert len(_records(tmp_path / "out")) == 4


def test_cli_exits_3_when_stopped_at_the_cost_cap(cli_env: Callable[..., Any]) -> None:
    transport, seen = _transport("0.10")
    result = cli_env(
        transport, "--concurrency", "1", "--max-cost-usd", "0.29", "--reserve-usd", "0.05"
    )
    assert result.exit_code == 3, result.output
    assert "cost_cap" in result.output
    assert seen == ["case-001", "case-002"]


def test_cli_stops_at_the_cap_when_server_errors_may_have_cost_money(
    cli_env: Callable[..., Any], tmp_path: Path
) -> None:
    transport, seen = _transport(status=lambda _case_id: 500)
    result = cli_env(
        transport, "--concurrency", "1", "--max-cost-usd", "0.10", "--reserve-usd", "0.05"
    )
    assert result.exit_code == 3, result.output
    assert "cost_cap" in result.output
    assert "assumed=0.10" in result.output
    assert seen == ["case-001", "case-002"]
    run_json = json.loads((tmp_path / "out" / "run.json").read_text(encoding="utf-8"))
    assert run_json["assumed_usd"] == "0.10"
    assert run_json["spent_usd"] == "0"


def test_cli_exits_4_when_harness_errors_remain(cli_env: Callable[..., Any]) -> None:
    transport, _ = _transport(status=lambda case_id: 502 if case_id == "case-003" else 200)
    result = cli_env(transport)
    assert result.exit_code == 4, result.output


def test_cli_exits_2_without_the_key_and_prints_a_message(
    cli_env: Callable[..., Any], monkeypatch: pytest.MonkeyPatch
) -> None:
    transport, seen = _transport()
    monkeypatch.delenv("CARIMBO_EVAL_API_KEY")
    result = cli_env(transport)
    assert result.exit_code == 2
    assert "CARIMBO_EVAL_API_KEY" in result.output
    assert seen == []


def test_cli_case_filter_runs_only_the_named_cases(cli_env: Callable[..., Any]) -> None:
    transport, seen = _transport()
    result = cli_env(transport, "--case", "case-002", "--case", "case-004")
    assert result.exit_code == 0, result.output
    assert sorted(seen) == ["case-002", "case-004"]


def test_cli_rejects_an_unknown_case_id(cli_env: Callable[..., Any]) -> None:
    transport, seen = _transport()
    result = cli_env(transport, "--case", "case-999")
    assert result.exit_code == 2
    assert seen == []


def test_cli_rejects_a_malformed_cost_cap(cli_env: Callable[..., Any]) -> None:
    transport, seen = _transport()
    result = cli_env(transport, "--max-cost-usd", "one dollar")
    assert result.exit_code == 2
    assert seen == []


def test_cli_resume_flag_reaches_the_runner(cli_env: Callable[..., Any], tmp_path: Path) -> None:
    flaky, _ = _transport(status=lambda case_id: 500 if case_id == "case-001" else 200)
    assert cli_env(flaky).exit_code == 4
    healthy, seen = _transport()
    result = cli_env(healthy, "--resume")
    assert result.exit_code == 0, result.output
    assert seen == ["case-001"]


def test_cli_help_lists_the_documented_options() -> None:
    env = {"NO_COLOR": "1", "COLUMNS": "200", "TERM": "dumb"}
    result = CliRunner().invoke(app, ["run", "--help"], env=env)
    assert result.exit_code == 0
    for option in ("--max-cost-usd", "--resume", "--concurrency", "--case", "--reserve-usd"):
        assert option in result.output
    assert "api-key" not in result.output.lower()


def test_discover_cases_pairs_pdfs_with_xml(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path, 2)
    assert discover_cases(tmp_path) == cases
