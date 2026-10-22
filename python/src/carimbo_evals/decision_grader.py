"""Offline, deterministic grading of decision runs (D-16, D-36).

The labels come from ``<cases>/expected.json`` and never from the run. There is no LLM judge, and
no network: this module only reads the stored ``cases.jsonl`` and the committed labels.

A typed failure (refused, truncated, schema_invalid, infrastructure_failure) and a harness error
are counted by status and carry no decision grade, so they never enter an accuracy denominator.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from carimbo_evals.decision_summary import build_summary
from carimbo_evals.grader import _load_manifest, _read_records

EXPECTED_NAME = "expected.json"
MODEL_CALL_OUTCOMES = frozenset({"ok", "error"})


def load_expected(cases_dir: Path) -> tuple[str | None, dict[str, dict[str, Any]]]:
    """``(labelling_version, labels by case id)`` from ``<cases_dir>/expected.json``.

    Raises ``ValueError`` when the file is missing or malformed.
    """
    path = cases_dir / EXPECTED_NAME
    if not path.is_file():
        raise ValueError(f"{path} not found")
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise ValueError(f"{path} is not readable JSON: {exc}") from exc
    entries = document.get("cases") if isinstance(document, dict) else None
    if not isinstance(entries, list):
        raise ValueError(f"{path} has no cases list")
    labels: dict[str, dict[str, Any]] = {}
    for entry in entries:
        if (
            not isinstance(entry, dict)
            or not isinstance(entry.get("case_id"), str)
            or not isinstance(entry.get("decision"), str)
            or not isinstance(entry.get("cause"), str)
        ):
            raise ValueError(f"{path} has a malformed case entry: {entry!r}")
        labels[entry["case_id"]] = entry
    version = document.get("labelling_version")
    return (version if isinstance(version, str) else None), labels


def _dict(value: object) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


def step_cache_counts(response: dict[str, Any]) -> tuple[int, int]:
    """``(cache hits, answered steps)`` of a stored decision response.

    A step is answered when the model produced a response for it (it has a ``usage`` object); it
    is a hit when its ``cache`` block says ``status: hit``. Both are 0 without a steps list.
    """
    steps = response.get("steps")
    answered = [
        step
        for step in (steps if isinstance(steps, list) else [])
        if isinstance(step, dict) and isinstance(step.get("usage"), dict)
    ]
    hits = sum(
        1
        for step in answered
        if isinstance(step.get("cache"), dict) and step["cache"].get("status") == "hit"
    )
    return hits, len(answered)


def _called_tools(response: dict[str, Any]) -> set[str]:
    """Names of the tools the model itself called in this run (outcome ok or error)."""
    steps = response.get("steps")
    names: set[str] = set()
    for step in steps if isinstance(steps, list) else []:
        calls = _dict(step).get("tool_calls")
        for call in calls if isinstance(calls, list) else []:
            call = _dict(call)
            if (
                call.get("source") == "model"
                and call.get("outcome") in MODEL_CALL_OUTCOMES
                and isinstance(call.get("name"), str)
            ):
                names.add(call["name"])
    return names


def _citations(decision: dict[str, Any], response: dict[str, Any]) -> tuple[bool, int]:
    """``(citations_valid, evidence_count)`` of a model decision."""
    evidence = decision.get("evidence")
    evidence = evidence if isinstance(evidence, list) else []
    called = _called_tools(response)
    valid = all(_dict(item).get("source") in called for item in evidence)
    return valid, len(evidence)


def grade_case(record: dict[str, Any], expected_entry: dict[str, Any]) -> dict[str, Any]:
    """Grade one stored record against its label."""
    response = record.get("response")
    completed = record.get("status") == "completed" and isinstance(response, dict)
    response = response if isinstance(response, dict) else {}
    outcome = _dict(response.get("outcome")) if completed else {}
    status = outcome.get("status") if isinstance(outcome.get("status"), str) else "harness_error"
    graded: dict[str, Any] = {
        "case_id": record["case_id"],
        "status": status,
        "expected_decision": expected_entry["decision"],
        "expected_cause": expected_entry["cause"],
        "decision": None,
        "cause": None,
        "decision_correct": None,
        "cause_correct": None,
        "citations_valid": None,
        "evidence_count": None,
        "decided_by": None,
        "guard_overridden": None,
        "trace_id": response.get("trace_id") if completed else None,
    }
    if status != "decided":
        return graded
    decision = _dict(outcome.get("decision"))
    if decision:
        graded["decision"] = decision.get("outcome")
        graded["cause"] = decision.get("cause")
        graded["decision_correct"] = decision.get("outcome") == expected_entry["decision"]
        if expected_entry["decision"] == "escalate":
            graded["cause_correct"] = decision.get("cause") == expected_entry["cause"]
    model_decision = _dict(outcome.get("model_decision"))
    if model_decision:
        graded["citations_valid"], graded["evidence_count"] = _citations(model_decision, response)
    graded["decided_by"] = outcome.get("decided_by")
    guard = outcome.get("guard")
    if isinstance(guard, dict):
        graded["guard_overridden"] = bool(guard.get("overridden"))
    return graded


def grade_run(run_dir: Path, cases_dir: Path) -> dict[str, Any]:
    """Grade ``run_dir/cases.jsonl`` against ``<cases_dir>/expected.json``; return the summary."""
    labelling_version, labels = load_expected(cases_dir)
    records = _read_records(run_dir / "cases.jsonl")
    unlabelled = sorted(set(records) - set(labels))
    if unlabelled:
        raise ValueError(f"no label in {cases_dir / EXPECTED_NAME} for: {', '.join(unlabelled)}")
    case_grades = [grade_case(record, labels[case_id]) for case_id, record in records.items()]
    first = next(iter(records.values()), None)
    run_meta = {
        "run_id": first["run_id"] if first else run_dir.name,
        "labelling_version": labelling_version,
    }
    return build_summary(case_grades, run_meta, _load_manifest(cases_dir))
