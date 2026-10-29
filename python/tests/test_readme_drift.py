"""README drift checks: the reviewer's entry point must not lag the repo.

The README is the first thing a reviewer reads. Its Status table, verification links,
docs-check range and run links drift silently from the ROADMAP, the decision records and
evals/results. Each check is a pure function returning a list of problem messages, so the
negative tests can feed it a drifted copy and prove it reports the drift.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]

README_STATUS_HEADER = "| Phase | What it delivers | State |"
ROADMAP_PROGRESS_HEADER = "| Phase | Plans Complete | Status | Completed |"
PHASE_CELL = re.compile(r"^(?P<number>\d+(?:\.\d+)*)\.\s")
ISO_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def _read(relative: str) -> str:
    return (REPO_ROOT / relative).read_text(encoding="utf-8")


def phase_key(number: str) -> str:
    """Normalize a phase number: ``03.1`` becomes ``3.1`` and ``01`` becomes ``1``."""
    return ".".join(str(int(part)) for part in number.split("."))


def table_rows(text: str, header: str, source: str) -> list[list[str]]:
    """Cells of every row under ``header``; fails when the table is absent or empty."""
    lines = [line.rstrip() for line in text.splitlines()]
    if header not in lines:
        raise AssertionError(
            f"{source} has no table with header {header}: restore the table or update "
            "README drift test"
        )
    rows: list[list[str]] = []
    for line in lines[lines.index(header) + 2 :]:
        if not line.startswith("|"):
            break
        rows.append([cell.strip() for cell in line.strip("|").split("|")])
    if not rows:
        raise AssertionError(
            f"{source} has a table with header {header} but no rows: add the phase rows"
        )
    return rows


def _phase_number(cell: str, source: str, row: list[str]) -> str:
    match = PHASE_CELL.match(cell)
    if match is None:
        raise AssertionError(
            f"{source} has a table row without a leading phase number: {row}; "
            "start the first cell with the phase number"
        )
    return phase_key(match["number"])


def readme_status(text: str) -> dict[str, str]:
    """Phase key to the State cell of README's Status table."""
    status: dict[str, str] = {}
    for row in table_rows(text, README_STATUS_HEADER, "README.md"):
        status[_phase_number(row[0], "README.md", row)] = row[-1]
    return status


def roadmap_progress(text: str) -> dict[str, tuple[str, str]]:
    """Phase key to (Status, Completed) of the ROADMAP progress table."""
    progress: dict[str, tuple[str, str]] = {}
    for row in table_rows(text, ROADMAP_PROGRESS_HEADER, ".planning/ROADMAP.md"):
        number = _phase_number(row[0], ".planning/ROADMAP.md", row)
        progress[number] = (row[2], row[3])
    return progress


def _sort_key(number: str) -> tuple[int, ...]:
    return tuple(int(part) for part in number.split("."))


def status_drift(
    readme_rows: dict[str, str], roadmap_rows: dict[str, tuple[str, str]]
) -> list[str]:
    """Problems between README's Status table and the ROADMAP progress table."""
    problems: list[str] = []
    for number in sorted(roadmap_rows, key=_sort_key):
        status, completed = roadmap_rows[number]
        state = readme_rows.get(number)
        if state is None:
            problems.append(
                f"README.md Status table has no row for Phase {number}, which "
                f".planning/ROADMAP.md lists: add a row for Phase {number}"
            )
            continue
        if status == "Complete":
            if not ISO_DATE.match(completed):
                problems.append(
                    f"Phase {number} is Complete in .planning/ROADMAP.md without an ISO date "
                    f"('{completed}'): record the completion date as YYYY-MM-DD in "
                    ".planning/ROADMAP.md"
                )
            elif state != f"Complete ({completed})":
                problems.append(
                    f"Phase {number} is Complete on {completed} in .planning/ROADMAP.md, but "
                    f"README.md's Status row says '{state}': update the State cell to "
                    f"'Complete ({completed})'"
                )
        elif state.startswith("Complete"):
            problems.append(
                f"README.md's Status row for Phase {number} says '{state}' before "
                ".planning/ROADMAP.md marks the phase Complete: keep the State cell off "
                "Complete until it does"
            )
    for number in sorted(set(readme_rows) - set(roadmap_rows), key=_sort_key):
        problems.append(
            f"README.md Status table lists Phase {number}, which .planning/ROADMAP.md does not: "
            f"remove the README row or add Phase {number} to the ROADMAP"
        )
    return problems


def test_status_table_matches_the_roadmap() -> None:
    problems = status_drift(
        readme_status(_read("README.md")), roadmap_progress(_read(".planning/ROADMAP.md"))
    )
    assert problems == [], "\n".join(problems)


ROADMAP_FIXTURE = f"""\
## Progress

{ROADMAP_PROGRESS_HEADER}
|-------|----------------|--------|-----------|
| 3.1. Thin Agent Slice | 13/13 | Complete    | 2026-10-22 |
| 4. Synthetic Dataset | 0/TBD | Not started | - |
"""


def _readme_fixture(state_31: str, state_4: str | None = "Not started") -> str:
    rows = [f"| 3.1. Thin Agent Slice (inserted) | Agent | {state_31} |"]
    if state_4 is not None:
        rows.append(f"| 4. Synthetic Dataset | Data | {state_4} |")
    return "\n".join([README_STATUS_HEADER, "|---|---|---|", *rows]) + "\n"


def _drift(readme: str) -> list[str]:
    return status_drift(readme_status(readme), roadmap_progress(ROADMAP_FIXTURE))


def test_status_drift_accepts_rows_in_sync() -> None:
    assert _drift(_readme_fixture("Complete (2026-10-22)")) == []


@pytest.mark.parametrize(
    ("state_31", "state_4", "phase"),
    [
        pytest.param("Not started", "Not started", "3.1", id="stale"),
        pytest.param("Complete (2026-10-21)", "Not started", "3.1", id="wrong-date"),
        pytest.param("Complete (2026-10-22)", "Complete (2026-10-29)", "4", id="premature"),
    ],
)
def test_status_drift_catches_a_stale_or_premature_row(
    state_31: str, state_4: str, phase: str
) -> None:
    problems = _drift(_readme_fixture(state_31, state_4))
    assert problems
    assert any(f"Phase {phase}" in problem for problem in problems)
    if phase == "3.1":
        assert any("Complete (2026-10-22)" in problem for problem in problems)


def test_status_drift_catches_a_missing_row() -> None:
    problems = _drift(_readme_fixture("Complete (2026-10-22)", None))
    assert problems
    assert any("Phase 4" in problem for problem in problems)


def test_table_rows_rejects_an_empty_or_unnumbered_table() -> None:
    with pytest.raises(AssertionError):
        table_rows(f"{README_STATUS_HEADER}\n|---|---|---|\n", README_STATUS_HEADER, "README.md")
    with pytest.raises(AssertionError):
        table_rows("no table here\n", README_STATUS_HEADER, "README.md")
    with pytest.raises(AssertionError):
        readme_status(f"{README_STATUS_HEADER}\n|---|---|---|\n| Intro | text | Complete |\n")
