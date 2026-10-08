---
phase: 02-validated-extraction
verified: 2026-10-08T23:30:00Z
status: passed
score: 5/5 must-haves verified
covered_files:
  - .planning/phases/02-validated-extraction/02-01-PLAN.md
  - .planning/phases/02-validated-extraction/02-01-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-02-PLAN.md
  - .planning/phases/02-validated-extraction/02-02-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-03-PLAN.md
  - .planning/phases/02-validated-extraction/02-03-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-04-PLAN.md
  - .planning/phases/02-validated-extraction/02-04-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-05-PLAN.md
  - .planning/phases/02-validated-extraction/02-05-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-06-PLAN.md
  - .planning/phases/02-validated-extraction/02-06-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-07-PLAN.md
  - .planning/phases/02-validated-extraction/02-07-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-08-PLAN.md
  - .planning/phases/02-validated-extraction/02-08-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-09-PLAN.md
  - .planning/phases/02-validated-extraction/02-09-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-10-PLAN.md
  - .planning/phases/02-validated-extraction/02-10-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-11-PLAN.md
  - .planning/phases/02-validated-extraction/02-11-SUMMARY.md
  - .planning/phases/02-validated-extraction/02-12-PLAN.md
  - .planning/phases/02-validated-extraction/02-12-SUMMARY.md
  - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Domain/Wire.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/src/Carimbo.Extraction/RepairFeedback.cs
  - dotnet/src/Carimbo.Validation/ArithmeticRules.cs
  - dotnet/src/Carimbo.Validation/Findings.cs
  - dotnet/src/Carimbo.Validation/InvoiceValidator.cs
covered_digest: "v3:sha256:01d6e8d74d9271ce0c7e6d08ecbbeddcd648e112aad52ff8e69b87079e403f7f"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 4/5
  gaps_closed:
    - "Validators never throw; success means schema-valid: a null items/installments element is now a typed schema_invalid at the parse boundary and a NULL_VALUE error finding in the validator (CR-01, plan 02-11)"
    - "Success means schema-valid for enums: Wire.Options reads enums by exact wire name only (WR-01, plan 02-12, D-25)"
  gaps_remaining: []
  regressions: []
---

# Phase 2: Validated Extraction Verification Report

**Phase Goal:** As a developer, I want to have every extraction target the full DANFE-visible invoice, pass deterministic validators and be repaired within a bounded budget, so that extracted invoices carry hard guarantees before anything is measured at scale.
**Verified:** 2026-10-08T23:30:00Z
**Status:** passed
**Re-verification:** Yes, after gap closure (plans 02-11 and 02-12)

## Goal Achievement

Both gaps from the first verification are closed in the code, not just in the summaries. I re-ran `devenv shell -- just check` myself at HEAD: exit 0, 587 .NET tests passed with 0 failed, 280 pytest passed, 5 e2e passed, ruff/pyright clean, schema regeneration produced no diff, `datagen-check` byte-identical, docs and secrets gates green. I also re-ran my own scratch console project (kept in the session scratchpad; `git status` is clean) against HEAD with the exact inputs that failed last time.

### Gap re-check (the two previous failures)

| Input | Previous result | Result at HEAD |
| ----- | --------------- | -------------- |
| `items = [null]` | parsed, 0 violations, `InvoiceValidator` threw NullReferenceException | `Invoice.NullViolations()` = `items[0]`; `InvoiceValidator.Validate` = exactly `NULL_VALUE@items[0]`, no throw |
| `installments = [null]` | same crash | `NullViolations()` = `installments[0]`; validator = `NULL_VALUE@installments[0]` |
| `recipient.tax_id_kind = 0` | parsed as Cnpj | `JsonException` at `$.recipient.tax_id_kind` |
| `"CNPJ"` | parsed as Cnpj | `JsonException` |
| `"0"` | not covered | `JsonException` |
| `7` | parsed as undefined enum value | `JsonException` |
| `"cnpj"` and the shared valid fixture | parsed | still parse, 0 violations, 0 findings (no regression) |

Code evidence for the pipeline guarantee: `InvoiceExtractor.Parse` (lines ~325-345) calls `invoice.NullViolations()` after deserialisation and returns `ExtractionOutcome.SchemaInvalid` listing paths only, before `PatternViolations()` and before the validator is reached; `Validate` is additionally total on nulls (it returns one `NULL_VALUE` error per null path and runs no other rule), so a direct caller cannot crash it either. `Wire.Options` registers `StrictEnumJsonConverter`, which accepts only a JSON string equal ordinally to a wire name from `Wire.EnumNames`, the same table `CanonicalSchema` uses to build the schema `enum`, so parser and schema cannot drift. The exported schema is unchanged (`just schema-check` ran a regeneration with no diff).

Tests that pin the closure (all passing): `NeverThrowsTests` (single/first/middle/last/adjacent nulls, null list, null nested record, null required string, null `ie` allowed), `ExtractorTests.NullElementScenarios` and `A_tax_id_kind_that_is_not_an_exact_wire_name_is_schema_invalid`, `RepairLoopTests` (null in initial answer, in a repair answer, in the last attempt), `EvalEndpointTests` (null items element is HTTP 200 with typed `schema_invalid`, null in a repair attempt keeps every paid attempt; non-exact `tax_id_kind` spellings), `DomainTests.Every_public_domain_enum_is_read_and_written_by_exact_wire_name_only`.

### Observable Truths

| #   | Truth (ROADMAP success criterion) | Status | Evidence |
| --- | --------------------------------- | ------ | -------- |
| 1 | `Invoice` holds exactly the DANFE-visible fields; XML-to-DANFE mapping documented; CNPJ/key accept numeric and alphanumeric; money is pattern-constrained decimal strings; half-up rounding identical in C# and Python | VERIFIED (regression check passed) | `Invoice.cs`, `docs/DANFE-MAPPING.md`, `MappingDocTests`, `Patterns` accept `[A-Z0-9]`; `schema-check` clean; rounding vectors consumed by both runtimes; `Money.RoundHalfUp` uses AwayFromZero. Decision D-25 added to `docs/DECISIONS.md` (line 494). |
| 2 | One hand-curated vector file passes under both xUnit and pytest | VERIFIED (regression check passed) | `data/vectors/validator-vectors.json` read by `VectorTests.cs` and `python/tests/test_vectors.py`; both green in my `just check` run. |
| 3 | For known-bad invoices validators never throw; they return structured errors (field, rule ID, expected, actual, severity) for the listed rule families | VERIFIED (previously FAILED) | Rule catalogue intact (`ValidationFinding(Field, RuleId, Expected, Actual, Severity)`, check-digit, key cross-check, arithmetic, tax and date rules). Null elements, null lists, null nested records and null required strings now yield `NULL_VALUE` error findings instead of throwing; reproduced by my scratch run. |
| 4 | Failed attempts retry with structured errors fed back under a no-fabrication prompt; stops at max (default 2) with a typed failure; scripted tests cover first-try success, repair success, budget exhaustion | VERIFIED (regression check passed) | `ExtractionSettings.MaxRepairs = 2`, loop in `InvoiceExtractor.ExtractAsync`, `repair-001` prompt, `RepairFeedback` redaction; `RepairLoopTests` passing (new null-element cases included: a null in a repair answer consumes budget and the previous candidate is returned when spent). |
| 5 | `POST /eval/extractions` returns validator outcomes and every attempt with output, validator results, tokens, cost, latency, alongside result and trace ID | VERIFIED (previously verified with caveat) | `EvalEndpoint.cs` contract version 2; `EvalEndpointTests` and pytest e2e (5 passed). The former unhandled 500 path is gone: a null element returns HTTP 200 with typed `schema_invalid` and keeps every paid attempt. |

**Score:** 5/5 truths verified (0 present, behavior-unverified)

### Deferred Items

None.

### Advisory (New Scope, Unevidenced)

None raised by the verifier. The code review (`02-REVIEW.md`, 0 critical, 5 warnings WR-02..WR-06 carried forward from the first review, 8 info) lists non-blocking quality items outside the must-haves; they are not repeated as gaps. The two most relevant to the phase goal, for the planner's backlog: WR-02 (recipient `UF = "EX"` for exports is flagged `UF_UNKNOWN`; the dataset cannot represent foreign recipients so no current case is affected) and IN-06 (the `NULL_VALUE` repair sentence is unreachable through the extractor and untested; defence in depth only).

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `dotnet/src/Carimbo.Domain/Invoice.cs` | Full DANFE-visible target plus `NullViolations()` | VERIFIED | Substantive; reflection-driven, honours nullability annotations (`ie` stays nullable); used by extractor and validator |
| `dotnet/src/Carimbo.Domain/Wire.cs` | Strict wire options | VERIFIED | `StrictEnumJsonConverter` registered in `Wire.Options`; `EnumNames` shared with schema export |
| `dotnet/src/Carimbo.Domain/CanonicalSchema.cs` | Schema export from shared enum names | VERIFIED | Output byte-identical to committed `schema/*.json` |
| `dotnet/src/Carimbo.Validation/*` | Rule catalogue, total on nulls | VERIFIED | `RuleIds.NULL_VALUE` added; `Validate` guards before rules run |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs`, `RepairFeedback.cs` | Bounded repair loop, null rejection at parse | VERIFIED | Parse rejects nulls and pattern violations before validation |
| `dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs` | XML to Invoice mapper | VERIFIED | `GroundTruthGateTests` green |
| `dotnet/src/Carimbo.Api/EvalEndpoint.cs` | Eval contract 2 | VERIFIED | Tests green |
| `data/vectors/validator-vectors.json`, `docs/DANFE-MAPPING.md`, `docs/DECISIONS.md` D-22..D-25 | Shared vectors and docs | VERIFIED | `docs ok` |
| `schema/*.json`, `python/src/carimbo_models/generated.py` | Generated contract | VERIFIED | no regeneration diff |
| `data/skeleton/*`, `python/src/carimbo_evals/*` | Cases and offline grader | VERIFIED | `datagen-check` ok; pytest green |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | -- | --- | ------ | ------- |
| `InvoiceExtractor.Parse` | `Invoice.NullViolations` | called before `PatternViolations` and before validation | WIRED | Was missing last time |
| `InvoiceExtractor` | `InvoiceValidator` | `validator.Validate(parsed.Invoice, ...)` | WIRED | Validator now total on nulls |
| `Wire.Options` | `StrictEnumJsonConverter` | `options.Converters.Add` | WIRED | |
| `StrictEnumJsonConverter` / `CanonicalSchema` | `Wire.EnumNames` | single name table | WIRED | parser and schema cannot drift |
| EvalEndpoint | IInvoiceExtractor | `extractor.ExtractAsync(...)` | WIRED | |
| InvoiceExtractor | RepairFeedback | `RepairFeedback.Build(...)` | WIRED | |
| C# Domain | Pydantic models | schema export then codegen | WIRED | |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| EvalResponse.Attempts | `result.Attempts` | real gateway responses, usage, cost per attempt (live run recorded in 02-10) | Yes | FLOWING |
| EvalResponse.Outcome findings | `validator.Validate` | parsed Invoice | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Whole offline gate | `devenv shell -- just check` | exit 0; 587 .NET passed / 0 failed; 280 pytest + 5 e2e passed | PASS |
| Null list elements | scratch console on `data/vectors/valid-invoice.json` with `items=[null]` / `installments=[null]` | `NULL_VALUE@items[0]` / `NULL_VALUE@installments[0]`, no throw | PASS |
| Enum strictness | `tax_id_kind` = 0, "CNPJ", "0", 7 | `JsonException` at `$.recipient.tax_id_kind` (all four) | PASS |
| Valid spelling and baseline | `"cnpj"`, unmodified fixture | parse, 0 null/pattern violations, 0 findings | PASS |

### Probe Execution

No `probe-*.sh` probes declared or present; SKIPPED.

### Requirements Coverage

All 12 IDs in the phase's ROADMAP entry (DOM-02, DOM-03, DOM-04, VAL-01..VAL-06, API-01, EXT-03, EXT-04) appear in at least one PLAN `requirements` field and in REQUIREMENTS.md. No orphaned Phase 2 IDs.

| Requirement | Source Plan(s) | Description | Status | Evidence |
| ----------- | -------------- | ----------- | ------ | -------- |
| DOM-02 | 02-01, 02-02, 02-03, 02-05, 02-07 | DANFE-visible target, mapping documented | SATISFIED | Invoice.cs, DANFE-MAPPING.md, MappingDocTests |
| DOM-03 | 02-01, 02-05 | Alphanumeric CNPJ and key forms | SATISFIED | Patterns, vectors, alphanumeric-issuer case-003 |
| DOM-04 | 02-01, 02-02 | Money string on wire, half-up in both languages | SATISFIED | Shared vectors in xUnit and pytest |
| VAL-01 | 02-04, 02-08, 02-11 | Structured errors, never throws | SATISFIED | NULL_VALUE handling, NeverThrowsTests, scratch repro |
| VAL-02 | 02-02, 02-04 | CNPJ check digits numeric and alphanumeric | SATISFIED | Vectors, Identity.cs |
| VAL-03 | 02-04, 02-07 | Key check digit and key-vs-field cross-checks | SATISFIED | KEY_* rules, KeyAndDateRuleTests |
| VAL-04 | 02-04, 02-07 | Items vs totals, taxes vs bases/rates, regime-aware | SATISFIED | ArithmeticRules, tests |
| VAL-05 | 02-04, 02-08 | Date plausibility | SATISFIED | DATE_PLAUSIBLE, DUE_DATE_ORDER |
| VAL-06 | 02-02, 02-04 | Shared vector file in xUnit and pytest | SATISFIED | VectorTests.cs, test_vectors.py |
| API-01 | 02-08, 02-10, 02-11 | Eval endpoint returns result, validator outcomes, attempts, cost, trace ID | SATISFIED | EvalEndpoint.cs; no 500 on null elements |
| EXT-03 | 02-03, 02-09, 02-10, 02-11, 02-12 | Bounded repair with structured errors, no fabrication | SATISFIED | InvoiceExtractor, repair-001, RepairLoopTests |
| EXT-04 | 02-08, 02-09, 02-10, 02-11 | Every attempt recorded | SATISFIED | Attempts list preserved on null-element input |

Note for the orchestrator: in `.planning/REQUIREMENTS.md` the traceability table still shows DOM-03, DOM-04, VAL-02..VAL-06 and EXT-03 as "Gaps Found" and their checkboxes unchecked (the first verification's revert, commit caf734f). With this report they should be flipped back to Complete.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| Phase source files | n/a | TBD / FIXME / XXX debt markers in changed files | none found in the gap-closure files | No blocker |
| `dotnet/src/Carimbo.Validation/Identity.cs` | 23-52, 271-277 | `EX` recipient UF flagged (review WR-02) | Warning | Out of must-haves; no case affected |
| `justfile` | 34-36 | `schema-check` uses worktree-only `git diff` (WR-06) | Warning | I compared worktree to HEAD after the regeneration and `git status` is clean, so the claim holds for this run |

### Human Verification Required

None. The phase delivers deterministic code paths fully covered by automated tests that I ran. The live paid skeleton run (02-10) is historical evidence and is not required to certify the gap closure.

### Gaps Summary

No gaps. CR-01 (null list elements) and WR-01 (lax enum parsing) are closed with code at the parse boundary and defence in depth in the validator, pinned by unit, repair-loop and HTTP-level tests, and reproduced as fixed by an independent scratch run. The five previously verified truths show no regression.

---

_Verified: 2026-10-08T23:30:00Z_
_Verifier: Claude (gsd-verifier)_
