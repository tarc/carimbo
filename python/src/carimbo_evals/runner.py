"""Async eval runner: posts each case PDF to the .NET eval endpoint, one JSONL line per case.

The runner stores the verbatim endpoint response so grading never needs the network. It never
serializes request headers, so the eval key cannot reach disk.

Money is only ever handled as ``Decimal`` parsed from the endpoint's decimal strings.
"""

from __future__ import annotations

import asyncio
import base64
import contextlib
import hashlib
import json
import os
import secrets
import time
from collections.abc import Callable
from dataclasses import dataclass
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any

import httpx2

from carimbo_evals.money import parse_cost

RECORD_VERSION = 1
CONTRACT_VERSION = "1"
EVAL_PATH = "/eval/extractions"
DEFAULT_MAX_COST_USD = Decimal("1.00")
DEFAULT_RESERVE_USD = Decimal("0.05")
STOPPED_COST_CAP = "cost_cap"
# The eval endpoint answers these before any provider call (request validation, auth, route
# absent, size limit, media type), so a harness error with one of them cost nothing.
NO_PROVIDER_CALL_STATUSES = frozenset({400, 401, 404, 413, 415})
# httpx2 exceptions raised before the request left the client, so the endpoint never saw it.
UNSENT_ERROR_TYPES = frozenset({"ConnectError", "ConnectTimeout", "PoolTimeout"})


class CorruptRunError(ValueError):
    """A ``cases.jsonl`` line that is complete (newline-terminated) but is not a run record."""


@dataclass(frozen=True)
class CaseRef:
    case_id: str
    pdf_path: Path
    xml_path: Path


@dataclass(frozen=True)
class RunReport:
    """Outcome of one ``run_cases`` invocation.

    ``completed``, ``harness_errors`` and ``skipped`` describe this invocation (``skipped`` are
    cases a previous invocation already completed). ``spent_usd`` is the priced spend of the whole
    run, earlier invocations included; unpriced cases are counted in ``unpriced_cases``, not here.
    ``assumed_usd`` is the reserve charged against the cap for unpriced completions and for harness
    errors that may have reached the provider; it is never part of ``spent_usd``.
    """

    run_id: str
    completed: int
    harness_errors: int
    spent_usd: Decimal = Decimal(0)
    assumed_usd: Decimal = Decimal(0)
    unpriced_cases: int = 0
    stopped_reason: str | None = None
    skipped: int = 0


def discover_cases(cases_dir: Path) -> list[CaseRef]:
    """Pair every ``*.pdf`` with the same-stem ``.xml``, sorted by case id."""
    pdfs = sorted(cases_dir.glob("*.pdf"), key=lambda p: p.stem)
    missing = [p.name for p in pdfs if not p.with_suffix(".xml").is_file()]
    if missing:
        raise ValueError(f"PDF without a matching XML in {cases_dir}: {', '.join(missing)}")
    return [CaseRef(p.stem, p, p.with_suffix(".xml")) for p in pdfs]


def new_traceparent() -> tuple[str, str]:
    """Mint a W3C traceparent. Returns ``(traceparent, trace_id)``."""
    trace_id = secrets.token_hex(16)
    while int(trace_id, 16) == 0:  # an all-zero trace id is invalid
        trace_id = secrets.token_hex(16)
    span_id = secrets.token_hex(8)
    while int(span_id, 16) == 0:
        span_id = secrets.token_hex(8)
    return f"00-{trace_id}-{span_id}-01", trace_id


def _parse_body(response: httpx2.Response) -> dict[str, Any] | None:
    try:
        body = response.json()
    except ValueError:
        return None
    return body if isinstance(body, dict) else None


async def _run_one(
    client: httpx2.AsyncClient, case: CaseRef, run_id: str, api_key: str
) -> dict[str, Any]:
    pdf = case.pdf_path.read_bytes()
    traceparent, trace_id = new_traceparent()
    payload = {
        "contract_version": CONTRACT_VERSION,
        "case_id": case.case_id,
        "document": {
            "media_type": "application/pdf",
            "content_base64": base64.b64encode(pdf).decode("ascii"),
        },
    }
    status = "harness_error"
    http_status: int | None = None
    error: str | None = None
    error_type: str | None = None
    body: dict[str, Any] | None = None
    started = time.perf_counter()
    try:
        response = await client.post(
            EVAL_PATH,
            json=payload,
            headers={"X-Api-Key": api_key, "traceparent": traceparent},
        )
        http_status = response.status_code
        body = _parse_body(response)
        if http_status == 200 and body is not None:
            status = "completed"
        elif http_status == 200:
            error = "HTTP 200 without a JSON object body"
        else:
            error = f"HTTP {http_status}"
    except httpx2.HTTPError as exc:
        error_type = type(exc).__name__
        error = f"{error_type}: {exc}"
    wall_ms = round((time.perf_counter() - started) * 1000)
    return {
        "record_version": RECORD_VERSION,
        "run_id": run_id,
        "case_id": case.case_id,
        "status": status,
        "request": {
            "pdf_sha256": hashlib.sha256(pdf).hexdigest(),
            "traceparent": traceparent,
            "trace_id": trace_id,
        },
        "http": {
            "status": http_status,
            "wall_ms": wall_ms,
            "error": error,
            "error_type": error_type,
        },
        "response": body,
    }


def may_have_reached_provider(record: dict[str, Any]) -> bool:
    """Whether a harness-error record may have cost money at the model provider.

    False when the endpoint rejected the request before any provider call (``http.status`` in
    ``NO_PROVIDER_CALL_STATUSES``) or the request never left the client (no status and an
    ``http.error_type`` in ``UNSENT_ERROR_TYPES``). True otherwise, which is the conservative
    default: 5xx, other statuses, HTTP 200 without a JSON body, read or write timeouts, protocol
    errors, and records written before ``error_type`` existed.
    """
    http = record.get("http")
    http = http if isinstance(http, dict) else {}
    status = http.get("status")
    if status in NO_PROVIDER_CALL_STATUSES:
        return False
    return not (status is None and http.get("error_type") in UNSENT_ERROR_TYPES)


def _load_existing(cases_path: Path) -> list[dict[str, Any]]:
    """Every record of an existing ``cases.jsonl``, in file order.

    Every newline-terminated, non-blank line must be a JSON object with a string ``case_id``,
    otherwise ``CorruptRunError`` names the line and the file is left untouched. A torn final line
    (a process killed mid-write, so no newline) is dropped from the file only after the whole file
    validated, so appends start on a clean line boundary.
    """
    records: list[dict[str, Any]] = []
    raw = cases_path.read_bytes()
    *terminated, tail = raw.split(b"\n")
    for number, line in enumerate(terminated, start=1):
        if not line.strip():
            continue
        try:
            record = json.loads(line)
        except ValueError as exc:
            raise _corrupt(cases_path, number, f"invalid JSON: {exc}") from exc
        if not isinstance(record, dict):
            raise _corrupt(cases_path, number, "not a JSON object")
        if not isinstance(record.get("case_id"), str):
            raise _corrupt(cases_path, number, "no string case_id")
        records.append(record)
    if tail:
        with cases_path.open("r+b") as handle:
            handle.truncate(len(raw) - len(tail))
    return records


def _corrupt(cases_path: Path, number: int, reason: str) -> CorruptRunError:
    return CorruptRunError(
        f"{cases_path}: line {number} is not a run record ({reason}); "
        "fix or remove that line, then resume"
    )


def _write_json_atomic(path: Path, payload: dict[str, Any]) -> None:
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(
        json.dumps(payload, sort_keys=True, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    os.replace(tmp, path)


def _now() -> str:
    return datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


async def run_cases(
    cases: list[CaseRef],
    *,
    base_url: str,
    api_key: str,
    out_dir: Path,
    run_id: str,
    concurrency: int = 2,
    max_cost_usd: Decimal = DEFAULT_MAX_COST_USD,
    reserve_usd: Decimal = DEFAULT_RESERVE_USD,
    resume: bool = False,
    transport: httpx2.AsyncBaseTransport | None = None,
    on_record: Callable[[dict[str, Any]], None] | None = None,
) -> RunReport:
    """Run every case against the endpoint, appending one JSONL record per case.

    Dispatch is sequential in case order with at most ``concurrency`` requests in flight. Before
    each dispatch the runner computes ``reserve = max(reserve_usd, largest cost seen so far)`` and
    dispatches only while ``spent + reserve <= max_cost_usd``; otherwise it stops dispatching, lets
    in-flight requests finish and reports ``stopped_reason="cost_cap"``. The cap bounds recorded
    plus assumed spend: a completed case with no usable ``cost_usd`` is assumed to have cost
    ``reserve_usd`` and is counted in ``unpriced_cases``, and so is every harness error that
    ``may_have_reached_provider`` (the endpoint may have called the model before failing). Assumed
    amounts are reported as ``assumed_usd`` and never as spend. In-flight requests can overshoot
    the cap by at most ``concurrency * largest case cost``.

    Without ``resume`` a non-empty ``cases.jsonl`` is refused (``FileExistsError``) so a case can
    never get a second record by accident. With ``resume`` completed cases are skipped (their cost
    still counts against the cap) and harness errors are re-run; every prior record of a selected
    case is charged, the harness errors that may have reached the provider included, not only the
    last record per case.
    """
    out_dir.mkdir(parents=True, exist_ok=True)
    cases_path = out_dir / "cases.jsonl"
    existing: list[dict[str, Any]] = []
    if cases_path.exists() and cases_path.stat().st_size > 0:
        if not resume:
            raise FileExistsError(
                f"{cases_path} already has records; pass --resume to continue the run "
                "or choose another --out"
            )
        existing = _load_existing(cases_path)

    started_at = _now()
    spent = Decimal(0)
    assumed = Decimal(0)  # reserve charged for unpriced or possibly-paid cases; not spend
    largest = Decimal(0)
    unpriced = 0
    completed = 0
    harness_errors = 0
    skipped = 0
    stopped_reason: str | None = None

    def account(record: dict[str, Any]) -> None:
        nonlocal spent, assumed, largest, unpriced
        response = record.get("response")
        cost = parse_cost(response.get("cost_usd")) if isinstance(response, dict) else None
        if cost is None:
            unpriced += 1
            assumed += reserve_usd
        else:
            spent += cost
            largest = max(largest, cost)

    def charge_harness_error(record: dict[str, Any]) -> None:
        nonlocal assumed
        if may_have_reached_provider(record):
            assumed += reserve_usd

    def charge(record: dict[str, Any]) -> None:
        if record.get("status") == "completed":
            account(record)
        else:
            charge_harness_error(record)

    last_by_case = {record["case_id"]: record for record in existing}
    selected_ids = {case.case_id for case in cases}
    to_run: list[CaseRef] = []
    for case in cases:
        prior = last_by_case.get(case.case_id)
        if prior is not None and prior.get("status") == "completed":
            skipped += 1
        else:
            to_run.append(case)
    for record in existing:
        if record["case_id"] in selected_ids:
            charge(record)

    in_flight: set[asyncio.Task[None]] = set()

    with cases_path.open("a", encoding="utf-8") as sink:

        async def one(case: CaseRef) -> None:
            nonlocal completed, harness_errors
            record = await _run_one(client, case, run_id, api_key)
            # No await below: the record is written and accounted for atomically, so a
            # cancellation can never leave a partial line.
            sink.write(json.dumps(record, sort_keys=True, ensure_ascii=False) + "\n")
            sink.flush()
            if record["status"] == "completed":
                completed += 1
                account(record)
            else:
                harness_errors += 1
                charge_harness_error(record)
            if on_record is not None:
                on_record(record)

        async with httpx2.AsyncClient(
            base_url=base_url,
            timeout=httpx2.Timeout(180.0, connect=5.0),
            transport=transport,
        ) as client:
            try:
                for case in to_run:
                    while len(in_flight) >= concurrency:
                        done, _ = await asyncio.wait(in_flight, return_when=asyncio.FIRST_COMPLETED)
                        in_flight -= done
                        for finished in done:
                            finished.result()
                    reserve = max(reserve_usd, largest)
                    if spent + assumed + reserve > max_cost_usd:
                        stopped_reason = STOPPED_COST_CAP
                        break
                    in_flight.add(asyncio.create_task(one(case)))
                while in_flight:
                    done, _ = await asyncio.wait(in_flight, return_when=asyncio.FIRST_COMPLETED)
                    in_flight -= done
                    for finished in done:
                        finished.result()
            finally:
                for task in in_flight:
                    task.cancel()
                for task in in_flight:
                    with contextlib.suppress(asyncio.CancelledError):
                        await task

    report = RunReport(
        run_id=run_id,
        completed=completed,
        harness_errors=harness_errors,
        spent_usd=spent,
        assumed_usd=assumed,
        unpriced_cases=unpriced,
        stopped_reason=stopped_reason,
        skipped=skipped,
    )
    _write_json_atomic(
        out_dir / "run.json",
        {
            "run_id": run_id,
            "base_url": base_url,
            "cases_dir": str(cases[0].pdf_path.parent) if cases else None,
            "case_ids": [case.case_id for case in cases],
            "concurrency": concurrency,
            "max_cost_usd": str(max_cost_usd),
            "spent_usd": str(spent),
            "assumed_usd": str(assumed),
            "unpriced_cases": unpriced,
            "stopped_reason": stopped_reason,
            "completed": completed,
            "harness_errors": harness_errors,
            "skipped": skipped,
            "started_at": started_at,
            "finished_at": _now(),
        },
    )
    return report
