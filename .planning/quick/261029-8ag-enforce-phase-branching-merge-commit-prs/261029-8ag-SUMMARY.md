---
phase: quick-261029-8ag
plan: 01
subsystem: repo-process
tags: [branching, readme-drift, pytest, agents-md, gsd-config]
requires: []
provides:
  - "python/tests/test_readme_drift.py: offline README drift checks (a) to (e) inside just py-check"
  - "AGENTS.md section Branching and pull requests"
  - ".planning/config.json: phase branching and quick branch template"
affects: [just py-check, just check, ci.yml, every later phase's ship step]
tech-stack:
  added: []
  patterns: ["pure check functions returning problem lists, with real-tree and negative tests"]
key-files:
  created: [python/tests/test_readme_drift.py]
  modified: [README.md, AGENTS.md, .planning/config.json]
decisions:
  - "Docs state the PR-only rule as a project convention and do not claim a GitHub ruleset (cannot be confirmed offline)"
  - "Non-Complete ROADMAP rows do not require README equality; only a premature Complete is flagged"
metrics:
  duration: "about 25 min"
  completed: 2026-10-29
status: complete
actuals:
  tokens: 14000
  tasks: 3
  commits: 3
plan_head_before: 13c6328a85cb0282b8ae1ae8696f1ed56557b405
plan_head_after: 16c23ee0a51b931b59caa36d5a2bdbc2a5c49770
---

# Phase quick-261029-8ag Plan 01: Enforce phase branching, merge-commit PRs and README upkeep Summary

GSD now branches per phase and per quick task, AGENTS.md states the merge-commit PR and README upkeep rules, and a new offline pytest module makes `just py-check` (so `just check` and CI) fail when the README's mechanical parts drift.

## What was built

- `python/tests/test_readme_drift.py`: pure parsers (`table_rows`, `readme_status`, `roadmap_progress`) and check functions `status_drift` (a), `verification_drift` (b), `docs_range_drift` (c), `results_drift` (d), `branching_drift` (e), each with a real-tree test and negative `catches` tests (16 `catches` cases). `table_rows` raises on a missing header, a zero-row table, and an unnumbered row, so nothing passes vacuously.
- `.planning/config.json`: `git.branching_strategy` is `phase`; `git.quick_branch_template` is `gsd/quick-{num}-{slug}`. Nothing else changed (2 lines).
- `AGENTS.md`: new `## Branching and pull requests` section (8 bullets) between `What not to use` and `Planning`.
- `README.md`: the Phase 1 verification bullet links `01-VERIFICATION.md`; one pointer sentence to AGENTS.md below the Status table. Status table byte-identical to 358f8e7.

## Commits

| Task | Commit | Content |
|------|--------|---------|
| 1 (tracer) | c1f5b1e | Drift test skeleton with the Status-table check |
| 2 | b8103e2 | Checks (b), (c), (d) and the Phase 1 README link |
| 3 | 16c23ee | Config, AGENTS.md section, README pointer, check (e) |

## TDD record

- Task 2 RED: with checks (b) to (d) and README unedited, only `test_every_complete_phase_links_its_verification` failed, with exactly one problem naming Phase 1 and `01-walking-skeleton/01-VERIFICATION.md`; GREEN after the link. The `baseline-red-ok` gate (README at 358f8e7) printed.
- Task 3 RED: `test_agents_md_documents_the_configured_branching` failed with the strategy, quick template and missing-section problems before the edits; `branching-red-ok` printed against 358f8e7 files; GREEN after.

## Verification

All gates printed their markers: drift-caught, baseline-red-ok, phase1-ok, config-ok, config-diff-ok, branching-red-ok, agents-ok, readme-ok, scope-ok (only the four planned files changed; no em dashes). `negatives` count is 16 (needed 12). `just docs-check` prints `docs ok`; `just py-check` exits 0 (557 passed, 13 deselected; ruff, format and pyright clean); `just secrets-check` prints `secrets ok`.

## Deviations from Plan

None - plan executed as written. One non-functional note: the first Task 2 draft had a line over 100 characters (ruff E501 and format); fixed by `ruff format` before the commit.

## Known Stubs

None.

## Threat Flags

None.

## Self-Check: PASSED

- python/tests/test_readme_drift.py, AGENTS.md section, config.json change and README edits exist.
- Commits c1f5b1e, b8103e2 and 16c23ee are on branch `gsd/quick-261029-8ag-enforce-phase-branching-merge-commit-prs`.
