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


# --- Check (b): every Complete phase links its verification report -------------------------

VERIFICATION_HEADING = "### Verification and UAT"
PHASE_BULLET = re.compile(r"^- Phase (?P<number>\d+(?:\.\d+)*): (?P<text>.*)$", flags=re.MULTILINE)


def subsection(text: str, heading: str) -> str | None:
    """Text after the ``heading`` line up to the next line starting with ``#``."""
    index = text.find(heading)
    if index == -1:
        return None
    start = index + len(heading)
    following = re.search(r"^#", text[start:], flags=re.MULTILINE)
    return text[start : start + following.start()] if following else text[start:]


def verification_files() -> dict[str, list[str]]:
    """Phase key to the repo-relative paths of its ``*-VERIFICATION.md`` files."""
    files: dict[str, list[str]] = {}
    for directory in sorted((REPO_ROOT / ".planning" / "phases").iterdir()):
        if directory.is_dir():
            files[phase_key(directory.name.split("-", 1)[0])] = sorted(
                path.relative_to(REPO_ROOT).as_posix()
                for path in directory.glob("*-VERIFICATION.md")
            )
    return files


def verification_drift(
    readme: str, roadmap_rows: dict[str, tuple[str, str]], files: dict[str, list[str]]
) -> list[str]:
    """Problems with the Verification and UAT bullets of the ROADMAP's Complete phases."""
    problems: list[str] = []
    section = subsection(readme, VERIFICATION_HEADING)
    if section is None:
        return [f"README.md has no '{VERIFICATION_HEADING}' section: restore it"]
    bullets: dict[str, str] = {}
    for match in PHASE_BULLET.finditer(section):
        key = phase_key(match["number"])
        bullets[key] = bullets.get(key, "") + match["text"] + "\n"
    for number in sorted(roadmap_rows, key=_sort_key):
        if roadmap_rows[number][0] != "Complete":
            continue
        reports = files.get(number, [])
        if len(reports) != 1:
            problems.append(
                f"Phase {number} is Complete in .planning/ROADMAP.md but .planning/phases has "
                f"{len(reports)} *-VERIFICATION.md files for it: run or restore the phase "
                "verification so exactly one exists"
            )
            continue
        path = reports[0]
        if number not in bullets:
            problems.append(
                f"README.md's Verification and UAT section has no '- Phase {number}:' bullet: "
                f"add one that links {path}"
            )
        elif f"]({path})" not in bullets[number]:
            problems.append(
                f"README.md's '- Phase {number}:' bullet does not link {path}: add "
                f"[`{path.rsplit('/', 1)[-1]}`]({path}) to it"
            )
    return problems


def test_every_complete_phase_links_its_verification() -> None:
    problems = verification_drift(
        _read("README.md"),
        roadmap_progress(_read(".planning/ROADMAP.md")),
        verification_files(),
    )
    assert problems == [], "\n".join(problems)


VERIFICATION_PATH = ".planning/phases/01-walking-skeleton/01-VERIFICATION.md"
VERIFICATION_ROADMAP = {"1": ("Complete", "2026-10-08"), "4": ("Not started", "-")}
VERIFICATION_README_OK = (
    f"## Results\n\n{VERIFICATION_HEADING}\n\n"
    f"- Phase 1: passed ([`01-VERIFICATION.md`]({VERIFICATION_PATH})).\n\n### Next\n"
)


def test_verification_drift_accepts_a_linked_bullet() -> None:
    files = {"1": [VERIFICATION_PATH]}
    assert verification_drift(VERIFICATION_README_OK, VERIFICATION_ROADMAP, files) == []


@pytest.mark.parametrize(
    ("readme", "files", "expected"),
    [
        pytest.param(
            f"{VERIFICATION_HEADING}\n\n- Phase 2: other.\n",
            {"1": [VERIFICATION_PATH]},
            "add one that links " + VERIFICATION_PATH,
            id="no-bullet",
        ),
        pytest.param(
            f"{VERIFICATION_HEADING}\n\n- Phase 1: passed.\n",
            {"1": [VERIFICATION_PATH]},
            "add [`01-VERIFICATION.md`](" + VERIFICATION_PATH + ") to it",
            id="no-link",
        ),
        pytest.param(
            VERIFICATION_README_OK,
            {"1": []},
            "0 *-VERIFICATION.md files",
            id="no-report",
        ),
    ],
)
def test_verification_drift_catches_a_missing_bullet_link_or_report(
    readme: str, files: dict[str, list[str]], expected: str
) -> None:
    problems = verification_drift(readme, VERIFICATION_ROADMAP, files)
    assert len(problems) == 1
    assert "Phase 1" in problems[0]
    assert expected in problems[0]


# --- Check (c): the docs-check range reaches the newest decision record --------------------

DECISION_HEADING = re.compile(r"^## D-(?P<number>\d+) ", flags=re.MULTILINE)
DOCS_LOOP = re.compile(r"^\s*for id in (?P<ids>D-\d+(?: D-\d+)*); do\s*$", flags=re.MULTILINE)
DOCS_ROW_PREFIX = "| `just docs-check` |"
FIRST_CHECKED_DECISION = 18


def _range_problem(source: str, found: list[str], newest: str) -> str | None:
    if len(found) == 1 and found[0] == f"D-18 to {newest}":
        return None
    return (
        f"{source} must contain exactly one 'D-18 to {newest}' but has {len(found)} "
        f"({', '.join(found) or 'none'}): write D-18 to {newest} there"
    )


def docs_range_drift(decisions: str, justfile: str, readme: str, agents: str) -> list[str]:
    """Problems between the newest decision record and every text that names the range."""
    numbers = [int(m["number"]) for m in DECISION_HEADING.finditer(decisions)]
    if not numbers:
        return ["docs/DECISIONS.md has no '## D-NN ' heading: restore the decision records"]
    newest = f"D-{max(numbers):02d}"
    problems: list[str] = []
    expected_ids = [f"D-{n:02d}" for n in range(FIRST_CHECKED_DECISION, max(numbers) + 1)]
    loops = DOCS_LOOP.findall(justfile)
    if len(loops) != 1:
        problems.append(
            f"justfile has {len(loops)} 'for id in D-NN ...; do' loops: keep exactly one, in "
            f"docs-check, listing every id from D-18 to {newest}"
        )
    elif loops[0].split() != expected_ids:
        listed = loops[0].split()
        problems.append(
            f"justfile docs-check loop checks {listed[0]} to {listed[-1]}, but "
            f"docs/DECISIONS.md's newest record is {newest}: make the loop list every id from "
            f"D-18 to {newest}"
        )
    lines = justfile.splitlines()
    header = next((i for i, line in enumerate(lines) if line.startswith("docs-check:")), None)
    if header is None or header == 0 or not lines[header - 1].startswith("#"):
        problems.append("justfile has no comment directly above docs-check: restore it")
    else:
        found = re.findall(r"D-18 to D-\d+", lines[header - 1])
        problem = _range_problem("justfile comment above docs-check", found, newest)
        if problem:
            problems.append(problem)
    for source, text in (("README.md", readme), ("AGENTS.md", agents)):
        rows = [line for line in text.splitlines() if line.startswith(DOCS_ROW_PREFIX)]
        if len(rows) != 1:
            problems.append(f"{source} has {len(rows)} '{DOCS_ROW_PREFIX}' rows: keep exactly one")
            continue
        problem = _range_problem(
            f"{source} docs-check row", re.findall(r"D-18 to D-\d+", rows[0]), newest
        )
        if problem:
            problems.append(problem)
    return problems


def test_docs_check_range_reaches_the_newest_decision() -> None:
    problems = docs_range_drift(
        _read("docs/DECISIONS.md"), _read("justfile"), _read("README.md"), _read("AGENTS.md")
    )
    assert problems == [], "\n".join(problems)


def _range_fixtures(last: int) -> tuple[str, str, str]:
    ids = " ".join(f"D-{n:02d}" for n in range(18, last + 1))
    justfile = (
        f"# Fail unless D-18 to D-{last:02d} are present.\ndocs-check:\n"
        f"    for id in {ids}; do\n      true\n    done\n"
    )
    row = f"| `just docs-check` | decision records D-18 to D-{last:02d} |\n"
    return justfile, row, row


def test_docs_range_drift_accepts_a_matching_range() -> None:
    justfile, readme, agents = _range_fixtures(39)
    assert docs_range_drift("## D-39 Newest\n", justfile, readme, agents) == []


def test_docs_range_drift_catches_a_stale_range() -> None:
    justfile, readme, agents = _range_fixtures(39)
    decisions = "## D-39 Old\n\n## D-40 Newest\n"
    problems = docs_range_drift(decisions, justfile, readme, agents)
    assert len(problems) == 4
    assert all("D-40" in problem for problem in problems)
    assert [p.split()[0] for p in problems] == ["justfile", "justfile", "README.md", "AGENTS.md"]


def test_docs_range_drift_catches_missing_headings() -> None:
    justfile, readme, agents = _range_fixtures(39)
    assert docs_range_drift("no headings\n", justfile, readme, agents)


# --- Check (d): evals/results runs and README links agree ----------------------------------

RUN_LINK = re.compile(r"\]\(evals/results/(?P<run>[^/)\s]+)")


def results_drift(readme: str, run_dirs: list[str]) -> list[str]:
    """Problems between the committed run directories and README's links to them."""
    linked = {m["run"] for m in RUN_LINK.finditer(readme)}
    problems = [
        f"evals/results/{run} is committed but README.md does not link it: cite it in Results "
        "so far (for example its summary.md) or remove the directory"
        for run in sorted(set(run_dirs) - linked)
    ]
    problems += [
        f"README.md links evals/results/{run}, which does not exist: fix the link or commit "
        "the run verbatim"
        for run in sorted(linked - set(run_dirs))
    ]
    return problems


def test_every_committed_run_is_linked_and_every_run_link_exists() -> None:
    run_dirs = sorted(p.name for p in (REPO_ROOT / "evals" / "results").iterdir() if p.is_dir())
    assert run_dirs, "evals/results/ has no run directories: commit the runs README.md cites"
    problems = results_drift(_read("README.md"), run_dirs)
    assert problems == [], "\n".join(problems)


def test_results_drift_accepts_linked_runs_and_ignores_the_bare_directory() -> None:
    readme = "[all](evals/results/) [one](evals/results/run-a/summary.md)"
    assert results_drift(readme, ["run-a"]) == []


@pytest.mark.parametrize(
    ("run_dirs", "expected"),
    [
        pytest.param(["run-a", "run-b"], "evals/results/run-b is committed", id="unlinked"),
        pytest.param([], "evals/results/run-a, which does not exist", id="dead-link"),
    ],
)
def test_results_drift_catches_an_unlinked_or_missing_run(
    run_dirs: list[str], expected: str
) -> None:
    problems = results_drift("[one](evals/results/run-a/summary.md)", run_dirs)
    assert len(problems) == 1
    assert expected in problems[0]
