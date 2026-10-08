---
phase: 02-validated-extraction
verified: 2026-10-08T21:00:00Z
status: gaps_found
score: 4/5 must-haves verified
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
  - dotnet/src/Carimbo.Domain/Wire.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/src/Carimbo.Validation/ArithmeticRules.cs
  - dotnet/src/Carimbo.Validation/InvoiceValidator.cs
covered_digest: "v3:sha256:79853d6c3fb9be320c23bdf6caca909bf598f7e67140381f00deb47f1c0e3e30"
behavior_unverified: 0
overrides_applied: 0
gaps:
  - truth: "For known-bad invoices, validators never throw (SC3, VAL-01); a success outcome means a schema-valid invoice (SC4/SC5 hard guarantees)"
    status: failed
    reason: "A null element in `items` or `installments` deserializes with Wire.Options, passes PatternViolations() with 0 violations, so Parse returns Success. InvoiceValidator.Validate then dereferences the element and throws NullReferenceException. Parse catches only JsonException/FormatException/OverflowException and the Validate call in ExtractAsync is not guarded, so the exception escapes: POST /eval/extractions answers an unhandled 500 and every already-paid attempt is lost from the record (EXT-04, API-01). Reproduced by the verifier with a scratch console project (case-001 mapped to Invoice, then items=[null] and installments=[null]): both parsed with violations=0, both then threw NullReferenceException. No existing test covers a null list element (NeverThrowsTests has none)."
    artifacts:
      - path: "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
        issue: "Parse (lines ~325-337) accepts null list elements; Validate call at ~line 266 is unguarded"
      - path: "dotnet/src/Carimbo.Validation/ArithmeticRules.cs"
        issue: "Dereferences items[i] / item.Total (~lines 119-121, 189) with no null guard"
      - path: "dotnet/src/Carimbo.Validation/KeyAndDateRules.cs"
        issue: "Dereferences invoice.Installments[i].DueDate (~line 91)"
    missing:
      - "Reject null elements of items and installments in Parse (SchemaInvalid) or in Invoice.PatternViolations"
      - "Regression cases for `[null]` in both lists in NeverThrowsTests and ExtractorTests (and an endpoint test that expects a typed schema_invalid, not 500)"
  - truth: "Success means schema-valid (Wire.Options and the committed schema agree on string enums)"
    status: partial
    reason: "Wire.Options' JsonStringEnumConverter allows integer values and any-case names, which the schema forbids. Reproduced: recipient.tax_id_kind = 0 and = \"CNPJ\" parse as Cnpj with 0 violations; 7 parses as an undefined enum value. These pass as `success`. Lower impact than the null-element crash (no exception, validators still run), tracked as review WR-01."
    artifacts:
      - path: "dotnet/src/Carimbo.Domain/Wire.cs"
        issue: "JsonStringEnumConverter(SnakeCaseLower) constructed with default allowIntegerValues: true"
    missing:
      - "Disallow integer enum values (allowIntegerValues: false) and add a case-exactness check, with tests"
---

# Phase 2: Validated Extraction Verification Report

**Phase Goal:** As a developer, I want to have every extraction target the full DANFE-visible invoice, pass deterministic validators and be repaired within a bounded budget, so that extracted invoices carry hard guarantees before anything is measured at scale.
**Verified:** 2026-10-08T21:00:00Z
**Status:** gaps_found
**Re-verification:** No, initial verification

## Goal Achievement

Almost everything the phase promised exists, is wired and is exercised by passing tests. I re-ran `devenv shell -- just check` myself: it exited 0 (all five .NET test assemblies passed with 0 failed, 280 pytest passed, 5 e2e passed, ruff/pyright clean, schema-check and the other gates green). One hard guarantee is nonetheless false on a reachable path. The "validators never throw" and "success means schema-valid" invariants break when the model output contains a `null` list element (review item CR-01, reproduced). The fix is small and local, but it is a genuine breach of SC3/VAL-01 and of the phase's own "hard guarantees" wording, so the phase is not marked passed.

### Observable Truths

| #   | Truth (ROADMAP success criterion) | Status | Evidence |
| --- | --------------------------------- | ------ | -------- |
| 1 | `Invoice` holds exactly the DANFE-visible fields; XML-to-DANFE mapping documented; CNPJ/key accept numeric and alphanumeric; money is pattern-constrained decimal strings; half-up rounding identical in C# and Python | VERIFIED | `Invoice.cs` record: header, issuer, recipient, items, totals, installments only. `docs/DANFE-MAPPING.md` (85 lines, field table) with a doc-drift test (`MappingDocTests`). `Patterns.AccessKey` and CNPJ patterns accept `[A-Z0-9]`. `schema/invoice.schema.json` is current (`schema-check` green). Rounding vectors (10 cases, plus a 33-entry rounding_rule table) consumed by both runtimes. |
| 2 | One hand-curated vector file passes under both xUnit and pytest, with valid/invalid CNPJs (numeric and alphanumeric), keys and rounding cases | VERIFIED | `data/vectors/validator-vectors.json` (7 cnpj, 5 cpf, 6 access_key, 10 rounding, tolerance and sum_tolerance blocks). Read by `dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs` and `python/tests/test_vectors.py`; both green in my `just check` run. |
| 3 | For known-bad invoices validators never throw; they return structured errors (field, rule ID, expected, actual, severity) for the listed rule families | FAILED | The rule catalogue is complete and the structured record exists: `ValidationFinding(Field, RuleId, Expected, Actual, Severity)`, 30 stable rule ids covering CNPJ/CPF check digits, key check digit, key vs issuer CNPJ / year-month / model / series / number, line items vs totals, ICMS/IPI vs bases and rates by regime, and date plausibility; `NeverThrowsTests` covers many bad shapes. But `Validate` throws `NullReferenceException` for an `Invoice` whose `Items` or `Installments` contains a null element, and the pipeline lets such an invoice reach it (see gap 1). Verified by running it. |
| 4 | Failed attempts retry with structured errors fed back under a no-fabrication prompt; stops at max (default 2) with a typed failure; scripted-model tests cover first-try success, repair success and budget exhaustion | VERIFIED | `ExtractionSettings.MaxRepairs = 2`; loop `for index <= MaxRepairs` in `InvoiceExtractor.ExtractAsync`; `repair-001` prompt says "Never calculate, adjust, balance or invent a value"; `RepairFeedback` withholds expected check digits/key components (fixed sentences) and reveals only validator-built arithmetic/date values. `RepairLoopTests` contains the three required scenarios plus refusal, truncation, gateway failure, schema-invalid repair, cancellation and max_repairs 0/1. Budget exhaustion yields `validation_failed` with the last candidate. Passing in `just check`. The repair path inherits the gap-1 crash only when the model emits a null element. |
| 5 | `POST /eval/extractions` returns validator outcomes and every attempt with output, validator results, tokens, cost and latency, alongside the result and trace ID | VERIFIED | `EvalEndpoint.cs` `EvalResponse` carries `Effective`, `Outcome`, `Attempts` (each via `EvalAttempt.From`), summed `Usage`, `CostUsd`/`cost_warning`, `LatencyMs`, `TraceId`; `reference_date` is strictly parsed; contract version "2" enforced. `EvalEndpointTests` plus the pytest e2e over HTTP against the scripted host (5 passed) exercise it. Caveat: the 500 path of gap 1 does not return any of this. |

**Score:** 4/5 truths verified (0 present, behavior-unverified)

### Deferred Items

None. The null-element hardening is not claimed by any later phase (Phase 3 covers cache/replay/tracing/overrides).

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `dotnet/src/Carimbo.Domain/Invoice.cs` | Full DANFE-visible target | VERIFIED | Substantive, wired to schema export and parsing |
| `dotnet/src/Carimbo.Validation/*` (Findings, Identity, KeyAndDateRules, ArithmeticRules, InvoiceValidator) | Rule catalogue | VERIFIED with defect | Complete; null-element crash (gap 1) |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs`, `RepairFeedback.cs` | Bounded repair loop | VERIFIED with defect | Parse accepts null list elements |
| `dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs` | XML to Invoice mapper | VERIFIED | `GroundTruthGateTests` assert zero validator errors on committed cases |
| `dotnet/src/Carimbo.Api/EvalEndpoint.cs` | Eval contract 2 | VERIFIED | |
| `data/vectors/validator-vectors.json` | Shared vectors | VERIFIED | Consumed by both stacks |
| `docs/DANFE-MAPPING.md`, `docs/DECISIONS.md` D-22..D-24 | Documentation | VERIFIED | `just docs-check` green |
| `schema/*.json`, `python/src/carimbo_models/generated.py` | Generated contract | VERIFIED | `schema-check` clean diff |
| `data/skeleton/*` (3 cases, manifest as_of_date) | Reworked cases | VERIFIED | `datagen-check` byte-identical |
| `python/src/carimbo_evals/{grader,runner,summary}.py` | Offline grader for validation_failed/findings/attempts | VERIFIED | pytest green |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | -- | --- | ------ | ------- |
| EvalEndpoint | IInvoiceExtractor | `extractor.ExtractAsync(...)` | WIRED | Same extractor and validator registered in Program.cs as production |
| InvoiceExtractor | InvoiceValidator | `validator.Validate(parsed.Invoice, context.ReferenceDate)` | WIRED | Unguarded; see gap 1 |
| InvoiceExtractor | RepairFeedback | `RepairFeedback.Build(...)` on `ValidationFailed when budgetLeft` | WIRED | |
| Domain `Wire.Options` | schema export and Parse | shared options | WIRED | Diverges from schema for enums (gap 2) |
| C# Domain | Pydantic models | schema export then codegen | WIRED | schema-check |
| justfile `skeleton` | Api `Extraction__MaxRepairs` | env export | WIRED | |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| EvalResponse.Attempts | `result.Attempts` | real gateway responses, usage and cost per attempt | Yes (live run recorded in 02-10) | FLOWING |
| EvalResponse.Outcome findings | `validator.Validate` | live Invoice | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Whole offline gate | `devenv shell -- just check` | exit 0; .NET 0 failed; 280 passed + 5 e2e passed | PASS |
| Null list element survives parse | scratch console: case-001 invoice with `items=[null]` / `installments=[null]`, `Deserialize<Invoice>(Wire.Options)` then `PatternViolations()` | parsed, violations=0 (both) | FAIL (expected rejection) |
| Validator on that parsed invoice | `new InvoiceValidator(new ValidationOptions()).Validate(...)` | NullReferenceException (both) | FAIL (expected findings, never a throw) |
| Enum strictness | `recipient.tax_id_kind` = 0, "CNPJ", 7 | parsed as Cnpj, Cnpj, undefined 7; 0 violations | FAIL (schema forbids) |

The scratch project lived in the session scratchpad; no repo file was modified (`git status` clean).

### Probe Execution

No `probe-*.sh` probes declared or present; SKIPPED.

### Requirements Coverage

All 12 IDs in the phase's ROADMAP entry appear in at least one PLAN `requirements` field and in REQUIREMENTS.md (marked Complete, traceability table maps each to Phase 2). No orphaned Phase 2 IDs.

| Requirement | Source Plan(s) | Description | Status | Evidence |
| ----------- | -------------- | ----------- | ------ | -------- |
| DOM-02 | 02-01, 02-02, 02-03, 02-05, 02-07 | DANFE-visible target, mapping documented | SATISFIED | Invoice.cs, DANFE-MAPPING.md, MappingDocTests |
| DOM-03 | 02-01, 02-05 | Alphanumeric CNPJ and key value forms | SATISFIED | Patterns, vectors, alphanumeric-issuer case-003 |
| DOM-04 | 02-01, 02-02 | Money string on wire, half-up in both languages | SATISFIED | Vectors in xUnit and pytest, Money pattern |
| VAL-01 | 02-04, 02-08 | Structured errors, never throws | BLOCKED (partially) | Throws NullReferenceException on null list elements (gap 1) |
| VAL-02 | 02-02, 02-04 | CNPJ check digits numeric and alphanumeric | SATISFIED | CNPJ vectors, Identity.cs |
| VAL-03 | 02-04, 02-07 | Key check digit and key-vs-field cross-checks | SATISFIED | KEY_* rule ids, KeyAndDateRuleTests |
| VAL-04 | 02-04, 02-07 | Items vs totals, taxes vs bases and rates, regime-aware | SATISFIED | ArithmeticRules, REGIME_CODE_MISMATCH, TAX_ARITH_*, tests |
| VAL-05 | 02-04, 02-08 | Date plausibility | SATISFIED | DATE_PLAUSIBLE, DUE_DATE_ORDER, reference_date wiring |
| VAL-06 | 02-02, 02-04 | Shared vector file in xUnit and pytest | SATISFIED | VectorTests.cs, test_vectors.py |
| API-01 | 02-08, 02-10 | Eval endpoint returns result, validator outcomes, attempts, cost, trace ID | SATISFIED on the normal path; unhandled 500 on gap-1 input | EvalEndpoint.cs and tests |
| EXT-03 | 02-03, 02-09, 02-10 | Bounded repair with structured errors, no fabrication | SATISFIED | InvoiceExtractor, repair-001, RepairLoopTests |
| EXT-04 | 02-08, 02-09, 02-10 | Every attempt recorded | SATISFIED on the normal path; attempts lost when Validate throws | Attempts list; see gap 1 |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` | ~325-337 | Parse accepts what the schema forbids (null list elements) | BLOCKER | Gap 1 |
| `dotnet/src/Carimbo.Domain/Wire.cs` | 251 | Enum converter allows integers and any case | WARNING | Gap 2 (WR-01) |
| TBD/FIXME/XXX markers | n/a | none found in dotnet/src, python/src, justfile, docs/DANFE-MAPPING.md | none | |

Advisory review items WR-02 to WR-06 and IN-01 to IN-04 (02-REVIEW.md, all `open` in 02-REVIEW-DISPOSITION.md) were not re-verified individually. They are not goal blockers as described: UF=EX recipients raising an error (WR-02, causes a needless repair for foreign recipients), the 900 s client timeout vs the repair chain (WR-03), unpriced vs no-call cost accounting (WR-04), gateway disposal (WR-05), schema-check blind to untracked artifacts (WR-06). They should be triaged, but none breaks a success criterion.

### Human Verification Required

None required for the status. Observation for the developer (not a gap): the live runs recorded in 02-10 (claude-haiku-4-5, US$0.0843 with max_repairs 2 and US$0.0642 with 0) show repair fixed nothing (case-002 stayed `validation_failed` after 3 attempts on a misread access key). The repair mechanism is proven by scripted-model tests (a successful repair is exercised there), not by a live success, which the phase criteria do not require.

### Gaps Summary

Two gaps share one root cause: the parse step accepts model output that the committed schema forbids, so "Success" does not always mean schema-valid.

1. **Blocker (CR-01):** `"items": [null]` or `"installments": [null]` parses as Success, then `InvoiceValidator.Validate` throws `NullReferenceException`. This falsifies SC3 / VAL-01 ("validators never throw") for a model-reachable input, returns an unhandled 500 from `/eval/extractions`, and drops the cost record of attempts already paid for (EXT-04). Fix: reject null elements in `Parse` (or `PatternViolations`) as `SchemaInvalid`, and add `[null]` regression tests at the validator, extractor and endpoint levels.
2. **Warning-level partial (WR-01):** integer and wrong-case enum values parse as valid. Fix: `allowIntegerValues: false` plus an exact-name check, with tests.

Both are small, local fixes in `InvoiceExtractor.cs`, `Invoice.cs` and `Wire.cs`. Run `/gsd-plan-phase 2 --gaps` to produce the closure plan.

---

_Verified: 2026-10-08T21:00:00Z_
_Verifier: Claude (gsd-verifier)_
