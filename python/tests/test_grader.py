"""EVAL-02: offline grading of stored runs, field by field, with typed failures kept honest."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import unicodedata
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest
from typer.testing import CliRunner

from carimbo_evals.cli import app
from carimbo_evals.grader import (
    GroundTruth,
    grade_case,
    grade_run,
    load_ground_truth,
    normalize_id,
    normalize_name,
)
from carimbo_evals.summary import write_summary

REPO_ROOT = Path(__file__).resolve().parents[2]
SKELETON = REPO_ROOT / "data" / "skeleton"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
SRC = REPO_ROOT / "python" / "src"
TOLERANCE = Decimal("0.01")
ALL_FIELDS = [
    "access_key",
    "number",
    "series",
    "issue_date",
    "issuer.cnpj",
    "issuer.name",
    "recipient.cnpj",
    "recipient.name",
    "total_amount",
]


def _truth(case_id: str = "case-001") -> GroundTruth:
    return load_ground_truth(SKELETON / f"{case_id}.xml")


def _invoice(truth: GroundTruth) -> dict[str, Any]:
    return {
        "access_key": truth.access_key,
        "number": truth.number,
        "series": truth.series,
        "issue_date": truth.issue_date,
        "issuer": {"cnpj": truth.issuer_cnpj, "name": truth.issuer_name},
        "recipient": {"cnpj": truth.recipient_cnpj, "name": truth.recipient_name},
        "total_amount": format(truth.total_amount, ".2f"),
    }


def _record(
    case_id: str,
    invoice: dict[str, Any] | None,
    *,
    status: str = "success",
    raw_output: str | None = None,
    cost: str | None = "0.0123",
    latency_ms: int = 1500,
) -> dict[str, Any]:
    if raw_output is None and invoice is not None:
        raw_output = json.dumps(invoice)
    return {
        "record_version": 1,
        "run_id": "run-grade",
        "case_id": case_id,
        "status": "completed",
        "request": {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "a" * 32},
        "http": {"status": 200, "wall_ms": latency_ms + 5, "error": None},
        "response": {
            "contract_version": "1",
            "case_id": case_id,
            "trace_id": "a" * 32,
            "effective": {
                "model": "claude-haiku-4-5",
                "prompt_version": "extract-001",
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
                "cache_read_tokens": 10,
                "cache_write_5m_tokens": 20,
                "cache_write_1h_tokens": 30,
            },
            "cost_usd": cost,
            "latency_ms": latency_ms,
            "stop_reason": "end_turn",
            "model_returned": "claude-haiku-4-5",
            "provider_message_id": "msg_1",
        },
    }


def _harness_error(case_id: str) -> dict[str, Any]:
    return {
        "record_version": 1,
        "run_id": "run-grade",
        "case_id": case_id,
        "status": "harness_error",
        "request": {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "c" * 32},
        "http": {"status": 502, "wall_ms": 7, "error": "HTTP 502"},
        "response": None,
    }


def _grade(record: dict[str, Any], truth: GroundTruth | None = None, tol: Decimal = TOLERANCE):
    return grade_case(record, truth or _truth(), tol)


def _write_run(tmp_path: Path, records: list[dict[str, Any]]) -> tuple[Path, Path]:
    """A run dir plus a cases dir where every record's case id has the case-001 ground truth."""
    run_dir = tmp_path / "run"
    cases_dir = tmp_path / "cases"
    run_dir.mkdir()
    cases_dir.mkdir()
    for record in records:
        shutil.copy(SKELETON / "case-001.xml", cases_dir / f"{record['case_id']}.xml")
    # Serialized exactly like runner.run_cases: ensure_ascii=False leaves U+2028, U+2029 and U+0085
    # unescaped inside strings, which is what the grader must survive.
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for r in records),
        encoding="utf-8",
    )
    return run_dir, cases_dir


# --- field grades ---------------------------------------------------------------------------


def test_matching_record_has_every_field_and_both_schema_grades_correct() -> None:
    truth = _truth()
    graded = _grade(_record("case-001", _invoice(truth)))
    assert graded["status"] == "success"
    assert graded["fields"] == dict.fromkeys(ALL_FIELDS, True)
    assert graded["schema_valid_jsonschema"] is True
    assert graded["schema_valid_pydantic"] is True


def _transpose(key: str) -> str:
    return key[:10] + key[11] + key[10] + key[12:]


@pytest.mark.parametrize(
    ("field", "mutate"),
    [
        ("access_key", lambda inv, t: inv.update(access_key=_transpose(t.access_key))),
        ("series", lambda inv, t: inv.update(series=t.series + 1)),
        ("number", lambda inv, t: inv.update(number=t.number + 1)),
        ("issue_date", lambda inv, t: inv.update(issue_date=_next_day(t.issue_date))),
        ("recipient.cnpj", lambda inv, t: inv["recipient"].update(cnpj="ZZZZZZZZZZZZ99")),
        ("issuer.name", lambda inv, t: inv["issuer"].update(name="OUTRA EMPRESA LTDA")),
    ],
)
def test_corrupted_fields_are_graded_wrong_and_only_that_field(field: str, mutate: Any) -> None:
    truth = _truth()
    invoice = _invoice(truth)
    mutate(invoice, truth)
    fields = _grade(_record("case-001", invoice))["fields"]
    assert fields[field] is False
    assert [name for name, ok in fields.items() if not ok] == [field]


def _next_day(iso: str) -> str:
    year, month, day = (int(part) for part in iso.split("-"))
    return f"{year:04d}-{month:02d}-{day + 1:02d}" if day < 28 else f"{year:04d}-{month:02d}-01"


def test_transposed_access_key_really_differs() -> None:
    key = _truth().access_key
    assert _transpose(key) != key  # guards the corruption above against a no-op


def test_printed_cnpj_form_is_graded_equal_to_the_plain_form() -> None:
    assert normalize_id("AB.1C2.D3E/0001-30") == normalize_id("AB1C2D3E000130")
    truth = _truth()
    invoice = _invoice(truth)
    plain = truth.issuer_cnpj
    invoice["issuer"]["cnpj"] = f"{plain[:2]}.{plain[2:5]}.{plain[5:8]}/{plain[8:12]}-{plain[12:]}"
    graded = _grade(_record("case-001", invoice))
    assert graded["fields"]["issuer.cnpj"] is True
    assert graded["schema_valid_jsonschema"] is False  # the printed form violates the pattern
    assert graded["schema_valid_pydantic"] is False


@pytest.mark.parametrize(
    "variant",
    [
        lambda n: unicodedata.normalize("NFD", n),
        lambda n: unicodedata.normalize("NFC", n),
        lambda n: n.lower(),
        lambda n: "  " + n.replace(" ", "   ") + " ",
        lambda n: n.replace(" ", ", ").replace("LTDA", "LTDA."),
    ],
)
def test_names_differing_only_in_form_case_spacing_or_punctuation_are_equal(variant: Any) -> None:
    truth = GroundTruth(
        **{**_truth().__dict__, "issuer_name": "INDÚSTRIA E COMÉRCIO SINTÉTICA LTDA"},
    )
    invoice = _invoice(truth)
    invoice["issuer"]["name"] = variant(truth.issuer_name)
    assert _grade(_record("case-001", invoice), truth)["fields"]["issuer.name"] is True


def test_normalize_name_keeps_accented_letters_distinct_from_their_bases() -> None:
    assert normalize_name("Comércio") != normalize_name("Comercio")


def test_total_one_cent_off_is_correct_two_cents_off_is_wrong_and_zero_tolerance_is_exact() -> None:
    truth = _truth()
    one_cent = {
        **_invoice(truth),
        "total_amount": format(truth.total_amount + Decimal("0.01"), ".2f"),
    }
    two_cents = {
        **_invoice(truth),
        "total_amount": format(truth.total_amount + Decimal("0.02"), ".2f"),
    }
    assert _grade(_record("case-001", one_cent))["fields"]["total_amount"] is True
    assert _grade(_record("case-001", two_cents))["fields"]["total_amount"] is False
    assert (
        _grade(_record("case-001", one_cent), tol=Decimal("0"))["fields"]["total_amount"] is False
    )


def test_total_delta_is_reported_as_a_decimal_string() -> None:
    truth = _truth()
    invoice = {
        **_invoice(truth),
        "total_amount": format(truth.total_amount + Decimal("1.00"), ".2f"),
    }
    assert _grade(_record("case-001", invoice))["total_delta"] == "1.00"


# --- statuses -------------------------------------------------------------------------------


def test_typed_failures_are_counted_but_never_graded_as_wrong(tmp_path: Path) -> None:
    truth = _truth()
    good = _record("case-a", _invoice(truth))
    wrong_total = _invoice(truth)
    wrong_total["total_amount"] = "0.00"
    records = [
        good,
        _record("case-b", _invoice(truth), status="refused", raw_output=None),
        _record("case-c", None, status="truncated", raw_output='{"access_key": "26'),
        _record("case-d", None, status="infrastructure_failure", raw_output=None, cost=None),
        _harness_error("case-e"),
        _record("case-f", None, status="schema_invalid", raw_output='{"access_key": 1}'),
        _record("case-g", wrong_total),
    ]
    run_dir, cases_dir = _write_run(tmp_path, records)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"] == {
        "success": 2,
        "refused": 1,
        "truncated": 1,
        "schema_invalid": 1,
        "infrastructure_failure": 1,
        "harness_error": 1,
        "total": 7,
    }
    by_case = {c["case_id"]: c for c in summary["cases"]}
    for case_id in ("case-b", "case-c", "case-d", "case-e"):
        assert "fields" not in by_case[case_id]
    # Denominators: 2 successes + 1 schema_invalid. The schema_invalid case is wrong everywhere.
    assert summary["field_accuracy"]["access_key"] == {"correct": 2, "n": 3}
    assert summary["field_accuracy"]["total_amount"] == {"correct": 1, "n": 3}
    assert by_case["case-f"]["fields"] == dict.fromkeys(ALL_FIELDS, False)
    assert by_case["case-f"]["schema_valid_jsonschema"] is False
    assert by_case["case-f"]["schema_valid_pydantic"] is False


def test_the_last_record_per_case_wins(tmp_path: Path) -> None:
    truth = _truth()
    records = [_harness_error("case-a"), _record("case-a", _invoice(truth))]
    run_dir, cases_dir = _write_run(tmp_path, records)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"]["total"] == 1
    assert summary["counts"]["success"] == 1


def test_raw_output_with_unicode_line_separators_is_graded(tmp_path: Path) -> None:
    truth = _truth()
    separators = "a\u2028b\u2029c\x85d"
    records = [
        _record("case-a", _invoice(truth), raw_output=json.dumps({"note": separators})),
        _harness_error("case-b"),
        _record("case-c", _invoice(truth)),
    ]
    records[1]["http"]["error"] = f"HTTP 502 {separators}"
    run_dir, cases_dir = _write_run(tmp_path, records)
    text = (run_dir / "cases.jsonl").read_text(encoding="utf-8")
    assert "\u2028" in text  # the file really holds the raw character, not an escape
    assert len(text.split("\n")) == len(records) + 1  # one record per newline-delimited line
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"]["total"] == len(records)


def test_a_corrupt_middle_line_fails_grading_but_a_torn_final_line_is_ignored(
    tmp_path: Path,
) -> None:
    truth = _truth()
    good = [_record("case-a", _invoice(truth)), _record("case-b", _invoice(truth))]
    run_dir, cases_dir = _write_run(tmp_path, good)
    lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").split("\n")[:-1]

    torn = lines[0] + "\n" + lines[1] + "\n" + '{"record_version": 1, "case_id": "case-0'
    (run_dir / "cases.jsonl").write_text(torn, encoding="utf-8")
    assert grade_run(run_dir, cases_dir, schema_path=SCHEMA)["counts"]["total"] == 2

    corrupt = lines[0] + "\n{not json\n" + lines[1] + "\n"
    (run_dir / "cases.jsonl").write_text(corrupt, encoding="utf-8")
    with pytest.raises(ValueError):
        grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    result = CliRunner().invoke(
        app, ["grade", "--run", str(run_dir), "--cases", str(cases_dir), "--schema", str(SCHEMA)]
    )
    assert result.exit_code == 2


# --- summary --------------------------------------------------------------------------------


def _typical_run(tmp_path: Path) -> tuple[Path, Path]:
    truth = _truth()
    wrong = _invoice(truth)
    wrong["total_amount"] = format(truth.total_amount + Decimal("1.00"), ".2f")
    records = [
        _record("case-a", _invoice(truth), cost="0.1", latency_ms=1000),
        _record("case-b", wrong, cost="0.2", latency_ms=3000),
        _record("case-c", None, status="refused", cost=None, latency_ms=2000),
    ]
    run_dir, cases_dir = _write_run(tmp_path, records)
    (cases_dir / "manifest.json").write_text(
        json.dumps({"dataset": "skeleton", "dataset_version": "skeleton-001", "cases": []}),
        encoding="utf-8",
    )
    return run_dir, cases_dir


def test_summary_carries_config_totals_latency_and_dataset(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["summary_version"] == 1
    assert summary["grader_version"] == "grader-001"
    assert summary["run_id"] == "run-grade"
    assert summary["tolerance"] == "0.01"
    assert summary["dataset"]["name"] == "skeleton"
    assert summary["dataset"]["version"] == "skeleton-001"
    assert len(summary["dataset"]["manifest_sha256"]) == 64
    assert summary["config"] == {
        "models_requested": ["claude-haiku-4-5"],
        "models_returned": ["claude-haiku-4-5"],
        "prompt_versions": ["extract-001"],
        "schema_sha256s": ["b" * 64],
        "pricing_versions": ["pricing-001"],
    }
    assert summary["totals"] == {
        "input_tokens": 3000,
        "output_tokens": 300,
        "cache_read_tokens": 30,
        "cache_write_5m_tokens": 60,
        "cache_write_1h_tokens": 90,
        "cost_usd": "0.3",
        "unpriced_cases": 1,
    }
    assert summary["latency_ms"] == {"min": 1000, "median": 2000, "max": 3000}
    refused = next(c for c in summary["cases"] if c["case_id"] == "case-c")
    assert refused["status"] == "refused"
    assert refused["trace_id"] == "a" * 32
    assert refused["latency_ms"] == 2000
    assert [c["case_id"] for c in summary["cases"]] == ["case-a", "case-b", "case-c"]
    assert summary["schema_validity"]["jsonschema"] == {"correct": 2, "n": 2}


def test_summary_files_are_deterministic_apart_from_the_meta_block(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    texts = []
    for _ in range(2):
        write_summary(run_dir, grade_run(run_dir, cases_dir, schema_path=SCHEMA))
        loaded = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
        assert set(loaded["meta"]) == {"graded_at"}
        del loaded["meta"]
        texts.append(json.dumps(loaded, sort_keys=True))
    assert texts[0] == texts[1]
    raw = (run_dir / "summary.json").read_text(encoding="utf-8")
    assert raw.endswith("\n")
    assert '"cost_usd": "0.3"' in raw


def test_summary_markdown_has_a_header_and_a_per_case_table(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    write_summary(run_dir, grade_run(run_dir, cases_dir, schema_path=SCHEMA))
    markdown = (run_dir / "summary.md").read_text(encoding="utf-8")
    assert "run-grade" in markdown
    assert "claude-haiku-4-5" in markdown
    for column in ("case_id", "status", "fields", "total delta", "cost", "latency", "trace"):
        assert column in markdown
    for case_id in ("case-a", "case-b", "case-c"):
        assert case_id in markdown
    assert "a" * 32 in markdown


# --- offline purity -------------------------------------------------------------------------

_OFFLINE_SCRIPT = """
import socket, sys
from pathlib import Path


def _deny(*args, **kwargs):
    raise RuntimeError("network access attempted while grading")


socket.socket = _deny  # type: ignore[misc]

from carimbo_evals import grader, summary

run_dir, cases_dir, schema = (Path(a) for a in sys.argv[1:4])
result = grader.grade_run(run_dir, cases_dir, schema_path=schema)
summary.write_summary(run_dir, result)
assert "httpx2" not in sys.modules, "grader/summary pulled in an HTTP client"
assert "httpx" not in sys.modules
print(result["counts"]["total"])
"""


def test_grading_a_stored_run_needs_no_network_and_imports_no_http_client(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    env = {**os.environ, "PYTHONPATH": str(SRC)}
    result = subprocess.run(
        [sys.executable, "-c", _OFFLINE_SCRIPT, str(run_dir), str(cases_dir), str(SCHEMA)],
        capture_output=True,
        text=True,
        env=env,
        cwd=tmp_path,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip() == "3"
    assert (run_dir / "summary.json").is_file()


# --- CLI ------------------------------------------------------------------------------------


def test_grade_command_writes_both_files_and_prints_the_markdown(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    result = CliRunner().invoke(
        app,
        [
            "grade",
            "--run",
            str(run_dir),
            "--cases",
            str(cases_dir),
            "--schema",
            str(SCHEMA),
            "--tolerance",
            "0",
        ],
    )
    assert result.exit_code == 0, result.output
    assert "case-a" in result.output
    summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    assert summary["tolerance"] == "0"
    assert (run_dir / "summary.md").is_file()


def test_grade_command_requires_a_run_and_a_readable_run_directory(tmp_path: Path) -> None:
    assert CliRunner().invoke(app, ["grade"]).exit_code == 2
    missing = CliRunner().invoke(app, ["grade", "--run", str(tmp_path / "nope")])
    assert missing.exit_code == 2


def test_grade_help_lists_the_documented_options() -> None:
    env = {"NO_COLOR": "1", "COLUMNS": "200", "TERM": "dumb"}
    result = CliRunner().invoke(app, ["grade", "--help"], env=env)
    assert result.exit_code == 0
    for option in ("--run", "--cases", "--tolerance", "--schema"):
        assert option in result.output
