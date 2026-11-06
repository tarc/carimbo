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
    "items columns is a list": lambda s: _break_case(s, items={"columns": []}),
    "items is a list": lambda s: _break_case(s, items=[]),
    "an infinite item count": lambda s: _break_case(
        s, items={"columns": {"code": {"correct": float("inf"), "n": 1}}}
    ),
    "a NaN item count": lambda s: _break_case(
        s, items={"columns": {"code": {"correct": 1, "n": float("nan")}}}
    ),
    "a string item count": lambda s: _break_case(
        s, items={"columns": {"code": {"correct": "x", "n": 1}}}
    ),
    "a boolean installment count": lambda s: _break_case(
        s, installments={"columns": {"amount": {"correct": True, "n": 1}}}
    ),
    "installments columns is a string": lambda s: _break_case(s, installments={"columns": "x"}),
    "fields is a list": lambda s: _break_case(s, fields=[]),
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


# --- the verdict, the case lists, what changed and the field deltas -----------------------------


def _compare(first: list[dict[str, Any]], second: list[dict[str, Any]], **kwargs: Any) -> Any:
    return compare.compare_summaries(_summary(first, **kwargs), _summary(second, **kwargs))


def _discordant(first_only: int, second_only: int) -> tuple[list[dict[str, Any]], ...]:
    """Two case lists whose test text-layer cases differ in exactly the given counts."""
    one: list[dict[str, Any]] = []
    two: list[dict[str, Any]] = []
    for index in range(first_only):
        one.append(_case(f"nfe-a{index:03d}", True))
        two.append(_case(f"nfe-a{index:03d}", False, (("number", "untagged"),)))
    for index in range(second_only):
        one.append(_case(f"nfe-b{index:03d}", False, (("number", "untagged"),)))
        two.append(_case(f"nfe-b{index:03d}", True))
    return one, two


@pytest.mark.parametrize(
    ("b", "c", "p", "verdict"),
    [
        (5, 0, "1/16", "inconclusive"),
        (6, 0, "1/32", "significant"),
        (0, 6, "1/32", "significant"),
        (3, 3, "1", "inconclusive"),
    ],
)
def test_the_exact_mcnemar_verdict_at_its_boundary(b: int, c: int, p: str, verdict: str) -> None:
    one, two = _discordant(b, c)

    result = _compare(one, two)["verdict"]

    assert (result["b"], result["c"], result["n"]) == (b, c, b + c)
    assert result["p"] == p
    assert result["verdict"] == verdict
    assert result["test"] == "exact_mcnemar"
    assert result["alpha"] == "0.05"
    assert result["row"] == "test text"
    assert result["cases"] == b + c


def test_a_p_of_exactly_one_twentieth_is_not_significant(monkeypatch: pytest.MonkeyPatch) -> None:
    from fractions import Fraction

    monkeypatch.setattr(compare, "mcnemar_exact", lambda b, c: Fraction(1, 20))
    one, two = _discordant(2, 0)

    result = _compare(one, two)["verdict"]

    assert result["p"] == "1/20"
    assert result["verdict"] == "inconclusive"


def test_shared_cases_without_a_discordant_pair_are_no_change() -> None:
    cases = [_case("nfe-0001", True), _case("nfe-0002", False, (("number", "untagged"),))]

    result = _compare(cases, cases)["verdict"]

    assert (result["b"], result["c"], result["p"]) == (0, 0, "1")
    assert result["verdict"] == "no change"


def test_no_shared_text_layer_case_is_inconclusive_with_a_reason_and_never_no_change() -> None:
    scans = [_case("nfe-0001", True, rendering="scan"), _case("nfe-0002", True, rendering=None)]
    flipped = [_case("nfe-0001", False, rendering="scan"), _case("nfe-0002", False, rendering=None)]

    result = _compare(scans, flipped)["verdict"]

    assert result["verdict"] == "inconclusive"
    assert result["reason"] == "no shared text-layer case"
    assert result["row"] is None
    assert (result["b"], result["c"], result["cases"]) == (0, 0, 0)


def test_no_shared_case_at_all_still_prints_the_excluded_counts_and_exits_0(
    tmp_path: Path,
) -> None:
    first, second = _pair(
        tmp_path, _summary([_case("nfe-0001", True)]), _summary([_case("nfe-0002", True)])
    )

    result = _invoke("--first", str(first), "--second", str(second))

    assert result.exit_code == 0, result.output
    assert "no shared text-layer case" in result.output
    assert "excluded: 2" in result.output
    assert "nfe-0001" in result.output
    assert "nfe-0002" in result.output


def test_the_row_is_the_test_split_then_the_text_layer_splits_and_never_image_only() -> None:
    mixed_one = [
        _case("nfe-0001", True, split="dev"),
        _case("nfe-0002", True, split="test"),
        _case("nfe-0003", True, split="test", rendering="scan"),
    ]
    mixed_two = [
        _case("nfe-0001", False, (("number", "untagged"),), split="dev"),
        _case("nfe-0002", True, split="test"),
        _case("nfe-0003", False, (("number", "untagged"),), split="test", rendering="scan"),
    ]
    test_row = _compare(mixed_one, mixed_two)["verdict"]
    assert (test_row["row"], test_row["cases"], test_row["b"]) == ("test text", 1, 0)

    dev_only = _compare(mixed_one[:1] + mixed_one[2:], mixed_two[:1] + mixed_two[2:])["verdict"]
    assert (dev_only["row"], dev_only["cases"], dev_only["b"]) == ("dev text", 1, 1)

    several = [_case("nfe-0001", True, split="dev"), _case("nfe-0002", True, split="holdout")]
    several_two = [
        _case("nfe-0001", True, split="dev"),
        _case("nfe-0002", True, split="holdout"),
    ]
    assert _compare(several, several_two)["verdict"]["row"] == "dev,holdout text"


def test_regressed_worsened_and_improved_follow_the_case_rules() -> None:
    wrong = ("totals.invoice_total", "digit_substitution")
    other = ("number", "untagged")
    first = [
        _case("nfe-0001", True),
        _case("nfe-0002", False, (wrong,)),
        _case("nfe-0003", False, (wrong, other)),
        _case("nfe-0004", False, (wrong,)),
        _case("nfe-0005", True),
        _case("nfe-0006", True),
        _case("nfe-0007", False, (wrong,)),
    ]
    second = [
        _case("nfe-0001", False, (wrong,)),
        _case("nfe-0002", False, (wrong, other)),
        _case("nfe-0003", False, (wrong,)),
        _case("nfe-0004", True),
        _case("nfe-0005", True),
        _case("nfe-0006", None),
        _case("nfe-0007", False, (other,)),
    ]

    result = _compare(first, second)

    assert [e["case_id"] for e in result["regressed"]] == ["nfe-0001"]
    assert [e["case_id"] for e in result["worsened"]] == ["nfe-0002"]
    assert [e["case_id"] for e in result["improved"]] == ["nfe-0003", "nfe-0004"]
    assert [e["case_id"] for e in result["not_comparable"]] == ["nfe-0006"]
    listed = {
        e["case_id"]
        for key in ("regressed", "worsened", "improved", "not_comparable")
        for e in result[key]
    }
    assert "nfe-0005" not in listed
    # equal wrong counts, both not correct, are in no list
    assert "nfe-0007" not in listed
    assert result["regressed"][0]["flipped"] == [
        {"path": "totals.invoice_total", "tag": "digit_substitution", "change": "became_wrong"}
    ]
    assert result["worsened"][0]["flipped"] == [
        {"path": "number", "tag": "untagged", "change": "became_wrong"}
    ]
    assert result["improved"][0]["flipped"] == [
        {"path": "number", "tag": "untagged", "change": "became_right"}
    ]


def test_a_case_that_became_refused_or_truncated_is_a_status_change_not_an_improvement() -> None:
    wrong = ("totals.invoice_total", "digit_substitution")
    other = ("number", "untagged")
    first = [
        _case("nfe-0001", False, (wrong, other), status="validation_failed"),
        _case("nfe-0002", False, (), status="refused"),
        _case("nfe-0003", False, (), status="truncated"),
        _case("nfe-0004", False, (wrong, other), status="validation_failed"),
        _case("nfe-0005", False, (), status="refused"),
    ]
    second = [
        _case("nfe-0001", False, (), status="refused"),
        _case("nfe-0002", False, (wrong,), status="validation_failed"),
        _case("nfe-0003", False, (), status="truncated"),
        _case("nfe-0004", False, (wrong,), status="success"),
        _case("nfe-0005", False, (), status="truncated"),
    ]

    result = _compare(first, second)

    assert [e["case_id"] for e in result["status_changed"]] == ["nfe-0001", "nfe-0002", "nfe-0005"]
    assert result["status_changed"][0]["first_status"] == "validation_failed"
    assert result["status_changed"][0]["second_status"] == "refused"
    # both graded: still ordered by wrong values; same non-graded status: in no list
    assert [e["case_id"] for e in result["improved"]] == ["nfe-0004"]
    assert result["worsened"] == []
    listed = {e["case_id"] for key in ("regressed", "worsened", "improved") for e in result[key]}
    assert "nfe-0003" not in listed
    assert "nfe-0003" not in {e["case_id"] for e in result["status_changed"]}
    assert "## status changed (3)" in compare.render_markdown(result)


def test_an_infrastructure_failure_is_not_comparable_and_never_regressed() -> None:
    first = [_case("nfe-0001", True), _case("nfe-0002", None)]
    second = [_case("nfe-0001", None, status="harness_error"), _case("nfe-0002", True)]

    result = _compare(first, second)

    assert [e["case_id"] for e in result["not_comparable"]] == ["nfe-0001", "nfe-0002"]
    assert result["regressed"] == []
    assert result["not_comparable"][0]["second_status"] == "harness_error"
    # out of the McNemar counts
    assert (result["verdict"]["b"], result["verdict"]["c"], result["verdict"]["cases"]) == (0, 0, 0)


def test_swapping_the_runs_swaps_regressed_with_improved_and_b_with_c() -> None:
    one, two = _discordant(4, 1)

    forward, backward = _compare(one, two), _compare(two, one)

    assert [e["case_id"] for e in forward["regressed"]] == [
        e["case_id"] for e in backward["improved"]
    ]
    assert [e["case_id"] for e in forward["improved"]] == [
        e["case_id"] for e in backward["regressed"]
    ]
    assert (forward["verdict"]["b"], forward["verdict"]["c"]) == (4, 1)
    assert (backward["verdict"]["b"], backward["verdict"]["c"]) == (1, 4)
    assert forward["verdict"]["p"] == backward["verdict"]["p"]


def test_every_case_list_is_sorted_by_case_id_and_every_flipped_list_by_path() -> None:
    paths = (("totals.icms_base", "untagged"), ("number", "untagged"), ("series", "untagged"))
    first = [_case(case_id, True) for case_id in ("nfe-0009", "nfe-0003", "nfe-0006")]
    second = [_case(case_id, False, paths) for case_id in ("nfe-0006", "nfe-0009", "nfe-0003")]

    regressed = _compare(first, second)["regressed"]

    assert [e["case_id"] for e in regressed] == ["nfe-0003", "nfe-0006", "nfe-0009"]
    assert [f["path"] for f in regressed[0]["flipped"]] == [
        "number",
        "series",
        "totals.icms_base",
    ]


def test_model_and_prompt_show_as_changed_and_equal_values_do_not() -> None:
    cases = [_case("nfe-0001", True)]
    first = _summary(cases, models_requested=["claude-haiku-4-5"], prompt_versions=["extract-003"])
    second = _summary(
        cases, models_requested=["claude-sonnet-5-5"], prompt_versions=["extract-003"]
    )

    changed = compare.compare_summaries(first, second)["changed"]

    assert changed == {
        "models_requested": {"first": ["claude-haiku-4-5"], "second": ["claude-sonnet-5-5"]}
    }


def test_different_case_sets_compare_on_the_intersection_and_count_what_was_left_out() -> None:
    first = [_case("nfe-0001", True), _case("nfe-0002", True), _case("nfe-0003", True)]
    second = [_case("nfe-0002", False, (("number", "untagged"),)), _case("nfe-0004", True)]

    result = _compare(first, second)

    assert result["cases"] == {
        "shared": 1,
        "excluded": 3,
        "only_first": ["nfe-0001", "nfe-0003"],
        "only_second": ["nfe-0004"],
    }
    assert [e["case_id"] for e in result["regressed"]] == ["nfe-0002"]


def _with_grades(
    case: dict[str, Any],
    fields: dict[str, bool],
    items: dict[str, tuple[int, int]] | None = None,
    installments: dict[str, tuple[int, int]] | None = None,
) -> dict[str, Any]:
    case["fields"] = fields
    if items is not None:
        case["items"] = {"columns": {k: {"correct": c, "n": n} for k, (c, n) in items.items()}}
    if installments is not None:
        case["installments"] = {
            "columns": {k: {"correct": c, "n": n} for k, (c, n) in installments.items()}
        }
    return case


def test_field_deltas_count_header_fields_and_sum_item_and_installment_columns() -> None:
    first = [
        _with_grades(
            _case("nfe-0001", True), {"access_key": True}, {"total": (3, 3)}, {"amount": (2, 2)}
        ),
        _with_grades(_case("nfe-0002", True), {"access_key": True}, {"total": (2, 2)}),
        _with_grades(_case("nfe-0003", None), {"access_key": False}, {"total": (0, 5)}),
    ]
    second = [
        _with_grades(
            _case("nfe-0001", False, (("access_key", "untagged"),)),
            {"access_key": False},
            {"total": (1, 3)},
            {"amount": (1, 2)},
        ),
        _with_grades(_case("nfe-0002", True), {"access_key": True}, {"total": (2, 2)}),
        _with_grades(_case("nfe-0003", True), {"access_key": True}, {"total": (5, 5)}),
    ]

    deltas = _compare(first, second)["field_deltas"]

    # nfe-0003 is not comparable (null in the first run): out of every delta
    assert deltas["access_key"] == {
        "first": {"correct": 2, "n": 2},
        "second": {"correct": 1, "n": 2},
        "delta": -1,
    }
    assert deltas["item.total"] == {
        "first": {"correct": 5, "n": 5},
        "second": {"correct": 3, "n": 5},
        "delta": -2,
    }
    assert deltas["installment.amount"]["delta"] == -1
    assert deltas["installment.amount"]["first"] == {"correct": 2, "n": 2}
    assert deltas["number"] == {
        "first": {"correct": 0, "n": 0},
        "second": {"correct": 0, "n": 0},
        "delta": 0,
    }
    header = [name for name in deltas if not name.startswith(("item.", "installment."))]
    assert len(header) == 27


def test_the_report_states_the_scope_and_compare_json_holds_every_section(
    tmp_path: Path,
) -> None:
    one, two = _discordant(6, 0)
    first, second = _pair(tmp_path, _summary(one), _summary(two))
    out = tmp_path / "out"

    result = _invoke("--first", str(first), "--second", str(second), "--out", str(out))

    assert result.exit_code == 0, result.output
    assert "case sampling only" in result.output
    assert "significant" in result.output
    assert "1/32" in result.output
    assert "case sampling only" in (out / "compare.md").read_text(encoding="utf-8")
    written = json.loads((out / "compare.json").read_text(encoding="utf-8"))
    for key in (
        "verdict",
        "regressed",
        "worsened",
        "improved",
        "not_comparable",
        "changed",
        "field_deltas",
    ):
        assert key in written
    assert written["verdict"]["scope"] == "case sampling only, not run-to-run model variation"
    assert written["verdict"]["p_value"] == 0.0312


def test_every_verdict_label_is_printed() -> None:
    for first_only, second_only, label in ((6, 0, "significant"), (5, 0, "inconclusive")):
        one, two = _discordant(first_only, second_only)
        text = compare.render_markdown(_compare(one, two))
        assert label in text
        assert "case sampling only" in text
    cases = [_case("nfe-0001", True)]
    assert "no change" in compare.render_markdown(_compare(cases, cases))
