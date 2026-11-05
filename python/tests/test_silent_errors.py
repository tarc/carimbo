"""EVAL-06, EVAL-07, EVAL-04: silent errors, tag counts, the incomplete flag and wrap hazards."""

from __future__ import annotations

import copy
import json
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from carimbo_evals.corruptions import corrupted, stored_record
from carimbo_evals.grader import (
    PAYMENT_HEADER_PATHS,
    GroundTruth,
    diff_invoice,
    grade_case,
    grade_run,
    is_payment_value,
    load_ground_truth,
)
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.slices import SLICES, render_hazards
from carimbo_evals.summary import (
    FIELDS,
    build_summary,
    render_markdown,
    silent_block,
    write_summary,
)
from carimbo_evals.tags import TAG_ORDER, TAGS_VERSION

REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET = REPO_ROOT / "data" / "dataset"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
EXACT = Decimal("0.00")


def _truth_invoice(case_id: str) -> dict[str, Any]:
    return invoice_from_xml(DATASET / f"{case_id}.xml")


def _write_run(tmp_path: Path, records: list[dict[str, Any]], **run_json: Any) -> Path:
    run_dir = tmp_path / "run"
    run_dir.mkdir()
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for r in records),
        encoding="utf-8",
    )
    if run_json:
        (run_dir / "run.json").write_text(json.dumps(run_json), encoding="utf-8")
    return run_dir


def _truth(case_id: str = "nfe-0003") -> GroundTruth:
    return load_ground_truth(DATASET / f"{case_id}.xml")


def _wrong(invoice: dict[str, Any], truth: GroundTruth, status: str = "success") -> list[Any]:
    return diff_invoice(status, invoice, truth, EXACT)["wrong"]


def test_tracer_a_stored_run_reports_two_silent_invoices_one_payment_and_one_other(
    tmp_path: Path,
) -> None:
    ids = ["nfe-0003", "nfe-0005", "nfe-0006", "nfe-0007"]
    wrong_item_total = _truth_invoice(ids[1])
    wrong_item_total["items"][0]["total"] = format(
        Decimal(wrong_item_total["items"][0]["total"]) + 1, "f"
    )
    wrong_description = _truth_invoice(ids[2])
    wrong_description["items"][0]["description"] = "UMA DESCRICAO QUE NAO EXISTE"
    caught = _truth_invoice(ids[3])
    caught["totals"]["invoice_total"] = format(Decimal(caught["totals"]["invoice_total"]) + 1, "f")
    records = [
        stored_record(ids[0], _truth_invoice(ids[0])),
        stored_record(ids[1], wrong_item_total),
        stored_record(ids[2], wrong_description),
        stored_record(ids[3], caught, status="validation_failed"),
    ]
    run_dir = _write_run(tmp_path, records)

    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    write_summary(run_dir, summary)
    stored = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))

    overall = stored["silent_errors"]["overall"]
    assert overall["success_cases"] == 3
    assert overall["silent"] == 2
    assert overall["payment"] == 1
    assert overall["other"] == 1
    assert overall["rate"] == 0.6667
    assert overall["payment"] + overall["other"] == overall["silent"]
    assert overall["ci95"] is not None
    case = {c["case_id"]: c for c in stored["cases"]}
    assert all("payment" in value for c in stored["cases"] for value in c["wrong"])
    assert [v["payment"] for v in case[ids[1]]["wrong"]] == [True]
    assert [v["payment"] for v in case[ids[2]]["wrong"]] == [False]
    assert case[ids[3]]["wrong"] and case[ids[3]]["caught"] is True


@pytest.mark.parametrize("path", sorted(PAYMENT_HEADER_PATHS))
def test_every_header_payment_path_is_a_payment_value(path: str) -> None:
    value = {"scope": "header", "path": path, "column": path}
    assert is_payment_value(value) is True


def test_the_payment_header_paths_are_exactly_the_d06_list() -> None:
    assert {
        "access_key",
        "issuer.cnpj",
        "recipient.tax_id",
        "issue_date",
        "totals.invoice_total",
    } == PAYMENT_HEADER_PATHS
    assert set(FIELDS) >= PAYMENT_HEADER_PATHS


@pytest.mark.parametrize(
    "value",
    [
        {"scope": "header", "path": "totals.freight", "column": "totals.freight"},
        {"scope": "header", "path": "number", "column": "number"},
        {"scope": "header", "path": "issuer.name", "column": "issuer.name"},
        {"scope": "header", "path": "item_count", "column": "item_count"},
        {"scope": "item", "path": "items[0].description", "column": "description"},
        {"scope": "item", "path": "items[0].unit_price", "column": "unit_price"},
        {"scope": "installment", "path": "installments[0].number", "column": "number"},
    ],
)
def test_cosmetic_and_non_payment_values_are_not_payment_values(value: dict[str, Any]) -> None:
    assert is_payment_value(value) is False


def test_item_total_and_installment_due_date_and_amount_are_payment_values() -> None:
    assert is_payment_value({"scope": "item", "column": "total", "path": "items[2].total"})
    assert is_payment_value({"scope": "installment", "column": "due_date", "path": "x"})
    assert is_payment_value({"scope": "installment", "column": "amount", "path": "x"})


def test_each_graded_wrong_value_carries_the_payment_flag_that_the_d06_list_gives() -> None:
    truth = _truth("nfe-0003")
    invoice = copy.deepcopy(truth.invoice)
    invoice["totals"]["freight"] = format(Decimal(invoice["totals"]["freight"]) + 5, "f")
    invoice["items"][0]["description"] = "OUTRA"
    invoice["issue_date"] = "1999-01-01"
    invoice["items"][1]["total"] = format(Decimal(invoice["items"][1]["total"]) + 5, "f")
    by_path = {value["path"]: value["payment"] for value in _wrong(invoice, truth)}
    assert by_path["totals.freight"] is False
    assert by_path["items[0].description"] is False
    assert by_path["issue_date"] is True
    assert by_path["items[1].total"] is True


def test_missing_and_extra_rows_and_installments_are_payment_values() -> None:
    truth = _truth("nfe-0003")
    assert len(truth.invoice["items"]) >= 2 and truth.invoice["installments"]

    missing_row = copy.deepcopy(truth.invoice)
    del missing_row["items"][1]
    values = [v for v in _wrong(missing_row, truth) if v["scope"] == "item"]
    assert [v["event"] for v in values] == ["missing"]
    assert values[0]["payment"] is True

    extra_row = copy.deepcopy(truth.invoice)
    invented = copy.deepcopy(extra_row["items"][0])
    invented.update(
        code="ZZ9999",
        description="PRODUTO INEXISTENTE INVENTADO",
        ncm="99999999",
        quantity="1.0000",
        unit_price="1.0000",
        total="1.00",
    )
    extra_row["items"].append(invented)
    values = [v for v in _wrong(extra_row, truth) if v["scope"] == "item"]
    assert [v["event"] for v in values] == ["extra"]
    assert values[0]["payment"] is True

    missing_installment = copy.deepcopy(truth.invoice)
    del missing_installment["installments"][0]
    values = [v for v in _wrong(missing_installment, truth) if v["scope"] == "installment"]
    assert [v["event"] for v in values] == ["missing"]
    assert values[0]["column"] == "amount" and values[0]["payment"] is True

    extra_installment = copy.deepcopy(truth.invoice)
    extra_installment["installments"].append(
        {"number": "999", "due_date": "2031-01-01", "amount": "1.00"}
    )
    values = [v for v in _wrong(extra_installment, truth) if v["scope"] == "installment"]
    assert [v["event"] for v in values] == ["extra"]
    assert values[0]["column"] == "amount" and values[0]["payment"] is True


def _case(case_id: str, status: str, wrong: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "case_id": case_id,
        "base_case": case_id,
        "status": status,
        "wrong": wrong,
        "correct": status == "success" and not wrong,
    }


_PAY = {"scope": "header", "path": "access_key", "column": "access_key", "payment": True}
_COSMETIC = {"scope": "item", "path": "items[0].description", "column": "description"} | {
    "payment": False
}


def test_one_wrong_value_makes_a_success_silent_and_a_caught_case_is_not_counted() -> None:
    block = silent_block(
        [
            _case("a", "success", []),
            _case("b", "success", [_COSMETIC]),
            _case("c", "validation_failed", [_PAY, _PAY]),
            _case("d", "refused", []),
        ]
    )
    assert block["success_cases"] == 2
    assert block["silent"] == 1
    assert (block["payment"], block["other"]) == (0, 1)
    assert block["rate"] == 0.5


def test_a_silent_invoice_with_a_payment_and_a_cosmetic_value_counts_once_as_payment() -> None:
    block = silent_block([_case("a", "success", [_COSMETIC, _PAY])])
    assert (block["silent"], block["payment"], block["other"]) == (1, 1, 0)


def test_with_no_success_case_the_rate_and_the_interval_are_null_not_zero() -> None:
    block = silent_block([_case("a", "validation_failed", [_PAY]), _case("b", "refused", [])])
    assert block["success_cases"] == 0
    assert block["rate"] is None
    assert block["ci95"] is None
    assert block["silent"] == block["payment"] == block["other"] == 0


def test_a_success_run_with_every_value_right_has_rate_zero_and_not_null() -> None:
    block = silent_block([_case("a", "success", [])])
    assert block["rate"] == 0.0
    assert block["ci95"] is not None


def test_the_summary_has_one_silent_block_per_scope_including_overall() -> None:
    cases = [
        _case("a", "success", [_PAY]) | {"split": "test"},
        _case("b", "success", []) | {"split": "dev"},
    ]
    summary = build_summary(cases, {"run_id": "r", "tolerance": EXACT}, None)
    assert set(summary["silent_errors"]) == {"overall", "test", "dev"}
    assert summary["silent_errors"]["test"]["silent"] == 1
    assert summary["silent_errors"]["dev"]["silent"] == 0


# --- per-field wrong counts on success cases (D-07) ---------------------------------------------


def _value(scope: str, path: str, column: str, tag: str = "untagged") -> dict[str, Any]:
    return {"scope": scope, "path": path, "column": column, "tag": tag, "payment": False}


def test_wrong_on_success_counts_labelled_values_on_success_cases_only() -> None:
    cases = [
        _case(
            "a",
            "success",
            [
                _value("header", "issuer.cnpj", "issuer.cnpj"),
                _value("item", "items[0].total", "total"),
                _value("item", "items[3].total", "total"),
                _value("installment", "installments[0].amount", "amount"),
            ],
        ),
        _case("b", "validation_failed", [_value("header", "issuer.cnpj", "issuer.cnpj")]),
        _case("c", "schema_invalid", [_value("item", "items[0].total", "total")]),
    ]
    summary = build_summary(cases, {"run_id": "r", "tolerance": EXACT}, None)
    assert summary["wrong_on_success"] == {
        "installment.amount": 1,
        "issuer.cnpj": 1,
        "item.total": 2,
    }
    assert list(summary["wrong_on_success"]) == sorted(summary["wrong_on_success"])


# --- tag counts add up (D-10) ------------------------------------------------------------------

SIX_KINDS = (
    "party_swap",
    "dropped_row",
    "digit_transposition",
    "text_wrong",
    "date_wrong",
    "tax_code_wrong",
)


def _corrupted_run(tmp_path: Path) -> Path:
    records = []
    manifest = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
    case_ids = [case["case_id"] for case in manifest["cases"]]
    for tag in SIX_KINDS:
        for case_id in case_ids:
            damaged = corrupted(tag, _truth_invoice(case_id), seed="silent-test")
            if damaged is not None:
                records.append(stored_record(case_id, damaged))
                case_ids.remove(case_id)
                break
        else:  # pragma: no cover - the dataset holds a case every kind applies to
            raise AssertionError(f"no case takes the {tag} corruption")
    records.append(stored_record(case_ids[0], _truth_invoice(case_ids[0])))
    return _write_run(tmp_path, records)


def test_tag_counts_add_up_to_the_wrong_value_total_with_every_tag_present(
    tmp_path: Path,
) -> None:
    run_dir = _corrupted_run(tmp_path)
    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    tags = summary["failure_tags"]
    assert tags["version"] == TAGS_VERSION == summary["tags_version"]
    assert set(tags["counts"]) == set(TAG_ORDER) == set(tags["cases"])
    assert len(tags["counts"]) == 19
    assert sum(tags["counts"].values()) == tags["wrong_values"] > 0
    assert tags["wrong_values"] == sum(len(c["wrong"]) for c in summary["cases"])
    assert tags["untagged"] == tags["counts"]["untagged"]
    assert all(tags["cases"][tag] <= tags["counts"][tag] for tag in TAG_ORDER)
    assert sum(1 for count in tags["counts"].values() if count == 0) >= 1  # zeros are kept
    assert list(tags["counts"]) == sorted(tags["counts"])
    assert summary["silent_errors"]["overall"]["silent"] == 6
    assert summary["silent_errors"]["overall"]["success_cases"] == 7


def test_the_failure_tags_block_has_all_keys_and_zeros_for_a_run_with_no_wrong_value() -> None:
    summary = build_summary([_case("a", "success", [])], {"run_id": "r", "tolerance": EXACT}, None)
    tags = summary["failure_tags"]
    assert set(tags) == {"version", "wrong_values", "counts", "cases", "untagged"}
    assert tags["wrong_values"] == 0 and set(tags["counts"].values()) == {0}


# --- wrap hazards (D-47) -----------------------------------------------------------------------


def _manifest_cases() -> list[dict[str, Any]]:
    return json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))["cases"]


def test_render_hazards_follow_the_measured_counts_of_d47() -> None:
    hazards = {
        case["case_id"]: render_hazards(_truth_invoice(case["case_id"]))
        for case in _manifest_cases()
    }
    assert sum("unit_wrap" in found for found in hazards.values()) == 43
    number_wrap = {case_id for case_id, found in hazards.items() if "number_wrap" in found}
    confusable = {c["case_id"] for c in _manifest_cases() if "confusable" in c["tags"]}
    assert len(number_wrap) == 18
    assert number_wrap == confusable
    assert all(found == sorted(found) for found in hazards.values())
    for split, carrying, total in (("test", 31, 60), ("dev", 12, 30)):
        text = [
            c["case_id"]
            for c in _manifest_cases()
            if c["split"] == split and c["rendering"] == "text"
        ]
        assert len(text) == total
        assert sum(1 for case_id in text if hazards[case_id]) == carrying


def test_no_hazard_is_a_slice() -> None:
    names = {"unit_wrap", "number_wrap", "render_hazards"}
    assert not any(name in sl.slice_id or name in sl.dimension for sl in SLICES for name in names)


def test_render_hazards_ignore_malformed_items() -> None:
    assert render_hazards({}) == []
    assert render_hazards({"items": "x"}) == []
    assert render_hazards({"items": [None, {"unit": 3, "total": 5}]}) == []
    assert render_hazards({"items": [{"unit": " par. ", "unit_price": "100000.00"}]}) == [
        "number_wrap",
        "unit_wrap",
    ]
    assert render_hazards({"items": [{"unit": "UN", "total": "99999.99"}]}) == []


def test_a_wrapped_unit_stays_wrong_and_tagged_text_wrong() -> None:
    case_id = next(
        case["case_id"]
        for case in _manifest_cases()
        if "unit_wrap" in render_hazards(_truth_invoice(case["case_id"]))
    )
    truth = _truth(case_id)
    invoice = copy.deepcopy(truth.invoice)
    row = next(i for i, item in enumerate(invoice["items"]) if item["unit"] == "PAR")
    invoice["items"][row]["unit"] = "PA"
    wrong = _wrong(invoice, truth)
    assert [(v["path"], v["tag"], v["expected"], v["predicted"]) for v in wrong] == [
        (f"items[{row}].unit", "text_wrong", "PAR", "PA")
    ]
    assert wrong[0]["payment"] is False


def test_grade_case_records_hazards_on_every_record_even_without_a_grade(
    tmp_path: Path,
) -> None:
    case_id = next(
        case["case_id"]
        for case in _manifest_cases()
        if "unit_wrap" in render_hazards(_truth_invoice(case["case_id"]))
    )
    refused = stored_record(case_id, None, status="refused")
    harness = stored_record(case_id, None, status="harness_error")
    run_dir = _write_run(tmp_path, [harness])
    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    assert "unit_wrap" in summary["cases"][0]["render_hazards"]
    assert summary["cases"][0]["status"] == "harness_error"
    graded = grade_case(refused, _truth(case_id), EXACT)
    assert "unit_wrap" in graded["render_hazards"]
    assert graded["wrong"] == [] and graded["correct"] is False


def _text_case(case_id: str, split: str, hazards: list[str], correct: bool) -> dict[str, Any]:
    return _case(case_id, "success", [] if correct else [_COSMETIC]) | {
        "split": split,
        "rendering": "text",
        "render_hazards": hazards,
    }


def test_the_summary_counts_hazards_per_scope_and_markdown_prints_the_notes() -> None:
    cases = [
        _text_case("t1", "test", ["unit_wrap"], True),
        _text_case("t2", "test", ["number_wrap", "unit_wrap"], False),
        _text_case("t3", "test", [], True),
        _text_case("d1", "dev", ["number_wrap"], False),
        _text_case("d2", "dev", [], True),
        _case("i1", "success", []) | {"split": "test", "rendering": "image_clean"},
    ]
    summary = build_summary(cases, {"run_id": "r", "tolerance": EXACT}, None)
    test = summary["render_hazards"]["test"]
    assert test["unit_wrap"] == {"cases": 2, "correct": 1}
    assert test["number_wrap"] == {"cases": 1, "correct": 0}
    assert test["any"] == {"cases": 2, "correct": 1}
    assert test["none"] == {"cases": 1, "correct": 1}
    assert summary["render_hazards"]["dev"]["any"] == {"cases": 1, "correct": 0}
    assert set(summary["render_hazards"]) == {"test", "dev"}
    text = render_markdown(summary)
    assert "test text: 2 of 3 cases carry a DANFE wrap hazard (see D-47)" in text
    assert "dev text: 1 of 2 cases carry a DANFE wrap hazard (see D-47)" in text
    assert "## Silent errors" in text and "## Failure tags" in text


def test_a_hazard_never_changes_a_grade() -> None:
    case_id = next(
        case["case_id"]
        for case in _manifest_cases()
        if "unit_wrap" in render_hazards(_truth_invoice(case["case_id"]))
    )
    truth = _truth(case_id)
    graded = grade_case(stored_record(case_id, copy.deepcopy(truth.invoice)), truth, EXACT)
    assert graded["render_hazards"] and graded["correct"] is True and graded["wrong"] == []


def test_the_failure_tags_section_leaves_zero_rows_out_and_states_the_untagged_count() -> None:
    cases = [
        _case("a", "success", [_value("item", "items[0].total", "total", "digit_count")]),
        _case("b", "success", [_value("header", "number", "number", "untagged")]),
    ]
    summary = build_summary(cases, {"run_id": "r", "tolerance": EXACT}, None)
    text = render_markdown(summary)
    assert "| digit_count | 1 | 1 |" in text
    assert "| party_swap |" not in text
    assert f"2 wrong values, 1 untagged, tag list {TAGS_VERSION}" in text
