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

from carimbo_evals.matching import INSTALLMENT_COLUMNS, ITEM_COLUMNS
from carimbo_evals.stats import is_significant, mcnemar_exact
from carimbo_evals.summary import FIELDS, GRADED_STATUSES, SUMMARY_VERSION

COMPARE_VERSION = "compare-1"
# A summary is a few hundred KB; this bounds the work a crafted file can cause (T-05-16).
MAX_SUMMARY_BYTES = 64 * 1024 * 1024
EXTRACTION_ONLY = "compare covers extraction runs only"
SCOPE = "case sampling only, not run-to-run model variation"
# The run configuration keys printed as "what changed" when the two runs differ.
CONFIG_KEYS = (
    "models_requested",
    "models_returned",
    "prompt_versions",
    "max_repairs",
    "cache_modes",
    "replicate_salts",
    "schema_sha256s",
    "pricing_versions",
)
REGRADE_HINT = (
    "regrade both runs with the current grader: "
    "carimbo-evals grade --run RUN --cases CASES --out DIR"
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


def _is_count(value: Any) -> bool:
    """A real integer: not a bool, and not the float that ``Infinity`` or ``NaN`` parse to."""
    return isinstance(value, int) and not isinstance(value, bool)


def _validate_row_block(path: Path, case_id: str, case: dict[str, Any], block: str) -> None:
    """``items`` or ``installments``: absent or null, or an object whose ``columns`` is an object
    of ``{"correct": int, "n": int}`` cells (the only members ``compare`` reads)."""
    value = case.get(block)
    if value is None:
        return
    columns = value.get("columns") if isinstance(value, dict) else None
    if not isinstance(value, dict) or (columns is not None and not isinstance(columns, dict)):
        raise _refuse(path, f"case {case_id}: {block}.columns must be an object")
    for column, cell in (columns or {}).items():
        if not isinstance(cell, dict):
            continue  # ignored by compare, as before
        for key in ("correct", "n"):
            if key in cell and not _is_count(cell[key]):
                where = f"{block}.columns.{column}.{key}"
                raise _refuse(path, f"case {case_id}: {where} is not a count")


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
    if case.get("fields") is not None and not isinstance(case["fields"], dict):
        raise _refuse(path, f"case {case_id}: fields must be an object")
    for block in ("items", "installments"):
        _validate_row_block(path, case_id, case, block)


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
        "first_wrong": len(first["wrong"]),
        "second_wrong": len(second["wrong"]),
        "flipped": _flipped(first, second),
    }


def _row(comparable: list[tuple[str, dict[str, Any], dict[str, Any]]]) -> tuple[str | None, list]:
    """The cases the McNemar test runs on, and the label of that row (D-16).

    The text-layer cases of split ``test`` when there are any, otherwise every text-layer case,
    labelled with its splits. Image-only cases never enter.
    """
    text = [item for item in comparable if item[1].get("rendering") == "text"]
    if not text:
        return None, []
    test = [item for item in text if item[1].get("split") == "test"]
    if test:
        return "test text", test
    splits = sorted({str(item[1].get("split")) for item in text})
    return f"{','.join(splits)} text", text


def _verdict(comparable: list[tuple[str, dict[str, Any], dict[str, Any]]]) -> dict[str, Any]:
    label, chosen = _row(comparable)
    b = sum(1 for _, first, second in chosen if first["correct"] and not second["correct"])
    c = sum(1 for _, first, second in chosen if second["correct"] and not first["correct"])
    p = mcnemar_exact(b, c)
    if not chosen:
        verdict, reason = "inconclusive", "no shared text-layer case"
    elif b + c == 0:
        verdict, reason = "no change", "no case is correct in one run only"
    elif is_significant(p):
        verdict, reason = "significant", "p is below alpha"
    else:
        verdict, reason = "inconclusive", "p is not below alpha"
    return {
        "test": "exact_mcnemar",
        "row": label,
        "cases": len(chosen),
        "b": b,
        "c": c,
        "n": b + c,
        "p": str(p),
        # a rendering for display only; the verdict above is decided on the exact fraction
        "p_value": round(float(p), 4),
        "alpha": "0.05",
        "verdict": verdict,
        "reason": reason,
        "scope": SCOPE,
    }


def _changed(first: dict[str, Any], second: dict[str, Any]) -> dict[str, Any]:
    changed: dict[str, Any] = {}
    for key in CONFIG_KEYS:
        before, after = first["config"].get(key, []), second["config"].get(key, [])
        if before != after:
            changed[key] = {"first": before, "second": after}
    return changed


def _tally(cases: list[dict[str, Any]], name: str) -> dict[str, int]:
    """How many of ``cases`` grade ``name`` right and how many grade it at all."""
    correct = n = 0
    for case in cases:
        if name.startswith(("item.", "installment.")):
            block, _, column = name.partition(".")
            cell = (case.get(f"{block}s") or {}).get("columns", {}).get(column)
            if isinstance(cell, dict):
                correct += int(cell.get("correct", 0))
                n += int(cell.get("n", 0))
        elif isinstance(case.get("fields"), dict) and name in case["fields"]:
            n += 1
            correct += 1 if case["fields"][name] is True else 0
    return {"correct": correct, "n": n}


def _field_deltas(
    comparable: list[tuple[str, dict[str, Any], dict[str, Any]]],
) -> dict[str, Any]:
    """Descriptive per-field counts over the comparable shared cases; each run counts the cases it
    graded, and no test is run on them (D-16)."""
    names = (
        list(FIELDS)
        + [f"item.{column}" for column in ITEM_COLUMNS]
        + [f"installment.{column}" for column in INSTALLMENT_COLUMNS]
    )
    firsts, seconds = [first for _, first, _ in comparable], [second for _, _, second in comparable]
    deltas: dict[str, Any] = {}
    for name in names:
        before, after = _tally(firsts, name), _tally(seconds, name)
        deltas[name] = {
            "first": before,
            "second": after,
            "delta": after["correct"] - before["correct"],
        }
    return deltas


def compare_summaries(first: dict[str, Any], second: dict[str, Any]) -> dict[str, Any]:
    """The comparison of two summaries already shown to be comparable."""
    left, right = _by_case(first), _by_case(second)
    shared = sorted(left.keys() & right.keys())
    pairs = [(case_id, left[case_id], right[case_id]) for case_id in shared]
    not_comparable = [p for p in pairs if p[1]["correct"] is None or p[2]["correct"] is None]
    comparable = [p for p in pairs if p not in not_comparable]

    regressed, worsened, improved, status_changed = [], [], [], []
    for case_id, before, after in comparable:
        if before["correct"] and not after["correct"]:
            regressed.append(_entry(case_id, before, after))
        elif not before["correct"] and after["correct"]:
            improved.append(_entry(case_id, before, after))
        elif not before["correct"] and not after["correct"]:
            # Wrong-value counts are comparable only when both answers were graded: a refusal or a
            # truncation has no wrong values because it has no grade, not because it is better.
            if before["status"] in GRADED_STATUSES and after["status"] in GRADED_STATUSES:
                if len(after["wrong"]) > len(before["wrong"]):
                    worsened.append(_entry(case_id, before, after))
                elif len(after["wrong"]) < len(before["wrong"]):
                    improved.append(_entry(case_id, before, after))
            elif before["status"] != after["status"]:
                status_changed.append(_entry(case_id, before, after))
    return {
        "compare_version": COMPARE_VERSION,
        "first": {"run_id": first["run_id"]},
        "second": {"run_id": second["run_id"]},
        "same": {
            "dataset": first["dataset"],
            "grader_version": first["grader_version"],
            "tolerance": first["tolerance"],
        },
        "changed": _changed(first, second),
        "cases": {
            "shared": len(shared),
            "excluded": len(left.keys() ^ right.keys()),
            "only_first": sorted(left.keys() - right.keys()),
            "only_second": sorted(right.keys() - left.keys()),
        },
        "verdict": _verdict(comparable),
        "field_deltas": _field_deltas(comparable),
        "regressed": regressed,
        "worsened": worsened,
        "improved": improved,
        "status_changed": status_changed,
        "not_comparable": [_entry(*p) for p in not_comparable],
    }


def _case_lines(title: str, entries: list[dict[str, Any]]) -> list[str]:
    lines = [f"## {title} ({len(entries)})", ""]
    for entry in entries:
        lines.append(
            f"- {entry['case_id']}: {entry['first_status']} to {entry['second_status']}, "
            f"wrong values {entry['first_wrong']} to {entry['second_wrong']}"
        )
        lines.extend(
            f"  - {flip['path']} {flip['change']} ({flip['tag']})" for flip in entry["flipped"]
        )
    return [*lines, ""]


def render_markdown(comparison: dict[str, Any]) -> str:
    """The comparison as markdown, in the order of D-45."""
    same, cases, verdict = comparison["same"], comparison["cases"], comparison["verdict"]
    dataset = same["dataset"]
    lines = [
        "# Run comparison",
        "",
        f"- First run: {comparison['first']['run_id']}",
        f"- Second run: {comparison['second']['run_id']}",
        "",
        "## Same in both runs",
        "",
        f"- Dataset: {dataset.get('name')} {dataset.get('version')} ({dataset['manifest_sha256']})",
        f"- Grader: {same['grader_version']}",
        f"- Tolerance: {same['tolerance']}",
        "",
        "## Changed",
        "",
    ]
    for key, pair in comparison["changed"].items():
        lines.append(f"- {key}: {pair['first']} to {pair['second']}")
    if not comparison["changed"]:
        lines.append("- Nothing in the run configuration differs.")
    lines += [
        "",
        "## Cases",
        "",
        f"- Shared cases: {cases['shared']} (excluded: {cases['excluded']})",
        f"- Only in the first run: {', '.join(cases['only_first']) or 'none'}",
        f"- Only in the second run: {', '.join(cases['only_second']) or 'none'}",
        "",
        "## Verdict",
        "",
        f"Exact McNemar test on the correct-invoice flag, row {verdict['row'] or 'none'} "
        f"({verdict['cases']} cases): **{verdict['verdict']}**",
        "",
        f"- b (correct in the first run only): {verdict['b']}",
        f"- c (correct in the second run only): {verdict['c']}",
        f"- n (discordant cases): {verdict['n']}",
        f"- exact p: {verdict['p']} (about {verdict['p_value']}), alpha {verdict['alpha']}",
        f"- reason: {verdict['reason']}",
        "",
        f"This covers {verdict['scope']}.",
        "",
        "## Field deltas",
        "",
    ]
    moved = {name: d for name, d in comparison["field_deltas"].items() if d["delta"] != 0}
    for name, d in moved.items():
        lines.append(
            f"- {name}: {d['first']['correct']}/{d['first']['n']} to "
            f"{d['second']['correct']}/{d['second']['n']} ({d['delta']:+d})"
        )
    if not moved:
        lines.append("- No field changed.")
    lines.append("")
    for key in ("regressed", "worsened", "improved", "status_changed", "not_comparable"):
        lines += _case_lines(key.replace("_", " "), comparison[key])
    return "\n".join(lines).rstrip("\n") + "\n"


def write_comparison(out_dir: Path, comparison: dict[str, Any]) -> None:
    """Write ``compare.json`` (sorted keys, indent 2) and ``compare.md`` into ``out_dir``."""
    out_dir.mkdir(parents=True, exist_ok=True)
    text = json.dumps(comparison, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    (out_dir / "compare.json").write_text(text, encoding="utf-8")
    (out_dir / "compare.md").write_text(render_markdown(comparison), encoding="utf-8")
