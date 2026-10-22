"""AGT-04: the decision grader is a deterministic comparison with the committed labels (D-16)."""

from __future__ import annotations

import copy
import json
import random
from pathlib import Path
from typing import Any

import pytest

from carimbo_evals import decision_summary
from carimbo_evals.decision_grader import grade_case, grade_run, load_expected
from carimbo_evals.decision_summary import (
    GRADER_VERSION,
    SUMMARY_VERSION,
    build_summary,
    render_markdown,
    write_summary,
)

# Eight synthetic cases: two approve, one reject, five escalate with the three model causes.
LABELS: list[tuple[str, str, str]] = [
    ("dc-001", "approve", "none"),
    ("dc-002", "reject", "none"),
    ("dc-003", "escalate", "policy_warning"),
    ("dc-004", "escalate", "extraction_not_success"),
    ("dc-005", "escalate", "over_limit"),
    ("dc-006", "escalate", "policy_warning"),
    ("dc-007", "approve", "none"),
    ("dc-008", "escalate", "extraction_not_success"),
]
EXPECTED = {
    case_id: {"case_id": case_id, "decision": decision, "cause": cause, "rule": 1}
    for case_id, decision, cause in LABELS
}


def _call(name: str, source: str = "model", outcome: str = "ok") -> dict[str, Any]:
    return {
        "id": None if source == "guard" else f"toolu_{name}",
        "name": name,
        "arguments": {},
        "source": source,
        "outcome": outcome,
        "error_kind": None,
        "error_message": None,
        "latency_ms": 2,
    }


def _usage(input_tokens: int = 100, output_tokens: int = 20) -> dict[str, int]:
    return {
        "input_tokens": input_tokens,
        "output_tokens": output_tokens,
        "cache_read_tokens": 0,
        "cache_write_5m_tokens": 0,
        "cache_write_1h_tokens": 0,
    }


def _step(
    index: int,
    calls: list[dict[str, Any]],
    *,
    cache: str = "miss",
    answered: bool = True,
    cost: str | None = "0.00500000",
    incurred: str | None = "0.00500000",
) -> dict[str, Any]:
    return {
        "index": index,
        "usage": _usage() if answered else None,
        "cost_usd": cost,
        "cost_warning": None,
        "incurred_cost_usd": incurred,
        "latency_ms": 10,
        "stop_reason": "tool_use" if calls else "end_turn",
        "model_returned": "claude-haiku-4-5",
        "provider_message_id": f"msg_{index}",
        "http_attempts": 1,
        "cache": {"mode": "read_write", "status": cache, "key": "k" * 64, "stored": True}
        if answered
        else None,
        "failure": None,
        "tool_calls": calls,
    }


def decision(outcome: str, cause: str, sources: list[str] | None = None) -> dict[str, Any]:
    return {
        "evidence": [
            {"source": source, "ref": "field", "value": "v"}
            for source in (["get_invoice"] if sources is None else sources)
        ],
        "reason": "Because.",
        "outcome": outcome,
        "cause": cause,
    }


def record(
    case_id: str,
    outcome: str,
    cause: str,
    *,
    sources: list[str] | None = None,
    model_calls: list[tuple[str, str]] | None = None,
    model_decision: dict[str, Any] | None | str = "same",
    decided_by: str = "model",
    overridden: bool | None = False,
    status: str = "decided",
    cost: str | None = "0.01000000",
    incurred: str | None = "0.01000000",
    cache: str = "miss",
    latency_ms: int = 90,
) -> dict[str, Any]:
    """A completed decision record. ``model_decision="same"`` mirrors the final decision."""
    calls = (
        model_calls if model_calls is not None else [("get_invoice", "ok"), ("get_policy", "ok")]
    )
    final = decision(outcome, cause, sources)
    if model_decision == "same":
        model_decision = copy.deepcopy(final)
    guard = (
        None
        if overridden is None
        else {
            "extraction_status": "success" if not overridden else "validation_failed",
            "overridden": overridden,
            "tool_call": _call("get_extraction_status", source="guard"),
        }
    )
    response = {
        "contract_version": "decision-1",
        "case_id": case_id,
        "trace_id": "0" * 31 + "1",
        "effective": {
            "model": "claude-haiku-4-5",
            "prompt_version": "agent-001",
            "max_steps": 6,
            "schema_sha256": "a" * 64,
            "tools_sha256": "b" * 64,
            "pricing_version": "pricing-001",
            "cache_mode": "read_write",
            "replicate_salt": None,
        },
        "outcome": {
            "status": status,
            "decision": final if status == "decided" else None,
            "model_decision": model_decision if status == "decided" else None,
            "decided_by": decided_by if status == "decided" else None,
            "guard": guard if status == "decided" else None,
            "tool_failure": None,
            "failure": None
            if status == "decided"
            else {
                "kind": status,
                "http_status": None,
                "request_id": None,
                "message": "m",
            },
            "raw_output": None,
        },
        "steps": [
            _step(0, [_call(name, outcome=result) for name, result in calls], cache=cache),
            _step(1, [], cache=cache),
        ],
        "usage": _usage(200, 40),
        "cost_usd": cost,
        "cost_warning": None,
        "incurred_cost_usd": incurred,
        "latency_ms": latency_ms,
    }
    return {
        "record_version": 1,
        "record_kind": "decision",
        "run_id": "run-test",
        "case_id": case_id,
        "status": "completed",
        "request": {
            "case_sha256": "c" * 64,
            "traceparent": "t",
            "trace_id": "i",
            "overrides": None,
        },
        "http": {"status": 200, "wall_ms": 100, "error": None, "error_type": None},
        "response": response,
    }


def harness_error(case_id: str) -> dict[str, Any]:
    return {
        "record_version": 1,
        "record_kind": "decision",
        "run_id": "run-test",
        "case_id": case_id,
        "status": "harness_error",
        "request": {
            "case_sha256": "c" * 64,
            "traceparent": "t",
            "trace_id": "i",
            "overrides": None,
        },
        "http": {"status": 500, "wall_ms": 5, "error": "HTTP 500", "error_type": None},
        "response": None,
    }


def perfect_records() -> list[dict[str, Any]]:
    return [record(case_id, outcome, cause) for case_id, outcome, cause in LABELS]


def write_run(tmp_path: Path, records: list[dict[str, Any]]) -> tuple[Path, Path]:
    """A run directory and a cases directory with the eight labels and a manifest."""
    cases_dir = tmp_path / "decision"
    run_dir = tmp_path / "run"
    cases_dir.mkdir()
    run_dir.mkdir()
    (cases_dir / "expected.json").write_text(
        json.dumps(
            {
                "labelling_version": "decision-labels-1",
                "decision_record": "D-34",
                "cases": list(EXPECTED.values()),
            }
        ),
        encoding="utf-8",
    )
    (cases_dir / "manifest.json").write_text(
        json.dumps({"dataset": "decision", "dataset_version": "decision-001", "cases": []}),
        encoding="utf-8",
    )
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True) + "\n" for r in records), encoding="utf-8"
    )
    return run_dir, cases_dir


def grade(tmp_path: Path, records: list[dict[str, Any]]) -> dict[str, Any]:
    run_dir, cases_dir = write_run(tmp_path, records)
    return grade_run(run_dir, cases_dir)


def case_of(summary: dict[str, Any], case_id: str) -> dict[str, Any]:
    return next(c for c in summary["cases"] if c["case_id"] == case_id)


# --- ground truth perfect ---------------------------------------------------------------------


def test_perfect_records_score_everything_correct(tmp_path: Path) -> None:
    summary = grade(tmp_path, perfect_records())
    assert summary["accuracy"] == {"n": 8, "correct": 8, "rate": 1.0}
    assert summary["cause_accuracy"] == {"n": 5, "correct": 5, "rate": 1.0}
    assert summary["model_accuracy"] == {"n": 8, "correct": 8, "rate": 1.0}
    assert summary["citations"] == {"n": 8, "valid": 8, "rate": 1.0}
    assert summary["guard"] == {"checked": 8, "overridden": 0}
    assert summary["counts"]["decided"] == 8 and summary["counts"]["total"] == 8
    assert summary["escalation"] == {"precision": 1.0, "recall": 1.0}
    assert summary["summary_version"] == SUMMARY_VERSION == "decision-summary-1"
    assert summary["grader_version"] == GRADER_VERSION == "decision-grader-001"
    assert summary["labelling_version"] == "decision-labels-1"
    assert summary["dataset"]["name"] == "decision"
    assert summary["dataset"]["version"] == "decision-001"
    assert len(summary["dataset"]["manifest_sha256"]) == 64


def test_cause_is_null_unless_the_expected_decision_is_escalate() -> None:
    approve = grade_case(record("dc-001", "approve", "none"), EXPECTED["dc-001"])
    reject = grade_case(record("dc-002", "reject", "none"), EXPECTED["dc-002"])
    escalate = grade_case(record("dc-003", "escalate", "policy_warning"), EXPECTED["dc-003"])
    assert approve["cause_correct"] is None and reject["cause_correct"] is None
    assert escalate["cause_correct"] is True


# --- corruptions score wrong ------------------------------------------------------------------


def test_a_wrong_outcome_is_not_decision_correct() -> None:
    graded = grade_case(record("dc-001", "reject", "none"), EXPECTED["dc-001"])
    assert graded["decision_correct"] is False
    assert graded["cause_correct"] is None


def test_an_escalate_with_the_wrong_cause_keeps_the_decision_correct() -> None:
    graded = grade_case(record("dc-003", "escalate", "over_limit"), EXPECTED["dc-003"])
    assert graded["decision_correct"] is True
    assert graded["cause_correct"] is False


def test_a_wrong_outcome_on_an_escalate_case_has_a_wrong_cause_too() -> None:
    graded = grade_case(record("dc-003", "approve", "none"), EXPECTED["dc-003"])
    assert graded["decision_correct"] is False
    assert graded["cause_correct"] is False


def test_a_citation_of_a_tool_the_model_never_called_is_invalid() -> None:
    graded = grade_case(
        record(
            "dc-001",
            "approve",
            "none",
            sources=["get_invoice", "get_policy"],
            model_calls=[("get_invoice", "ok")],
        ),
        EXPECTED["dc-001"],
    )
    assert graded["citations_valid"] is False
    assert graded["evidence_count"] == 2


def test_a_citation_that_only_the_guard_called_is_invalid() -> None:
    graded = grade_case(
        record("dc-001", "approve", "none", sources=["get_extraction_status"]),
        EXPECTED["dc-001"],
    )
    assert graded["citations_valid"] is False


def test_a_citation_of_a_skipped_call_is_invalid() -> None:
    graded = grade_case(
        record(
            "dc-001",
            "approve",
            "none",
            sources=["get_policy"],
            model_calls=[("get_invoice", "ok"), ("get_policy", "skipped")],
        ),
        EXPECTED["dc-001"],
    )
    assert graded["citations_valid"] is False


def test_a_citation_of_a_model_call_that_errored_is_valid() -> None:
    graded = grade_case(
        record(
            "dc-001",
            "approve",
            "none",
            sources=["get_policy"],
            model_calls=[("get_invoice", "ok"), ("get_policy", "error")],
        ),
        EXPECTED["dc-001"],
    )
    assert graded["citations_valid"] is True


def test_a_guard_sourced_call_in_a_step_never_counts_as_a_model_call() -> None:
    rec = record("dc-001", "approve", "none", sources=["get_extraction_status"])
    rec["response"]["steps"][1]["tool_calls"].append(_call("get_extraction_status", "guard"))
    assert grade_case(rec, EXPECTED["dc-001"])["citations_valid"] is False


def test_empty_evidence_is_valid_and_visibly_uncited() -> None:
    graded = grade_case(record("dc-001", "approve", "none", sources=[]), EXPECTED["dc-001"])
    assert graded["citations_valid"] is True
    assert graded["evidence_count"] == 0


# --- typed failures and harness errors --------------------------------------------------------


@pytest.mark.parametrize(
    "status", ["refused", "truncated", "schema_invalid", "infrastructure_failure"]
)
def test_typed_failures_carry_no_decision_grade(status: str) -> None:
    graded = grade_case(record("dc-001", "approve", "none", status=status), EXPECTED["dc-001"])
    assert graded["status"] == status
    assert graded["decision_correct"] is None
    assert graded["cause_correct"] is None
    assert graded["citations_valid"] is None
    assert graded["model_decision_correct"] is None


def test_a_harness_error_carries_no_decision_grade() -> None:
    graded = grade_case(harness_error("dc-001"), EXPECTED["dc-001"])
    assert graded["status"] == "harness_error"
    assert graded["decision_correct"] is None


def test_failures_are_counted_by_status_and_leave_every_denominator(tmp_path: Path) -> None:
    records = perfect_records()[:4] + [
        record("dc-005", "escalate", "over_limit", status="refused"),
        record("dc-006", "escalate", "policy_warning", status="truncated"),
        record("dc-007", "approve", "none", status="infrastructure_failure"),
        harness_error("dc-008"),
    ]
    summary = grade(tmp_path, records)
    assert summary["counts"]["decided"] == 4
    assert summary["counts"]["refused"] == 1
    assert summary["counts"]["truncated"] == 1
    assert summary["counts"]["infrastructure_failure"] == 1
    assert summary["counts"]["harness_error"] == 1
    assert summary["counts"]["schema_invalid"] == 0
    assert summary["counts"]["total"] == 8
    assert summary["accuracy"]["n"] == 4
    assert summary["accuracy"]["correct"] == 4
    assert summary["citations"]["n"] == 4
    assert summary["guard"]["checked"] == 4


# --- the guard --------------------------------------------------------------------------------


def test_a_guard_override_is_counted_and_the_model_decision_is_graded_alone(
    tmp_path: Path,
) -> None:
    overridden = record(
        "dc-004",
        "escalate",
        "extraction_not_success",
        model_decision=decision("approve", "none"),
        decided_by="guard",
        overridden=True,
    )
    records = [r for r in perfect_records() if r["case_id"] != "dc-004"] + [overridden]
    summary = grade(tmp_path, records)
    graded = case_of(summary, "dc-004")
    assert graded["decision_correct"] is True
    assert graded["model_decision_correct"] is False
    assert graded["guard_overridden"] is True
    assert graded["decided_by"] == "guard"
    assert summary["guard"] == {"checked": 8, "overridden": 1}
    assert summary["accuracy"] == {"n": 8, "correct": 8, "rate": 1.0}
    assert summary["model_accuracy"] == {"n": 8, "correct": 7, "rate": 0.875}


def test_model_decision_correct_is_null_without_a_model_decision() -> None:
    graded = grade_case(
        record(
            "dc-004",
            "escalate",
            "extraction_not_success",
            model_decision=None,
            decided_by="step_budget",
            overridden=None,
        ),
        EXPECTED["dc-004"],
    )
    assert graded["model_decision_correct"] is None
    assert graded["citations_valid"] is None
    assert graded["guard_overridden"] is None
    assert graded["decision_correct"] is True


# --- empty, precision and recall --------------------------------------------------------------


def test_zero_decided_cases_report_null_rates(tmp_path: Path) -> None:
    summary = grade(tmp_path, [record("dc-001", "approve", "none", status="refused")])
    assert summary["accuracy"] == {"n": 0, "correct": 0, "rate": None}
    assert summary["cause_accuracy"] == {"n": 0, "correct": 0, "rate": None}
    assert summary["model_accuracy"]["rate"] is None
    assert summary["citations"] == {"n": 0, "valid": 0, "rate": None}
    assert summary["escalation"] == {"precision": None, "recall": None}
    assert summary["cache"]["hit_rate"] is None


def test_a_run_with_no_records_still_summarises(tmp_path: Path) -> None:
    summary = grade(tmp_path, [])
    assert summary["counts"]["total"] == 0
    assert summary["accuracy"]["rate"] is None
    assert summary["steps"] == {"min": None, "median": None, "max": None, "total": 0}
    assert summary["latency_ms"] == {"min": None, "median": None, "max": None}


def test_escalate_is_the_positive_class(tmp_path: Path) -> None:
    records = perfect_records()
    # dc-001 (expect approve) predicted escalate: a false positive.
    records[0] = record("dc-001", "escalate", "policy_warning")
    # dc-003 (expect escalate) predicted reject: a missed escalation.
    records[2] = record("dc-003", "reject", "none")
    summary = grade(tmp_path, records)
    # predicted escalate: dc-001 (fp), dc-004, dc-005, dc-006, dc-008 = 5, true positives 4.
    # expected escalate: 5 (dc-003..dc-006, dc-008), found 4.
    assert summary["escalation"] == {"precision": 4 / 5, "recall": 4 / 5}


# --- steps, tool calls, tokens, cost, cache, latency ------------------------------------------


def test_steps_tool_calls_tokens_cost_cache_and_latency_are_aggregated(tmp_path: Path) -> None:
    records = [
        record(
            "dc-001",
            "approve",
            "none",
            cost="0.01000000",
            incurred="0.00000000",
            cache="hit",
            latency_ms=10,
        ),
        record("dc-002", "reject", "none", cost="0.03000000", incurred="0.03000000", latency_ms=30),
        record(
            "dc-003",
            "escalate",
            "policy_warning",
            model_calls=[("get_invoice", "ok")],
            latency_ms=50,
        ),
    ]
    summary = grade(tmp_path, records)
    first = case_of(summary, "dc-001")
    assert first["steps"] == 2
    assert first["model_tool_calls"] == 2
    assert first["guard_tool_calls"] == 1
    assert first["usage"]["input_tokens"] == 200 and first["usage"]["output_tokens"] == 40
    assert summary["steps"] == {"min": 2, "median": 2, "max": 2, "total": 6}
    assert summary["tool_calls"]["model_total"] == 5
    assert summary["tool_calls"]["model_per_case"] == {"min": 1, "median": 2, "max": 2}
    assert summary["tool_calls"]["guard_total"] == 3
    totals = summary["totals"]
    assert totals["input_tokens"] == 600 and totals["output_tokens"] == 120
    assert totals["cost_usd"] == "0.05000000"
    assert totals["incurred_cost_usd"] == "0.04000000"
    # 6 answered steps; the two steps of dc-001 were cache hits.
    assert summary["cache"] == {"hits": 2, "answered_steps": 6, "hit_rate": 2 / 6}
    assert summary["latency_ms"] == {"min": 10, "median": 30, "max": 50}


def test_money_is_a_decimal_string_and_unknown_when_any_cost_is_null(tmp_path: Path) -> None:
    records = [
        record("dc-001", "approve", "none", cost="0.1", incurred="0.1"),
        record("dc-002", "reject", "none", cost="0.2", incurred="0.2"),
    ]
    totals = grade(tmp_path, records)["totals"]
    assert totals["cost_usd"] == "0.3"  # 0.1 + 0.2 as Decimal, never 0.30000000000000004
    assert totals["unpriced_cases"] == 0

    second = tmp_path / "second"
    second.mkdir()
    unknown = grade(
        second,
        [
            record("dc-001", "approve", "none", cost="0.1", incurred=None),
            record("dc-002", "reject", "none", cost=None, incurred="0.2"),
        ],
    )["totals"]
    assert unknown["cost_usd"] is None and unknown["incurred_cost_usd"] is None
    assert unknown["unpriced_cases"] == 1 and unknown["unpriced_incurred_cases"] == 1


def test_a_zero_sum_prints_without_an_exponent(tmp_path: Path) -> None:
    records = [record("dc-001", "approve", "none", cost="0.00000000", incurred="0.00000000")]
    totals = grade(tmp_path, records)["totals"]
    assert totals["incurred_cost_usd"] == "0.00000000"
    assert "E" not in totals["cost_usd"]


# --- ordering and duplicates -------------------------------------------------------------------


def test_cases_are_sorted_by_case_id_whatever_the_file_order(tmp_path: Path) -> None:
    records = perfect_records()
    random.Random(7).shuffle(records)
    summary = grade(tmp_path, records)
    ids = [c["case_id"] for c in summary["cases"]]
    assert ids == sorted(ids)
    reference_dir = tmp_path / "reference"
    reference_dir.mkdir()
    reference = grade(reference_dir, perfect_records())
    for key in ("accuracy", "cause_accuracy", "citations", "guard", "totals", "counts"):
        assert summary[key] == reference[key]
    assert summary["cases"] == reference["cases"]


def test_with_duplicate_records_the_later_line_wins(tmp_path: Path) -> None:
    records = [
        record("dc-001", "reject", "none"),
        *perfect_records(),
    ]
    summary = grade(tmp_path, records)
    assert summary["counts"]["total"] == 8
    assert case_of(summary, "dc-001")["decision_correct"] is True

    flipped_dir = tmp_path / "flipped"
    flipped_dir.mkdir()
    flipped = grade(flipped_dir, [*perfect_records(), record("dc-001", "reject", "none")])
    assert case_of(flipped, "dc-001")["decision_correct"] is False


def test_a_torn_final_line_is_ignored(tmp_path: Path) -> None:
    run_dir, cases_dir = write_run(tmp_path, perfect_records()[:2])
    with (run_dir / "cases.jsonl").open("a", encoding="utf-8") as sink:
        sink.write('{"case_id": "dc-003", "stat')
    assert grade_run(run_dir, cases_dir)["counts"]["total"] == 2


# --- configuration and the decision schema ------------------------------------------------------


def test_config_lists_the_distinct_effective_settings(tmp_path: Path) -> None:
    records = perfect_records()
    records[0]["response"]["effective"]["model"] = "claude-sonnet-5-5"
    records[1]["response"]["effective"]["replicate_salt"] = "r1"
    config = grade(tmp_path, records)["config"]
    assert config["models"] == ["claude-haiku-4-5", "claude-sonnet-5-5"]
    assert config["prompt_versions"] == ["agent-001"]
    assert config["max_steps"] == [6]
    assert config["cache_modes"] == ["read_write"]
    assert config["replicate_salts"] == ["r1"]


def test_an_invalid_decision_object_is_reported_not_raised() -> None:
    rec = record("dc-001", "approve", "none")
    rec["response"]["outcome"]["decision"]["surprise"] = True
    graded = grade_case(rec, EXPECTED["dc-001"])
    assert graded["decision_schema_valid"] is False
    assert graded["decision_correct"] is True  # the outcome string still equals the label

    ok = grade_case(record("dc-001", "approve", "none"), EXPECTED["dc-001"])
    assert ok["decision_schema_valid"] is True


def test_the_schema_validity_is_summarised(tmp_path: Path) -> None:
    records = perfect_records()
    records[0]["response"]["outcome"]["model_decision"]["outcome"] = "maybe"
    summary = grade(tmp_path, records)
    assert summary["decision_schema"] == {"n": 8, "valid": 7}


# --- labels and CLI-facing errors ---------------------------------------------------------------


def test_a_missing_expected_file_raises_value_error(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match=r"expected\.json"):
        load_expected(tmp_path)


def test_a_malformed_expected_file_raises_value_error(tmp_path: Path) -> None:
    (tmp_path / "expected.json").write_text("{not json", encoding="utf-8")
    with pytest.raises(ValueError, match=r"expected\.json"):
        load_expected(tmp_path)
    (tmp_path / "expected.json").write_text('{"cases": [{"case_id": "x"}]}', encoding="utf-8")
    with pytest.raises(ValueError, match="malformed"):
        load_expected(tmp_path)


def test_a_record_without_a_label_raises_value_error(tmp_path: Path) -> None:
    run_dir, cases_dir = write_run(tmp_path, [record("dc-099", "approve", "none")])
    with pytest.raises(ValueError, match="dc-099"):
        grade_run(run_dir, cases_dir)


def test_the_labels_are_read_from_the_file_not_from_the_run(tmp_path: Path) -> None:
    run_dir, cases_dir = write_run(tmp_path, [record("dc-001", "approve", "none")])
    expected = json.loads((cases_dir / "expected.json").read_text(encoding="utf-8"))
    expected["cases"][0]["decision"] = "reject"
    (cases_dir / "expected.json").write_text(json.dumps(expected), encoding="utf-8")
    assert case_of(grade_run(run_dir, cases_dir), "dc-001")["decision_correct"] is False


# --- summary files ------------------------------------------------------------------------------


def test_summary_files_are_written_with_the_per_case_table(tmp_path: Path) -> None:
    run_dir, cases_dir = write_run(tmp_path, perfect_records())
    summary = grade_run(run_dir, cases_dir)
    write_summary(run_dir, summary)
    stored = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    assert stored["accuracy"]["rate"] == 1.0
    assert (run_dir / "summary.json").read_text(encoding="utf-8").endswith("\n")
    markdown = (run_dir / "summary.md").read_text(encoding="utf-8")
    assert markdown == render_markdown(summary)
    header = next(line for line in markdown.splitlines() if line.startswith("| case"))
    for column in (
        "case",
        "expected",
        "decision",
        "correct",
        "cause ok",
        "citations ok",
        "guard",
        "steps",
        "tool calls",
        "incurred",
    ):
        assert column in header
    for case_id, _, _ in LABELS:
        assert f"| {case_id} |" in markdown
    assert "US$" in markdown


def test_grading_twice_gives_the_same_summary_apart_from_the_timestamp(tmp_path: Path) -> None:
    run_dir, cases_dir = write_run(tmp_path, perfect_records())
    first = grade_run(run_dir, cases_dir)
    second = grade_run(run_dir, cases_dir)
    first.pop("meta")
    second.pop("meta")
    assert json.dumps(first, sort_keys=True) == json.dumps(second, sort_keys=True)


def test_build_summary_is_a_pure_function_of_its_inputs() -> None:
    assert decision_summary.STATUSES[0] == "decided"
    summary = build_summary([], {"run_id": "r", "labelling_version": None}, None)
    assert summary["dataset"] is None and summary["labelling_version"] is None
