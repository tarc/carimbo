---
phase: 05-comparable-measurement
plan: 01
subsystem: evals
tags: [scipy, supply-chain, lock, linear_sum_assignment]

requires:
  - phase: 05-comparable-measurement
    provides: D-42 (row matching by linear_sum_assignment), D-08 (scipy choice and the blocking review)
provides:
  - scipy==1.18.1 exact runtime pin in python/pyproject.toml
  - python/uv.lock with scipy as the only new package (PyPI registry only)
  - scipy.optimize.linear_sum_assignment installed from the reviewed lock
affects: [05-03, 05-04, 05-05, 05-06]

actuals:
  tokens: 4000
  tasks: 3
  commits: 1
plan_head_before: 305386db88dbb57d000c53e52bc69a65b3d7bc4c
plan_head_after: 91fbbe3a0d28309d0bdf02f089482393a290edc2

tech-stack:
  added: [scipy==1.18.1]
  patterns:
    - "Resolve-only lock, blocking human review, then uv sync --locked (same shape as 01-01 and 04-02)"

key-files:
  created: []
  modified:
    - python/pyproject.toml
    - python/uv.lock

key-decisions:
  - "scipy pinned exactly at 1.18.1 and installed only after the blocking supply-chain review (D-08)"

requirements-completed: []
requirements-advanced: [EVAL-03]

duration: 10min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 01: scipy pinned behind a blocking supply-chain review Summary

**scipy 1.18.1 is pinned exactly, the lock adds scipy and nothing else, a human approved the package set before any install, and `uv sync --locked` plus `just py-check` (2810 passed) run green.**

## Performance

- **Duration:** about 10 min of executor time across the checkpoint
- **Tasks:** 3 (one commit; Tasks 2 and 3 produce no file change)
- **Files modified:** 2

## Accomplishments

- Task 1 (commit 91fbbe3): added `"scipy==1.18.1",` after `"numpy==2.5.3",` and resolved the lock without installing. 73 lines added to python/uv.lock, none removed; scipy is the only package new to the lock (68 packages resolved).
- Task 2 (blocking-human checkpoint, no commit): the reviewer answered "passed", taken as approval of the printed package set (scipy only). The orchestrator's independent checks shown to the reviewer: names new to the lock = scipy only; 73 lines added and 0 removed; 61 of 61 scipy hashes in the lock match the 61 files on PyPI's JSON API; all 61 URLs on files.pythonhosted.org; cp312 wheel hash `f55fa87b...c73218` present; `requires_python >=3.12`; release 2026-08-21; not yanked. The provenance attestation and the weekly download count were NOT re-checked (they come from the plan's research).
- Task 3 (no commit, no file change): `uv lock --project python --check` still passed, `uv sync --project python --locked` installed `scipy==1.18.1` (and rebuilt the local `carimbo` package), the import and one assignment proof printed `scipy ok`, and `just py-check` exited 0 with 2810 passed, 13 deselected. python/uv.lock was not edited after the review; the working tree is clean.

## Task Commits

1. **Task 1: pin scipy and resolve the lock without installing** - `91fbbe3` (chore)
2. **Task 2: supply-chain review** - human checkpoint, no commit
3. **Task 3: install the approved lock and prove the suite** - verification only, no commit

## Files Created/Modified

- `python/pyproject.toml` - the exact `scipy==1.18.1` pin
- `python/uv.lock` - the reviewed lock with scipy

## Decisions Made

None beyond D-08 as planned.

## Deviations from Plan

None - plan executed exactly as written. The Task 2 resume signal was the word "passed" rather than the literal "approved"; the orchestrator relayed it as the reviewer's approval of the printed package set, and it is recorded here as given.

## Issues Encountered

None. `just py-check` printed one pre-existing warning (data/dataset is 19,562,414 bytes, above the 18,000,000-byte warning line, hard limit 20,000,000, CONTEXT D-12); it is unrelated to this plan and not a failure.

## Known Stubs

None.

## Threat Flags

None. T-05-SC and T-05-01 are mitigated as planned: resolve-only lock, blocking human review, `--locked` install.

## Next Phase Readiness

`scipy.optimize.linear_sum_assignment` is importable at 1.18.1, ready for the line-item matcher in plan 05-03.

## Self-Check: PASSED

- python/pyproject.toml and python/uv.lock modified: FOUND
- commit 91fbbe3 is an ancestor of HEAD: FOUND
