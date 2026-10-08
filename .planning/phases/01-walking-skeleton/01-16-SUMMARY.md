---
phase: 01-walking-skeleton
plan: 16
subsystem: testing
tags: [e2e, scripted-host, pytest, schema-invalid, gap-closure, review-ledger]

requires:
  - phase: 01-walking-skeleton
    provides: "01-14 typed outcomes for unrepresentable amounts and pattern violations (.NET); 01-15 cost cap, grader newline split and corrupt-resume hardening (Python)"
provides:
  - "TestTypedEdgeOutcomesOverHttp: both gap truths (CR-01, WR-03) proven across the HTTP boundary through carimbo-evals run and grade against the real ASP.NET composition"
  - "01-REVIEW-DISPOSITION.md with CR-01, WR-01, WR-02, WR-03 and WR-05 fixed, each citing its plan and resolving commit (open 8 of 13)"
  - "01-VALIDATION.md rows 01-14-T1 to 01-16-T2"
affects: [verify-work re-verification of phase 01, phase-02-money-spec]

actuals:
  tokens: 2600
  tasks: 2
  commits: 2

plan_head_before: 0c8a2789591a65c950cf4696a1bfd4be455ff86f
plan_head_after: 9438125187be1a0dcf74d9529535c285c0b64bb4

tech-stack:
  added: []
  patterns:
    - "Class-level pytest fixture override (responses_dir) feeds the module-level host fixture, so one ScriptedHost startup path serves several scripted scenarios"
    - "A regression test is validated against the pre-fix sources by temporarily restoring them, then restoring the working tree file by file"

key-files:
  created: []
  modified:
    - python/tests/test_e2e_fake.py
    - .planning/phases/01-walking-skeleton/01-REVIEW-DISPOSITION.md
    - .planning/phases/01-walking-skeleton/01-VALIDATION.md

key-decisions:
  - "The class-level responses_dir override reaches the module host fixture, so no host refactor was needed"
  - "The ledger Source cell cites the fix commit(s) only (not the RED test commits), space separated after the plan id, so the gate regex and the git cat-file check both read it"
  - "The oversized-amount Python schema verdict is deliberately not asserted: the money pattern has no length bound"

patterns-established:
  - "Ledger rows cite '<plan> <sha> [<sha>]' with every sha resolving via git cat-file"

requirements-completed: [EXT-01, EXT-02, EVAL-01, EVAL-02]

coverage:
  - id: D1
    description: "A scripted answer with a 32-digit total_amount and one with a space-grouped access key each end as a completed record (HTTP 200) with outcome schema_invalid, raw output kept and a priced cost_usd; zero harness errors, runner exit 0"
    requirement: EXT-02
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#TestTypedEdgeOutcomesOverHttp::test_unrepresentable_amount_and_pattern_violation_are_schema_invalid_end_to_end"
        status: pass
    human_judgment: false
  - id: D2
    description: "run.json spent_usd equals the sum of the three record costs and assumed_usd is 0, so every paid call is inside the cost cap's view"
    requirement: EVAL-01
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#TestTypedEdgeOutcomesOverHttp::test_unrepresentable_amount_and_pattern_violation_are_schema_invalid_end_to_end"
        status: pass
    human_judgment: false
  - id: D3
    description: "The grader counts success 1, schema_invalid 2, harness_error 0, marks both schema_invalid cases wrong on every field, and the Python jsonschema and Pydantic verdicts agree with .NET for the pattern violation"
    requirement: EVAL-02
    verification:
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py#TestTypedEdgeOutcomesOverHttp::test_unrepresentable_amount_and_pattern_violation_are_schema_invalid_end_to_end"
        status: pass
    human_judgment: false
  - id: D4
    description: "The full offline gate passes with plans 01-14 and 01-15 applied"
    requirement: EXT-01
    verification:
      - kind: other
        ref: "devenv shell -- just check (exit 0)"
        status: pass
    human_judgment: false
  - id: D5
    description: "01-REVIEW-DISPOSITION.md records the five closed findings as fixed with commits that resolve, open count 8; WR-04 and IN-01..IN-07 stay open"
    verification:
      - kind: other
        ref: "grep -cE on the ledger table prints 5; git cat-file -e on each cited sha; open: 8"
        status: pass
    human_judgment: false

duration: 3min
completed: 2026-10-08
status: complete
---

# Phase 1 Plan 16: both gap truths proven across the stacks, finding ledger closed Summary

**An oversized total_amount and a space-grouped access key now provably end as typed schema_invalid records (HTTP 200, priced, zero harness errors) through `carimbo-evals run` and `grade` against the real ASP.NET composition, `just check` is green, and CR-01, WR-01, WR-02, WR-03 and WR-05 are recorded as fixed with resolving commits.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-10-08T03:25:04Z
- **Completed:** 2026-10-08T03:28:30Z
- **Tasks:** 2
- **Files modified:** 3 (none created)

## Accomplishments

- `TestTypedEdgeOutcomesOverHttp` runs the documented commands against the ScriptedHost with replies keyed by each committed PDF's SHA-256: case-001 answers with a 32-nines `total_amount`, case-002 with the access key in eleven space-separated groups of four, case-003 correctly. All three records are `completed` with HTTP 200; cases 001 and 002 are `schema_invalid` with `failure.kind` `schema_invalid`, the raw output equal to the scripted text, and messages containing "fits in a decimal" and "access_key"; every `cost_usd` is a decimal greater than 0. `run.json` shows `harness_errors` 0, `completed` 3, `stopped_reason` null, `assumed_usd` 0, and `Decimal(spent_usd)` equals the sum of the three `Decimal(cost_usd)`.
- The grader summary counts success 1, schema_invalid 2, harness_error 0, total 3; cases 001 and 002 are wrong on every field, case-003 right on all nine; `field_accuracy.access_key` is `{correct: 1, n: 3}`; for case-002 both `schema_valid_jsonschema` and `schema_valid_pydantic` are false, agreeing with .NET. No file under the run directory contains the ephemeral eval key.
- The test has teeth: with the pre-01-14 `Wire.cs`, `Invoice.cs` and `InvoiceExtractor.cs` temporarily restored, it fails at `run.returncode == 0` (the runner reported `case-001 harness_error` and exited 4). The sources were then restored with `git checkout --` on those three files and the tree was clean.
- `devenv shell -- just check` exited 0 before the ledger edit (dotnet-check, py-check 144 passed, schema-check, datagen-check, e2e 4 passed, docs-check, secrets-check).
- The ledger now shows five findings fixed and eight open; the validation map covers the eight tasks of plans 01-14 to 01-16.

## Task Commits

1. **Task 1 (tracer): both gap truths through the documented commands** - `622583b` (test)
2. **Task 2: full offline gate, ledger and validation map** - `9438125` (docs)

**Plan metadata:** recorded in the `docs(01-16)` commit that adds this SUMMARY (hash in `git log`).

Ledger citations (every sha verified with `git show --stat` and `git cat-file -e`):

| Finding | Plan | Fix commit(s) | Test commit(s) |
|---------|------|---------------|----------------|
| CR-01 | 01-14 | `cc0f7a2`, `bc6a4fb` | `d8ae54b`, `609ef11` |
| WR-03 | 01-14 | `7acaac7` | `cc9db28` |
| WR-02 | 01-15 | `5d7990d` | `fe647a3` |
| WR-01 | 01-15 | `4021173` | `427c850` |
| WR-05 | 01-15 | `09d2cf8` | `70f91d1` |

## Files Created/Modified

- `python/tests/test_e2e_fake.py` - `OVERSIZED_AMOUNT` constant and `TestTypedEdgeOutcomesOverHttp` (class-level `responses_dir` fixture plus the cross-stack test); the original e2e test is untouched
- `.planning/phases/01-walking-skeleton/01-REVIEW-DISPOSITION.md` - five findings `fixed`, `open: 8`, Source cells cite plan and commit
- `.planning/phases/01-walking-skeleton/01-VALIDATION.md` - eight rows appended to the Per-Task Verification Map; frontmatter and sign-off unchanged

## Decisions Made

- The class-level `responses_dir` fixture overrides the module one for the module `host` fixture (pytest resolves fixture dependencies from the requesting test's node), so the fallback refactor to a shared context-manager helper was not needed.
- Source cells cite the fixing commit(s), not the RED test commits; those are listed in this SUMMARY and in the 01-14 and 01-15 SUMMARYs.
- The text of the cited-sha verify (`awk '{print $4}'`) reads only the first sha of a row, so CR-01's second fix sha `bc6a4fb` was verified separately with `git show --stat`.

## Deviations from Plan

None - plan executed exactly as written.

## TDD Gate Compliance

- **No RED commit for the tracer, by design.** The production fixes (01-14, 01-15) were already committed, so a new regression test over them passes on first run; a `test(01-16)` commit that fails cannot precede an implementation that already exists. `622583b` is the single `test(01-16)` commit and there is no `feat(01-16)`, which is correct for a gap-closure proof plan.
- **RED evidence, obtained by mutation instead:** with the three pre-fix source files from `053b2f0` restored, the new test failed with `assert run.returncode == 0` (`case-001 harness_error`, exit 4), the CR-01 symptom. Semantic assessment: the target test executed and failed on the planned behaviour for the intended reason (the oversized amount escaping as a harness error). `gsd_run check tdd-red-evidence` was not used; `workflow.tdd_mode` is off and the pytest console output is not one of the classifier's supported formats. The mutation was reverted before the commit.

## Issues Encountered

None.

## Residual observation (not a closed gap)

The JSON Schema money pattern `^-?[0-9]+\.[0-9]{2}$` has no length bound. For case-001 the Python grader's `jsonschema` and Pydantic checks therefore still call the 32-digit amount schema-valid (`schema_valid_jsonschema` and `schema_valid_pydantic` are true) while .NET reports `schema_invalid` ("does not fit in a decimal"). The test deliberately does not assert the case-001 schema grades. Bounding the digit count is a wire-contract change, deferred to Phase 2 (DOM-04) by plan 01-14.

## Known Stubs

None.

## Threat Flags

None. T-01-47 (ephemeral key absent from the run directory) is asserted by the new test; T-01-48 (ledger cites resolving commits) is verified with `git cat-file -e`; T-01-SC holds, no package, lockfile or project file was touched.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 01 gap closure is complete (16 of 16 plans); ready for `/gsd-verify-work` re-verification of the CR-01 and WR-03 truths.
- Still open by scope: WR-04 and IN-01..IN-07. Human items CI-01 (first PR run), REPO-03 (fresh clone) and REPO-04 (agent discovery) remain human verification, untouched here.

## Self-Check: PASSED

- `python/tests/test_e2e_fake.py`, `01-REVIEW-DISPOSITION.md`, `01-VALIDATION.md` exist and are modified as described.
- Commits `622583b` and `9438125` are ancestors of HEAD; `git rev-list --count 0c8a278..HEAD` gave 2 at write time.
- Task 1 acceptance greps print 1, 1, 1, 1; the verify command reports 1 passed. Task 2 greps print `open: 8` 1, `disposition: fixed` 5, open rows 8, validation rows 8, `status: validated` 1, ledger regex count 5, and every cited sha resolves.

---
*Phase: 01-walking-skeleton*
*Completed: 2026-10-08*
