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
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any

import httpx2

RECORD_VERSION = 1
CONTRACT_VERSION = "1"
EVAL_PATH = "/eval/extractions"
DEFAULT_MAX_COST_USD = Decimal("1.00")
DEFAULT_RESERVE_USD = Decimal("0.05")
STOPPED_COST_CAP = "cost_cap"


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
    """

    run_id: str
    completed: int
    harness_errors: int
    spent_usd: Decimal = Decimal(0)
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


def parse_cost(value: object) -> Decimal | None:
    """Parse the endpoint's ``cost_usd`` decimal string; ``None`` when absent or unusable.

    Only strings are accepted: a JSON number would already have been through a float.
    """
    if not isinstance(value, str):
        return None
    try:
        cost = Decimal(value)
    except InvalidOperation:
        return None
    return cost if cost.is_finite() and cost >= 0 else None


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
        error = f"{type(exc).__name__}: {exc}"
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
        "http": {"status": http_status, "wall_ms": wall_ms, "error": error},
        "response": body,
    }


def _load_existing(cases_path: Path) -> dict[str, dict[str, Any]]:
    """Last record per case id from an existing ``cases.jsonl``.

    A torn final line (a process killed mid-write) is dropped from the file so appends start on a
    clean line boundary.
    """
    records: dict[str, dict[str, Any]] = {}
    raw = cases_path.read_bytes()
    keep = 0
    for line in raw.splitlines(keepends=True):
        if not line.endswith(b"\n"):
            break
        keep += len(line)
        if line.strip():
            record = json.loads(line)
            records[record["case_id"]] = record
    if keep != len(raw):
        with cases_path.open("r+b") as handle:
            handle.truncate(keep)
    return records


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
    in-flight requests finish and reports ``stopped_reason="cost_cap"``. A completed case with no
    usable ``cost_usd`` is assumed to have cost ``reserve_usd`` for the cap and is counted in
    ``unpriced_cases``. In-flight requests can overshoot the cap by at most
    ``concurrency * largest case cost``.

    Without ``resume`` a non-empty ``cases.jsonl`` is refused (``FileExistsError``) so a case can
    never get a second record by accident. With ``resume`` the last record per case is kept,
    completed cases are skipped (their cost still counts against the cap) and harness errors are
    re-run.
    """
    out_dir.mkdir(parents=True, exist_ok=True)
    cases_path = out_dir / "cases.jsonl"
    existing: dict[str, dict[str, Any]] = {}
    if cases_path.exists() and cases_path.stat().st_size > 0:
        if not resume:
            raise FileExistsError(
                f"{cases_path} already has records; pass --resume to continue the run "
                "or choose another --out"
            )
        existing = _load_existing(cases_path)

    started_at = _now()
    spent = Decimal(0)
    assumed = Decimal(0)  # reserve charged for unpriced cases; counts for the cap, not for spend
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

    to_run: list[CaseRef] = []
    for case in cases:
        prior = existing.get(case.case_id)
        if prior is not None and prior.get("status") == "completed":
            skipped += 1
            account(prior)
        else:
            to_run.append(case)

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
