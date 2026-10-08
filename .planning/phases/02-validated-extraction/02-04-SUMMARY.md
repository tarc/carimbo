---
phase: 02-validated-extraction
plan: 04
subsystem: validation
tags: [dotnet, validators, check-digits, cnpj, cpf, access-key, decimal-arithmetic, xunit, nf-e]

requires:
  - phase: 02-validated-extraction
    provides: "02-01 v2 Invoice records, Money.RoundHalfUp, Patterns.IsFullMatch and the shared fixture data/vectors/valid-invoice.json"
  - phase: 02-validated-extraction
    provides: "02-02 data/vectors/validator-vectors.json (cnpj, cpf, access_key, rounding, tolerance, sum_tolerance) and decisions D-22 to D-24"
provides:
  - "Carimbo.Validation: pure library (Domain reference only, no package) with InvoiceValidator.Validate(invoice, referenceDate)"
  - "ValidationFinding {Field, RuleId, Expected, Actual, Severity}, ValidationOptions (Tolerance 0.01, SumToleranceCap 1.00, SumTolerance(n)) and the 30 stable RuleIds"
  - "Identifiers: CNPJ, CPF and access-key check digits and the 27-UF IBGE table, gated by explicit [0-9A-Z] patterns"
  - "Carimbo.Validation.Tests: xUnit over every section of the shared vector file, per-rule exact rule-id tests and a seeded never-throw loop"
affects: [02-07 ground-truth gate, 02-08 extraction wiring, 02-09 repair loop, Phase 5 reports]

plan_head_before: 06aae375ab7de69cd1b85475ced2f73c59f34788
plan_head_after: 76601aa1f60714bf2feb54575d3c56dbb59b8cea

estimate:
  tokens: 95000
  raw_tokens: 95000
  tasks: 3
  confidence: low
actuals:
  tokens: 25000
  tasks: 3
  commits: 3

tech-stack:
  added: []
  patterns:
    - "Validators are total: SafeMath scopes each catch (OverflowException) to one decimal operation and the failed check becomes ARITH_OVERFLOW on its own field; there is no top-level catch"
    - "Character arithmetic (ASCII - 48) runs only after an explicit pattern match, so non-ASCII digits, lowercase, newlines and mask punctuation never reach it"
    - "Dates are compared as DayNumber integers and the reference date is a parameter, so DateOnly.MaxValue cannot overflow and no clock is read"
    - "Expected is what the extracted fields imply (or the computed value), Actual is what the key, date or printed amount says"
    - "A reflection walker over the record graph writes hostile values at every leaf and rebuilds records through their public constructor, so the never-throw loop covers new members automatically"

key-files:
  created:
    - dotnet/src/Carimbo.Validation/Carimbo.Validation.csproj
    - dotnet/src/Carimbo.Validation/Findings.cs
    - dotnet/src/Carimbo.Validation/Identity.cs
    - dotnet/src/Carimbo.Validation/InvoiceValidator.cs
    - dotnet/src/Carimbo.Validation/KeyAndDateRules.cs
    - dotnet/src/Carimbo.Validation/ArithmeticRules.cs
    - dotnet/tests/Carimbo.Validation.Tests/Carimbo.Validation.Tests.csproj
    - dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs
    - dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs
  modified:
    - dotnet/Carimbo.slnx

key-decisions:
  - "Recipient identifier rules are routed by the identifier's shape (CNPJ pattern, then CPF pattern, else the FORMAT rule of the declared kind); a shape that disagrees with tax_id_kind adds TAX_ID_KIND_MISMATCH at recipient.tax_id_kind. This is what makes the masked CPF vector 529.982.247-25 (14 characters) give CPF_FORMAT with kind cpf, as the vector file says"
  - "All KEY_* findings carry the field access_key; Expected is the value the extracted field implies (UF code 33 for RJ, 2604 for 2026-04-01), Actual is the key segment"
  - "ICMS_not_taxed codes must carry exactly 0.00 (no tolerance); a cst_csosn that does not match the 3-4 digit pattern is treated like an unsupported code (warning), since the catalogue has no FORMAT rule for it and the extractor already enforces the pattern"
  - "DUP_SUM is reported at field installments; ARITH_OVERFLOW has Expected 'a representable amount' and an empty Actual"
  - "Carimbo.Validation exposes internals to Carimbo.Validation.Tests through an InternalsVisibleTo item (no package) so the inclusive SafeMath.TryWithin comparison is tested directly against the within cases of the vector file"

requirements-completed: [VAL-01, VAL-02, VAL-03, VAL-04, VAL-05, VAL-06]

coverage:
  - id: D1
    description: "InvoiceValidator returns structured findings with stable catalogue rule ids; the shared fixture validates clean at reference 2026-10-01"
    requirement: VAL-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#The_shared_fixture_validates_clean_at_the_reference_date"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Rule_ids_are_unique_and_each_value_equals_its_name"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#A_finding_serializes_with_snake_case_members_and_a_lowercase_severity"
        status: pass
    human_judgment: false
  - id: D2
    description: "Validators never throw: 2000 seeded hostile mutations plus every hostile value at every fixture leaf return catalogued findings, and an overflow becomes ARITH_OVERFLOW on its field"
    requirement: VAL-01
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs#Two_thousand_seeded_mutations_never_throw_and_only_use_catalogued_rule_ids"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs#Every_hostile_value_at_every_leaf_of_the_fixture_returns_a_list"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs#An_overflowing_line_total_is_an_overflow_finding_on_that_field"
        status: pass
    human_judgment: false
  - id: D3
    description: "CNPJ (numeric, alphanumeric, lowercase, identical characters), CPF and access-key check digits, tax id kind and UF rules, driven by the shared vector file"
    requirement: VAL-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Cnpj_vectors_at_the_issuer_give_exactly_the_vectors_rule"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Cpf_vectors_at_a_cpf_recipient_give_exactly_the_vectors_rule"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Access_key_vectors_give_the_vectors_rule_on_the_key_itself"
        status: pass
    human_judgment: false
  - id: D4
    description: "The access key is cross-checked against UF, issuer CNPJ, local year-month, model 55, series and number with one rule id each"
    requirement: VAL-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs#The_last_day_of_march_matches_2603_and_not_2604"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs#Series_and_number_are_compared_as_integers_not_as_text"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs#Several_segment_mismatches_are_listed_in_the_documented_order"
        status: pass
    human_judgment: false
  - id: D5
    description: "Items are checked against totals and taxes against bases and rates per inferred regime family, with inclusive tolerances and the vNF formula"
    requirement: VAL-04
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs#A_wrong_line_total_trips_the_item_the_ipi_and_the_product_sum_rules"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs#Not_taxed_codes_must_carry_a_zero_icms_amount"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs#The_icms_base_sum_tolerance_is_exactly_two_cents_for_two_items"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs#A_wrong_invoice_total_breaks_the_formula_and_the_installment_sum"
        status: pass
    human_judgment: false
  - id: D6
    description: "Issue-date plausibility against an injected reference date and installment due dates on or after the issue date"
    requirement: VAL-05
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs#An_issue_date_one_day_after_the_reference_passes_and_two_days_fails"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs#The_extreme_reference_dates_do_not_throw"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs#A_due_date_before_the_issue_date_is_reported_at_that_installment"
        status: pass
    human_judgment: false
  - id: D7
    description: "xUnit runs every section of data/vectors/validator-vectors.json (CNPJ, CPF, access key, rounding, tolerance, sum tolerance) and the options defaults equal the file"
    requirement: VAL-06
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Rounding_vectors_are_half_away_from_zero_with_no_negative_zero"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Sum_tolerance_comparison_is_inclusive"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs#Options_defaults_equal_the_tolerance_block_of_the_vector_file"
        status: pass
    human_judgment: false

duration: 11min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 04: deterministic validators that return structured findings and never throw Summary

**Pure Carimbo.Validation library: 30 stable rule ids over identity, access-key, date, item, tax, totals and installment checks, overflow-safe decimal math that reports ARITH_OVERFLOW instead of throwing, run under xUnit from the same vector file Python uses**

## Performance

- **Duration:** 11 min
- **Started:** 2026-10-08T14:34:59Z
- **Completed:** 2026-10-08T14:46:00Z
- **Tasks:** 3
- **Files modified:** 12 (11 created, 1 modified)

## Accomplishments
- `InvoiceValidator.Validate(invoice, referenceDate)` returns a deterministic list of `ValidationFinding` in the documented order (identity, access key, dates, items by index, totals, installments). The shared fixture gives an empty list; every known-bad mutation of it trips exactly its expected rule set (asserted as exact lists or sets).
- Identity rules port the Python check digits (numeric and alphanumeric CNPJ, CPF, access key) behind explicit `[0-9A-Z]` patterns, with identical-character rejection, tax-id-kind consistency and the 27-UF table. The access key is cross-checked at fixed slices (UF, issuer CNPJ, local AAMM, model 55, series and number as integers).
- Arithmetic rules cover `quantity x unit_price`, ICMS by regime family (Normal 00/20/90 and Simples 900 taxed; 40/41/50/60 and 101/102/103/300/400/500 must be 0.00; anything else a warning), IPI on the item total, item-column sums with `tolerance x max(n,1)` capped at 1.00, the vNF formula and the installment sum. Computed values are rounded with `Money.RoundHalfUp` and compared inclusively.
- Totality: `SafeMath` scopes every `catch (OverflowException)` to one operation. 2000 seeded mutations (seed 20261008) and every hostile value at every one of the fixture's leaves (strings including a 100000-character one, `decimal.MaxValue/MinValue`, `DateOnly` extremes, `int` extremes, an undefined enum value, empty and 200-item lists) return catalogued findings and a second run is sequence-equal.
- The three vector sections used by 02-02 (including the `sum_tolerance` `cap_cases` and `within` arrays) are read by xUnit theories, so Python and .NET share one oracle. `just dotnet-check` is green (438 tests across the solution, 152 in the new project).

## Task Commits

Each task was committed atomically:

1. **Task 1 (tracer): library and test project wired, identity rules driven by the shared vector file** - `9df4d7f` (feat)
2. **Task 2: access-key cross-checks and date plausibility with an injected reference date** - `fc7a2fc` (feat)
3. **Task 3: item, tax, totals and installment arithmetic, overflow-safe, with the never-throw loop** - `76601aa` (feat)

**Plan metadata:** committed after this summary (docs: complete plan)

_Note: the tasks are `tdd="true"` but each ships as one commit with tests and implementation together, as in plans 02-01 and 02-02 (see Deviations)._

## Files Created/Modified
- `dotnet/src/Carimbo.Validation/Findings.cs` - `FindingSeverity`, `ValidationFinding`, `ValidationOptions`, the 30 `RuleIds`, and the internal `Finding` builders
- `dotnet/src/Carimbo.Validation/Identity.cs` - `Identifiers` (check digits, UF table) and the internal `IdentityRules`
- `dotnet/src/Carimbo.Validation/KeyAndDateRules.cs` - access-key cross-checks, DATE_PLAUSIBLE, DUE_DATE_ORDER
- `dotnet/src/Carimbo.Validation/ArithmeticRules.cs` - `SafeMath` and the item, tax, totals and installment rules
- `dotnet/src/Carimbo.Validation/InvoiceValidator.cs` - composition in the documented order
- `dotnet/tests/Carimbo.Validation.Tests/*` - `RepoFiles` helper and vector theories, per-rule tests, never-throw tests
- `dotnet/Carimbo.slnx` - both projects added

## Decisions Made
- Recipient identifier rules follow the identifier's shape rather than its length alone: a plan wording of "14 characters" would route the masked CPF `529.982.247-25` to the CNPJ rules, but the vector file says `CPF_FORMAT` for it with kind `cpf`. Shape first (CNPJ pattern, then CPF pattern, else the FORMAT rule of the declared kind), then `TAX_ID_KIND_MISMATCH` when the shape and the declared kind differ.
- KEY_* findings sit on `access_key`; the issuer CNPJ mismatch is therefore not a finding on `issuer.cnpj`, which keeps each field's own rule list independent (the vector theories filter by field for that reason).
- Not-taxed ICMS amounts are compared exactly to zero. A `cst_csosn` that fails the 3-4 digit pattern gets the unsupported warning (the extractor already rejects it before validators run).
- `InternalsVisibleTo` for the test assembly instead of widening the public API for `SafeMath.TryWithin`.

## Deviations from Plan

### Plan-wording notes

**1. [Plan wording] TDD gate shape**
- Each `tdd="true"` task shipped as one commit (tests and implementation together). The new project did not exist before Task 1, so the red state was a compile failure; Tasks 2 and 3 passed on their first run after the tests were written alongside the rules. No RED evidence record was produced; the plan type is `execute`, so the plan-level TDD gate does not apply.

**2. [Plan wording] `SafeMath` file split**
- `ArithmeticRules.cs` was created in Task 1 holding only `SafeMath` (the inclusive `TryWithin` comparison is needed by the Task 1 within-vector tests); Task 3 added the rules to the same file. The file list of the plan is unchanged.

**3. [Plan wording] Exact-one-finding vector tests filter by field**
- The Task 1 bullet "exactly one finding" for an invalid vector at `issuer.cnpj` holds per field: the access-key cross-check (Task 2) adds `KEY_ISSUER_CNPJ_MISMATCH` on `access_key` for any CNPJ other than the key's, so the theories assert the rule list on the identifier's own field(s).

**Total deviations:** 0 auto-fixed (3 plan-wording notes)
**Impact on plan:** none; every behavior bullet and acceptance criterion is met as written.

## Issues Encountered
- The first Task 1 build failed on missing `using Xunit;` (xunit.v3 does not add a global using in this repo); added, as in the other test projects.
- In the Task 3 loop the required-rule guard briefly listed `CPF_FORMAT`, which seeded single-leaf mutations rarely reach (it needs a malformed id and kind `cpf` together); it is covered by a deterministic test instead and removed from the guard.
- One test expectation of mine was off (installment sum with +0.01 and +0.02 is 0.03 away, past the 0.02 tolerance); corrected before the commit.

## Authentication Gates
None.

## Known Stubs
None.

## Threat Flags
None. No network endpoint, auth path, file access or schema change was added; the library has no package reference (T-02-SC) and the mitigations T-02-12 to T-02-15 are implemented and tested (SafeMath and the never-throw loop; FORMAT findings and ARITH_OVERFLOW instead of silence; linear patterns on a 100000-character string; explicit `[0-9]` patterns with Arabic-Indic digits in the hostile set).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- 02-07 can run `InvoiceValidator` over ground truth (D-17) and 02-08/02-09 can wire the findings into the extraction outcome and the repair loop; findings serialize with `Wire.Options` as `field`, `rule_id`, `expected`, `actual`, `severity`.
- `Identifiers.UfCodes`, `AccessKeyCheckDigit` and `CnpjCheckDigits` are public for the datagen cross-check tests if wanted.
- Limitations carried from the plan: CST 60 is treated as not taxed (RESEARCH A6) and the IPI base is the item total (A7); both are recorded in docs/DANFE-MAPPING.md Limitations.

## Self-Check: PASSED
- Created files exist: all 11 files under `dotnet/src/Carimbo.Validation/` and `dotnet/tests/Carimbo.Validation.Tests/` listed above (checked with `[ -f ]`).
- Task commits `9df4d7f`, `fc7a2fc`, `76601aa` are ancestors of HEAD on `gsd/phase-02-validated-extraction`; `git rev-list --count` from the ledger base gives 3.
- Acceptance criteria re-run: PackageReference count 0 and Domain reference count 1; `Carimbo.Validation` appears twice in the slnx; 30 `const string` rule ids; 0 `char.IsDigit`; 8 rule-id references in `KeyAndDateRules.cs` and 0 clock references; 3 `catch (OverflowException)` and 0 catch-alls in `src`; 5 `Money.RoundHalfUp`; seed 20261008 and 2000 iterations present; fixture validates to an empty list.
- Plan verification: `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/Carimbo.Validation.Tests'` and `devenv shell -- just dotnet-check` exit 0 (438 tests, 0 warnings, format clean).

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
