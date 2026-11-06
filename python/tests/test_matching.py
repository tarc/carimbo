"""EVAL-03: whole-invoice grading with rows matched by optimal assignment (D-42).

The tracer test runs real dataset cases from stored records through ``grade_run`` to
``summary.json``. The remaining tests pin the assignment and the column rules.
"""

from __future__ import annotations

import copy
import functools
import json
import random
import subprocess
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from carimbo_evals.grader import grade_run
from carimbo_evals.matching import (
    ITEM_COLUMNS,
    ITEM_MINIMUM,
    ITEM_WEIGHTS,
    Assignment,
    assign_rows,
    diff_rows,
    row_score,
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
    # A usable run.json, as run_cases writes it: without one the run grades as incomplete (D-49).
    (run_dir / "run.json").write_text(
        json.dumps({"case_ids": sorted({r["case_id"] for r in records}), "stopped_reason": None}),
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


# --- proofs over the 154 cases of dataset-001 -----------------------------------------------


@functools.cache
def _dataset() -> tuple[tuple[str, tuple[str, ...], dict[str, Any]], ...]:
    """``(case_id, tags, ground-truth invoice)`` for every manifest case; twins share XML bytes."""
    manifest = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
    by_hash: dict[str, dict[str, Any]] = {}
    cases = []
    for case in manifest["cases"]:
        invoice = by_hash.get(case["xml_sha256"])
        if invoice is None:
            invoice = by_hash[case["xml_sha256"]] = _truth_invoice(case["case_id"])
        cases.append((case["case_id"], tuple(case["tags"]), invoice))
    return tuple(cases)


def _lists(invoice: dict[str, Any]) -> tuple[tuple[str, list[dict[str, Any]]], ...]:
    return (("item", invoice["items"]), ("installment", invoice["installments"]))


def test_the_dataset_has_154_cases_with_141_of_at_least_two_rows() -> None:
    cases = _dataset()
    assert len(cases) == 154
    assert sum(1 for _, _, invoice in cases if len(invoice["items"]) >= 2) == 141
    assert sum(1 for _, _, invoice in cases if len(invoice["installments"]) == 0) == 86


def test_ground_truth_against_itself_pairs_the_diagonal_with_no_wrong_value() -> None:
    for case_id, _, invoice in _dataset():
        for kind, rows in _lists(invoice):
            diff = diff_rows(kind, copy.deepcopy(rows), rows, EXACT)
            assert diff.assignment.pairs == tuple((n, n) for n in range(len(rows))), case_id
            assert diff.wrong == [], case_id
            assert diff.stats["order_preserved"] is True, case_id


def test_a_reversed_ground_truth_has_no_wrong_value_in_154_of_154_cases() -> None:
    order_broken = 0
    for case_id, _, invoice in _dataset():
        for kind, rows in _lists(invoice):
            reversed_rows = copy.deepcopy(rows[::-1])
            diff = diff_rows(kind, reversed_rows, rows, EXACT)
            last = len(rows) - 1
            assert dict(diff.assignment.pairs) == {n: last - n for n in range(len(rows))}, case_id
            assert diff.wrong == [], case_id
            assert diff.stats["order_preserved"] is (len(rows) < 2), case_id
            if kind == "item" and len(rows) >= 2:
                order_broken += 1
    assert order_broken == 141


def test_a_seeded_shuffle_has_no_wrong_value_in_154_of_154_cases() -> None:
    reordered = 0
    for number, (case_id, _, invoice) in enumerate(_dataset()):
        rng = random.Random(20261105 + number)
        for kind, rows in _lists(invoice):
            order = list(range(len(rows)))
            rng.shuffle(order)
            predicted = [copy.deepcopy(rows[k]) for k in order]
            diff = diff_rows(kind, predicted, rows, EXACT)
            assert dict(diff.assignment.pairs) == dict(enumerate(order)), case_id
            assert diff.wrong == [], case_id
            changed = order != sorted(order)
            assert diff.stats["order_preserved"] is not changed, case_id
            reordered += int(kind == "item" and changed)
    assert reordered > 100


def test_one_deleted_row_leaves_exactly_that_true_row_unmatched_in_141_cases() -> None:
    checked = 0
    for case_id, _, invoice in _dataset():
        items = invoice["items"]
        if len(items) < 2:
            continue
        gone = len(items) // 2
        predicted = copy.deepcopy(items[:gone] + items[gone + 1 :])
        diff = diff_rows("item", predicted, items, EXACT)
        assert len(diff.assignment.pairs) == len(items) - 1, case_id
        assert diff.assignment.unmatched_true == (gone,), case_id
        assert diff.assignment.unmatched_predicted == (), case_id
        assert [(w["path"], w["event"]) for w in diff.wrong] == [
            (f"items[{gone}].total", "missing")
        ], case_id
        checked += 1
    assert checked == 141


def test_one_duplicated_row_leaves_exactly_one_predicted_row_unmatched_in_154_cases() -> None:
    for case_id, _, invoice in _dataset():
        items = invoice["items"]
        predicted = [*copy.deepcopy(items), copy.deepcopy(items[0])]
        diff = diff_rows("item", predicted, items, EXACT)
        assert len(diff.assignment.pairs) == len(items), case_id
        assert diff.assignment.unmatched_predicted == (len(items),), case_id
        assert diff.assignment.unmatched_true == (), case_id
        assert [(w["path"], w["event"]) for w in diff.wrong] == [
            (f"items[+{len(items)}].total", "extra")
        ], case_id


def test_a_dropped_installment_leaves_the_others_paired_in_every_case_with_two() -> None:
    checked = 0
    for case_id, _, invoice in _dataset():
        installments = invoice["installments"]
        if len(installments) < 2:
            continue
        gone = len(installments) // 2
        predicted = copy.deepcopy(installments[:gone] + installments[gone + 1 :])
        diff = diff_rows("installment", predicted, installments, EXACT)
        assert len(diff.assignment.pairs) == len(installments) - 1, case_id
        assert diff.assignment.unmatched_true == (gone,), case_id
        assert [w["path"] for w in diff.wrong] == [f"installments[{gone}].amount"], case_id
        checked += 1
    assert checked > 0


def _partner_pairs() -> list[tuple[str, tuple[str, ...], list[dict[str, Any]], int, int]]:
    found = []
    for case_id, tags, invoice in _dataset():
        items = invoice["items"]
        for a in range(len(items)):
            for b in range(a + 1, len(items)):
                if row_score(items[a], items[b], ITEM_WEIGHTS) == 7:
                    found.append((case_id, tags, items, a, b))
    return found


def test_the_confusable_partners_score_seven_and_every_other_pair_at_most_five() -> None:
    partners = 0
    for case_id, tags, invoice in _dataset():
        items = invoice["items"]
        for a in range(len(items)):
            for b in range(a + 1, len(items)):
                score = row_score(items[a], items[b], ITEM_WEIGHTS)
                if score == 7:
                    partners += 1
                    assert "confusable" in tags, case_id
                else:
                    assert score <= 5, (case_id, a, b, score)
    assert partners == 36
    assert 5 < ITEM_MINIMUM < 7


def test_a_partner_copied_into_the_other_slot_stays_paired_with_wrong_values_on_that_row() -> None:
    pairs = _partner_pairs()
    assert len(pairs) == 36
    for case_id, _, items, a, b in pairs:
        predicted = copy.deepcopy(items)
        predicted[a] = copy.deepcopy(items[b])
        diff = diff_rows("item", predicted, items, EXACT)
        assert dict(diff.assignment.pairs) == {n: n for n in range(len(items))}, (case_id, a, b)
        assert all(w["event"] is None for w in diff.wrong), (case_id, a, b)
        differing = [c for c in ITEM_COLUMNS if items[a][c] != items[b][c]]
        assert [w["path"] for w in diff.wrong] == [f"items[{a}].{c}" for c in differing]
        assert "code" in differing
        # The reverse slot holds the first partner's values.
        predicted = copy.deepcopy(items)
        predicted[b] = copy.deepcopy(items[a])
        diff = diff_rows("item", predicted, items, EXACT)
        assert dict(diff.assignment.pairs) == {n: n for n in range(len(items))}, (case_id, a, b)
        assert {w["row"] for w in diff.wrong} == {b}


def test_dropping_a_partner_unmatches_only_that_row_and_the_other_partner_keeps_its_own() -> None:
    for case_id, _, items, a, b in _partner_pairs():
        for gone, kept in ((a, b), (b, a)):
            predicted = copy.deepcopy(items[:gone] + items[gone + 1 :])
            diff = diff_rows("item", predicted, items, EXACT)
            assert diff.assignment.unmatched_true == (gone,), (case_id, gone)
            assert diff.assignment.unmatched_predicted == (), (case_id, gone)
            kept_index = kept if kept < gone else kept - 1
            assert (kept_index, kept) in diff.assignment.pairs, (case_id, kept)


def test_two_transposed_digits_in_the_differing_value_leave_one_wrong_value_on_the_row() -> None:
    for case_id, _, items, a, b in _partner_pairs():
        columns = [c for c in ("quantity", "unit_price") if items[a][c] != items[b][c]]
        assert len(columns) == 1, (case_id, a, b)
        column = columns[0]
        # The partner's value is the digit transposition of this row's value.
        assert sorted(items[a][column]) == sorted(items[b][column]), (case_id, a, b)
        predicted = copy.deepcopy(items)
        predicted[a][column] = items[b][column]
        diff = diff_rows("item", predicted, items, EXACT)
        assert dict(diff.assignment.pairs) == {n: n for n in range(len(items))}, (case_id, a, b)
        assert [w["path"] for w in diff.wrong] == [f"items[{a}].{column}"], (case_id, a, b)


def test_a_prediction_with_no_rows_and_a_truth_with_no_rows_are_both_empty() -> None:
    for kind in ("item", "installment"):
        for prediction in ([], None, [None], "x"):
            diff = diff_rows(kind, prediction, [], EXACT)
            assert diff.assignment == Assignment((), (), ())
            assert diff.wrong == []
    extra = diff_rows(
        "installment", [{"number": "001", "due_date": "2026-01-01", "amount": "1.00"}], [], EXACT
    )
    assert [(w["path"], w["event"]) for w in extra.wrong] == [("installments[+0].amount", "extra")]


def test_two_identical_predicted_rows_against_one_true_row_pair_the_nearer_index() -> None:
    row = _item()
    stranger = _item(code="Z-9", description="outra coisa", ncm="1", unit="X", quantity="1")
    stranger.update(unit_price="1", total="1")
    only = assign_rows([row, copy.deepcopy(row)], [row], ITEM_WEIGHTS, ITEM_MINIMUM)
    assert only.pairs == ((0, 0),)
    assert only.unmatched_predicted == (1,)
    later = assign_rows(
        [copy.deepcopy(row), copy.deepcopy(row)], [stranger, row], ITEM_WEIGHTS, ITEM_MINIMUM
    )
    assert later.pairs == ((1, 1),)
    assert later.unmatched_true == (0,)
    assert later.unmatched_predicted == (0,)


def test_two_calls_on_the_same_rows_give_equal_assignments() -> None:
    for _, _, invoice in _dataset()[:20]:
        rows = invoice["items"]
        predicted = copy.deepcopy(rows[::-1])
        first = assign_rows(predicted, rows, ITEM_WEIGHTS, ITEM_MINIMUM)
        second = assign_rows(
            copy.deepcopy(predicted), copy.deepcopy(rows), ITEM_WEIGHTS, ITEM_MINIMUM
        )
        assert first == second


def test_wrong_values_are_ordered_by_header_field_then_true_row_then_column() -> None:
    case_id = _case_with_items(4)
    invoice = _truth_invoice(case_id)
    broken = copy.deepcopy(invoice)
    broken["items"][2]["quantity"] = "1.0000"
    broken["items"][1]["unit_price"] = "1.0000"
    broken["items"][1]["unit"] = "ZZ"
    broken["totals"]["freight"] = "123.00"
    broken["number"] = broken["number"] + 1
    run = _record(case_id, broken)
    from carimbo_evals.grader import grade_case, load_ground_truth

    graded = grade_case(run, load_ground_truth(DATASET / f"{case_id}.xml"), EXACT)
    assert [w["path"] for w in graded["wrong"]] == [
        "number",
        "totals.freight",
        "items[1].unit",
        "items[1].unit_price",
        "items[2].quantity",
    ]


def test_a_near_miss_is_never_correct() -> None:
    # D-04: nothing fuzzy counts. One letter off, one digit off and a stripped accent all fail.
    truth = [_item(description="Parafuso sextavado", ncm="73181500", code="Cód-1")]
    for column, value in (
        ("description", "Parafuso sextavad"),
        ("description", "Parafuso sextavado M8"),
        ("ncm", "73181501"),
        ("code", "Cod-1"),
    ):
        predicted = [_item(description="Parafuso sextavado", ncm="73181500", code="Cód-1")]
        predicted[0][column] = value
        diff = diff_rows("item", predicted, truth, Decimal("0.01"))
        assert any(w["column"] == column for w in diff.wrong) or not diff.assignment.pairs, column
