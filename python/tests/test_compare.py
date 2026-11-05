"""EVAL-08: `carimbo-evals compare` reads two summary.json files and nothing else."""

from __future__ import annotations

import json
import subprocess
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest
import typer.main
from typer.testing import CliRunner

from carimbo_evals import compare
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


# --- in-memory summaries -------------------------------------------------------------------------

GRADER = "grader-003"
MANIFEST = "a" * 64
DECISION_SUMMARY = REPO_ROOT / "evals" / "results" / "decisions-20261022T034741Z" / "summary.json"
OLD_SUMMARY = REPO_ROOT / "evals" / "results" / "skeleton-20261015T090949Z" / "summary.json"


def _case(
    case_id: str,
    correct: bool | None,
    wrong: tuple[tuple[str, str], ...] = (),
    *,
    status: str | None = None,
    split: str = "test",
    rendering: str | None = "text",
) -> dict[str, Any]:
    """A minimal graded case: ``wrong`` holds (path, tag) pairs."""
    if status is None:
        status = "infrastructure_failure" if correct is None else "success"
    return {
        "case_id": case_id,
        "status": status,
        "correct": correct,
        "split": split,
        "base_case": case_id,
        "rendering": rendering,
        "wrong": [{"path": path, "tag": tag} for path, tag in wrong],
    }


def _summary(
    cases: list[dict[str, Any]],
    *,
    run_id: str = "run",
    grader_version: str = GRADER,
    tolerance: str = "0.00",
    manifest: str | None = MANIFEST,
    **config: list[str],
) -> dict[str, Any]:
    keys = (
        "models_requested",
        "models_returned",
        "prompt_versions",
        "max_repairs",
        "cache_modes",
        "replicate_salts",
        "schema_sha256s",
        "pricing_versions",
    )
    return {
        "summary_version": 4,
        "grader_version": grader_version,
        "run_id": run_id,
        "tolerance": tolerance,
        "dataset": None
        if manifest is None
        else {"name": "dataset", "version": "dataset-001", "manifest_sha256": manifest},
        "config": {key: config.get(key, []) for key in keys},
        "cases": cases,
    }


def _store(root: Path, name: str, summary: Any) -> Path:
    """Write ``summary`` as the summary.json of a new run directory."""
    run_dir = root / name
    run_dir.mkdir()
    (run_dir / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
    return run_dir


def _pair(root: Path, first: dict[str, Any], second: dict[str, Any]) -> tuple[Path, Path]:
    return _store(root, "first", first), _store(root, "second", second)


def _refused(result: Any) -> str:
    assert result.exit_code == 2, result.output
    assert "Traceback" not in result.output
    assert not any(line.startswith("Traceback") for line in result.output.splitlines())
    return str(result.output)


# --- the refusal contract ------------------------------------------------------------------------


def test_a_different_dataset_manifest_is_refused_and_both_hashes_are_named(tmp_path: Path) -> None:
    cases = [_case("nfe-0001", True)]
    first, second = _pair(tmp_path, _summary(cases), _summary(cases, manifest="b" * 64))

    output = _refused(_invoke("--first", str(first), "--second", str(second)))

    assert "a" * 64 in output
    assert "b" * 64 in output
    assert "regrade both runs with the current grader" in output


def test_a_different_tolerance_is_refused_but_zero_and_zero_point_zero_zero_are_equal(
    tmp_path: Path,
) -> None:
    cases = [_case("nfe-0001", True)]
    first, second = _pair(
        tmp_path, _summary(cases, tolerance="0.00"), _summary(cases, tolerance="0.01")
    )

    output = _refused(_invoke("--first", str(first), "--second", str(second)))
    assert "0.00" in output
    assert "0.01" in output

    same = tmp_path / "same"
    same.mkdir()
    zero, zero_cents = _pair(
        same, _summary(cases, tolerance="0"), _summary(cases, tolerance="0.00")
    )
    assert _invoke("--first", str(zero), "--second", str(zero_cents)).exit_code == 0


def test_a_decision_summary_is_refused_as_not_an_extraction_run(tmp_path: Path) -> None:
    valid = _store(tmp_path, "valid", _summary([_case("nfe-0001", True)]))

    for args in (
        ("--first", str(DECISION_SUMMARY), "--second", str(valid)),
        ("--first", str(valid), "--second", str(DECISION_SUMMARY)),
    ):
        assert "compare covers extraction runs only" in _refused(_invoke(*args))


def test_a_summary_from_an_older_grader_is_refused_with_the_regrade(tmp_path: Path) -> None:
    valid = _store(tmp_path, "valid", _summary([_case("nfe-0001", True)]))

    output = _refused(_invoke("--first", str(OLD_SUMMARY), "--second", str(valid)))

    assert "regrade both runs with the current grader" in output
    assert "summary version 3" in output


def _break(summary: dict[str, Any], key: str, value: Any) -> dict[str, Any]:
    summary[key] = value
    return summary


def _break_case(summary: dict[str, Any], **changes: Any) -> dict[str, Any]:
    summary["cases"][0].update(changes)
    return summary


def _drop_case_key(summary: dict[str, Any], key: str) -> dict[str, Any]:
    del summary["cases"][0][key]
    return summary


HOSTILE = {
    "root is a list": lambda s: [s],
    "cases is a dict": lambda s: _break(s, "cases", {"nfe-0001": {}}),
    "a case with no case_id": lambda s: _drop_case_key(s, "case_id"),
    "a case that is not an object": lambda s: _break(s, "cases", ["nfe-0001"]),
    "a wrong entry that is a string": lambda s: _break_case(s, wrong=["totals.invoice_total"]),
    "a wrong entry without a path": lambda s: _break_case(s, wrong=[{"tag": "untagged"}]),
    "a duplicate case id": lambda s: _break(s, "cases", s["cases"] * 2),
    "dataset is null": lambda s: _break(s, "dataset", None),
    "dataset without a manifest hash": lambda s: _break(s, "dataset", {"name": "dataset"}),
    "a tolerance that is not a decimal": lambda s: _break(s, "tolerance", "abc"),
    "a tolerance that is not finite": lambda s: _break(s, "tolerance", "NaN"),
    "a numeric tolerance": lambda s: _break(s, "tolerance", 0.01),
    "correct is a string": lambda s: _break_case(s, correct="yes"),
    "correct is a number": lambda s: _break_case(s, correct=1),
    "status is missing": lambda s: _drop_case_key(s, "status"),
    "config is a list": lambda s: _break(s, "config", []),
    "run_id is a number": lambda s: _break(s, "run_id", 7),
    "grader_version is missing": lambda s: {k: v for k, v in s.items() if k != "grader_version"},
    "an unknown summary version": lambda s: _break(s, "summary_version", 5),
    "a boolean summary version": lambda s: _break(s, "summary_version", True),
}


def test_a_hostile_or_malformed_summary_is_refused_without_a_traceback(tmp_path: Path) -> None:
    valid = _store(tmp_path, "valid", _summary([_case("nfe-0001", True)]))
    for label, damage in HOSTILE.items():
        broken = _store(
            tmp_path,
            f"broken-{len(list(tmp_path.iterdir()))}",
            damage(_summary([_case("nfe-0001", True)])),
        )
        for args in (
            ("--first", str(broken), "--second", str(valid)),
            ("--first", str(valid), "--second", str(broken)),
        ):
            output = _refused(_invoke(*args))
            assert "broken-" in output, label


def test_a_missing_path_invalid_json_and_an_empty_directory_are_refused(tmp_path: Path) -> None:
    valid = _store(tmp_path, "valid", _summary([_case("nfe-0001", True)]))
    invalid = tmp_path / "invalid"
    invalid.mkdir()
    (invalid / "summary.json").write_text("{not json", encoding="utf-8")
    empty = tmp_path / "empty"
    empty.mkdir()

    for bad in (tmp_path / "missing", invalid, empty):
        output = _refused(_invoke("--first", str(bad), "--second", str(valid)))
        assert str(bad) in output


def test_a_file_over_the_size_limit_is_refused_before_it_is_parsed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    first, second = _pair(
        tmp_path, _summary([_case("nfe-0001", True)]), _summary([_case("nfe-0001", True)])
    )
    monkeypatch.setattr(compare, "MAX_SUMMARY_BYTES", 10)

    output = _refused(_invoke("--first", str(first), "--second", str(second)))

    assert "larger than" in output


def test_the_command_has_no_override_option() -> None:
    command = typer.main.get_command(app).commands["compare"]  # type: ignore[attr-defined]
    assert {param.name for param in command.params} == {"first", "second", "out"}


def test_the_committed_fixtures_are_only_read() -> None:
    status = subprocess.run(
        ["git", "status", "--porcelain", "evals/results"],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    assert status.stdout == ""
