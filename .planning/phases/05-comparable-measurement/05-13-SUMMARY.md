---
phase: 05-comparable-measurement
plan: 13
subsystem: docs
tags: [d-48, tag-freeze, readme, docs-check, d-12, d-47]

requires:
  - phase: 05-comparable-measurement
    provides: the live dev run and its replay (05-11), the tag list decision freeze-as-is (05-12), the offline full-dataset proof (05-10)
provides:
  - D-48, the freeze record of failure-tags-1 with grader-003
  - the docs-check range D-18 to D-48 in the justfile, README.md and AGENTS.md
  - the README narrative for Phase 5 (what works, the dev failure analysis with the wrap hazards, spend, not measured yet)
affects: [phase-06, verification]

key-files:
  created: []
  modified:
    - docs/DECISIONS.md
    - justfile
    - README.md
    - AGENTS.md

key-decisions:
  - "D-48 freezes TAGS_VERSION failure-tags-1 and GRADER_VERSION grader-003 unchanged: the live dev run changed nothing, no live model run of a test case exists, any later change is a grader version bump"

requirements-completed: [EVAL-03, EVAL-04, EVAL-05, EVAL-06, EVAL-07, EVAL-08]

status: complete
actuals:
  tokens: 9500    # chars/4 over the files changed in this plan (README, DECISIONS, AGENTS, justfile diffs)
  tasks: 2
  commits: 2      # MEASURED: git rev-list --count f011a7c..HEAD before the closing docs commit
plan_head_before: f011a7ce81b53ce7be995b7fa64efd1dc60ff624
plan_head_after: 64b1ce61ef29402502ba43a455fe9bf6a006edc4

duration: 30min
completed: 2026-11-06
---

# Phase 5 Plan 13: freeze the tag list and bring the README up to the phase

**One-liner:** D-48 records failure-tags-1 and grader-003 as frozen before any live `test` run (verified: `no test-split model answer 100`), the docs gate reads D-18 to D-48 in all four places, and the README states what Phase 5 measured on 51 of 54 dev cases, the wrap-hazard measurement and what is still not measured.

## Accomplishments

- **Task 1 (tracer).** The no-holdout check printed `no test-split model answer 100` before anything was written. D-48 was then added above `## Open questions` with the ordered 19-rule list, `failure-tags-1`, `grader-003`, the dev run changing nothing (human reply `freeze-as-is`, 05-12), the D-43 definition of a test run, the seeded and scripted proofs that graded test cases with no model output, the grader-version-bump rule, the pinned-order tests, and the open limit. D-43 carries `**Refined by:** D-48 (2026-11-06)`. The range moved to D-48 in the justfile comment and loop, README.md and AGENTS.md.
- **Task 2.** README.md gained `### Comparable measurement (Phase 5)`, the failure analysis and a wrap-hazard paragraph in `### Phase 5 live dev run`, an updated `### Spend` and `### Not measured yet`, a line on `carimbo-evals compare` and `grade --out` under Commands, and an intro that names Phase 5 as in progress. AGENTS.md has the new evals layout line and the all-154-cases note on `just e2e`.

## Verification

- `devenv shell -- just docs-check`: `docs ok`. `pytest python/tests/test_readme_drift.py python/tests/test_tags.py -q`: 79 passed.
- Final `devenv shell -- just check`: exit 0, first run, on the tree with both commits. dotnet: 2173 succeeded, 0 failed, 0 skipped; python: 3231 passed, 16 deselected, 1 warning (the existing dataset size-budget note); e2e: 16 passed, 3231 deselected; docs ok; secrets ok. The known flaky `AgentToolboxFaultTests` timeout test did not fail, so no rerun was needed.
- Acceptance (Task 1): `## D-48 ` is above `## Open questions`; D-48 names failure-tags-1 and grader-003 as in code; it contains `live model run` and `no model output`; the justfile loop ends `D-47 D-48; do`; README.md, AGENTS.md and the justfile comment each contain `D-18 to D-48`.
- Acceptance (Task 2): README has `### Comparable measurement (Phase 5)`; the dev-run section contains `wrap` and `D-47`; "No extraction run on the 154-case dataset yet" and "no failure taxonomy (Phase 5)" are gone; the Phase 5 Status cell is `In progress`; `just check` exits 0.

## Sources of the new numbers

| Figure in README / D-48 | Source |
|---|---|
| 827 wrong values; 4 in success cases; tag counts (265, 172, 94, 79, 70, 62); five zero-count tags; 12 of 30 text correct, image 0 of 21; silent errors 4 of 16; payment 0, other 4 | `evals/results/dev-split-20261106T001800Z/summary.json` and `summary.md` (re-read by script this session) |
| 67 text-layer vs 760 image wrong values; 30 text cases and 12 hazard / 5 correct, 18 non-hazard / 7 correct (script over `cases`); 7 of 18 not-correct text cases with a hazard | same `summary.json` (`render_hazards`, `cases[].wrong`, `cases[].render_hazards`), recomputed by script |
| alphanumeric CNPJ 2 of 10 vs 10 of 20 | same `summary.json`, `cases[].case_tags`, recomputed by script |
| 40 of 67 text wrong values are `invented_row` on nfe-0038; 3 SINTETICA names and PA for PAR; recipient.name wrong on 21 of 21 image cases, 15 caption or notice (10 / 2 / 3), 6 misread; 265 untagged = 243 image + 22 text; 1 of 18 wrong units on a PAR row, 14 of 15 PAR rows correct; confusable 3 of 4 correct with 2 wrong values; twins 8, 8 and 5 | `05-12-SUMMARY.md` (Findings for plan 05-13 and sections 2 to 4), which rechecked them against the same `summary.json` |
| spend US$2.30699480, cap US$2.50, replay 0.00000000 | `run.json` of both run directories (read by script) |
| 51 of 54 dev cases, nfe-0144 to nfe-0146 not run | `05-11-SUMMARY.md`, `run.json` `stopped_reason` |
| 154-case offline proof, 16 e2e tests | `05-10-SUMMARY.md`, `just e2e` in the final `just check` |
| docs/D-nn behaviour descriptions (matching, Wilson, bootstrap 10,000, McNemar, exit codes, 19 tags) | `docs/DECISIONS.md` D-42 to D-47 and `python/src/carimbo_evals/tags.py` |

## Deviations from Plan

Differences from the plan's wording, taken from the orchestrator's notes (all follow the live facts, none is a code change):

1. **[Rule 2 - accuracy] 51 of 54 dev cases, not 54.** D-48's open limit and `### Not measured yet` say the rules rest on seeded corruptions and the 51 of 54 dev cases that ran, on one model, because the cost cap stopped the live run. No bare "54 dev cases" claims they all ran.
2. **Freeze date.** `**Refined by:** D-48 (2026-11-06)`, the actual date, not 2026-11-05.
3. **Dev text row.** The three SINTETICA cases are described as staying wrong and as each being a case whose only wrong value is the name; no alternative score is stated.
4. **Status row.** `Not started` was false, so the Phase 5 cell reads `In progress` (the ROADMAP's wording); `test_readme_drift.py` accepts it, and there is no Phase 5 verification bullet.
5. **Wrap hazards answered with measurements** (12 of 30 text-layer dev cases, 5 of 12 vs 7 of 18, 1 of 18 wrong units on a PAR row, confusable 3 of 4): they do not dominate the dev failures, and the README says so while keeping the D-47 statement that the confusable slice does not measure row matching alone.

**Total deviations:** 5 documentation-level adjustments to the plan's wording, no code or test change.

## Known Stubs

None. No code was created or modified; docs only.

## Threat Flags

None. T-05-28 held: every dev figure is labelled dev, no `test` result exists, and D-48 records the freeze after the no-holdout check. T-05-29 held: the wrap hazards are named with measured shares.

## Issues Encountered

None. The first `just check` after the README and AGENTS.md edits exited 0.

## Self-Check: PASSED

- `docs/DECISIONS.md`, `README.md`, `AGENTS.md`, `justfile` exist and contain the strings checked above.
- Commits 546888a (Task 1) and 64b1ce6 (Task 2) are ancestors of HEAD.
- `requirements.ready-ids` returned EVAL-03 to EVAL-08 all ready (this is the last plan of the phase).
