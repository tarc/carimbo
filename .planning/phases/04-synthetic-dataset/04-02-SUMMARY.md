---
phase: 04-synthetic-dataset
plan: 02
subsystem: infra
tags: [uv, supply-chain, numpy, reportlab, pillow, pypdfium2, zxing-cpp]

requires:
  - phase: 01-walking-skeleton
    provides: the python uv project, py-check recipe and the review-table checkpoint precedent (01-01)
provides:
  - exact runtime pins for the scan stack (pillow, numpy, reportlab, pypdfium2, zxing-cpp)
  - a human-reviewed python/uv.lock containing numpy, reportlab and charset-normalizer
  - the scan stack installed in python/.venv from the approved lock
affects: [04-03 scan renderer, 04-04 phase tracer, 04-07 committed image-only twins]

actuals:
  tokens: 4000
  tasks: 3
  commits: 1
plan_head_before: 3178e2f6491cb823229cd99f6ccebbe5adaa8788
plan_head_after: c19be9c776b5867d616426181176bd0aadc8182e

tech-stack:
  added: [numpy 2.5.3, reportlab 5.0.1, charset-normalizer 3.5.2]
  patterns: [resolve-only lock, blocking human supply-chain review, then uv sync --locked]

key-files:
  created: []
  modified: [python/pyproject.toml, python/uv.lock]

key-decisions:
  - "Scan stack pinned as exact runtime dependencies; pypdfium2 and zxing-cpp moved out of the dev group because the generator needs them at runtime"
  - "Install happened only after the human approved exactly charset-normalizer 3.5.2, numpy 2.5.3 and reportlab 5.0.1, plus the already-locked pillow 12.3.0, pypdfium2 5.13.0 and zxing-cpp 3.1.1"

requirements-completed: []  # DATA-03 is only enabled here (supply chain); it completes with plans 04-03 and 04-07

status: complete
---

# Phase 4 Plan 02: the scan stack pinned behind a blocking supply-chain review Summary

**Exact runtime pins for pillow, numpy, reportlab, pypdfium2 and zxing-cpp, resolved into the lock without installing, approved by a human at a blocking checkpoint, then installed with `uv sync --locked`.**

## Performance

- **Tasks:** 3 (1 auto, 1 blocking-human checkpoint, 1 auto)
- **Files modified:** 2 (python/pyproject.toml, python/uv.lock)

## Accomplishments

- Task 1 (c19be9c): added `pillow==12.3.0`, `numpy==2.5.3`, `reportlab==5.0.1`, `pypdfium2==5.13.0`, `zxing-cpp==3.1.1` to `[project].dependencies` and removed the last two from the dev group. `uv lock` ran without installing anything.
- Task 2 (review only, no commit): the human reviewed the lock diff and provenance table and typed "approved". The orchestrator had confirmed independently that the only names new to the lock versus 3178e2f were charset-normalizer, numpy and reportlab, that no lock source is git or URL, and that numpy and reportlab were not yet in python/.venv.
- Task 3 (no commit; the sync did not change the lock): `uv lock --check` still exited 0 after the approval, `uv sync --project python --locked` installed charset-normalizer 3.5.2, numpy 2.5.3 and reportlab 5.0.1 and rebuilt the local carimbo package, the import check printed `scan stack ok` (numpy 2.5.3, reportlab 5.0.1, PIL 12.3.0, pypdfium2 PDFium 5.13.0), and `just py-check` passed (ruff, ruff format, pyright 0 errors, 557 passed, 13 deselected).

## Review outcome (Task 2)

Approved by the human for exactly: charset-normalizer 3.5.2, numpy 2.5.3, reportlab 5.0.1, and the existing pillow 12.3.0, pypdfium2 5.13.0 and zxing-cpp 3.1.1 pins. Nothing outside that set was installed.

## Task Commits

1. **Task 1: pin the scan stack and resolve the lock without installing** - c19be9c
2. **Task 2: supply-chain review** - no commit (human checkpoint)
3. **Task 3: install the approved lock and verify** - no commit (the lock was unchanged; `git status --porcelain python/uv.lock` printed nothing)

## Deviations from Plan

None to the code. Task 3 produces no commit by design, since python/uv.lock must stay unchanged.

Bookkeeping note: the plan lists DATA-03 in its `requirements`, but this plan only pins the dependencies; the renderer (04-03) and the committed twins (04-07) deliver the requirement. `requirements.mark-complete DATA-03` was run and then reverted in REQUIREMENTS.md so the requirement is not shown as Complete early.

## Issues Encountered

None. The zxingcpp module exposes no `__version__`; the pin is verified by the lock and the sync instead.

## Known Stubs

None.

## Threat Flags

None. The change adds only pinned PyPI dependencies, covered by T-04-SC and T-04-03 in the plan's threat model.

## Next Phase Readiness

Plans 04-03 onward can import numpy, reportlab, PIL, pypdfium2 and zxingcpp at the pinned versions.

## Self-Check: PASSED

- FOUND: python/pyproject.toml, python/uv.lock
- FOUND commit c19be9c (ancestor of HEAD)
- `uv lock --check` exits 0; `just py-check` exits 0
