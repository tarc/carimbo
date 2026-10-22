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

from pydantic import ValidationError

from carimbo_evals.decision_summary import TOKEN_FIELDS, build_summary
from carimbo_evals.grader import _load_manifest, _read_records
from carimbo_models.decision_generated import Decision

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


def _steps(response: dict[str, Any]) -> list[dict[str, Any]]:
    steps = response.get("steps")
    return [step for step in (steps if isinstance(steps, list) else []) if isinstance(step, dict)]


def _step_calls(step: dict[str, Any]) -> list[dict[str, Any]]:
    calls = step.get("tool_calls")
    return [call for call in (calls if isinstance(calls, list) else []) if isinstance(call, dict)]


def _guard_tool_calls(response: dict[str, Any], outcome: dict[str, Any]) -> int:
    """Calls the guard made: the steps' ``source: guard`` calls, else the outcome's guard call."""
    in_steps = sum(
        1
        for step in _steps(response)
        for call in _step_calls(step)
        if call.get("source") == "guard"
    )
    if in_steps:
        return in_steps
    return 1 if isinstance(_dict(outcome.get("guard")).get("tool_call"), dict) else 0


def _token_totals(response: dict[str, Any]) -> dict[str, int]:
    """Tokens summed over the answered steps (a step with a ``usage`` object)."""
    tokens: dict[str, int] = dict.fromkeys(TOKEN_FIELDS, 0)
    for step in _steps(response):
        usage = step.get("usage")
        if not isinstance(usage, dict):
            continue
        for name in TOKEN_FIELDS:
            value = usage.get(name)
            if isinstance(value, int) and not isinstance(value, bool):
                tokens[name] += value
    return tokens


def _schema_valid(decision: dict[str, Any]) -> bool:
    """Whether a decision object passes the generated ``Decision`` model in strict mode."""
    try:
        Decision.model_validate_json(json.dumps(decision), strict=True)
    except (ValidationError, ValueError):
        return False
    return True


def _int(value: object) -> int | None:
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def grade_case(record: dict[str, Any], expected_entry: dict[str, Any]) -> dict[str, Any]:
    """Grade one stored record against its label."""
    response = record.get("response")
    completed = record.get("status") == "completed" and isinstance(response, dict)
    response = response if isinstance(response, dict) else {}
    outcome = _dict(response.get("outcome")) if completed else {}
    status = outcome.get("status") if isinstance(outcome.get("status"), str) else "harness_error"
    effective = _dict(response.get("effective")) if completed else {}
    hits, answered = step_cache_counts(response) if completed else (None, None)
    graded: dict[str, Any] = {
        "case_id": record["case_id"],
        "status": status,
        "expected_decision": expected_entry["decision"],
        "expected_cause": expected_entry["cause"],
        "decision": None,
        "cause": None,
        "decision_correct": None,
        "cause_correct": None,
        "model_decision_correct": None,
        "citations_valid": None,
        "evidence_count": None,
        "decision_schema_valid": None,
        "decided_by": None,
        "guard_overridden": None,
        "steps": len(_steps(response)) if completed else None,
        "model_tool_calls": None,
        "guard_tool_calls": None,
        "usage": _token_totals(response) if completed else None,
        "cost_usd": response.get("cost_usd") if completed else None,
        "incurred_cost_usd": response.get("incurred_cost_usd") if completed else None,
        "latency_ms": _int(response.get("latency_ms")) if completed else None,
        "cache_hits": hits,
        "answered_steps": answered,
        "model": effective.get("model"),
        "prompt_version": effective.get("prompt_version"),
        "max_steps": effective.get("max_steps"),
        "cache_mode": effective.get("cache_mode"),
        "replicate_salt": effective.get("replicate_salt"),
        "trace_id": response.get("trace_id") if completed else None,
    }
    if completed:
        graded["model_tool_calls"] = sum(
            1 for step in _steps(response) for c in _step_calls(step) if c.get("source") == "model"
        )
        graded["guard_tool_calls"] = _guard_tool_calls(response, outcome)
    if status != "decided":
        return graded
    decision = _dict(outcome.get("decision"))
    model_decision = _dict(outcome.get("model_decision"))
    if not decision:
        # ``decided`` promises a final decision. Without one the endpoint broke its contract (or the
        # agent loop regressed): the case is graded wrong and schema-invalid rather than silently
        # leaving the denominators.
        graded["decision_correct"] = False
        graded["decision_schema_valid"] = False
        graded["decided_by"] = outcome.get("decided_by")
        return graded
    graded["decision"] = decision.get("outcome")
    graded["cause"] = decision.get("cause")
    graded["decision_correct"] = decision.get("outcome") == expected_entry["decision"]
    if expected_entry["decision"] == "escalate":
        graded["cause_correct"] = decision.get("cause") == expected_entry["cause"]
    if model_decision:
        graded["model_decision_correct"] = (
            model_decision.get("outcome") == expected_entry["decision"]
        )
        graded["citations_valid"], graded["evidence_count"] = _citations(model_decision, response)
    present = [d for d in (decision, model_decision) if d]
    if present:
        graded["decision_schema_valid"] = all(_schema_valid(d) for d in present)
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
