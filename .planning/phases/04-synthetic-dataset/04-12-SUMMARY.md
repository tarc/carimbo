---
phase: 04-synthetic-dataset
plan: 12
subsystem: docs
tags: [decisions, docs-check, readme, dataset, uat-gap, d-41]
gap_closure: true
gap_ids: [G-04-1, G-04-2]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-10 (twin bytes independent of SOURCE_DATE_EPOCH) and plan 04-11 (blur severe 1.8, 19,562,414 bytes)
provides:
  - decision record D-41 (refines D-40) with both UAT fixes, the user's own transcription and what it supersedes in D-40
  - a pointer line under D-40 (no D-40 line deleted or rewritten)
  - docs-check, AGENTS.md and README.md docs-check rows over D-18 to D-41
  - README Phase 4 dataset paragraph with the measured size and D-41
  - `devenv shell -- env -u SOURCE_DATE_EPOCH just check` green (exit 0)
affects: [UAT re-test 1 (nfe-0095 legibility), UAT re-test 3 (CI run on the pushed branch), phase 4 verification, /gsd-ship]

actuals:
  tokens: 4152   # chars/4 over `git diff plan_head_before..plan_head_after` (docs only)
  tasks: 2
  commits: 2

plan_head_before: 99f5a02da2156c80ea09465561639e44b9b0d862
plan_head_after: d899511a90ce98ceb04f64bd9f1022fcee4c9c5b
commits: 2

tech-stack:
  added: []
  patterns:
    - "A decision refined after UAT is a new entry plus a one-line 'Superseded in part by' pointer, so the earlier record keeps its text"

key-files:
  created: []
  modified:
    - docs/DECISIONS.md
    - justfile
    - AGENTS.md
    - README.md

key-decisions:
  - "D-41 records blur severe 1.8 (user's choice on native-resolution previews), the PDF_EPOCH pin and how environment independence is tested; it supersedes only the D-40 statements the plan named"
  - "D-41 states the user's choice as resting on the nfe-0095 item table alone: the optional nfe-0127 and access-key cross-check was not performed, and the plan wording that the user chose 'on previews of nfe-0095 and nfe-0127' was corrected to the measured fact"
  - "The local CI-like run is described as local evidence; the GitHub runner is UAT re-test 3"

requirements-completed: [DATA-05, DATA-06]

coverage:
  - id: D1
    description: "D-41 exists, records both UAT fixes from measured facts, and D-40 gained only the pointer line (24 added, 0 deleted lines in docs/DECISIONS.md)"
    requirement: DATA-06
    verification:
      - kind: other
        ref: "git diff --numstat 99f5a02 -- docs/DECISIONS.md (24 0)"
        status: pass
      - kind: other
        ref: "devenv shell -- just docs-check (docs ok)"
        status: pass
    human_judgment: false
  - id: D2
    description: "docs-check, the justfile comment, the AGENTS.md row and the README row all say D-18 to D-41 and the drift test accepts them"
    requirement: DATA-06
    verification:
      - kind: unit
        ref: "python/tests/test_readme_drift.py and python/tests/test_repo_layout.py (42 passed)"
        status: pass
    human_judgment: false
  - id: D3
    description: "README Phase 4 dataset paragraph states 19,562,414 bytes and cites D-40 and D-41; the size equals `du -sb data/dataset`"
    requirement: DATA-05
    verification:
      - kind: other
        ref: "size assertion of Task 2 verify (19,562,414 found in README.md and docs/DECISIONS.md)"
        status: pass
    human_judgment: false
  - id: D4
    description: "Every offline gate passes with SOURCE_DATE_EPOCH unset"
    requirement: DATA-06
    verification:
      - kind: integration
        ref: "devenv shell -- env -u SOURCE_DATE_EPOCH just check (exit 0, 193 s)"
        status: pass
    human_judgment: true
    rationale: "The GitHub runner is the environment under test; only UAT re-test 3, after a push the user approves, proves byte identity and the dataset-check time in CI."
  - id: D5
    description: "nfe-0095 at blur severe 1.8 is readable with effort at native 220 DPI (UAT re-test 1)"
    requirement: DATA-03
    verification: []
    human_judgment: true
    rationale: "Legibility is a human reading judgment (CONTEXT D-08); handed to UAT re-test 1."

duration: 8 min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 12: decision record D-41, the dataset docs and the CI-like gate (gap closure G-04-1, G-04-2) Summary

**D-41 refines D-40 with blur severe sigma 1.8 (the user's own reading of the nfe-0095 item table) and the PDF_EPOCH pin that makes twin bytes independent of SOURCE_DATE_EPOCH, with docs-check and the README reaching D-41 and `env -u SOURCE_DATE_EPOCH just check` green in 193 s.**

## Performance

- **Duration:** 8 min (execution of this plan, including a 193 s full gate)
- **Started:** 2026-10-29T19:25:23Z
- **Completed:** 2026-10-29T19:33Z
- **Tasks:** 2
- **Files modified:** 4 (docs/DECISIONS.md, justfile, AGENTS.md, README.md); nothing under data/ or evals/

## Accomplishments

- D-41 (24 added lines, 0 deleted in docs/DECISIONS.md) records, from measured facts: sigma 1.8 replacing 2.4 and why; the five candidates (1.2, 1.6, 1.8, 2.0, 2.4); the user's transcription per sigma, quoted from the 04-11 SUMMARY; that only the eight blur-severe twins and the manifest changed; 19,562,414 bytes and 437,586 bytes of headroom; `barcode_readable` false for all eight and 44 `barcode_unreadable` cases; the PDF_EPOCH pin (315532800, D:19800101000000) and the root cause (CI run 40915848246); how environment independence is tested; the dataset-001 label under the D-17 qualification, now naming commit e84a9a9; which D-40 statements it supersedes; Rationale and Rejected bullets.
- D-40 changed by one line only: `**Superseded in part by:** D-41 (2026-10-29)` between its heading and its `**Phase:** 4` line.
- `just docs-check` prints `docs ok` over D-18 to D-41; the justfile comment and loop, the AGENTS.md row and the README row agree, and `test_readme_drift.py` plus `test_repo_layout.py` pass (42 passed).
- README Phase 4 paragraph: cites D-40 and D-41, states 19,562,414 bytes, the severe blur 1.8, and that byte-identical regeneration does not depend on SOURCE_DATE_EPOCH; "about 50 seconds" kept because the measured `dataset-check` took 49 s wall (including devenv shell start).

## Task Commits

1. **Task 1 (tracer): D-41, D-40 pointer, docs-check range** - `5dd5a3d` (docs)
2. **Task 2: README dataset paragraph and the CI-like gate** - `d899511` (docs)

**Plan metadata:** the docs(04-12) commit that carries this SUMMARY.

The tracer's `<verify>` is automated only and passed (`just docs-check`: `docs ok`; drift and layout tests: 42 passed; numstat of docs/DECISIONS.md `24 0`), so expansion to Task 2 proceeded without a checkpoint.

## Measured results

- Baseline ledger and `plan_head_before`: `99f5a02da2156c80ea09465561639e44b9b0d862` (printed by Task 1's baseline step; equals the value above).
- `du -sb data/dataset`: 19,562,414 bytes; manifest `generator.scan.degradation.blur.severe` 1.8; 309 files.
- `barcode_readable` of nfe-0095, 0103, 0111, 0119, 0127, 0135, 0143, 0151: all false; cases with the `barcode_unreadable` tag: 44; cases with `barcode_readable` false: 44.
- `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check`: `ok: data/dataset matches a regeneration from seed 20261022`, 49 s wall.
- `devenv shell -- env -u SOURCE_DATE_EPOCH just check`: **exit 0, 193 s wall**. Contents: schema regeneration with no diff, ruff clean, pyright 0 errors, py-check `2810 passed, 13 deselected, 1 warning` (the warning is the existing size line: 19,562,414 bytes above the 18,000,000 warning line, hard limit 20,000,000), `datagen-check` ok, `dataset-check` ok, DecisionCases `--check` up to date, .NET tests 2173 succeeded, e2e `13 passed, 2810 deselected in 40.33s`, `docs ok`, `secrets ok`.
- `git diff --name-only 99f5a02 HEAD -- data evals` is empty and `git status --porcelain -- data evals` is empty.
- This is a local CI-like run: it stands in for the GitHub runner but does not replace UAT re-test 3.

## Files Created/Modified

- `docs/DECISIONS.md` - D-41 entry before "Open questions"; pointer line under the D-40 heading.
- `justfile` - docs-check loop gains D-41; comment above it says D-18 to D-41.
- `AGENTS.md` - docs-check row says D-18 to D-41.
- `README.md` - docs-check row says D-18 to D-41; Phase 4 dataset paragraph updated (D-40 and D-41, measured size, blur severe lowered, PDF date pinned).

## Decisions Made

- D-41 itself (see key-decisions). README Status row and the "- Phase 4:" verification bullet are deliberately not written: AGENTS.md allows them only after verification marks the phase Complete.

## Deviations from Plan

None in code or scope. One wording correction made on purpose: the plan text says the user chose the value "on native-resolution previews of nfe-0095 and nfe-0127". The 04-11 SUMMARY records that the optional nfe-0127 and access-key cross-check was not performed and that the user chose 1.8 directly from the nfe-0095 item table. D-41 states exactly that (previews of nfe-0095, choice resting on nfe-0095 alone) rather than the plan's wording, so the record does not claim a check that did not happen.

**Total deviations:** 0 auto-fixed.

## Issues Encountered

- The first `dataset-check` timing printed an empty duration (`bc` is not installed); redone with `date +%s` arithmetic (49 s). No repository effect.

## Known Stubs

None.

## Threat Flags

None. Docs-only change; no new network, auth or file-access surface. T-04-30 (D-41 new, D-40 only a pointer, numstat 0 deleted lines), T-04-31 (`secrets ok` inside `just check`) and T-04-32 (the local run is stated as local) are mitigated as planned; no package was added (`uv sync --locked` inside py-check passed).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- This is the last plan of Phase 4. Handed to the verifier and the user, not run here: UAT re-test 1 (read the committed nfe-0095 at native 220 DPI cold; blur severe still clearly harder than blur mild nfe-0094; the six other twins of nfe-0001 byte-identical to the ones judged fine) and UAT re-test 3 (after a push the user approves: dispatch ci.yml by workflow_dispatch, read each job's conclusion from the check runs, `dataset-check` at most about 180 s; above that CONTEXT D-13 moves it to a path-filtered workflow).
- Nothing was pushed, no pull request opened, no CI dispatched, no model called (US$0).
- README Status row and the `- Phase 4:` verification bullet are still to be written after verification marks Phase 4 Complete, before `/gsd-ship`.

## Self-Check: PASSED

- docs/DECISIONS.md, justfile, AGENTS.md, README.md and this SUMMARY exist.
- Commits `5dd5a3d` and `d899511` are ancestors of HEAD; `git rev-list --count 99f5a02..HEAD` was 2 before this SUMMARY.
- Acceptance greps: `^## D-41 ` 1, `Superseded in part by:.* D-41` 1, `SOURCE_DATE_EPOCH` in DECISIONS 4 and in README 1, `PDF_EPOCH` 2, `D-40 D-41; do` 1, `D-18 to D-41` once each in justfile, AGENTS.md, README.md; README `D-41` count 2.
- The plan_head_before above equals the hash the baseline step printed.

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*
