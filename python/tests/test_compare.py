"""EVAL-08: `carimbo-evals compare` reads two summary.json files and nothing else."""

from __future__ import annotations

import json
from decimal import Decimal
from pathlib import Path
from typing import Any

from typer.testing import CliRunner

from carimbo_evals.cli import app
from carimbo_evals.corruptions import stored_record
from carimbo_evals.grader import grade_run
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.summary import write_summary

REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET = REPO_ROOT / "data" / "dataset"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
EXACT = Decimal("0.00")


def _truth_invoice(case_id: str) -> dict[str, Any]:
    return invoice_from_xml(DATASET / f"{case_id}.xml")


def _graded_run(root: Path, name: str, invoices: dict[str, dict[str, Any]]) -> Path:
    """Store one record per invoice, grade the run over ``data/dataset`` and write its summary."""
    run_dir = root / name
    run_dir.mkdir()
    records = [stored_record(case_id, invoice) for case_id, invoice in invoices.items()]
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for r in records),
        encoding="utf-8",
    )
    summary = grade_run(run_dir, DATASET, tolerance=EXACT, schema_path=SCHEMA)
    write_summary(run_dir, summary)
    return run_dir


def _invoke(*args: str) -> Any:
    return CliRunner().invoke(app, ["compare", *args])


def _regression_pair(tmp_path: Path) -> tuple[Path, Path]:
    wrong = _truth_invoice("nfe-0002")
    wrong["totals"]["invoice_total"] = format(Decimal(wrong["totals"]["invoice_total"]) + 1, "f")
    first = _graded_run(tmp_path, "first", {"nfe-0002": _truth_invoice("nfe-0002")})
    second = _graded_run(tmp_path, "second", {"nfe-0002": wrong})
    return first, second


def test_tracer_compare_names_a_regressed_case_and_its_flipped_value(tmp_path: Path) -> None:
    first, second = _regression_pair(tmp_path)

    result = _invoke("--first", str(first), "--second", str(second))

    assert result.exit_code == 0, result.output
    assert "regressed" in result.output
    assert "nfe-0002" in result.output
    assert "totals.invoice_total" in result.output
    assert "totals.invoice_total became_wrong (digit_substitution)" in result.output


def test_tracer_compare_needs_no_cases_jsonl(tmp_path: Path) -> None:
    first, second = _regression_pair(tmp_path)
    with_cases = _invoke("--first", str(first), "--second", str(second))

    (first / "cases.jsonl").unlink()
    (second / "cases.jsonl").unlink()
    without_cases = _invoke("--first", str(first), "--second", str(second))

    assert without_cases.exit_code == 0, without_cases.output
    assert without_cases.output == with_cases.output


def test_tracer_a_different_grader_version_is_refused_with_the_regrade_hint(
    tmp_path: Path,
) -> None:
    first, second = _regression_pair(tmp_path)
    summary = json.loads((second / "summary.json").read_text(encoding="utf-8"))
    summary["grader_version"] = "grader-000"
    (second / "summary.json").write_text(json.dumps(summary), encoding="utf-8")

    result = _invoke("--first", str(first), "--second", str(second))

    assert result.exit_code == 2
    assert "grader-000" in result.output
    assert "grader-003" in result.output
    assert "regrade both runs with the current grader" in result.output
