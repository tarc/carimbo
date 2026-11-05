"""EVAL-03: whole-invoice grading with rows matched by optimal assignment (D-42).

The tracer test runs real dataset cases from stored records through ``grade_run`` to
``summary.json``. The remaining tests pin the assignment and the column rules.
"""

from __future__ import annotations

import copy
import json
import subprocess
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from carimbo_evals.grader import grade_run
from carimbo_evals.matching import (
    ITEM_MINIMUM,
    ITEM_WEIGHTS,
    Assignment,
    assign_rows,
    diff_rows,
)
from carimbo_evals.summary import write_summary

REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET = REPO_ROOT / "data" / "dataset"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
EXACT = Decimal("0.00")


def _record(case_id: str, invoice: dict[str, Any] | None, *, status: str = "success") -> dict:
    """A stored run record whose outcome carries ``invoice`` (copied from test_grader.py)."""
    raw_output = json.dumps(invoice) if invoice is not None else None
    return {
        "record_version": 1,
        "run_id": "run-match",
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
    from carimbo_evals.ground_truth import invoice_from_xml

    return invoice_from_xml(DATASET / f"{case_id}.xml")


def _case_with_items(minimum: int) -> str:
    manifest = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
    for case in manifest["cases"]:
        if case["expected"]["item_count"] >= minimum:
            return case["case_id"]
    raise AssertionError("no dataset case with enough items")


def _write_run(tmp_path: Path, records: list[dict[str, Any]]) -> Path:
    run_dir = tmp_path / "run"
    run_dir.mkdir()
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for r in records),
        encoding="utf-8",
    )
    return run_dir


def test_tracer_a_dropped_row_is_not_correct_and_the_untouched_invoice_is(tmp_path: Path) -> None:
    untouched_id = "nfe-0001"
    dropped_id = _case_with_items(4)
    assert dropped_id != untouched_id
    dropped = _truth_invoice(dropped_id)
    middle = len(dropped["items"]) // 2
    del dropped["items"][middle]
    records = [
        _record(untouched_id, _truth_invoice(untouched_id)),
        _record(dropped_id, dropped),
    ]
    run_dir = _write_run(tmp_path, records)

    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    write_summary(run_dir, summary)
    stored = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    by_id = {case["case_id"]: case for case in stored["cases"]}

    assert by_id[untouched_id]["correct"] is True
    assert by_id[untouched_id]["wrong"] == []
    assert by_id[dropped_id]["correct"] is False
    wrong = {(w["path"], w["event"]) for w in by_id[dropped_id]["wrong"]}
    assert (f"items[{middle}].total", "missing") in wrong
    assert ("item_count", None) in wrong


# --- assign_rows ----------------------------------------------------------------------------


def _item(**changes: str) -> dict[str, str]:
    row = {
        "code": "P-100",
        "description": "Parafuso sextavado",
        "ncm": "73181500",
        "cst_csosn": "00",
        "cfop": "5102",
        "unit": "UN",
        "quantity": "10.0000",
        "unit_price": "2.5000",
        "total": "25.00",
        "icms_base": "25.00",
        "icms_rate": "18.00",
        "icms_amount": "4.50",
        "ipi_rate": "0.00",
        "ipi_amount": "0.00",
    }
    row.update(changes)
    return row


def test_a_pair_scoring_five_is_not_matched_and_a_pair_scoring_six_is() -> None:
    truth = [_item()]
    five = _item(description="outro", ncm="1", unit="X", quantity="1", unit_price="1")
    five["total"] = "25.00"  # code 3 + total 2 = 5
    assert assign_rows([five], truth, ITEM_WEIGHTS, ITEM_MINIMUM).pairs == (), (
        "a score of 5 stays unmatched"
    )
    six = _item(ncm="1", unit="X", quantity="1", unit_price="1", total="1")  # code 3 + desc 3
    assigned = assign_rows([six], truth, ITEM_WEIGHTS, ITEM_MINIMUM)
    assert assigned.pairs == ((0, 0),)
    assert assigned.unmatched_true == ()
    assert assigned.unmatched_predicted == ()


def test_no_rows_on_either_side_is_an_empty_assignment_that_needs_no_scipy(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # A None entry makes any import of scipy.optimize raise, so this fails if the call imports it.
    monkeypatch.setitem(sys.modules, "scipy.optimize", None)
    assert assign_rows([], [], ITEM_WEIGHTS, ITEM_MINIMUM) == Assignment((), (), ())
    diff = diff_rows("item", [], [], EXACT)
    assert diff.wrong == []
    assert diff.assignment == Assignment((), (), ())
    assert diff.stats["matched"] == 0


def test_importing_the_matching_module_does_not_import_scipy() -> None:
    code = (
        "import sys; import carimbo_evals.matching; "
        "sys.exit(1 if any(m == 'scipy' or m.startswith('scipy.') for m in sys.modules) else 0)"
    )
    assert subprocess.run([sys.executable, "-I", "-c", code], check=False).returncode == 0


def test_a_prediction_that_is_not_a_list_marks_every_true_row_missing() -> None:
    truth = [_item(), _item(code="P-200", total="9.00")]
    for prediction in (None, "rows", {"code": "P-100"}, [], [None, 3, "x"]):
        diff = diff_rows("item", prediction, truth, EXACT)
        assert [(w["path"], w["event"]) for w in diff.wrong] == [
            ("items[0].total", "missing"),
            ("items[1].total", "missing"),
        ]
        assert diff.assignment.unmatched_true == (0, 1)


def test_a_single_row_invoice_matches_itself() -> None:
    diff = diff_rows("item", [_item()], [_item()], EXACT)
    assert diff.assignment.pairs == ((0, 0),)
    assert diff.wrong == []
    assert diff.stats["rows_all_correct"] == 1
    assert diff.stats["order_preserved"] is True


def test_repeated_calls_give_identical_pairs_and_wrong_lists() -> None:
    truth = [_item(code=f"P-{n}", description=f"item {n}", total=f"{n}.00") for n in range(5)]
    predicted = copy.deepcopy(truth[::-1])
    predicted[1]["quantity"] = "99.0000"
    first = diff_rows("item", predicted, truth, EXACT)
    second = diff_rows("item", copy.deepcopy(predicted), copy.deepcopy(truth), EXACT)
    assert first == second
    assert [w["path"] for w in first.wrong] == ["items[3].quantity"]
