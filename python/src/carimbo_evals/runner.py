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
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any

import httpx2

from carimbo_evals.money import parse_cost

RECORD_VERSION = 3
CONTRACT_VERSION = "3"
MANIFEST_NAME = "manifest.json"
EVAL_PATH = "/eval/extractions"
DECISION_PATH = "/eval/decisions"
DECISION_RECORD_VERSION = 1
DECISION_CONTRACT_VERSION = "decision-1"
DEFAULT_MAX_COST_USD = Decimal("1.00")
DEFAULT_RESERVE_USD = Decimal("0.25")
STOPPED_COST_CAP = "cost_cap"
# The eval endpoint answers these before any provider call (request validation, auth, route
# absent, size limit, media type), so a harness error with one of them cost nothing.
NO_PROVIDER_CALL_STATUSES = frozenset({400, 401, 404, 413, 415})
# httpx2 exceptions raised before the request left the client, so the endpoint never saw it.
UNSENT_ERROR_TYPES = frozenset({"ConnectError", "ConnectTimeout", "PoolTimeout"})


class CorruptRunError(ValueError):
    """A ``cases.jsonl`` line that is complete (newline-terminated) but is not a run record."""


class RunConfigMismatchError(ValueError):
    """A resumed run whose existing records carry other ``overrides`` than the current flags."""


@dataclass(frozen=True)
class CaseRef:
    case_id: str
    pdf_path: Path
    xml_path: Path


@dataclass(frozen=True)
class DecisionCaseRef:
    """One case of the decision dataset: ``<cases_dir>/cases/<case_id>.json`` (D-36)."""

    case_id: str
    case_path: Path


@dataclass(frozen=True)
class RunSpec:
    """What differs between an extraction run and a decision run (RESEARCH Q6, phase D-19).

    ``run_cases`` owns the cap, accounting, resume and torn-line repair for every spec. A spec only
    says where to post, how to build one request, which members describe it in the record, and
    how long one case may take. ``record_kind`` is written to the record only when it is set, so
    extraction records keep their record_version 3 shape.
    """

    path: str
    record_version: int
    record_kind: str | None
    timeout_s: float
    payload: Callable[[Any, str | None, dict[str, Any] | None], dict[str, Any]]
    # The record's ``request`` members apart from the traceparent and trace id the runner mints.
    request_info: Callable[[Any, str | None, dict[str, Any] | None], dict[str, Any]]
    cases_dir: Callable[[Sequence[Any]], str | None]


@dataclass(frozen=True)
class RunReport:
    """Outcome of one ``run_cases`` invocation.

    ``completed``, ``harness_errors`` and ``skipped`` describe this invocation (``skipped`` are
    cases a previous invocation already completed). ``spent_usd`` is the incurred spend of the
    whole run, earlier invocations included: what the provider was actually paid, zero for a cache
    replay. ``notional_usd`` is what the same calls cost when first made (a replay reports the
    original cost), so a replay shows spent 0 beside a positive notional cost. Unpriced cases are
    counted in ``unpriced_cases``, not in either total. ``assumed_usd`` is the reserve charged
    against the cap for unpriced completions and for harness errors that may have reached the
    provider; it is never part of ``spent_usd``.
    """

    run_id: str
    completed: int
    harness_errors: int
    spent_usd: Decimal = Decimal(0)
    assumed_usd: Decimal = Decimal(0)
    unpriced_cases: int = 0
    stopped_reason: str | None = None
    skipped: int = 0
    notional_usd: Decimal = Decimal(0)


def discover_cases(cases_dir: Path) -> list[CaseRef]:
    """Pair every ``*.pdf`` with the same-stem ``.xml``, sorted by case id."""
    pdfs = sorted(cases_dir.glob("*.pdf"), key=lambda p: p.stem)
    missing = [p.name for p in pdfs if not p.with_suffix(".xml").is_file()]
    if missing:
        raise ValueError(f"PDF without a matching XML in {cases_dir}: {', '.join(missing)}")
    return [CaseRef(p.stem, p, p.with_suffix(".xml")) for p in pdfs]


def read_as_of_date(cases_dir: Path) -> str | None:
    """The dataset ``as_of_date`` (D-11): the reference date the validators use for these cases.

    Read from ``manifest.json`` next to the cases. ``None`` when there is no manifest or it has no
    string ``as_of_date``; the endpoint then falls back to its own UTC date.
    """
    manifest_path = cases_dir / MANIFEST_NAME
    if not manifest_path.is_file():
        return None
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    if not isinstance(manifest, dict):
        return None
    as_of = manifest.get("as_of_date")
    return as_of if isinstance(as_of, str) else None


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


def build_payload(
    case_id: str,
    pdf: bytes,
    reference_date: str | None,
    overrides: Mapping[str, Any] | None,
) -> dict[str, Any]:
    """The request body of eval contract 3 for one case.

    ``reference_date`` and ``overrides`` are omitted when ``None`` (``overrides`` also when empty),
    so the host defaults apply. ``data/contract/eval-request.json`` is checked against this builder
    (D-32).
    """
    payload: dict[str, Any] = {
        "contract_version": CONTRACT_VERSION,
        "case_id": case_id,
        "document": {
            "media_type": "application/pdf",
            "content_base64": base64.b64encode(pdf).decode("ascii"),
        },
    }
    if reference_date is not None:
        payload["reference_date"] = reference_date
    if overrides:
        payload["overrides"] = dict(overrides)
    return payload


def discover_decision_cases(cases_dir: Path) -> list[DecisionCaseRef]:
    """The cases listed in ``<cases_dir>/manifest.json``, in manifest order (D-36).

    Raises ``ValueError`` naming the file when the manifest or a listed ``cases/<id>.json`` is
    absent, or when the manifest has no usable ``cases`` list.
    """
    manifest_path = cases_dir / MANIFEST_NAME
    if not manifest_path.is_file():
        raise ValueError(f"{manifest_path} not found")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ValueError(f"{manifest_path} is not readable JSON: {exc}") from exc
    entries = manifest.get("cases") if isinstance(manifest, dict) else None
    if not isinstance(entries, list):
        raise ValueError(f"{manifest_path} has no cases list")
    cases: list[DecisionCaseRef] = []
    for entry in entries:
        case_id = entry.get("case_id") if isinstance(entry, dict) else None
        if not isinstance(case_id, str):
            raise ValueError(f"{manifest_path} has a case without a string case_id")
        case_path = cases_dir / "cases" / f"{case_id}.json"
        if not case_path.is_file():
            raise ValueError(f"{case_path} not found (listed in {manifest_path})")
        cases.append(DecisionCaseRef(case_id, case_path))
    return cases


def build_decision_payload(case_id: str, overrides: Mapping[str, Any] | None) -> dict[str, Any]:
    """The request body of eval contract decision-1 for one case (D-36).

    ``overrides`` is omitted when ``None`` or empty. There is no document and no reference date:
    the endpoint loads the case itself.
    """
    payload: dict[str, Any] = {"contract_version": DECISION_CONTRACT_VERSION, "case_id": case_id}
    if overrides:
        payload["overrides"] = dict(overrides)
    return payload


def _extraction_payload(
    case: CaseRef, reference_date: str | None, overrides: dict[str, Any] | None
) -> dict[str, Any]:
    return build_payload(case.case_id, case.pdf_path.read_bytes(), reference_date, overrides)


def _extraction_request_info(
    case: CaseRef, reference_date: str | None, overrides: dict[str, Any] | None
) -> dict[str, Any]:
    return {
        "pdf_sha256": hashlib.sha256(case.pdf_path.read_bytes()).hexdigest(),
        "reference_date": reference_date,
        "overrides": overrides,
    }


def _decision_payload(
    case: DecisionCaseRef, reference_date: str | None, overrides: dict[str, Any] | None
) -> dict[str, Any]:
    return build_decision_payload(case.case_id, overrides)


def _decision_request_info(
    case: DecisionCaseRef, reference_date: str | None, overrides: dict[str, Any] | None
) -> dict[str, Any]:
    return {
        "case_sha256": hashlib.sha256(case.case_path.read_bytes()).hexdigest(),
        "overrides": overrides,
    }


EXTRACTION_SPEC = RunSpec(
    path=EVAL_PATH,
    record_version=RECORD_VERSION,
    record_kind=None,
    # D-28: one case may take (max_repairs + 1) x (MaxRetries + 1) x 300 s = 3 x 3 x 300 s
    # = 2700 s at the defaults, because the endpoint answers once per case.
    timeout_s=2700.0,
    payload=_extraction_payload,
    request_info=_extraction_request_info,
    cases_dir=lambda cases: str(cases[0].pdf_path.parent) if cases else None,
)

DECISION_SPEC = RunSpec(
    path=DECISION_PATH,
    record_version=DECISION_RECORD_VERSION,
    record_kind="decision",
    # One case may take Agent:MaxSteps x (MaxRetries + 1) x 300 s = 6 x 3 x 300 s = 5400 s at
    # the defaults, because the endpoint answers once per case after the whole agent loop.
    timeout_s=5400.0,
    payload=_decision_payload,
    request_info=_decision_request_info,
    cases_dir=lambda cases: str(cases[0].case_path.parent.parent) if cases else None,
)


async def _run_one(
    client: httpx2.AsyncClient,
    case: Any,
    run_id: str,
    api_key: str,
    reference_date: str | None = None,
    overrides: dict[str, Any] | None = None,
    spec: RunSpec = EXTRACTION_SPEC,
) -> dict[str, Any]:
    traceparent, trace_id = new_traceparent()
    overrides = overrides or None
    payload = spec.payload(case, reference_date, overrides)
    request_info = spec.request_info(case, reference_date, overrides)
    status = "harness_error"
    http_status: int | None = None
    error: str | None = None
    error_type: str | None = None
    body: dict[str, Any] | None = None
    started = time.perf_counter()
    try:
        response = await client.post(
            spec.path,
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
    record: dict[str, Any] = {
        "record_version": spec.record_version,
        "run_id": run_id,
        "case_id": case.case_id,
        "status": status,
        "request": {**request_info, "traceparent": traceparent, "trace_id": trace_id},
        "http": {
            "status": http_status,
            "wall_ms": wall_ms,
            "error": error,
            "error_type": error_type,
        },
        "response": body,
    }
    if spec.record_kind is not None:
        record["record_kind"] = spec.record_kind
    return record


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


def _recorded_overrides(record: dict[str, Any]) -> dict[str, Any] | None:
    """The overrides a record was requested with; none for records without ``request.overrides``."""
    request = record.get("request")
    recorded = request.get("overrides") if isinstance(request, dict) else None
    return recorded if isinstance(recorded, dict) and recorded else None


def _require_same_overrides(
    cases_path: Path, existing: list[dict[str, Any]], overrides: dict[str, Any] | None
) -> None:
    """Refuse to mix cache configurations inside one run directory (T-03-11).

    Nothing has been written when this raises. A missing, null or empty ``request.overrides``
    (record_version 2 included) and an empty current dict all mean "no overrides".
    """
    for record in existing:
        recorded = _recorded_overrides(record)
        if recorded != (overrides or None):
            raise RunConfigMismatchError(
                f"{cases_path}: its records were requested with overrides "
                f"{json.dumps(recorded, sort_keys=True)} but this run uses "
                f"{json.dumps(overrides or None, sort_keys=True)}; resume with the same "
                "flags or choose another --out"
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
    cases: Sequence[Any],
    *,
    base_url: str,
    api_key: str,
    out_dir: Path,
    run_id: str,
    concurrency: int = 2,
    max_cost_usd: Decimal = DEFAULT_MAX_COST_USD,
    reserve_usd: Decimal = DEFAULT_RESERVE_USD,
    resume: bool = False,
    reference_date: str | None = None,
    overrides: dict[str, Any] | None = None,
    transport: httpx2.AsyncBaseTransport | None = None,
    on_record: Callable[[dict[str, Any]], None] | None = None,
    spec: RunSpec = EXTRACTION_SPEC,
) -> RunReport:
    """Run every case against the endpoint, appending one JSONL record per case.

    Dispatch is sequential in case order with at most ``concurrency`` requests in flight. Before
    each dispatch the runner computes ``reserve = max(reserve_usd, largest cost seen so far)`` and
    dispatches only while ``spent + reserve <= max_cost_usd``; otherwise it stops dispatching, lets
    in-flight requests finish and reports ``stopped_reason="cost_cap"``. The cap bounds recorded
    plus assumed spend, and recorded spend is the response's ``incurred_cost_usd`` (``cost_usd``
    for a record that has none), so replaying cached cases never stops at the cap while their
    notional cost is still reported as ``notional_usd`` (D-29). A completed case with no usable
    incurred cost is assumed to have cost ``reserve_usd`` and is counted in ``unpriced_cases``,
    and so is every harness error that
    ``may_have_reached_provider`` (the endpoint may have called the model before failing). Assumed
    amounts are reported as ``assumed_usd`` and never as spend. In-flight requests can overshoot
    the cap by at most ``concurrency * largest case cost``.

    Without ``resume`` a non-empty ``cases.jsonl`` is refused (``FileExistsError``) so a case can
    never get a second record by accident. With ``resume`` completed cases are skipped (their cost
    still counts against the cap) and harness errors are re-run; every prior record of a selected
    case is charged, the harness errors that may have reached the provider included, not only the
    last record per case.

    ``reference_date`` (YYYY-MM-DD, normally the dataset ``as_of_date``) is sent with every
    request and stored under ``request.reference_date``; ``None`` leaves the choice to the endpoint.

    ``spec`` (default ``EXTRACTION_SPEC``) selects the endpoint, the record shape and the
    per-case timeout: ``DECISION_SPEC`` runs the decision cases (``DecisionCaseRef``) through the
    same cap, accounting and resume code.

    ``overrides`` (D-29, for example ``{"cache_mode": "read_only"}``) is sent as the request's
    ``overrides`` member, stored under ``request.overrides`` of every record and in ``run.json``;
    ``None`` or an empty dict sends no ``overrides`` member and the host defaults apply.
    """
    overrides = overrides or None
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
        _require_same_overrides(cases_path, existing, overrides)

    started_at = _now()
    spent = Decimal(0)
    notional = Decimal(0)  # what the same calls cost when first made; not what this run paid
    assumed = Decimal(0)  # reserve charged for unpriced or possibly-paid cases; not spend
    largest = Decimal(0)
    unpriced = 0
    completed = 0
    harness_errors = 0
    skipped = 0
    stopped_reason: str | None = None

    def account(record: dict[str, Any]) -> None:
        nonlocal spent, notional, assumed, largest, unpriced
        response = record.get("response")
        response = response if isinstance(response, dict) else {}
        notional_cost = parse_cost(response.get("cost_usd"))
        if notional_cost is not None:
            notional += notional_cost
        # incurred_cost_usd wins when the key is present, null included (unknown: charged the
        # reserve); records of contract 2 have no such key and are accounted on cost_usd.
        cost = parse_cost(
            response["incurred_cost_usd"]
            if "incurred_cost_usd" in response
            else response.get("cost_usd")
        )
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
    to_run: list[Any] = []
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

        async def one(case: Any) -> None:
            nonlocal completed, harness_errors
            record = await _run_one(client, case, run_id, api_key, reference_date, overrides, spec)
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
            # The per-case timeout and its derivation live on the spec (D-28 for extraction).
            timeout=httpx2.Timeout(spec.timeout_s, connect=5.0),
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
        notional_usd=notional,
    )
    _write_json_atomic(
        out_dir / "run.json",
        {
            "run_id": run_id,
            "base_url": base_url,
            "cases_dir": spec.cases_dir(cases),
            "case_ids": [case.case_id for case in cases],
            "concurrency": concurrency,
            "reference_date": reference_date,
            "overrides": overrides,
            "max_cost_usd": str(max_cost_usd),
            # "f" format: a Decimal sum of zeros with a scale would print as 0E-8.
            "spent_usd": format(spent, "f"),
            "notional_usd": format(notional, "f"),
            "assumed_usd": format(assumed, "f"),
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
