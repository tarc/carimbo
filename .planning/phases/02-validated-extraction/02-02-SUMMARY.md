---
phase: 02-validated-extraction
plan: 02
subsystem: testing
tags: [check-digits, cnpj, cpf, access-key, half-up-rounding, validator-vectors, decisions, danfe-mapping]

requires:
  - phase: 02-validated-extraction
    provides: "02-01 v2 Invoice contract chain and data/vectors/valid-invoice.json fixture"
provides:
  - "data/vectors/validator-vectors.json: shared hand-curated CNPJ, CPF, access-key, rounding, tolerance and sum_tolerance vectors"
  - "Python cpf_check_digits, is_valid_cpf, make_cpf, is_valid_access_key, stricter is_valid_cnpj, round_half_up"
  - "pytest over the vector file (31 cases)"
  - "docs/DECISIONS.md D-22 (Invoice v2), D-23 (validation and bounded repair), D-24 (eval contract 2)"
  - "docs/DANFE-MAPPING.md: 42 Invoice paths mapped to DANFE label, XML source and rule"
affects: [02-04 xUnit vectors, 02-05, 02-06, 02-07 mapper and mapping test]

plan_head_before: d4d7aebe4869c69ae45eb5ff413f04dd1e416e7c
plan_head_after: c702b5866e83b7b0c8af2b37e86526d2aa830b69

estimate:
  tokens: 50000
actuals:
  tokens: 8580
  tasks: 2
  commits: 3

tech-stack:
  added: []
  patterns:
    - "Cross-stack oracle: one JSON vector file read by pytest and xUnit; expected values are published examples or hand computations, never produced by the code under test"
    - "Decision records appended before Open questions, with a pointer line under each refined record"

key-files:
  created:
    - data/vectors/validator-vectors.json
    - python/tests/test_vectors.py
    - docs/DANFE-MAPPING.md
  modified:
    - python/src/carimbo_datagen/ids.py
    - python/src/carimbo_evals/money.py
    - docs/DECISIONS.md
    - justfile
    - AGENTS.md

key-decisions:
  - "D-22: totals.invoice_total replaces the Phase 1 total_amount with no alias; Recipient separate from Party; regime inferred from cst_csosn length"
  - "D-23: findings with stable rule ids, only errors trigger repair, feedback never reveals identifier or check-digit expectations, MaxRepairs default 2 (0..5, configuration only)"
  - "D-24: eval contract_version 2 with optional reference_date, attempts[] and outcome; validation_failed is HTTP 200"
  - "All-identical CNPJ and CPF values are rejected by project rule (they pass the arithmetic)"

requirements-completed: [VAL-06, VAL-02, DOM-04, DOM-02]

coverage:
  - id: D1
    description: "Shared validator vector file checked by pytest for CNPJ, CPF and access-key check digits and half-up rounding"
    requirement: VAL-06
    verification:
      - kind: unit
        ref: "python/tests/test_vectors.py (31 cases)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Python identifier helpers and round_half_up (CPF, access key, stricter CNPJ, no negative zero)"
    requirement: VAL-02
    verification:
      - kind: unit
        ref: "python/tests/test_vectors.py#test_make_cpf_only_yields_valid_cpfs"
        status: pass
      - kind: unit
        ref: "python/tests/test_synthetic_only.py (nfelib oracle still passes)"
        status: pass
    human_judgment: false
  - id: D3
    description: "D-22, D-23, D-24 recorded append-only and enforced by just docs-check"
    requirement: DOM-04
    verification:
      - kind: other
        ref: "just docs-check; git diff --numstat main -- docs/DECISIONS.md shows 0 deleted lines"
        status: pass
    human_judgment: false
  - id: D4
    description: "DANFE mapping document covering every Invoice leaf path, not-on-DANFE list and limitations"
    requirement: DOM-02
    verification:
      - kind: other
        ref: "grep: 42 backticked path rows, 17 items/installments rows, Limitations section"
        status: pass
    human_judgment: true
    rationale: "Whether each DANFE label and XML source is the right one is a content judgment; plan 02-07 adds a test that every schema path appears in the document"

duration: 7min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 02: one vector file for both stacks, and the decisions and mapping the phase builds on Summary

**One hand-curated validator-vectors.json (CNPJ, CPF, access key, half-up rounding, tolerance) drives Python's identifier math under pytest, plus D-22 to D-24 and a 42-path DANFE-to-Invoice mapping doc**

## Performance

- **Duration:** 7 min
- **Started:** 2026-10-08T14:10:58Z
- **Completed:** 2026-10-08T14:17:00Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- `data/vectors/validator-vectors.json` (vector_version 1) holds 7 CNPJ, 5 CPF, 6 access-key and 10 rounding vectors, the shared tolerance (0.01, cap 1.00) and the sum_tolerance cases xUnit will read in 02-04. Every expected value is a published example or a hand computation recorded in a note.
- Python gained `cpf_check_digits`, `is_valid_cpf`, `make_cpf`, `is_valid_access_key`, a CNPJ check that rejects 14 identical characters, and `round_half_up` (half away from zero, no negative zero). The hand-computed vectors pass against the implementation without any value adjustment, which is independent evidence that the weighted sums in the plan were right.
- D-22, D-23 and D-24 are appended before "Open questions" with pointer lines under D-02, D-03 and D-06 (142 lines added, 0 deleted against main); `just docs-check` now requires D-18 to D-24.
- `docs/DANFE-MAPPING.md` has one row per Invoice leaf path (5 header, 4 issuer, 5 recipient, 14 item, 11 totals, 3 installment) plus the "Not on the DANFE" and "Limitations" sections.

## Task Commits

1. **Task 1 (tracer, TDD): vector file drives identifier check digits and rounding**
   - RED: `26d1d3b` (test) vector file and pytest, failing on the missing helpers
   - GREEN: `283aeb1` (feat) ids.py and money.py helpers
2. **Task 2: D-22..D-24, DANFE mapping, docs-check extended** - `c702b58` (docs)

**Plan metadata:** committed after this summary (docs: complete plan)

## Files Created/Modified
- `data/vectors/validator-vectors.json` - shared cross-stack oracle
- `python/tests/test_vectors.py` - 31 parametrized cases over the file; checks DEFAULT_TOLERANCE equals the file tolerance
- `python/src/carimbo_datagen/ids.py` - CPF, access-key validation, stricter CNPJ
- `python/src/carimbo_evals/money.py` - `round_half_up`
- `docs/DANFE-MAPPING.md` - field mapping, not-on-DANFE list, limitations
- `docs/DECISIONS.md` - D-22, D-23, D-24 and three pointer lines
- `justfile`, `AGENTS.md` - docs-check requires and documents D-18 to D-24

## Decisions Made
- D-22, D-23 and D-24 as recorded in docs/DECISIONS.md (the STATE.md note from 02-01 that `totals.invoice_total` replaces `total_amount` is now recorded as D-22).
- The `sum_tolerance` section is an object with two arrays, `cap_cases` (`{n, expected}`) and `within` (`{computed, printed, n, within}`). The plan fixed the values but not the container shape; 02-04 reads this shape. Python does not read the section (D-09).
- The D-03 pointer is a second line, `**Also refined by:** D-22`, under the existing `Superseded in part by: D-18` line, so no existing line is edited.

## Deviations from Plan

None - plan executed exactly as written.

**Total deviations:** 0
**Impact on plan:** none.

## Issues Encountered
None. `python3` is not on PATH outside the devenv shell, so the first scripted edit failed and the edits were redone with the Edit tool; no file was changed by the failed attempt.

## Authentication Gates
None.

## Known Stubs
None.

## Threat Flags
None. The vector file holds only published examples and keys built from them (T-02-05); no package, lockfile or Directory.Packages.props change (T-02-SC).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- 02-04 can run the same vector file under xUnit (shape of `sum_tolerance` documented above).
- 02-07 can add the test that every schema path appears in docs/DANFE-MAPPING.md; the doc lists exactly the 42 paths of the current schema.
- `just py-check` (221 passed), `just docs-check` and `just secrets-check` are green. `just dotnet-check` was not rerun: this plan touched no .NET file.

## Self-Check: PASSED
- Created files exist: data/vectors/validator-vectors.json, python/tests/test_vectors.py, docs/DANFE-MAPPING.md.
- Commits 26d1d3b, 283aeb1, c702b58 are on gsd/phase-02-validated-extraction.
- All task acceptance criteria re-run: vector counts `7 5 6 10`, 4 ids helpers, 1 round_half_up, 31 collected tests, 3 D-2x headings, justfile and AGENTS.md strings, 17 items/installments rows, 1 Limitations heading.

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
