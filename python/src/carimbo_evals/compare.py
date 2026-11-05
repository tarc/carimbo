"""Compare two graded runs from their ``summary.json`` files (D-45).

Pure and offline: standard library only, no HTTP client, and ``cases.jsonl`` is never opened, so
it works on the summaries that are committed. A comparison that is not meaningful is refused with
:class:`CompareRefusal`; there is no override (D-18).
"""

from __future__ import annotations

import json
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any

from carimbo_evals.summary import SUMMARY_VERSION

COMPARE_VERSION = "compare-1"
# A summary is a few hundred KB; this bounds the work a crafted file can cause (T-05-16).
MAX_SUMMARY_BYTES = 64 * 1024 * 1024
EXTRACTION_ONLY = "compare covers extraction runs only"
REGRADE_HINT = (
    "regrade both runs with the current grader: carimbo-evals grade --run RUN --cases CASES"
)


class CompareRefusal(ValueError):
    """The two runs cannot be compared, or a file is not a usable summary."""


def _refuse(path: Path, problem: str) -> CompareRefusal:
    return CompareRefusal(f"{path}: {problem}")


def _is_text(value: Any) -> bool:
    return isinstance(value, str)


def _tolerance(summary: dict[str, Any]) -> Decimal | None:
    """The tolerance as a finite Decimal, or None when it is not one."""
    value = summary.get("tolerance")
    if not isinstance(value, str):
        return None
    try:
        parsed = Decimal(value)
    except InvalidOperation:
        return None
    return parsed if parsed.is_finite() else None


def _validate_case(path: Path, index: int, case: Any, seen: set[str]) -> None:
    where = f"cases[{index}]"
    if not isinstance(case, dict):
        raise _refuse(path, f"{where} is not an object")
    case_id = case.get("case_id")
    if not isinstance(case_id, str):
        raise _refuse(path, f"{where} has no string case_id")
    if case_id in seen:
        raise _refuse(path, f"case id {case_id} appears twice")
    seen.add(case_id)
    if not _is_text(case.get("status")):
        raise _refuse(path, f"case {case_id} has no string status")
    if "correct" not in case or not (case["correct"] is None or isinstance(case["correct"], bool)):
        raise _refuse(path, f"case {case_id}: correct must be true, false or null")
    wrong = case.get("wrong")
    if not isinstance(wrong, list) or not all(
        isinstance(entry, dict) and _is_text(entry.get("path")) for entry in wrong
    ):
        raise _refuse(path, f"case {case_id}: wrong must be a list of objects with a string path")


def _validate(path: Path, summary: Any) -> dict[str, Any]:
    """The summary when it has the shape ``compare`` relies on; otherwise a refusal."""
    if not isinstance(summary, dict):
        raise _refuse(path, "the root is not an object")
    version = summary.get("summary_version")
    if isinstance(version, str):
        raise _refuse(path, f"summary version {version} is a decision run; {EXTRACTION_ONLY}")
    if isinstance(version, bool) or not isinstance(version, int):
        raise _refuse(path, "summary_version is missing or not a number")
    if version < SUMMARY_VERSION:
        raise _refuse(path, f"summary version {version} comes from an older grader; {REGRADE_HINT}")
    if version != SUMMARY_VERSION:
        raise _refuse(path, f"summary version {version} is unknown")
    if not _is_text(summary.get("grader_version")) or not _is_text(summary.get("run_id")):
        raise _refuse(path, "grader_version and run_id must be strings")
    if _tolerance(summary) is None:
        raise _refuse(path, f"tolerance {summary.get('tolerance')!r} is not a finite decimal")
    dataset = summary.get("dataset")
    if not isinstance(dataset, dict) or not _is_text(dataset.get("manifest_sha256")):
        raise _refuse(path, "no dataset manifest hash, so sameness cannot be shown")
    if not isinstance(summary.get("config"), dict):
        raise _refuse(path, "config is not an object")
    cases = summary.get("cases")
    if not isinstance(cases, list):
        raise _refuse(path, "cases is not a list")
    seen: set[str] = set()
    for index, case in enumerate(cases):
        _validate_case(path, index, case, seen)
    return summary


def load_summary(path: Path) -> dict[str, Any]:
    """The validated ``summary.json`` of a run directory, or of a summary file given directly.

    The file is data: it is size-checked before it is parsed, parsed as JSON only, and shape-checked
    so that anything unusable becomes a :class:`CompareRefusal` rather than a traceback.
    """
    target = path / "summary.json" if path.is_dir() else path
    try:
        size = target.stat().st_size
        if size > MAX_SUMMARY_BYTES:
            raise _refuse(target, f"the file is larger than {MAX_SUMMARY_BYTES} bytes")
        data = json.loads(target.read_text(encoding="utf-8"))
    except (OSError, ValueError, RecursionError) as exc:
        raise _refuse(target, f"cannot be read as JSON ({exc})") from exc
    return _validate(target, data)


def check_comparable(first: dict[str, Any], second: dict[str, Any]) -> None:
    """Raise :class:`CompareRefusal` unless the two summaries were graded the same way.

    Checked in order: the dataset manifest hash, the grader version, then the tolerance as a
    ``Decimal`` (so ``0`` and ``0.00`` are the same). The message carries both values.
    """
    hashes = (first["dataset"]["manifest_sha256"], second["dataset"]["manifest_sha256"])
    if hashes[0] != hashes[1]:
        raise CompareRefusal(
            f"dataset manifests differ: {hashes[0]} and {hashes[1]}; {REGRADE_HINT}"
        )
    graders = (first["grader_version"], second["grader_version"])
    if graders[0] != graders[1]:
        raise CompareRefusal(
            f"grader versions differ: {graders[0]} and {graders[1]}; {REGRADE_HINT}"
        )
    tolerances = (first["tolerance"], second["tolerance"])
    if Decimal(tolerances[0]) != Decimal(tolerances[1]):
        raise CompareRefusal(
            f"tolerances differ: {tolerances[0]} and {tolerances[1]}; {REGRADE_HINT}"
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
