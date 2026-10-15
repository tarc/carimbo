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

from carimbo_evals import cli, runner
from carimbo_evals.cli import app
from carimbo_evals.runner import (
    CaseRef,
    RunConfigMismatchError,
    RunReport,
    discover_cases,
    read_as_of_date,
    run_cases,
)

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
        "contract_version": "3",
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


# --- cost cap: incurred versus notional cost (D-29, LLM-05) ---------------------------------

_ABSENT = object()


def _priced_transport(
    cost: str | None,
    incurred: object = _ABSENT,
) -> tuple[httpx2.MockTransport, list[str]]:
    """A mock endpoint answering ``cost_usd`` and, unless ``_ABSENT``, ``incurred_cost_usd``."""
    seen: list[str] = []

    async def handler(request: httpx2.Request) -> httpx2.Response:
        case_id = json.loads(request.content)["case_id"]
        seen.append(case_id)
        body = _body(case_id, cost)
        if incurred is not _ABSENT:
            body["incurred_cost_usd"] = incurred
        return httpx2.Response(200, json=body)

    return httpx2.MockTransport(handler), seen


async def test_replayed_cases_add_nothing_to_spend_so_ten_of_them_fit_under_the_cap(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 10)
    transport, seen = _priced_transport("0.30", "0.00000000")
    report = await _run(tmp_path, cases, transport, concurrency=1, max_cost_usd=Decimal("1.00"))
    assert len(seen) == 10
    assert report.completed == 10
    assert report.stopped_reason is None
    assert report.spent_usd == Decimal(0)
    assert report.notional_usd == Decimal("3.00")
    assert report.unpriced_cases == 0
    assert report.assumed_usd == Decimal(0)
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert Decimal(run_json["spent_usd"]) == 0
    assert Decimal(run_json["notional_usd"]) == Decimal("3.00")


async def test_a_null_incurred_cost_is_charged_the_reserve_even_when_the_notional_cost_is_known(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 5)
    transport, seen = _priced_transport("0.01", None)
    report = await _run(
        tmp_path,
        cases,
        transport,
        concurrency=1,
        max_cost_usd=Decimal("0.10"),
        reserve_usd=Decimal("0.05"),
    )
    assert seen == ["case-001", "case-002"]
    assert report.unpriced_cases == 2
    assert report.assumed_usd == Decimal("0.10")
    assert report.spent_usd == Decimal(0)
    assert report.notional_usd == Decimal("0.02")
    assert report.stopped_reason == "cost_cap"


async def test_incurred_spend_is_what_the_cap_counts_when_it_differs_from_the_notional_cost(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 3)
    transport, _ = _priced_transport("0.50", "0.10")
    report = await _run(tmp_path, cases, transport, concurrency=1)
    assert report.spent_usd == Decimal("0.30")
    assert report.notional_usd == Decimal("1.50")


async def test_a_record_without_incurred_cost_is_accounted_on_cost_usd(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    transport, _ = _priced_transport("0.10")
    report = await _run(tmp_path, cases, transport, concurrency=1)
    assert report.spent_usd == Decimal("0.20")
    assert report.notional_usd == Decimal("0.20")


async def test_a_cache_miss_with_no_cost_and_zero_incurred_is_free_not_unpriced(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    transport, _ = _priced_transport(None, "0.00000000")
    report = await _run(tmp_path, cases, transport, concurrency=1)
    assert report.unpriced_cases == 0
    assert report.spent_usd == Decimal(0)
    assert report.notional_usd == Decimal(0)
    assert report.assumed_usd == Decimal(0)


async def test_a_resumed_replay_charges_earlier_records_on_incurred_cost(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 4)
    first, _ = _priced_transport("0.30", "0.00000000")
    await _run(tmp_path, cases[:2], first, concurrency=1)
    second, seen = _priced_transport("0.30", "0.00000000")
    report = await _run(tmp_path, cases, second, concurrency=1, resume=True)
    assert seen == ["case-003", "case-004"]
    assert report.spent_usd == Decimal(0)
    assert report.notional_usd == Decimal("1.20")  # the whole run, earlier invocations included


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


def _good_line(case_id: str) -> str:
    record = {"record_version": 1, "case_id": case_id, "status": "completed", "response": None}
    return json.dumps(record, sort_keys=True)


def _write_corrupt_run(tmp_path: Path, bad_line: str) -> Path:
    """A run directory whose cases.jsonl has ``bad_line`` between two good records."""
    out_dir = tmp_path / "run"
    out_dir.mkdir(parents=True)
    sink = out_dir / "cases.jsonl"
    sink.write_text(
        "\n".join([_good_line("case-001"), bad_line, _good_line("case-002")]) + "\n",
        encoding="utf-8",
    )
    return sink


CORRUPT_LINES = ["{not json", '{"record_version": 1}', "[1, 2]"]


@pytest.mark.parametrize("bad_line", CORRUPT_LINES)
async def test_resume_with_a_corrupt_middle_line_raises_and_leaves_the_file_unchanged(
    tmp_path: Path, bad_line: str
) -> None:
    cases = _make_cases(tmp_path / "cases", 3)
    sink = _write_corrupt_run(tmp_path, bad_line)
    before = sink.read_bytes()
    transport, seen = _transport()
    with pytest.raises(runner.CorruptRunError, match="line 2") as excinfo:
        await _run(tmp_path, cases, transport, resume=True)
    assert str(sink) in str(excinfo.value)
    assert isinstance(excinfo.value, ValueError)
    assert seen == []
    assert sink.read_bytes() == before


async def test_a_corrupt_line_is_not_repaired_even_when_the_file_also_ends_torn(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 3)
    sink = _write_corrupt_run(tmp_path, "{not json")
    with sink.open("a", encoding="utf-8") as handle:
        handle.write('{"record_version": 1, "case_id": "case-0')
    before = sink.read_bytes()
    transport, _ = _transport()
    with pytest.raises(runner.CorruptRunError, match="line 2"):
        await _run(tmp_path, cases, transport, resume=True)
    assert sink.read_bytes() == before


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


# --- contract 3: the reference date and the overrides -----------------------------------------


async def test_payload_is_contract_3_and_carries_the_reference_date(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    payloads: list[dict[str, Any]] = []
    transport, _ = _transport(on_request=lambda r: payloads.append(json.loads(r.content)))
    await _run(tmp_path, cases, transport, concurrency=1, reference_date="2026-10-01")
    assert len(payloads) == 2
    for payload in payloads:
        assert payload["contract_version"] == "3"
        assert payload["reference_date"] == "2026-10-01"
        assert set(payload) == {"contract_version", "case_id", "document", "reference_date"}
    for record in _records(tmp_path / "run"):
        assert record["record_version"] == 3
        assert record["request"]["reference_date"] == "2026-10-01"
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["reference_date"] == "2026-10-01"


async def test_payload_has_no_reference_date_member_without_one(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 1)
    payloads: list[dict[str, Any]] = []
    transport, _ = _transport(on_request=lambda r: payloads.append(json.loads(r.content)))
    await _run(tmp_path, cases, transport)
    assert payloads[0]["contract_version"] == "3"
    assert "reference_date" not in payloads[0]
    record = _records(tmp_path / "run")[0]
    assert record["record_version"] == 3
    assert record["request"]["reference_date"] is None
    assert record["request"]["overrides"] is None
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["overrides"] is None


async def test_overrides_are_sent_with_every_request_and_recorded(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    payloads: list[dict[str, Any]] = []
    transport, _ = _transport(on_request=lambda r: payloads.append(json.loads(r.content)))
    await _run(tmp_path, cases, transport, concurrency=1, overrides={"cache_mode": "read_only"})
    assert len(payloads) == 2
    for payload in payloads:
        assert payload["overrides"] == {"cache_mode": "read_only"}
    records = _records(tmp_path / "run")
    assert len(records) == 2
    for record in records:
        assert record["request"]["overrides"] == {"cache_mode": "read_only"}
    run_json = json.loads((tmp_path / "run" / "run.json").read_text(encoding="utf-8"))
    assert run_json["overrides"] == {"cache_mode": "read_only"}


def test_read_as_of_date_reads_the_manifest_next_to_the_cases(tmp_path: Path) -> None:
    (tmp_path / "manifest.json").write_text(
        json.dumps({"as_of_date": "2026-10-01", "cases": []}), encoding="utf-8"
    )
    assert read_as_of_date(tmp_path) == "2026-10-01"


@pytest.mark.parametrize(
    "manifest",
    [
        None,
        "{not json",
        "[1, 2]",
        "{}",
        json.dumps({"as_of_date": 20261001}),
        json.dumps({"as_of_date": None}),
    ],
    ids=["missing", "invalid-json", "not-an-object", "no-key", "not-a-string", "null"],
)
def test_read_as_of_date_is_none_without_a_usable_manifest(
    tmp_path: Path, manifest: str | None
) -> None:
    if manifest is not None:
        (tmp_path / "manifest.json").write_text(manifest, encoding="utf-8")
    assert read_as_of_date(tmp_path) is None


def test_the_committed_skeleton_manifest_has_the_as_of_date_the_runner_sends() -> None:
    skeleton = Path(__file__).resolve().parents[2] / "data" / "skeleton"
    assert read_as_of_date(skeleton) == "2026-10-01"


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


@pytest.mark.parametrize("bad_line", CORRUPT_LINES)
def test_cli_resume_on_a_corrupt_run_exits_2_with_the_line_number(
    cli_env: Callable[..., Any], tmp_path: Path, bad_line: str
) -> None:
    out_dir = tmp_path / "out"
    out_dir.mkdir()
    sink = out_dir / "cases.jsonl"
    sink.write_text(
        "\n".join([_good_line("case-001"), bad_line, _good_line("case-002")]) + "\n",
        encoding="utf-8",
    )
    before = sink.read_bytes()
    transport, seen = _transport()
    result = cli_env(transport, "--resume")
    assert result.exit_code == 2, result.output
    assert "line 2" in result.output
    assert "Traceback" not in result.output
    assert seen == []
    assert sink.read_bytes() == before


def test_cli_help_lists_the_documented_options() -> None:
    env = {"NO_COLOR": "1", "COLUMNS": "200", "TERM": "dumb"}
    result = CliRunner().invoke(app, ["run", "--help"], env=env)
    assert result.exit_code == 0
    for option in (
        "--max-cost-usd",
        "--resume",
        "--concurrency",
        "--case",
        "--reserve-usd",
        "--cache-mode",
        "--replicate-salt",
    ):
        assert option in result.output
    assert "api-key" not in result.output.lower()


def test_discover_cases_pairs_pdfs_with_xml(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path, 2)
    assert discover_cases(tmp_path) == cases


def test_cli_sends_the_dataset_as_of_date_as_reference_date(
    cli_env: Callable[..., Any], tmp_path: Path
) -> None:
    (tmp_path / "cases" / "manifest.json").write_text(
        json.dumps({"as_of_date": "2026-10-01"}), encoding="utf-8"
    )
    payloads: list[dict[str, Any]] = []
    transport, _ = _transport("0.01", on_request=lambda r: payloads.append(json.loads(r.content)))
    result = cli_env(transport)
    assert result.exit_code == 0, result.output
    assert [p["reference_date"] for p in payloads] == ["2026-10-01"] * 4


def test_cli_sends_the_replicate_salt_alone_or_with_the_cache_mode(
    cli_env: Callable[..., Any], tmp_path: Path
) -> None:
    payloads: list[dict[str, Any]] = []
    transport, _ = _transport("0.01", on_request=lambda r: payloads.append(json.loads(r.content)))
    result = cli_env(transport, "--replicate-salt", "r2")
    assert result.exit_code == 0, result.output
    assert [p["overrides"] for p in payloads] == [{"replicate_salt": "r2"}] * 4
    assert all(
        r["request"]["overrides"] == {"replicate_salt": "r2"} for r in _records(tmp_path / "out")
    )

    payloads.clear()
    both, _ = _transport("0.01", on_request=lambda r: payloads.append(json.loads(r.content)))
    result = cli_env(
        both, "--cache-mode", "read_only", "--replicate-salt", "r2", "--out", str(tmp_path / "b")
    )
    assert result.exit_code == 0, result.output
    expected = {"cache_mode": "read_only", "replicate_salt": "r2"}
    assert [p["overrides"] for p in payloads] == [expected] * 4


def test_cli_without_override_flags_sends_no_overrides_member(
    cli_env: Callable[..., Any],
) -> None:
    payloads: list[dict[str, Any]] = []
    transport, _ = _transport("0.01", on_request=lambda r: payloads.append(json.loads(r.content)))
    assert cli_env(transport).exit_code == 0
    assert all("overrides" not in p for p in payloads)


@pytest.mark.parametrize(
    "flags",
    [
        ("--cache-mode", "bogus"),
        ("--cache-mode", "READ_ONLY"),
        ("--replicate-salt", ""),
        ("--replicate-salt", "a b"),
        ("--replicate-salt", "../x"),
        ("--replicate-salt", "caf\u00e9"),
        ("--replicate-salt", "a\n"),
        ("--replicate-salt", "s" * 65),
    ],
)
def test_cli_rejects_a_bad_override_before_any_request(
    cli_env: Callable[..., Any], flags: tuple[str, str]
) -> None:
    transport, seen = _transport()
    result = cli_env(transport, *flags)
    assert result.exit_code == 2, result.output
    assert seen == []


def test_cli_accepts_the_salt_length_limits(cli_env: Callable[..., Any], tmp_path: Path) -> None:
    for salt in ("s", "s" * 64):
        transport, _ = _transport("0.01")
        out = tmp_path / f"out-{len(salt)}"
        result = cli_env(transport, "--replicate-salt", salt, "--out", str(out))
        assert result.exit_code == 0, result.output
        assert _records(out)[0]["request"]["overrides"] == {"replicate_salt": salt}


# --- a resumed run keeps the overrides it started with (T-03-11) ---------------------------------


def _line_with_overrides(
    case_id: str, overrides: dict[str, Any] | None, *, version: int = 3
) -> str:
    request: dict[str, Any] = {"pdf_sha256": "0" * 64}
    if version >= 3:
        request["overrides"] = overrides
    record = {
        "record_version": version,
        "case_id": case_id,
        "status": "harness_error",
        "request": request,
        "http": {"status": 500},
        "response": None,
    }
    return json.dumps(record, sort_keys=True)


async def test_resume_with_different_overrides_is_refused_and_leaves_the_file_unchanged(
    tmp_path: Path,
) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    first, _ = _transport()
    await _run(tmp_path, cases, first, overrides={"cache_mode": "read_only"})
    sink = tmp_path / "run" / "cases.jsonl"
    before = sink.read_bytes()

    transport, seen = _transport()
    with pytest.raises(RunConfigMismatchError) as excinfo:
        await _run(tmp_path, cases, transport, resume=True, overrides={"cache_mode": "refresh"})

    message = str(excinfo.value)
    assert "read_only" in message
    assert "refresh" in message
    assert isinstance(excinfo.value, ValueError)
    assert seen == []
    assert sink.read_bytes() == before


async def test_resume_with_the_same_overrides_continues(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 2)
    flaky, _ = _transport(status=lambda case_id: 500 if case_id == "case-001" else 200)
    await _run(tmp_path, cases, flaky, concurrency=1, overrides={"replicate_salt": "r2"})
    healthy, seen = _transport()
    report = await _run(tmp_path, cases, healthy, resume=True, overrides={"replicate_salt": "r2"})
    assert report.harness_errors == 0
    assert seen == ["case-001"]


@pytest.mark.parametrize(
    ("recorded", "current", "mismatch"),
    [
        (None, None, False),
        (None, {}, False),
        ({}, None, False),
        (None, {"cache_mode": "read_only"}, True),
        ({"cache_mode": "read_only"}, None, True),
        ({"cache_mode": "read_only"}, {"cache_mode": "read_only"}, False),
        ({"cache_mode": "read_only"}, {"cache_mode": "read_only", "replicate_salt": "r2"}, True),
    ],
)
async def test_resume_compares_recorded_and_current_overrides(
    tmp_path: Path,
    recorded: dict[str, Any] | None,
    current: dict[str, Any] | None,
    mismatch: bool,
) -> None:
    cases = _make_cases(tmp_path / "cases", 1)
    run_dir = tmp_path / "run"
    run_dir.mkdir()
    (run_dir / "cases.jsonl").write_text(
        _line_with_overrides("case-001", recorded) + "\n", encoding="utf-8"
    )
    transport, seen = _transport()
    if mismatch:
        with pytest.raises(RunConfigMismatchError):
            await _run(tmp_path, cases, transport, resume=True, overrides=current)
        assert seen == []
    else:
        await _run(tmp_path, cases, transport, resume=True, overrides=current)
        assert seen == ["case-001"]


async def test_records_without_request_overrides_count_as_no_overrides(tmp_path: Path) -> None:
    cases = _make_cases(tmp_path / "cases", 1)
    run_dir = tmp_path / "run"
    run_dir.mkdir()
    (run_dir / "cases.jsonl").write_text(
        _line_with_overrides("case-001", None, version=2) + "\n", encoding="utf-8"
    )
    transport, seen = _transport()
    await _run(tmp_path, cases, transport, resume=True)
    assert seen == ["case-001"]
    with pytest.raises(RunConfigMismatchError):
        await _run(tmp_path, cases, transport, resume=True, overrides={"cache_mode": "off"})


def test_cli_resume_with_different_overrides_exits_2(
    cli_env: Callable[..., Any], tmp_path: Path
) -> None:
    transport, _ = _transport(status=lambda case_id: 500 if case_id == "case-001" else 200)
    assert cli_env(transport, "--cache-mode", "read_only").exit_code == 4
    sink = tmp_path / "out" / "cases.jsonl"
    before = sink.read_bytes()
    healthy, seen = _transport()
    result = cli_env(healthy, "--resume", "--cache-mode", "refresh")
    assert result.exit_code == 2, result.output
    assert "read_only" in result.output
    assert "refresh" in result.output
    assert "Traceback" not in result.output
    assert seen == []
    assert sink.read_bytes() == before


def test_the_default_reserve_covers_a_three_attempt_case() -> None:
    assert Decimal("0.25") == runner.DEFAULT_RESERVE_USD


def test_the_cli_reserve_option_defaults_to_the_module_reserve() -> None:
    env = {"NO_COLOR": "1", "COLUMNS": "200", "TERM": "dumb"}
    result = CliRunner().invoke(app, ["run", "--help"], env=env)
    assert result.exit_code == 0
    assert "0.25" in result.output


async def test_the_client_read_timeout_covers_a_full_repair_chain(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    captured: list[httpx2.Timeout] = []
    real_client = httpx2.AsyncClient

    def spy(*args: Any, **kwargs: Any) -> httpx2.AsyncClient:
        captured.append(kwargs["timeout"])
        return real_client(*args, **kwargs)

    monkeypatch.setattr(runner.httpx2, "AsyncClient", spy)
    transport, _ = _transport()
    await _run(tmp_path, _make_cases(tmp_path / "cases", 1), transport)
    assert len(captured) == 1
    assert captured[0].read == 2700.0
    assert captured[0].connect == 5.0
