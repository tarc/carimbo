"""EVAL-05: headline rows, slices and field statistics of the run summary (D-13, D-14, D-15)."""

from __future__ import annotations

import json
import re
from decimal import Decimal
from pathlib import Path
from typing import Any

from carimbo_evals.grader import grade_run, load_case_metadata
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.matching import INSTALLMENT_COLUMNS, ITEM_COLUMNS
from carimbo_evals.slices import SLICES, SLICES_VERSION
from carimbo_evals.stats import wilson
from carimbo_evals.summary import FIELDS, _latency, build_summary, render_markdown, write_summary

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


# --- all rows, the declared slices, field stats ---------------------------------------------

# slice id -> (test, dev, full) as "cases/invoices", measured from data/dataset/manifest.json
PINNED_SLICES: dict[str, tuple[str, str, str]] = {
    "regime=normal": ("54/30", "23/15", "77/45"),
    "regime=simples": ("46/30", "31/15", "77/45"),
    "length=short": ("34/18", "26/10", "60/28"),
    "length=medium": ("42/18", "18/10", "60/28"),
    "length=many_items": ("24/24", "10/10", "34/34"),
    "multi_page=yes": ("14/14", "6/6", "20/20"),
    "multi_page=no": ("86/46", "48/24", "134/70"),
    "alphanumeric_cnpj=yes": ("24/16", "16/8", "40/24"),
    "cpf_recipient=yes": ("21/13", "5/5", "26/18"),
    "st_deferral=yes": ("9/9", "13/5", "22/14"),
    "mixed_tax_codes=yes": ("31/23", "25/17", "56/40"),
    "long_text=yes": ("12/12", "6/6", "18/18"),
    "confusable=yes": ("14/14", "4/4", "18/18"),
    "barcode_readable=yes": ("73/60", "37/30", "110/90"),
    "barcode_readable=no": ("27/5", "17/3", "44/8"),
    "rendering=text": ("60/60", "30/30", "90/90"),
    "rendering=image_clean": ("5/5", "3/3", "8/8"),
    "rendering=barcode_damaged": ("5/5", "3/3", "8/8"),
    "rendering=rotation/mild": ("5/5", "3/3", "8/8"),
    "rendering=rotation/severe": ("5/5", "3/3", "8/8"),
    "rendering=blur/mild": ("5/5", "3/3", "8/8"),
    "rendering=blur/severe": ("5/5", "3/3", "8/8"),
    "rendering=low_dpi/mild": ("5/5", "3/3", "8/8"),
    "rendering=low_dpi/severe": ("5/5", "3/3", "8/8"),
}

# (cases, invoices) of the headline rows per scope: text, image
PINNED_HEADLINE = {
    "test": {"text": (60, 60), "image": (40, 5)},
    "dev": {"text": (30, 30), "image": (24, 3)},
    "full": {"text": (90, 90), "image": (64, 8)},
}


def _dataset_grades(**changes: Any) -> list[dict[str, Any]]:
    """One minimal case dict per manifest case, carrying the dataset metadata."""
    metadata = load_case_metadata(DATASET)
    return [
        _grade(
            case_id,
            split=meta["split"],
            base_case=meta["base_case"],
            rendering=meta["rendering"],
            severity=meta["severity"],
            barcode_readable=meta["barcode_readable"],
            case_tags=meta["tags"],
            **changes,
        )
        for case_id, meta in sorted(metadata.items())
    ]


def _full_meta() -> dict[str, Any]:
    return _meta() | {"manifest_case_ids": sorted(load_case_metadata(DATASET))}


def test_the_six_headline_rows_have_the_pinned_counts() -> None:
    summary = build_summary(_dataset_grades(), _full_meta(), None)

    for scope, rows in PINNED_HEADLINE.items():
        for name, (cases, invoices) in rows.items():
            row = summary["headline"][scope][name]
            assert (row["cases"], row["invoices"]) == (cases, invoices), (scope, name)
            assert row["rate"] == 1.0
        pooled = summary["headline"][scope]["pooled"]
        assert pooled["cases"] == sum(c for c, _ in rows.values())
    assert summary["overall"]["cases"] == 154


def test_the_declared_slices_have_the_pinned_counts_in_every_scope() -> None:
    summary = build_summary(_dataset_grades(), _full_meta(), None)

    assert list(PINNED_SLICES) == [s.slice_id for s in SLICES]
    for index, scope in enumerate(("test", "dev", "full")):
        assert list(summary["slices"][scope]) == list(PINNED_SLICES)
        for slice_id, pinned in PINNED_SLICES.items():
            row = summary["slices"][scope][slice_id]
            assert f"{row['cases']}/{row['invoices']}" == pinned[index], (scope, slice_id)
    assert summary["slices"]["test"]["rendering=rotation/mild"]["cases"] == 5


def test_there_are_24_slices_over_11_dimensions_at_version_slices_1() -> None:
    assert len(SLICES) == 24
    assert len({s.dimension for s in SLICES}) == 11
    assert SLICES_VERSION == "slices-1"
    summary = build_summary(_dataset_grades(), _full_meta(), None)
    assert summary["slices_version"] == "slices-1"


def test_a_run_of_only_dev_cases_has_no_test_and_no_full_key() -> None:
    dev = [g for g in _dataset_grades() if g["split"] == "dev"]

    summary = build_summary(dev, _full_meta(), None)

    assert list(summary["headline"]) == ["dev"]
    assert list(summary["slices"]) == ["dev"]
    assert list(summary["field_by_slice"]) == ["dev"]


def test_header_field_stats_have_an_interval_and_field_accuracy_keeps_two_keys() -> None:
    fields = dict.fromkeys(FIELDS, True)
    grades = [g | {"fields": dict(fields)} for g in _dataset_grades()]

    summary = build_summary(grades, _full_meta(), None)

    assert set(summary["field_stats"]) == {"overall", "test", "dev", "full"}
    for scope, stats in summary["field_stats"].items():
        assert len(stats) == 27, scope
        for entry in stats.values():
            assert set(entry) == {"correct", "n", "invoices", "rate", "ci95"}
    assert summary["field_stats"]["test"]["access_key"]["n"] == 100
    assert summary["field_stats"]["test"]["access_key"]["invoices"] == 60
    assert summary["field_accuracy"]["access_key"] == {"correct": 154, "n": 154}
    assert set(summary["field_accuracy"]["access_key"]) == {"correct", "n"}


def test_a_field_without_graded_cases_has_a_null_interval() -> None:
    summary = build_summary([_grade("a", split="test")], _meta(), None)

    entry = summary["field_stats"]["overall"]["access_key"]
    assert (entry["n"], entry["invoices"], entry["rate"], entry["ci95"]) == (0, 0, None, None)


def test_summary_md_labels_dev_rows_and_lists_the_rendering_table() -> None:
    summary = build_summary(_dataset_grades(), _full_meta(), None)

    text = render_markdown(summary)

    assert "## Correct invoices" in text
    assert "test text (primary)" in text
    assert "dev text (dev)" in text
    assert "dev image (dev)" in text
    assert "dev pooled (dev)" in text
    assert "full text |" in text
    assert "## Rendering" in text
    assert "dev rendering=rotation/mild (dev)" in text
    assert text.index("## Correct invoices") < text.index("| case_id")
    assert "(dev)" not in "".join(
        line for line in text.splitlines() if line.startswith(("| test ", "| full "))
    )


def test_summary_md_without_split_metadata_prints_the_overall_row_only() -> None:
    text = render_markdown(build_summary([_grade("a", correct=False)], _meta(), None))

    assert "| overall | 1 | 1 | 0 | 0 | 0.0000 | [0.0000, 0.7935] |" in text
    assert "## Rendering" not in text


def test_a_null_rate_prints_a_dash() -> None:
    grades = [_grade("a", split="test", rendering="text")]

    text = render_markdown(build_summary(grades, _meta(), None))

    assert "| test image | 0 | 0 | 0 | 0 | - | - |" in text


def _items_block(correct: int, n: int) -> dict[str, Any]:
    return {
        "true_rows": 3,
        "predicted_rows": 3,
        "matched": 3,
        "rows_all_correct": 2,
        "order_preserved": True,
        "columns": {column: {"correct": correct, "n": n} for column in ITEM_COLUMNS},
    }


def test_field_by_slice_counts_twins_once_and_pools_row_columns() -> None:
    fields = dict.fromkeys(FIELDS, True)
    grades = [
        g | {"fields": dict(fields), "items": _items_block(2, 3), "installments": None}
        for g in _dataset_grades()
    ]

    summary = build_summary(grades, _full_meta() | {"resamples": 200}, None)
    entries = summary["field_by_slice"]["test"]["alphanumeric_cnpj=yes"]

    access_key = entries["access_key"]
    interval = wilson(24, 24, 16)
    assert interval is not None
    assert (access_key["correct"], access_key["n"], access_key["invoices"]) == (24, 24, 16)
    assert access_key["rate"] == 1.0
    assert access_key["ci95"] == [float(interval[0]), float(interval[1])]
    total = entries["item.total"]
    assert (total["correct"], total["n"], total["rate"]) == (48, 72, 0.6667)
    assert set(total) == {"correct", "n", "rate", "invoices", "ci95"}
    for scope in ("test", "dev", "full"):
        for slice_id in PINNED_SLICES:
            for name, entry in summary["field_by_slice"][scope][slice_id].items():
                if name in FIELDS:
                    assert entry["ci95"] is None or len(entry["ci95"]) == 2
                    assert entry["n"] > 0 or entry["ci95"] is None


def test_the_summary_declares_its_primary_metric_and_interval_method() -> None:
    summary = build_summary([_grade("a")], _meta(), None)

    assert summary["primary_metric"]["headline_row"] == "test.text"
    assert summary["primary_metric"]["second_row"] == "test.image"
    assert summary["stats"]["z"] == "1.959963984540054"
    assert summary["stats"]["interval"] == "wilson"


# --- line items: pooled row metrics with a bootstrap over invoices (D-14, EVAL-05) ---------------

BOOTSTRAP = {
    "seed": 20261105,
    "resamples": 200,
    "unit": "base_case",
    "method": "percentile",
    "rng": "random.Random.random",
}


def _without_last_item(case_id: str) -> dict[str, Any]:
    invoice = _truth_invoice(case_id)
    invoice["items"] = invoice["items"][:-1]
    return invoice


def test_tracer_a_stored_run_with_a_dropped_row_reports_item_precision_and_recall(
    tmp_path: Path,
) -> None:
    ids = ["nfe-0005", "nfe-0006"]
    true_rows = sum(len(_truth_invoice(case_id)["items"]) for case_id in ids)
    records = [_record(ids[0], _truth_invoice(ids[0])), _record(ids[1], _without_last_item(ids[1]))]
    run_dir = _write_run(tmp_path, records)

    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)
    write_summary(run_dir, summary)
    stored = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))

    block = stored["items"]["overall"]
    assert (block["true_rows"], block["predicted_rows"], block["matched"]) == (
        true_rows,
        true_rows - 1,
        true_rows - 1,
    )
    assert block["precision"]["value"] == 1.0
    assert block["recall"]["value"] == round((true_rows - 1) / true_rows, 4)
    for name in ("precision", "recall", "f1", "all_values_correct"):
        interval = block[name]["ci95"]
        assert len(interval) == 2 and interval[0] <= interval[1], name
    assert stored["stats"]["bootstrap"] == BOOTSTRAP
    assert set(stored["field_accuracy"]["access_key"]) == {"correct", "n"}
    assert set(block["columns"]) == set(ITEM_COLUMNS)
    assert set(stored["installments"]["overall"]["columns"]) == set(INSTALLMENT_COLUMNS)


def test_a_base_and_its_twin_are_one_resampling_unit(tmp_path: Path) -> None:
    records = [
        _record("nfe-0003", _without_last_item("nfe-0003")),
        _record("nfe-0099", _without_last_item("nfe-0003")),
    ]
    run_dir = _write_run(tmp_path, records)

    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)

    block = summary["items"]["overall"]
    assert block["recall"]["value"] < 1.0
    low, high = block["recall"]["ci95"]
    assert low == high == block["recall"]["value"]
    assert block["precision"]["ci95"] == [1.0, 1.0]


def test_a_run_of_only_refused_cases_has_null_row_values_and_intervals() -> None:
    grades = [_grade("a", status="refused", correct=False), _grade("b", status="truncated")]

    summary = build_summary(grades, _meta() | {"resamples": 200}, None)

    for key in ("items", "installments"):
        block = summary[key]["overall"]
        assert (block["true_rows"], block["predicted_rows"], block["matched"]) == (0, 0, 0)
        for name in ("precision", "recall", "f1", "all_values_correct"):
            assert block[name] == {"value": None, "ci95": None}, (key, name)
        for entry in block["columns"].values():
            assert (entry["n"], entry["rate"], entry["ci95"]) == (0, None, None)
        assert block["order_preserved"] == {"cases": 0, "n": 0}


def test_order_preserved_counts_cases_with_two_matched_pairs_and_never_gates() -> None:
    kept = _items_block(3, 3) | {"matched": 3, "order_preserved": True}
    swapped = _items_block(3, 3) | {"matched": 3, "order_preserved": False}
    single = _items_block(1, 1) | {"matched": 1, "order_preserved": True}
    grades = [
        _grade("a", items=kept),
        _grade("b", items=swapped),
        _grade("c", items=single),
        _grade("d", items=None),
    ]

    block = build_summary(grades, _meta() | {"resamples": 50}, None)["items"]["overall"]

    assert block["order_preserved"] == {"cases": 1, "n": 2}
    assert block["matched"] == 7


def test_the_pinned_interval_of_a_three_case_run_at_10000_resamples() -> None:
    def case(case_id: str, matched: int, predicted: int, true: int) -> dict[str, Any]:
        block = _items_block(matched, true) | {
            "matched": matched,
            "predicted_rows": predicted,
            "true_rows": true,
            "rows_all_correct": matched,
        }
        return _grade(case_id, items=block)

    grades = [case("a", 3, 3, 4), case("b", 5, 6, 6), case("c", 2, 2, 2)]

    block = build_summary(grades, _meta(), None)["items"]["overall"]

    # computed once on the implementation: a change of stream, rank rule or rounding fails here
    assert block["precision"] == {"value": 0.9091, "ci95": [0.8333, 1.0]}
    assert block["recall"] == {"value": 0.8333, "ci95": [0.75, 1.0]}
    assert block["columns"]["total"]["ci95"] == [0.75, 1.0]


# --- every item and installment column of field_by_slice has a bootstrap interval (D-44) ---------


def _row_columns(summary: dict[str, Any], scope: str, slice_id: str) -> dict[str, Any]:
    entries = summary["field_by_slice"][scope][slice_id]
    return {name: entry for name, entry in entries.items() if name not in FIELDS}


def test_a_slice_column_carries_the_invoice_count_and_an_interval(tmp_path: Path) -> None:
    ids = ["nfe-0005", "nfe-0006"]
    records = [_record(ids[0], _truth_invoice(ids[0])), _record(ids[1], _without_last_item(ids[1]))]
    run_dir = _write_run(tmp_path, records)

    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)

    both = [
        (scope, slice_id)
        for scope, slices in summary["field_by_slice"].items()
        for slice_id, entries in slices.items()
        if entries["item.total"]["invoices"] == 2
    ]
    assert both, "no slice holds both cases"
    scope, slice_id = both[0]
    total = summary["field_by_slice"][scope][slice_id]["item.total"]
    assert set(total) == {"correct", "n", "rate", "invoices", "ci95"}
    assert total["n"] > 0
    assert len(total["ci95"]) == 2 and total["ci95"][0] <= total["ci95"][1]


def test_a_base_and_its_twin_in_a_slice_are_one_invoice_with_a_point_interval() -> None:
    grades = [
        g | {"items": _items_block(2, 3), "installments": None}
        for g in _dataset_grades()
        if g["case_id"] in ("nfe-0003", "nfe-0099")
    ]

    summary = build_summary(grades, _full_meta() | {"resamples": 200}, None)

    total = summary["field_by_slice"]["test"]["alphanumeric_cnpj=yes"]["item.total"]
    assert (total["correct"], total["n"], total["invoices"]) == (4, 6, 1)
    assert total["ci95"][0] == total["ci95"][1] == total["rate"]


def test_a_run_of_only_refused_cases_has_null_slice_intervals_and_zero_counts() -> None:
    grades = [
        g | {"status": "refused", "correct": False}
        for g in _dataset_grades()
        if g["split"] == "test" and g["rendering"] == "text"
    ]

    summary = build_summary(grades, _full_meta() | {"resamples": 200}, None)

    for scope in summary["field_by_slice"]:
        for slice_id in PINNED_SLICES:
            columns = _row_columns(summary, scope, slice_id)
            assert len(columns) == len(ITEM_COLUMNS) + len(INSTALLMENT_COLUMNS)
            for entry in columns.values():
                assert (entry["n"], entry["invoices"]) == (0, 0)
                assert (entry["rate"], entry["ci95"]) == (None, None)


def test_every_field_by_slice_entry_has_an_interval() -> None:
    fields = dict.fromkeys(FIELDS, True)
    grades = [
        g | {"fields": dict(fields), "items": _items_block(2, 3), "installments": None}
        for g in _dataset_grades()
    ]

    summary = build_summary(grades, _full_meta() | {"resamples": 200}, None)

    seen = 0
    for scope, slices in summary["field_by_slice"].items():
        for slice_id, entries in slices.items():
            for name, entry in entries.items():
                assert "ci95" in entry, (scope, slice_id, name)
                if entry["n"] > 0:
                    assert len(entry["ci95"]) == 2, (scope, slice_id, name)
                    assert entry["ci95"][0] <= entry["ci95"][1], (scope, slice_id, name)
                seen += 1
    assert seen == 3 * 24 * (27 + len(ITEM_COLUMNS) + len(INSTALLMENT_COLUMNS))
    total = summary["field_by_slice"]["test"]["alphanumeric_cnpj=yes"]["item.total"]
    assert isinstance(total["ci95"][0], float) and isinstance(total["ci95"][1], float)
    assert (
        summary["field_by_slice"]["test"]["alphanumeric_cnpj=yes"]["installment.amount"]["ci95"]
        is None
    )


def test_a_slice_interval_depends_on_no_other_slice() -> None:
    grades = [
        g | {"items": _items_block(2, 3), "installments": None}
        for g in _dataset_grades()
        if g["split"] == "test"
    ]
    reversed_ = list(reversed(grades))

    forward = build_summary(grades, _meta() | {"resamples": 100}, None)
    backward = build_summary(reversed_, _meta() | {"resamples": 100}, None)

    assert forward["field_by_slice"] == backward["field_by_slice"]


def test_the_summary_records_the_resample_count_and_the_slice_interval_method() -> None:
    grades = [_grade("a", split="test", items=_items_block(2, 3))]

    summary = build_summary(grades, _meta() | {"resamples": 37}, None)

    assert summary["stats"]["bootstrap"]["resamples"] == 37
    assert "bootstrap" in summary["stats"]["field_by_slice"]
    assert "wilson" in summary["stats"]["field_by_slice"]


# --- latency by nearest rank, the items section and byte-stable regrades -------------------------


def _latency_of(values: list[int]) -> dict[str, Any]:
    grades = [_grade(f"c{index}", model_latency_ms=value) for index, value in enumerate(values)]
    return _latency(grades, "model_latency_ms")


def test_latency_is_min_nearest_rank_p50_and_p95_max_and_n() -> None:
    assert _latency_of([1000, 2000, 3000]) == {
        "min": 1000,
        "p50": 2000,
        "p95": 3000,
        "max": 3000,
        "n": 3,
    }
    assert _latency_of([]) == {"min": None, "p50": None, "p95": None, "max": None, "n": 0}
    two = _latency_of([1200, 34])
    assert (two["p50"], two["p95"]) == (34, 1200)


def test_latency_percentiles_are_well_defined_on_ties_and_a_single_value() -> None:
    assert _latency_of([7, 7, 7, 7]) == {"min": 7, "p50": 7, "p95": 7, "max": 7, "n": 4}
    assert _latency_of([5]) == {"min": 5, "p50": 5, "p95": 5, "max": 5, "n": 1}
    twenty = _latency_of(list(range(1, 21)))
    assert (twenty["p50"], twenty["p95"]) == (10, 19)


def test_a_replay_reports_the_original_model_latency_not_its_own_wall_time() -> None:
    recorded = [
        _grade("a", latency_ms=5000, model_latency_ms=1200, cache_hits=0, answered_attempts=1),
        _grade("b", latency_ms=4000, model_latency_ms=800, cache_hits=0, answered_attempts=1),
    ]
    replayed = [
        _grade("a", latency_ms=3, model_latency_ms=1200, cache_hits=1, answered_attempts=1),
        _grade("b", latency_ms=2, model_latency_ms=800, cache_hits=1, answered_attempts=1),
    ]

    first = build_summary(recorded, _meta(), None)
    again = build_summary(replayed, _meta(), None)

    assert first["model_latency_ms"] == again["model_latency_ms"]
    assert again["cache"]["hit_rate"] == 1.0
    assert again["latency_ms"]["max"] == 3
    assert "median" not in again["model_latency_ms"]


def _text_without_graded_at(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    assert '"graded_at"' in text
    return re.sub(r'"graded_at": "[^"]*"', '"graded_at": ""', text)


def _split_run(tmp_path: Path) -> Path:
    ids = ["nfe-0003", "nfe-0005", "nfe-0006", "nfe-0099"]
    records = [
        _record(ids[0], _truth_invoice(ids[0])),
        _record(ids[1], _truth_invoice(ids[1])),
        _record(ids[2], _without_last_item(ids[2])),
        _record(ids[3], _without_last_item(ids[0])),
    ]
    return _write_run(tmp_path, records)


def test_a_regrade_is_byte_identical_apart_from_graded_at(tmp_path: Path) -> None:
    run_dir = _split_run(tmp_path)

    first = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)
    write_summary(run_dir, first)
    one = _text_without_graded_at(run_dir / "summary.json")
    markdown = (run_dir / "summary.md").read_text(encoding="utf-8")
    second = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)
    write_summary(run_dir, second)

    assert _text_without_graded_at(run_dir / "summary.json") == one
    assert (run_dir / "summary.md").read_text(encoding="utf-8") == markdown


def test_shuffled_lines_of_cases_jsonl_give_a_byte_identical_summary(tmp_path: Path) -> None:
    run_dir = _split_run(tmp_path)
    cases = run_dir / "cases.jsonl"
    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)
    write_summary(run_dir, summary)
    straight = _text_without_graded_at(run_dir / "summary.json")
    lines = cases.read_text(encoding="utf-8").splitlines(keepends=True)
    cases.write_text("".join(reversed(lines)), encoding="utf-8")

    shuffled = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)
    write_summary(run_dir, shuffled)

    assert _text_without_graded_at(run_dir / "summary.json") == straight
    slices = shuffled["field_by_slice"]["test"]
    assert any(
        entry.get("ci95") is not None
        for entries in slices.values()
        for name, entry in entries.items()
        if name.startswith("item.")
    ), "the per-slice draw must be inside the byte check"


def test_the_same_inputs_and_resamples_give_equal_intervals(tmp_path: Path) -> None:
    run_dir = _split_run(tmp_path)

    first = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)
    second = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA, resamples=200)

    assert first["items"] == second["items"]
    assert first["installments"] == second["installments"]
    assert first["field_by_slice"] == second["field_by_slice"]


def test_summary_md_prints_model_latency_and_an_items_section() -> None:
    grades = [
        _grade("a", split="test", model_latency_ms=1200, items=_items_block(2, 3)),
        _grade("b", split="dev", model_latency_ms=800, items=_items_block(3, 3)),
        _grade("c", split="test", model_latency_ms=3000, items=None),
    ]

    text = render_markdown(build_summary(grades, _meta() | {"resamples": 50}, None))

    assert "- model latency: p50 1200 ms, p95 3000 ms (original latency, also on a replay)" in text
    section = text[text.index("## Items") :].split("\n## ")[0]
    header = section.splitlines()[2]
    for column in ("precision", "recall", "F1", "all values correct"):
        assert column in header
    for scope in ("| overall |", "| test |", "| dev (dev) |"):
        assert scope in section
    assert text.index("## Items") < text.index("## Silent errors")


def test_a_run_without_model_latency_prints_a_dash() -> None:
    text = render_markdown(build_summary([_grade("a")], _meta(), None))

    assert "- model latency: -" in text
    assert "## Items" in text
