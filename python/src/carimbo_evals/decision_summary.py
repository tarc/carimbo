"""Decision run summary: aggregates per-case grades into ``summary.json`` and ``summary.md``.

Pure and offline. The only volatile value is ``meta.graded_at``. Rates are computed only when the
denominator is above zero and are ``null`` otherwise, never ``0.0``.
"""

from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

SUMMARY_VERSION = "decision-summary-1"
GRADER_VERSION = "decision-grader-001"

STATUSES = (
    "decided",
    "refused",
    "truncated",
    "schema_invalid",
    "infrastructure_failure",
    "harness_error",
)


def _rate(correct: int, n: int) -> float | None:
    return correct / n if n else None


def _accuracy(cases: list[dict[str, Any]], key: str) -> dict[str, Any]:
    graded = [c[key] for c in cases if c.get(key) is not None]
    correct = sum(1 for value in graded if value)
    return {"n": len(graded), "correct": correct, "rate": _rate(correct, len(graded))}


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
    guarded = [c for c in cases if c.get("guard_overridden") is not None]
    return {
        "summary_version": SUMMARY_VERSION,
        "grader_version": GRADER_VERSION,
        "run_id": run_meta["run_id"],
        "labelling_version": run_meta.get("labelling_version"),
        "dataset": manifest,
        "counts": {**counts, "total": len(cases)},
        "accuracy": _accuracy(cases, "decision_correct"),
        "cause_accuracy": _accuracy(cases, "cause_correct"),
        "citations": {
            "n": len(citation_grades),
            "valid": sum(1 for value in citation_grades if value),
        },
        "guard": {
            "checked": len(guarded),
            "overridden": sum(1 for c in guarded if c["guard_overridden"]),
        },
        "cases": cases,
        "meta": {"graded_at": datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")},
    }


def render_markdown(summary: dict[str, Any]) -> str:
    counts = summary["counts"]
    accuracy = summary["accuracy"]
    count_text = ", ".join(f"{name} {counts[name]}" for name in STATUSES if counts[name])
    lines = [
        f"# Decision run {summary['run_id']}",
        "",
        f"- cases: {counts['total']} ({count_text or 'none'})",
        f"- decision accuracy: {accuracy['correct']}/{accuracy['n']}",
        f"- grader: {summary['grader_version']}",
        "",
        "| case | expected | decision | correct |",
        "|---|---|---|---|",
    ]
    for case in summary["cases"]:
        correct = case.get("decision_correct")
        lines.append(
            f"| {case['case_id']} | {case['expected_decision']} | {case.get('decision') or '-'} "
            f"| {'-' if correct is None else str(correct).lower()} |"
        )
    return "\n".join(lines) + "\n"


def write_summary(run_dir: Path, summary: dict[str, Any]) -> None:
    """Write ``summary.json`` (sorted keys, indent 2) and ``summary.md`` into ``run_dir``."""
    text = json.dumps(summary, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    (run_dir / "summary.json").write_text(text, encoding="utf-8")
    (run_dir / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
