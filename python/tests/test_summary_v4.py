"""EVAL-05: headline rows, slices and field statistics of the run summary (D-13, D-14, D-15)."""

from __future__ import annotations

import json
from decimal import Decimal
from pathlib import Path
from typing import Any

from carimbo_evals.grader import grade_run
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.summary import build_summary, write_summary

REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET = REPO_ROOT / "data" / "dataset"
SKELETON = REPO_ROOT / "data" / "skeleton"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
EXACT = Decimal("0.00")
TOLERANCE = Decimal("0.00")


def _record(case_id: str, invoice: dict[str, Any] | None, *, status: str = "success") -> dict:
    """A stored run record whose outcome carries ``invoice`` (copied from test_grader.py)."""
    raw_output = json.dumps(invoice) if invoice is not None else None
    return {
        "record_version": 1,
        "run_id": "run-v4",
        "case_id": case_id,
        "status": "completed",
        "request": {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "a" * 32},
        "http": {"status": 200, "wall_ms": 1505, "error": None},
        "response": {
            "contract_version": "1",
            "case_id": case_id,
            "trace_id": "a" * 32,
            "effective": {
                "model": "claude-haiku-4-5",
                "prompt_version": "extract-003",
                "schema_sha256": "b" * 64,
                "pricing_version": "pricing-001",
            },
            "outcome": {
                "status": status,
                "invoice": invoice,
                "failure": None if status == "success" else {"kind": status},
                "raw_output": raw_output,
            },
            "usage": {
                "input_tokens": 1000,
                "output_tokens": 100,
                "cache_read_tokens": 0,
                "cache_write_5m_tokens": 0,
                "cache_write_1h_tokens": 0,
            },
            "cost_usd": "0.0123",
            "latency_ms": 1500,
            "stop_reason": "end_turn",
            "model_returned": "claude-haiku-4-5",
            "provider_message_id": "msg_1",
        },
    }


def _truth_invoice(case_id: str) -> dict[str, Any]:
    return invoice_from_xml(DATASET / f"{case_id}.xml")


def _write_run(tmp_path: Path, records: list[dict[str, Any]]) -> Path:
    run_dir = tmp_path / "run"
    run_dir.mkdir()
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for r in records),
        encoding="utf-8",
    )
    return run_dir


def _grade(case_id: str, **changes: Any) -> dict[str, Any]:
    """A minimal case dict as ``grade_case`` returns it, for tests of the aggregation alone."""
    base: dict[str, Any] = {
        "case_id": case_id,
        "status": "success",
        "correct": True,
        "base_case": case_id,
        "split": None,
        "rendering": None,
        "severity": None,
        "barcode_readable": None,
        "case_tags": [],
    }
    return base | changes


def _meta() -> dict[str, Any]:
    return {"run_id": "r", "tolerance": TOLERANCE}


def test_tracer_a_stored_run_reports_its_correct_invoice_rate_with_a_wilson_interval(
    tmp_path: Path,
) -> None:
    ids = ["nfe-0003", "nfe-0005", "nfe-0006"]
    wrong_total = _truth_invoice(ids[2])
    wrong_total["totals"]["invoice_total"] = format(
        Decimal(wrong_total["totals"]["invoice_total"]) + 1, "f"
    )
    records = [
        _record(ids[0], _truth_invoice(ids[0])),
        _record(ids[1], _truth_invoice(ids[1])),
        _record(ids[2], wrong_total),
    ]
    run_dir = _write_run(tmp_path, records)

    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    write_summary(run_dir, summary)
    stored = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))

    expected = {
        "cases": 3,
        "invoices": 3,
        "excluded": 0,
        "correct": 2,
        "rate": 0.6667,
        "ci95": [0.2077, 0.9385],
    }
    assert stored["headline"]["test"]["text"] == expected
    assert stored["overall"] == expected
    assert stored["headline"]["test"]["image"]["cases"] == 0
    assert stored["headline"]["test"]["image"]["rate"] is None
    assert stored["headline"]["test"]["image"]["ci95"] is None
    assert "dev" not in stored["headline"]
    assert "full" not in stored["headline"]  # the run does not hold every manifest case


def test_a_skeleton_run_has_an_empty_headline_and_still_has_overall(tmp_path: Path) -> None:
    truth = invoice_from_xml(SKELETON / "case-001.xml")
    run_dir = _write_run(tmp_path, [_record("case-001", truth)])

    summary = grade_run(run_dir, SKELETON, tolerance=EXACT, schema_path=SCHEMA)

    assert summary["headline"] == {}
    assert summary["overall"]["cases"] == 1
    assert summary["overall"]["correct"] == 1


def test_a_refused_case_stays_in_the_row_and_an_infrastructure_failure_leaves_it() -> None:
    grades = [
        _grade("a", split="test", rendering="text"),
        _grade("b", split="test", rendering="text", status="refused", correct=False),
        _grade("c", split="test", rendering="text", status="truncated", correct=False),
        _grade("d", split="test", rendering="text", status="schema_invalid", correct=False),
        _grade("e", split="test", rendering="text", status="infrastructure_failure", correct=None),
        _grade("f", split="test", rendering="text", status="harness_error", correct=None),
    ]

    summary = build_summary(grades, _meta(), None)
    row = summary["headline"]["test"]["text"]

    assert (row["cases"], row["correct"], row["excluded"]) == (4, 1, 2)
    assert row["rate"] == 0.25
    assert summary["overall"]["cases"] == 4


def test_twins_of_one_base_count_as_two_cases_and_one_invoice() -> None:
    grades = [
        _grade("nfe-1", split="test", rendering="text", base_case="nfe-1"),
        _grade("nfe-1-twin", split="test", rendering="image_clean", base_case="nfe-1"),
        _grade("nfe-2", split="test", rendering="text", base_case="nfe-2"),
    ]

    pooled = build_summary(grades, _meta(), None)["headline"]["test"]["pooled"]

    assert (pooled["cases"], pooled["invoices"]) == (3, 2)
