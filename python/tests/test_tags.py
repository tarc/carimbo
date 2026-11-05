"""EVAL-07: every wrong value carries one tag and its rule number (failure-tags-1, D-43).

The tracer tests run real dataset cases from stored records through ``grade_run`` to the case
record. The rest pin the rule list and the order of the rules.
"""

from __future__ import annotations

import copy
import json
from decimal import Decimal
from pathlib import Path
from typing import Any

from carimbo_evals.grader import grade_run
from carimbo_evals.tags import TAG_ORDER, TAGS_VERSION

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
