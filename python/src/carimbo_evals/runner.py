"""Async eval runner: posts each case PDF to the .NET eval endpoint, one JSONL line per case.

The runner stores the verbatim endpoint response so grading never needs the network. It never
serializes request headers, so the eval key cannot reach disk.
"""

from __future__ import annotations

import asyncio
import base64
import hashlib
import json
import secrets
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import httpx2

RECORD_VERSION = 1
CONTRACT_VERSION = "1"
EVAL_PATH = "/eval/extractions"


@dataclass(frozen=True)
class CaseRef:
    case_id: str
    pdf_path: Path
    xml_path: Path


@dataclass(frozen=True)
class RunReport:
    run_id: str
    completed: int
    harness_errors: int


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


async def run_cases(
    cases: list[CaseRef],
    *,
    base_url: str,
    api_key: str,
    out_dir: Path,
    run_id: str,
    concurrency: int = 2,
    transport: httpx2.AsyncBaseTransport | None = None,
) -> RunReport:
    """Run every case against the endpoint, appending one JSONL record per case."""
    out_dir.mkdir(parents=True, exist_ok=True)
    semaphore = asyncio.Semaphore(concurrency)
    lock = asyncio.Lock()
    completed = 0
    harness_errors = 0

    async with httpx2.AsyncClient(
        base_url=base_url,
        timeout=httpx2.Timeout(180.0, connect=5.0),
        transport=transport,
    ) as client:
        with (out_dir / "cases.jsonl").open("a", encoding="utf-8") as sink:

            async def one(case: CaseRef) -> None:
                nonlocal completed, harness_errors
                async with semaphore:
                    record = await _run_one(client, case, run_id, api_key)
                async with lock:
                    sink.write(json.dumps(record, sort_keys=True, ensure_ascii=False) + "\n")
                    sink.flush()
                    if record["status"] == "completed":
                        completed += 1
                    else:
                        harness_errors += 1

            await asyncio.gather(*(one(case) for case in cases))

    return RunReport(run_id=run_id, completed=completed, harness_errors=harness_errors)
