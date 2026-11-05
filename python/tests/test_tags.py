"""EVAL-07: every wrong value carries one tag and its rule number (failure-tags-1, D-43).

The tracer tests run real dataset cases from stored records through ``grade_run`` to the case
record. The rest pin the rule list and the order of the rules.
"""

from __future__ import annotations

import copy
import json
from collections.abc import Callable
from decimal import Decimal
from difflib import SequenceMatcher
from pathlib import Path
from typing import Any

import pytest

from carimbo_evals.grader import GroundTruth, diff_invoice, grade_run
from carimbo_evals.matching import Assignment
from carimbo_evals.tags import (
    PREDICATES,
    TAG_ORDER,
    TAGS_VERSION,
    CaseContext,
    first_match,
)

REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET = REPO_ROOT / "data" / "dataset"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
EXACT = Decimal("0.00")
LAST_RULE = len(TAG_ORDER) - 1


def _record(case_id: str, invoice: dict[str, Any] | None, *, status: str = "success") -> dict:
    """A stored run record whose outcome carries ``invoice``."""
    return {
        "record_version": 3,
        "run_id": "run-tags",
        "case_id": case_id,
        "status": "completed",
        "request": {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "a" * 32},
        "http": {"status": 200, "wall_ms": 1505, "error": None},
        "response": {
            "contract_version": "3",
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
                "raw_output": json.dumps(invoice) if invoice is not None else None,
            },
            "usage": {
                "input_tokens": 1000,
                "output_tokens": 100,
                "cache_read_tokens": 0,
                "cache_write_5m_tokens": 0,
                "cache_write_1h_tokens": 0,
            },
            "cost_usd": "0.0100",
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


def _grade_one(tmp_path: Path, record: dict[str, Any]) -> dict[str, Any]:
    run_dir = tmp_path / "run"
    run_dir.mkdir()
    (run_dir / "cases.jsonl").write_text(json.dumps(record, sort_keys=True) + "\n", "utf-8")
    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    (case,) = summary["cases"]
    return case


def _count_wrong(case: dict[str, Any], path: str) -> dict[str, Any]:
    (value,) = (w for w in case["wrong"] if w["path"] == path)
    return value


# --- tracer: tags travel from grade_run to the case record ------------------------------------


def test_tracer_a_dropped_middle_row_is_tagged_on_the_row_and_on_item_count(
    tmp_path: Path,
) -> None:
    case_id = _case_with_items(5)
    invoice = _truth_invoice(case_id)
    middle = len(invoice["items"]) // 2
    assert 0 < middle < len(invoice["items"]) - 2
    del invoice["items"][middle]

    case = _grade_one(tmp_path, _record(case_id, invoice))

    assert case["failure_tags"] == ["dropped_row"]
    row = _count_wrong(case, f"items[{middle}].total")
    count = _count_wrong(case, "item_count")
    assert row["tag"] == count["tag"] == "dropped_row"
    assert row["rule"] == count["rule"] == TAG_ORDER.index("dropped_row")
    assert len(case["wrong"]) == 2


def test_tracer_losing_the_last_two_rows_is_trailing_rows_lost_on_rows_and_count(
    tmp_path: Path,
) -> None:
    case_id = _case_with_items(4)
    invoice = _truth_invoice(case_id)
    n = len(invoice["items"])
    del invoice["items"][-2:]

    case = _grade_one(tmp_path, _record(case_id, invoice))

    assert case["failure_tags"] == ["trailing_rows_lost"]
    rule = TAG_ORDER.index("trailing_rows_lost")
    for path in (f"items[{n - 2}].total", f"items[{n - 1}].total", "item_count"):
        value = _count_wrong(case, path)
        assert (value["tag"], value["rule"]) == ("trailing_rows_lost", rule)


def test_tracer_an_extra_row_is_invented_row_on_the_row_and_on_item_count(
    tmp_path: Path,
) -> None:
    case_id = "nfe-0001"
    invoice = _truth_invoice(case_id)
    invented = copy.deepcopy(invoice["items"][0])
    invented.update(
        code="ZZ9999",
        description="PRODUTO INEXISTENTE INVENTADO",
        ncm="99999999",
        unit="KG",
        quantity="1.0000",
        unit_price="1.0000",
        total="1.00",
        icms_base="0.00",
        icms_amount="0.00",
    )
    invoice["items"].append(invented)

    case = _grade_one(tmp_path, _record(case_id, invoice))

    assert case["failure_tags"] == ["invented_row"]
    extra = next(w for w in case["wrong"] if w["event"] == "extra")
    count = _count_wrong(case, "item_count")
    assert extra["tag"] == count["tag"] == "invented_row"
    assert extra["rule"] == count["rule"] == TAG_ORDER.index("invented_row")


def test_tracer_a_schema_invalid_answer_takes_rule_zero_on_every_wrong_value(
    tmp_path: Path,
) -> None:
    case_id = "nfe-0001"
    case = _grade_one(tmp_path, _record(case_id, None, status="schema_invalid"))

    assert case["failure_tags"] == ["schema_invalid"]
    assert case["wrong"], "a schema_invalid answer is wrong on every field"
    assert {(w["tag"], w["rule"]) for w in case["wrong"]} == {("schema_invalid", 0)}
    assert {w["path"] for w in case["wrong"]} >= {"item_count", "installment_count", "access_key"}


def test_tracer_a_value_no_rule_explains_is_untagged_at_the_last_rule(tmp_path: Path) -> None:
    case_id = "nfe-0001"
    invoice = _truth_invoice(case_id)
    invoice["totals"]["icms_amount"] = str(
        Decimal(invoice["totals"]["icms_amount"]) + Decimal("3817.46")
    )

    case = _grade_one(tmp_path, _record(case_id, invoice))

    assert case["failure_tags"] == ["untagged"]
    value = _count_wrong(case, "totals.icms_amount")
    assert (value["tag"], value["rule"]) == ("untagged", LAST_RULE)


def test_a_clean_answer_and_an_excluded_status_carry_no_tags(tmp_path: Path) -> None:
    case_id = "nfe-0001"
    clean = _grade_one(tmp_path, _record(case_id, _truth_invoice(case_id)))
    assert clean["failure_tags"] == []
    assert clean["wrong"] == []
    other = tmp_path / "other"
    other.mkdir()
    refused = _grade_one(other, _record(case_id, None, status="refused"))
    assert refused["failure_tags"] == []


def test_tags_version_and_rule_list_size() -> None:
    assert TAGS_VERSION == "failure-tags-1"
    assert len(TAG_ORDER) == 19


# --- the rules: one case per tag, and one step either side of each threshold -------------------

FIXTURE = REPO_ROOT / "data" / "vectors" / "valid-invoice.json"
BASE = GroundTruth(json.loads(FIXTURE.read_text(encoding="utf-8")))


def _answer(mutate: Callable[[dict[str, Any]], None]) -> dict[str, Any]:
    invoice = copy.deepcopy(BASE.invoice)
    mutate(invoice)
    return invoice


def _tags_by_path(invoice: dict[str, Any]) -> dict[str, str]:
    diff = diff_invoice("success", invoice, BASE, EXACT)
    return {w["path"]: w["tag"] for w in diff["wrong"]}


def _set(path: str, value: Any) -> Callable[[dict[str, Any]], None]:
    def apply(invoice: dict[str, Any]) -> None:
        *parents, last = path.split(".")
        target: Any = invoice
        for part in parents:
            target = target[int(part)] if isinstance(target, list) else target[part]
        target[last] = value

    return apply


def _all(*steps: Callable[[dict[str, Any]], None]) -> Callable[[dict[str, Any]], None]:
    def apply(invoice: dict[str, Any]) -> None:
        for step in steps:
            step(invoice)

    return apply


def _merge_first_two(invoice: dict[str, Any]) -> None:
    first, second = invoice["items"][0], invoice["items"][1]
    first["quantity"] = "5.5000"
    first["total"] = "135.00"
    del invoice["items"][1]
    assert second["total"] == "35.00"


def _split_first(invoice: dict[str, Any]) -> None:
    first = invoice["items"][0]
    second = copy.deepcopy(first)
    first["quantity"], second["quantity"] = "1.0000", "1.0000"
    first["total"], second["total"] = "50.00", "50.00"
    invoice["items"].insert(1, second)


CASES: list[tuple[str, Callable[[dict[str, Any]], None], dict[str, str]]] = [
    (
        "party_swap",
        _all(
            _set("issuer.cnpj", "12ABC34501DE35"),
            _set("recipient.tax_id", "11222333000181"),
            _set("issuer.name", "DESTINATARIA SINTETICA SA"),
            _set("recipient.name", "EMITENTE SINTETICA LTDA"),
        ),
        {
            "issuer.cnpj": "party_swap",
            "issuer.name": "party_swap",
            "recipient.tax_id": "party_swap",
            "recipient.name": "party_swap",
        },
    ),
    (
        "merged_rows",
        _merge_first_two,
        {
            "items[1].total": "merged_rows",
            "items[0].quantity": "merged_rows",
            "items[0].total": "merged_rows",
            "item_count": "merged_rows",
        },
    ),
    (
        "split_rows",
        _split_first,
        {
            "items[+1].total": "split_rows",
            "items[0].quantity": "split_rows",
            "items[0].total": "split_rows",
            "item_count": "split_rows",
        },
    ),
    (
        "value_from_other_row",
        _set("items.0.total", "35.00"),
        {"items[0].total": "value_from_other_row"},
    ),
    (
        "value_from_other_box",
        _set("totals.freight", "135.00"),
        {"totals.freight": "value_from_other_box"},
    ),
    (
        "value_from_other_box",
        _set("items.0.icms_amount", "10.00"),
        {"items[0].icms_amount": "value_from_other_box"},
    ),
    (
        "thousands_misparse",
        _set("totals.invoice_total", "15500.00"),
        {"totals.invoice_total": "thousands_misparse"},
    ),
    (
        "thousands_misparse",
        _set("installments.0.amount", "7750.00"),
        {"installments[0].amount": "thousands_misparse"},
    ),
    ("zero_for_blank", _set("totals.freight", "0.00"), {"totals.freight": "zero_for_blank"}),
    (
        "zero_for_blank",
        _set("items.0.icms_amount", "0.00"),
        {"items[0].icms_amount": "zero_for_blank"},
    ),
    (
        "digit_transposition",
        _set("access_key", "35260311222333000181550010000001231000012364"),
        {"access_key": "digit_transposition"},
    ),
    ("digit_transposition", _set("number", 132), {"number": "digit_transposition"}),
    (
        "digit_transposition",
        _set("items.1.quantity", "5.3000"),
        {"items[1].quantity": "digit_transposition"},
    ),
    (
        "digit_substitution",
        _set("issuer.cnpj", "01222333000181"),
        {"issuer.cnpj": "digit_substitution"},
    ),
    ("digit_substitution", _set("series", 7), {"series": "digit_substitution"}),
    (
        "digit_substitution",
        _set("totals.insurance", "0.10"),
        {"totals.insurance": "digit_substitution"},
    ),
    (
        "digit_substitution",
        _set("items.0.code", "P00212"),
        {"items[0].code": "digit_substitution"},
    ),
    ("digit_count", _set("recipient.tax_id", "12ABC34501DE3"), {"recipient.tax_id": "digit_count"}),
    ("digit_count", _set("issuer.ie", "12345678901"), {"issuer.ie": "digit_count"}),
    ("digit_count", _set("installments.1.number", "02"), {"installments[1].number": "digit_count"}),
    (
        "text_near_miss",
        _set("items.0.description", "PARAFUSO SEXTAVADO ACO ZINCAD"),
        {"items[0].description": "text_near_miss"},
    ),
    (
        "text_near_miss",
        _set("issuer.name", "EMITENTE SINTETICA LTD"),
        {"issuer.name": "text_near_miss"},
    ),
    (
        "text_wrong",
        _set("operation_nature", "DEVOLUCAO DE MERCADORIA"),
        {"operation_nature": "text_wrong"},
    ),
    ("text_wrong", _set("issuer.uf", "RJ"), {"issuer.uf": "text_wrong"}),
    ("text_wrong", _set("recipient.tax_id_kind", "cpf"), {"recipient.tax_id_kind": "text_wrong"}),
    ("text_wrong", _set("items.0.unit", "KG"), {"items[0].unit": "text_wrong"}),
    ("tax_code_wrong", _set("items.0.cfop", "6403"), {"items[0].cfop": "tax_code_wrong"}),
    ("tax_code_wrong", _set("items.1.cst_csosn", "900"), {"items[1].cst_csosn": "tax_code_wrong"}),
    ("tax_code_wrong", _set("items.1.ncm", "99999999"), {"items[1].ncm": "tax_code_wrong"}),
    ("date_wrong", _set("issue_date", "2026-03-02"), {"issue_date": "date_wrong"}),
    (
        "date_wrong",
        _set("installments.0.due_date", "2026-04-15"),
        {"installments[0].due_date": "date_wrong"},
    ),
    ("untagged", _set("totals.icms_amount", "3835.46"), {"totals.icms_amount": "untagged"}),
]


@pytest.mark.parametrize(
    ("tag", "mutate", "expected"),
    CASES,
    ids=[f"{tag}-{index}" for index, (tag, _, _) in enumerate(CASES)],
)
def test_each_rule_tags_its_hand_built_case(
    tag: str, mutate: Callable[[dict[str, Any]], None], expected: dict[str, str]
) -> None:
    assert tag in expected.values()
    assert _tags_by_path(_answer(mutate)) == expected


def _first(path: str, expected: Any, predicted: Any, *, scope: str = "header", **extra: Any) -> str:
    """The tag ``first_match`` gives one hand-built wrong value in the fixture's context."""
    column = extra.pop("column", path)
    value = {
        "path": path,
        "scope": scope,
        "column": column,
        "row": None,
        "predicted_row": None,
        "event": None,
        "expected": expected,
        "predicted": predicted,
        "within_cent": False,
    }
    value.update(extra)
    context = CaseContext.build(
        "success", BASE.invoice, BASE.invoice, Assignment((), (), ()), Assignment((), (), ())
    )
    return first_match(value, context)[0]


NINE_TENTHS = ("abcdefghijklmnopqrst", "abcdefghijklmnopqrXY")
BELOW = ("abcdefghijklmnopqrst", "abcdefghijklmnopqXYZ")


def test_text_similarity_of_exactly_nine_tenths_is_a_near_miss_and_just_below_is_not() -> None:
    assert SequenceMatcher(None, *NINE_TENTHS, autojunk=False).ratio() == 0.9
    assert SequenceMatcher(None, *BELOW, autojunk=False).ratio() < 0.9
    assert _first("issuer.name", NINE_TENTHS[0], NINE_TENTHS[1]) == "text_near_miss"
    assert _first("issuer.name", BELOW[0], BELOW[1]) == "text_wrong"


def test_a_near_miss_still_counts_as_wrong_and_is_compared_after_normalization() -> None:
    answer = _answer(_set("issuer.name", "Emitente  Sintetica, Ltd"))
    diff = diff_invoice("success", answer, BASE, EXACT)
    assert [(w["path"], w["tag"]) for w in diff["wrong"]] == [("issuer.name", "text_near_miss")]


def test_a_date_one_digit_off_is_date_wrong_and_never_a_digit_rule() -> None:
    assert _first("issue_date", "2026-03-15", "2026-03-16") == "date_wrong"
    assert _first("issue_date", "2026-03-15", "2026-03-51") == "date_wrong"
    assert _first("issue_date", "2026-03-15", "2026-03-5") == "date_wrong"
    assert (
        _first(
            "installments[0].due_date",
            "2026-04-14",
            "2026-04-15",
            scope="installment",
            column="due_date",
        )
        == "date_wrong"
    )


def test_zero_for_blank_needs_a_zero_prediction_and_a_non_zero_expectation() -> None:
    assert _first("totals.freight", "12.34", "0.00") == "zero_for_blank"
    assert _first("totals.freight", "12.34", "0") == "zero_for_blank"
    assert _first("totals.discount", "0.00", "12.34") != "zero_for_blank"
    assert _first("totals.discount", "0.00", "12.34") == "untagged"
    assert _first("totals.discount", "12.34", None) != "zero_for_blank"


def test_thousands_misparse_needs_the_same_significant_digits_and_a_different_value() -> None:
    assert _first("totals.invoice_total", "155.00", "15500.00") == "thousands_misparse"
    assert _first("totals.invoice_total", "155.00", "1.55") == "thousands_misparse"
    assert _first("totals.invoice_total", "155.00", "155.01") != "thousands_misparse"
    assert _first("totals.invoice_total", "155.00", "0.00") == "zero_for_blank"


def test_digit_rules_are_exact_about_positions_and_length() -> None:
    assert _first("access_key", "1234", "1243") == "digit_transposition"
    assert _first("access_key", "1234", "1324") == "digit_transposition"
    assert _first("access_key", "1234", "1334") == "digit_substitution"
    assert _first("access_key", "1234", "4321") == "untagged"
    assert _first("access_key", "1234", "123") == "digit_count"
    assert _first("access_key", "1234", "12345") == "digit_count"
    assert _first("access_key", "1234", "12") == "untagged"
    assert _first("access_key", "12.3-4", "1 2 4 3") == "digit_transposition"


def test_a_numeric_value_goes_through_the_plain_decimal_form_before_the_digit_rules() -> None:
    assert _first("totals.freight", "15.00", "15.10") == "digit_substitution"
    assert _first("totals.freight", "15.00", "51.00") == "digit_transposition"
    assert _first("totals.freight", "15.00", "1.00") == "digit_count"


def test_tax_codes_that_miss_the_digit_rules_are_tax_code_wrong() -> None:
    kwargs: dict[str, Any] = {"scope": "item", "row": 0}
    assert _first("items[0].cfop", "5102", "6403", column="cfop", **kwargs) == "tax_code_wrong"
    assert _first("items[0].cfop", "5102", "5103", column="cfop", **kwargs) == "digit_substitution"
    assert _first("items[0].cfop", "5102", "5120", column="cfop", **kwargs) == "digit_transposition"
    assert _first("items[0].ncm", "73181500", "7318150", column="ncm", **kwargs) == "digit_count"


def test_party_swap_only_tags_party_values_of_a_case_with_a_crossover() -> None:
    swapped = _answer(
        _all(_set("issuer.cnpj", "12ABC34501DE35"), _set("totals.invoice_total", "156.00"))
    )
    tags = _tags_by_path(swapped)
    assert tags["issuer.cnpj"] == "party_swap"
    assert tags["totals.invoice_total"] != "party_swap"
    no_crossover = _answer(_set("issuer.cnpj", "99999999000199"))
    assert _tags_by_path(no_crossover)["issuer.cnpj"] != "party_swap"


def test_a_row_that_matches_nothing_in_the_answer_is_not_called_merged_or_split() -> None:
    dropped = _answer(lambda invoice: invoice["items"].pop(1))
    assert _tags_by_path(dropped) == {
        "items[1].total": "trailing_rows_lost",
        "item_count": "trailing_rows_lost",
    }
    # A dropped row with a zero total is not a merge into a neighbour.
    zero = copy.deepcopy(BASE.invoice)
    zero["items"][1]["total"] = "0.00"
    zero["items"][1]["quantity"] = "0.0000"
    truth = GroundTruth(zero)
    answer = copy.deepcopy(zero)
    answer["items"].pop(1)
    diff = diff_invoice("success", answer, truth, EXACT)
    assert {w["tag"] for w in diff["wrong"] if w["scope"] == "item"} == {"trailing_rows_lost"}


def test_tag_order_is_pinned() -> None:
    assert TAG_ORDER == (
        "schema_invalid",
        "party_swap",
        "merged_rows",
        "split_rows",
        "trailing_rows_lost",
        "dropped_row",
        "invented_row",
        "value_from_other_row",
        "value_from_other_box",
        "thousands_misparse",
        "zero_for_blank",
        "digit_transposition",
        "digit_substitution",
        "digit_count",
        "text_near_miss",
        "text_wrong",
        "tax_code_wrong",
        "date_wrong",
        "untagged",
    )
    assert TAGS_VERSION == "failure-tags-1"


def test_every_tag_has_exactly_one_predicate() -> None:
    assert set(PREDICATES) == set(TAG_ORDER)


def test_every_non_trivial_tag_has_a_hand_built_case() -> None:
    covered = {tag for tag, _, _ in CASES} | {
        "schema_invalid",
        "trailing_rows_lost",
        "dropped_row",
        "invented_row",
    }
    assert covered == set(TAG_ORDER)
