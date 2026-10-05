"""Run summary: aggregates per-case grades into ``summary.json`` and ``summary.md``.

Pure and offline: no HTTP client, no sockets. The only volatile value is ``meta.graded_at``.
"""

from __future__ import annotations

import json
import statistics
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any

from carimbo_evals.money import parse_cost

SUMMARY_VERSION = 1
GRADER_VERSION = "grader-001"

OUTCOME_STATUSES = (
    "success",
    "refused",
    "truncated",
    "schema_invalid",
    "infrastructure_failure",
    "harness_error",
)
# Statuses whose answer is graded field by field. A schema_invalid answer is a wrong answer;
# refusals, truncations, infrastructure failures and harness errors are not.
GRADED_STATUSES = ("success", "schema_invalid")
FIELDS = (
    "access_key",
    "number",
    "series",
    "issue_date",
    "issuer.cnpj",
    "issuer.name",
    "recipient.cnpj",
    "recipient.name",
    "total_amount",
)
SCHEMA_CHECKS = ("jsonschema", "pydantic")
TOKEN_FIELDS = (
    "input_tokens",
    "output_tokens",
    "cache_read_tokens",
    "cache_write_5m_tokens",
    "cache_write_1h_tokens",
)
CONFIG_KEYS = {
    "models_requested": "model_requested",
    "models_returned": "model_returned",
    "prompt_versions": "prompt_version",
    "schema_sha256s": "schema_sha256",
    "pricing_versions": "pricing_version",
}


def _unique(case_grades: list[dict[str, Any]], key: str) -> list[str]:
    return sorted({str(c[key]) for c in case_grades if c.get(key) is not None})


def _whole(value: int | float) -> int | float:
    return int(value) if float(value).is_integer() else value


def _latency(case_grades: list[dict[str, Any]]) -> dict[str, int | float | None]:
    values = [
        c["latency_ms"]
        for c in case_grades
        if isinstance(c.get("latency_ms"), int) and not isinstance(c["latency_ms"], bool)
    ]
    if not values:
        return {"min": None, "median": None, "max": None}
    return {"min": min(values), "median": _whole(statistics.median(values)), "max": max(values)}


def _totals(case_grades: list[dict[str, Any]]) -> dict[str, Any]:
    tokens = dict.fromkeys(TOKEN_FIELDS, 0)
    cost = Decimal(0)
    unpriced = 0
    for case in case_grades:
        usage = case.get("usage")
        if isinstance(usage, dict):
            for name in TOKEN_FIELDS:
                value = usage.get(name)
                if isinstance(value, int) and not isinstance(value, bool):
                    tokens[name] += value
        if case["status"] == "harness_error":
            continue  # no response, so nothing was priced or unpriced
        priced = parse_cost(case.get("cost_usd"))
        if priced is None:
            unpriced += 1
        else:
            cost += priced
    return {**tokens, "cost_usd": str(cost), "unpriced_cases": unpriced}


def build_summary(
    case_grades: list[dict[str, Any]],
    run_meta: dict[str, Any],
    manifest: dict[str, Any] | None,
) -> dict[str, Any]:
    """Aggregate per-case grades.

    ``run_meta`` carries ``run_id`` and ``tolerance``; ``manifest`` is ``{name, version,
    manifest_sha256}`` or ``None`` when the cases directory has no manifest.
    """
    cases = sorted(case_grades, key=lambda c: c["case_id"])
    counts = dict.fromkeys(OUTCOME_STATUSES, 0)
    for case in cases:
        counts[case["status"]] += 1
    field_accuracy = {field: {"correct": 0, "n": 0} for field in FIELDS}
    schema_validity = {check: {"correct": 0, "n": 0} for check in SCHEMA_CHECKS}
    for case in cases:
        for field, correct in case.get("fields", {}).items():
            field_accuracy[field]["n"] += 1
            field_accuracy[field]["correct"] += int(correct)
        if case["status"] in GRADED_STATUSES:
            for check in SCHEMA_CHECKS:
                schema_validity[check]["n"] += 1
                schema_validity[check]["correct"] += int(bool(case.get(f"schema_valid_{check}")))
    return {
        "summary_version": SUMMARY_VERSION,
        "grader_version": GRADER_VERSION,
        "run_id": run_meta["run_id"],
        "tolerance": str(run_meta["tolerance"]),
        "dataset": manifest,
        "config": {name: _unique(cases, key) for name, key in CONFIG_KEYS.items()},
        "counts": {**counts, "total": len(cases)},
        "field_accuracy": field_accuracy,
        "schema_validity": schema_validity,
        "cases": cases,
        "totals": _totals(cases),
        "latency_ms": _latency(cases),
        "meta": {"graded_at": datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")},
    }


def render_markdown(summary: dict[str, Any]) -> str:
    counts = summary["counts"]
    count_text = ", ".join(f"{name} {counts[name]}" for name in OUTCOME_STATUSES if counts[name])
    models = ", ".join(
        summary["config"]["models_returned"] or summary["config"]["models_requested"]
    )
    totals = summary["totals"]
    lines = [
        f"# Run {summary['run_id']}",
        "",
        f"- models: {models or '-'}",
        f"- cases: {counts['total']} ({count_text or 'none'})",
        f"- cost: US${totals['cost_usd']} ({totals['unpriced_cases']} unpriced), "
        f"tokens in/out: {totals['input_tokens']}/{totals['output_tokens']}",
        f"- grader: {summary['grader_version']}, tolerance {summary['tolerance']}",
        "",
        "| case_id | status | fields | total delta | cost | latency ms | trace id |",
        "|---|---|---|---|---|---|---|",
    ]
    for case in summary["cases"]:
        fields = case.get("fields")
        graded = f"{sum(fields.values())}/{len(fields)}" if fields is not None else "-"
        lines.append(
            "| "
            + " | ".join(
                [
                    case["case_id"],
                    case["status"],
                    graded,
                    str(case.get("total_delta") or "-"),
                    str(case.get("cost_usd") or "-"),
                    str(case.get("latency_ms") if case.get("latency_ms") is not None else "-"),
                    str(case.get("trace_id") or "-"),
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
