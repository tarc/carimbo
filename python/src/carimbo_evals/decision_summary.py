"""Decision run summary: aggregates per-case grades into ``summary.json`` and ``summary.md``.

Pure and offline. The only volatile value is ``meta.graded_at``. Rates are computed only when the
denominator is above zero and are ``null`` otherwise, never ``0.0``. Money is a ``Decimal`` parsed
from the endpoint's strings and rendered with ``format(x, 'f')``, so no float touches it and a zero
sum never prints as ``0E-8``.
"""

from __future__ import annotations

import json
import statistics
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any

from carimbo_evals.money import parse_cost

SUMMARY_VERSION = "decision-summary-1"
GRADER_VERSION = "decision-grader-002"

# Typed failures and harness errors are counted here and carry no decision grade.
STATUSES = (
    "decided",
    "refused",
    "truncated",
    "schema_invalid",
    "infrastructure_failure",
    "harness_error",
)
TOKEN_FIELDS = (
    "input_tokens",
    "output_tokens",
    "cache_read_tokens",
    "cache_write_5m_tokens",
    "cache_write_1h_tokens",
)
CONFIG_KEYS = {
    "models": "model",
    "prompt_versions": "prompt_version",
    "max_steps": "max_steps",
    "cache_modes": "cache_mode",
    "replicate_salts": "replicate_salt",
}


def _rate(correct: int, n: int) -> float | None:
    return correct / n if n else None


def _accuracy(cases: list[dict[str, Any]], key: str) -> dict[str, Any]:
    graded = [c[key] for c in cases if c.get(key) is not None]
    correct = sum(1 for value in graded if value)
    return {"n": len(graded), "correct": correct, "rate": _rate(correct, len(graded))}


def _whole(value: int | float) -> int | float:
    return int(value) if float(value).is_integer() else value


def _spread(values: list[int]) -> dict[str, int | float | None]:
    if not values:
        return {"min": None, "median": None, "max": None}
    return {"min": min(values), "median": _whole(statistics.median(values)), "max": max(values)}


def _ints(cases: list[dict[str, Any]], key: str) -> list[int]:
    return [
        c[key] for c in cases if isinstance(c.get(key), int) and not isinstance(c.get(key), bool)
    ]


def _unique(cases: list[dict[str, Any]], key: str) -> list[Any]:
    return sorted({c[key] for c in cases if c.get(key) is not None}, key=str)


def _escalation(cases: list[dict[str, Any]]) -> dict[str, float | None]:
    """Precision and recall with escalate as the positive class, over the decided cases."""
    decided = [c for c in cases if c.get("decision_correct") is not None]
    predicted = [c for c in decided if c["decision"] == "escalate"]
    actual = [c for c in decided if c["expected_decision"] == "escalate"]
    true_positive = sum(1 for c in predicted if c["expected_decision"] == "escalate")
    return {
        "precision": _rate(true_positive, len(predicted)),
        "recall": _rate(true_positive, len(actual)),
    }


def _totals(cases: list[dict[str, Any]]) -> dict[str, Any]:
    tokens = dict.fromkeys(TOKEN_FIELDS, 0)
    cost = Decimal(0)
    incurred = Decimal(0)
    unpriced = 0
    unpriced_incurred = 0
    for case in cases:
        usage = case.get("usage")
        if isinstance(usage, dict):
            for name in TOKEN_FIELDS:
                tokens[name] += usage.get(name, 0)
        if case["status"] == "harness_error":
            continue  # no response, so nothing was priced or unpriced
        priced = parse_cost(case.get("cost_usd"))
        if priced is None:
            unpriced += 1
        else:
            cost += priced
        paid = parse_cost(case.get("incurred_cost_usd"))
        if paid is None:
            unpriced_incurred += 1
        else:
            incurred += paid
    return {
        **tokens,
        # cost_usd is the notional cost (what the calls cost when first made); incurred_cost_usd
        # is what this run paid, zero for a cache replay. Either is unknown (null) when any
        # response had no usable value, rather than a partial sum presented as the total.
        "cost_usd": None if unpriced else format(cost, "f"),
        "incurred_cost_usd": None if unpriced_incurred else format(incurred, "f"),
        "unpriced_cases": unpriced,
        "unpriced_incurred_cases": unpriced_incurred,
    }


def build_summary(
    case_grades: list[dict[str, Any]],
    run_meta: dict[str, Any],
    manifest: dict[str, Any] | None,
) -> dict[str, Any]:
    """Aggregate per-case grades.

    ``run_meta`` carries ``run_id`` and ``labelling_version``; ``manifest`` is ``{name, version,
    manifest_sha256}`` or ``None`` when the cases directory has no manifest.
    """
    cases = sorted(case_grades, key=lambda c: c["case_id"])
    counts = dict.fromkeys(STATUSES, 0)
    for case in cases:
        counts[case["status"]] = counts.get(case["status"], 0) + 1
    citation_grades = [c["citations_valid"] for c in cases if c.get("citations_valid") is not None]
    valid_citations = sum(1 for value in citation_grades if value)
    schema_grades = [
        c["decision_schema_valid"] for c in cases if c.get("decision_schema_valid") is not None
    ]
    guarded = [c for c in cases if c.get("guard_overridden") is not None]
    answered = sum(c["answered_steps"] for c in cases if c.get("answered_steps") is not None)
    hits = sum(c["cache_hits"] for c in cases if c.get("cache_hits") is not None)
    steps = _ints(cases, "steps")
    model_calls = _ints(cases, "model_tool_calls")
    return {
        "summary_version": SUMMARY_VERSION,
        "grader_version": GRADER_VERSION,
        "run_id": run_meta["run_id"],
        "labelling_version": run_meta.get("labelling_version"),
        "dataset": manifest,
        "config": {name: _unique(cases, key) for name, key in CONFIG_KEYS.items()},
        "counts": {**counts, "total": len(cases)},
        "accuracy": _accuracy(cases, "decision_correct"),
        "cause_accuracy": _accuracy(cases, "cause_correct"),
        "model_accuracy": _accuracy(cases, "model_decision_correct"),
        "citations": {
            "n": len(citation_grades),
            "valid": valid_citations,
            "rate": _rate(valid_citations, len(citation_grades)),
        },
        "decision_schema": {
            "n": len(schema_grades),
            "valid": sum(1 for value in schema_grades if value),
        },
        "guard": {
            "checked": len(guarded),
            "overridden": sum(1 for c in guarded if c["guard_overridden"]),
        },
        "escalation": _escalation(cases),
        "steps": {**_spread(steps), "total": sum(steps)},
        "tool_calls": {
            "model_total": sum(model_calls),
            "model_per_case": _spread(model_calls),
            "guard_total": sum(_ints(cases, "guard_tool_calls")),
        },
        "totals": _totals(cases),
        "cache": {"hits": hits, "answered_steps": answered, "hit_rate": _rate(hits, answered)},
        "latency_ms": _spread(_ints(cases, "latency_ms")),
        "cases": cases,
        "meta": {"graded_at": datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")},
    }


def _flag(value: bool | None) -> str:
    return "-" if value is None else str(value).lower()


def _percent(rate: float | None) -> str:
    return "-" if rate is None else f"{rate:.3f}"


def _money(value: str | None) -> str:
    return "unknown" if value is None else value


def render_markdown(summary: dict[str, Any]) -> str:
    counts = summary["counts"]
    accuracy = summary["accuracy"]
    cause = summary["cause_accuracy"]
    citations = summary["citations"]
    guard = summary["guard"]
    totals = summary["totals"]
    cache = summary["cache"]
    steps = summary["steps"]
    calls = summary["tool_calls"]
    count_text = ", ".join(f"{name} {counts[name]}" for name in STATUSES if counts[name])
    models = ", ".join(summary["config"]["models"])
    median_steps = "-" if steps["median"] is None else steps["median"]
    lines = [
        f"# Decision run {summary['run_id']}",
        "",
        f"- models: {models or '-'}",
        f"- cases: {counts['total']} ({count_text or 'none'})",
        f"- decision accuracy: {accuracy['correct']}/{accuracy['n']} "
        f"({_percent(accuracy['rate'])})",
        f"- cause accuracy (expected escalate): {cause['correct']}/{cause['n']} "
        f"({_percent(cause['rate'])})",
        f"- model decision accuracy: {summary['model_accuracy']['correct']}/"
        f"{summary['model_accuracy']['n']}",
        f"- citations valid: {citations['valid']}/{citations['n']}",
        f"- guard: {guard['overridden']} overrides in {guard['checked']} checked",
        f"- steps: total {steps['total']}, median {median_steps}",
        f"- tool calls: {calls['model_total']} model, {calls['guard_total']} guard",
        f"- cost: US${_money(totals['incurred_cost_usd'])} incurred, "
        f"US${_money(totals['cost_usd'])} notional ({totals['unpriced_cases']} unpriced)",
        f"- cache: {cache['hits']}/{cache['answered_steps']} answered steps from the cache",
        f"- tokens in/out: {totals['input_tokens']}/{totals['output_tokens']}",
        f"- grader: {summary['grader_version']}, labels {summary['labelling_version'] or '-'}",
        "",
        "| case | expected | decision | correct | cause ok | citations ok | guard | steps "
        "| tool calls | incurred |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for case in summary["cases"]:
        expected = case["expected_decision"]
        if case["expected_decision"] == "escalate":
            expected = f"escalate/{case['expected_cause']}"
        decided = case["decision"]
        if decided is not None and case.get("cause") not in (None, "none"):
            decided = f"{decided}/{case['cause']}"
        if case["status"] != "decided":
            decided = case["status"]
        overridden = case.get("guard_overridden")
        guard_text = "-" if overridden is None else ("overridden" if overridden else "pass")
        model_calls = case.get("model_tool_calls")
        lines.append(
            "| "
            + " | ".join(
                [
                    case["case_id"],
                    expected,
                    decided or "-",
                    _flag(case.get("decision_correct")),
                    _flag(case.get("cause_correct")),
                    _flag(case.get("citations_valid")),
                    guard_text,
                    "-" if case.get("steps") is None else str(case["steps"]),
                    "-" if model_calls is None else str(model_calls),
                    str(case.get("incurred_cost_usd") or "-"),
                ]
            )
            + " |"
        )
    return "\n".join(lines) + "\n"


def write_summary(run_dir: Path, summary: dict[str, Any]) -> None:
    """Write ``summary.json`` (sorted keys, indent 2) and ``summary.md`` into ``run_dir``."""
    text = json.dumps(summary, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    (run_dir / "summary.json").write_text(text, encoding="utf-8")
    (run_dir / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
