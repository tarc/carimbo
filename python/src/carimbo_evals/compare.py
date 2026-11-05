"""Compare two graded runs from their ``summary.json`` files (D-45).

Pure and offline: standard library only, no HTTP client, and ``cases.jsonl`` is never opened, so
it works on the summaries that are committed. A comparison that is not meaningful is refused with
:class:`CompareRefusal`; there is no override (D-18).
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

COMPARE_VERSION = "compare-1"
REGRADE_HINT = (
    "regrade both runs with the current grader: carimbo-evals grade --run RUN --cases CASES"
)


class CompareRefusal(ValueError):
    """The two runs cannot be compared, or a file is not a usable summary."""


def load_summary(path: Path) -> dict[str, Any]:
    """The parsed ``summary.json`` of a run directory, or of a summary file given directly."""
    target = path / "summary.json" if path.is_dir() else path
    try:
        data = json.loads(target.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise CompareRefusal(f"{target}: cannot be read as JSON ({exc})") from exc
    if not isinstance(data, dict):
        raise CompareRefusal(f"{target}: the root is not an object")
    return data


def check_comparable(first: dict[str, Any], second: dict[str, Any]) -> None:
    """Raise :class:`CompareRefusal` unless the two summaries were graded the same way."""
    if first.get("grader_version") != second.get("grader_version"):
        raise CompareRefusal(
            f"grader versions differ: {first.get('grader_version')} and "
            f"{second.get('grader_version')}; {REGRADE_HINT}"
        )


def _wrong_by_path(case: dict[str, Any]) -> dict[str, str]:
    """Path to tag of the wrong values of one case; the first entry wins for a repeated path."""
    found: dict[str, str] = {}
    for entry in case.get("wrong", []):
        found.setdefault(entry["path"], str(entry.get("tag")))
    return found


def _flipped(first: dict[str, Any], second: dict[str, Any]) -> list[dict[str, str]]:
    """The paths wrong in exactly one of the two runs, sorted by path, with the tag of that run."""
    before, after = _wrong_by_path(first), _wrong_by_path(second)
    flips = [
        {"path": path, "tag": after[path], "change": "became_wrong"}
        for path in after.keys() - before.keys()
    ] + [
        {"path": path, "tag": before[path], "change": "became_right"}
        for path in before.keys() - after.keys()
    ]
    return sorted(flips, key=lambda flip: flip["path"])


def _by_case(summary: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {case["case_id"]: case for case in summary["cases"]}


def _entry(case_id: str, first: dict[str, Any], second: dict[str, Any]) -> dict[str, Any]:
    return {
        "case_id": case_id,
        "first_status": first["status"],
        "second_status": second["status"],
        "first_wrong": len(first.get("wrong", [])),
        "second_wrong": len(second.get("wrong", [])),
        "flipped": _flipped(first, second),
    }


def compare_summaries(first: dict[str, Any], second: dict[str, Any]) -> dict[str, Any]:
    """The comparison of two summaries already shown to be comparable."""
    left, right = _by_case(first), _by_case(second)
    shared = sorted(left.keys() & right.keys())
    regressed = [
        _entry(case_id, left[case_id], right[case_id])
        for case_id in shared
        if left[case_id]["correct"] is True and right[case_id]["correct"] is False
    ]
    return {
        "compare_version": COMPARE_VERSION,
        "first": {"run_id": first["run_id"]},
        "second": {"run_id": second["run_id"]},
        "cases": {
            "shared": len(shared),
            "excluded": len(left.keys() ^ right.keys()),
            "only_first": sorted(left.keys() - right.keys()),
            "only_second": sorted(right.keys() - left.keys()),
        },
        "regressed": regressed,
    }


def render_markdown(comparison: dict[str, Any]) -> str:
    """The comparison as markdown."""
    lines = [
        "# Run comparison",
        "",
        f"- First run: {comparison['first']['run_id']}",
        f"- Second run: {comparison['second']['run_id']}",
        f"- Shared cases: {comparison['cases']['shared']} "
        f"(excluded: {comparison['cases']['excluded']})",
        "",
        f"## regressed ({len(comparison['regressed'])})",
        "",
    ]
    for entry in comparison["regressed"]:
        lines.append(
            f"- {entry['case_id']}: {entry['first_status']} to {entry['second_status']}, "
            f"wrong values {entry['first_wrong']} to {entry['second_wrong']}"
        )
        lines.extend(
            f"  - {flip['path']} {flip['change']} ({flip['tag']})" for flip in entry["flipped"]
        )
    return "\n".join(lines) + "\n"


def write_comparison(out_dir: Path, comparison: dict[str, Any]) -> None:
    """Write ``compare.json`` (sorted keys, indent 2) and ``compare.md`` into ``out_dir``."""
    out_dir.mkdir(parents=True, exist_ok=True)
    text = json.dumps(comparison, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    (out_dir / "compare.json").write_text(text, encoding="utf-8")
    (out_dir / "compare.md").write_text(render_markdown(comparison), encoding="utf-8")
