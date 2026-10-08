---
phase: 02-validated-extraction
plan: 01
subsystem: extraction-contract
tags: [dotnet, system-text-json, json-schema, pydantic, datamodel-codegen, xunit, pytest, nf-e, danfe]

# Dependency graph
requires:
  - phase: 01-skeleton
    provides: "7-field Invoice, Money wire decimal, CanonicalSchema export, ModelSchemaProjector and SchemaBudget, Python grader, scripted-model e2e"
provides:
  - "Invoice v2: the full DANFE-visible target (header, issuer, recipient CNPJ or CPF, 14-column items, 11 totals boxes, installments)"
  - "Decimal4 and Rate wire decimals; Money.RoundHalfUp (half away from zero, no negative zero)"
  - "Reflection-driven Invoice.PatternViolations with list-index paths"
  - "Prompt extract-002 and a v2 model-facing schema inside the 24/16 budget (0 optional, 2 union)"
  - "data/vectors/valid-invoice.json, the shared hand-checked fixture read by xUnit and pytest"
  - "carimbo_evals.ground_truth.invoice_from_xml and a 27-field grader (grader-002, summary version 2)"
affects: [02-02, 02-03, 02-04, 02-05, 02-06, 02-07, validators, repair-loop, datagen, evals]

# Actuals (#2632): chars/4 over the realized diff (git diff of the plan range, 361780 chars)
actuals:
  tokens: 90000
  tasks: 3
  commits: 3
plan_head_before: ce42d68a393365f1650bddbe7298c154fc7b1fd8
plan_head_after: 35fed2ab3d1ccdc5cbf97f6bcb73da6a05416ccb

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Wire decimals check the pattern before an invariant decimal.TryParse and wrap Format/OverflowException in JsonException"
    - "PatternViolations is a reflection walker over [RegularExpression]; a schema-driven drift test proves every schema pattern is enforced"
    - "One shared hand-checked JSON fixture read by both stacks"
    - "Ground truth is the documented XML-to-DANFE mapping, not anything smarter"

key-files:
  created:
    - data/vectors/valid-invoice.json
    - python/src/carimbo_evals/ground_truth.py
  modified:
    - dotnet/src/Carimbo.Domain/Invoice.cs
    - dotnet/src/Carimbo.Domain/Wire.cs
    - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
    - dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - dotnet/tools/LlmSpike/Program.cs
    - schema/invoice.schema.json
    - schema/invoice.model.schema.json
    - python/src/carimbo_models/generated.py
    - python/src/carimbo_evals/grader.py
    - python/src/carimbo_evals/summary.py
    - python/src/carimbo_evals/cli.py
    - python/tests/test_grader.py
    - python/tests/test_e2e_fake.py
    - python/tests/test_models.py
    - python/tests/test_datagen.py

key-decisions:
  - "The Phase 1 top-level total_amount is removed and the invoice total lives only at totals.invoice_total (no alias: an alias makes the model emit one value twice and needs its own consistency rule); plan 02-02 records this as D-22"
  - "Recipient is a separate record from Party: the issuer stays CNPJ-only in the type, the recipient carries tax_id plus tax_id_kind (cnpj | cpf); tax_id replaces cnpj outright"
  - "tax_id_kind has no Description so its schema node is exactly {type: string, enum: [cnpj, cpf]}; Description on object-typed members lands on the hoisted $defs entry, not on the $ref property"
  - "GroundTruth in the grader holds the wire-form invoice dict from invoice_from_xml and answers expected(field) for the 27 graded fields"

patterns-established:
  - "Every patterned member must carry [RegularExpression(Patterns.X)] with an explicit [0-9] class; reflection and the drift test make an unenforced pattern a failing test"
  - "Edge cases of the HTTP boundary are scripted through the shared fixture with one mutated path"

requirements-completed: [DOM-02, DOM-03, DOM-04]

coverage:
  - id: D1
    description: "Invoice v2 is the full DANFE-visible target; every field required, only issuer.ie and recipient.ie nullable; canonical and model-facing schemas and generated Pydantic models regenerate reproducibly"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs#Every_property_is_required_and_only_the_two_ie_members_are_unions"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs#Export_equals_the_committed_schema_file"
        status: pass
      - kind: other
        ref: "devenv shell -- just schema-check"
        status: pass
    human_judgment: false
  - id: D2
    description: "The model-facing schema fits the structured-output budget with 0 optional and 2 union properties"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs#The_v2_invoice_schema_fits_the_structured_output_budget"
        status: pass
    human_judgment: false
  - id: D3
    description: "Numeric and alphanumeric CNPJ and access-key forms and an 11-digit CPF recipient parse; empty, whitespace, trailing-newline, lowercase and non-ASCII-digit identifiers are schema_invalid naming the path"
    requirement: DOM-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Identifiers_are_exact_ascii_strings_of_their_length"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#A_value_violating_its_schema_pattern_is_schema_invalid_and_names_the_field"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#A_cpf_recipient_with_kind_cpf_is_a_success"
        status: pass
    human_judgment: false
  - id: D4
    description: "Money, Decimal4 and Rate keep their printed precision and reject pt-BR separators, signs, exponents, padding and wrong fraction counts; Money.RoundHalfUp rounds half away from zero with no negative zero"
    requirement: DOM-04
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Money_RoundHalfUp_rounds_half_away_from_zero_and_never_returns_negative_zero"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Decimal4_rejects_every_other_form"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#Rate_rejects_every_other_form"
        status: pass
    human_judgment: false
  - id: D5
    description: "Every pattern in the model-facing schema (28 paths through $ref and array items) is enforced by the extractor"
    requirement: DOM-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#Every_pattern_in_the_model_facing_schema_is_enforced_by_the_extractor"
        status: pass
    human_judgment: false
  - id: D6
    description: "The shared fixture parses strictly in .NET and validates strictly against the generated Pydantic Invoice"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs#The_shared_fixture_parses_strictly_into_the_v2_invoice"
        status: pass
      - kind: unit
        ref: "python/tests/test_models.py#test_valid_invoice_is_accepted_in_strict_json_mode"
        status: pass
    human_judgment: false
  - id: D7
    description: "Python reads the XML ground truth and grades 27 header, party, totals and count fields; scripted v2 invoices go over real HTTP and are graded end to end"
    requirement: DOM-02
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_corrupted_fields_are_graded_wrong_and_only_that_field"
        status: pass
      - kind: e2e
        ref: "devenv shell -- just e2e"
        status: pass
    human_judgment: false
  - id: D8
    description: "Prompt extract-002 wording lists every v2 field with the DANFE copying rules; whether the live provider accepts the v2 schema (alternation pattern, typed enum, 14-field item array) is confirmed only by plan 02-03"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs#The_contract_is_prompt_version_extract_002_and_the_prompt_names_every_v2_field"
        status: pass
    human_judgment: true
    rationale: "Prompt quality and provider acceptance of the schema are measured by live calls in plan 02-03 (RESEARCH assumptions A1/A2), not by an offline test"

# Metrics
duration: 15min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 01: the full DANFE-visible Invoice crosses the contract chain Summary

**v2 Invoice (items, 11 totals, installments, CNPJ or CPF recipient) from C# records through canonical and model-facing schema and generated Pydantic to the HTTP grader, with Decimal4/Rate wire decimals, half-up rounding, reflection-driven pattern checks and prompt extract-002**

## Performance

- **Duration:** 15 min
- **Started:** 2026-10-08T13:52:18Z
- **Completed:** 2026-10-08T14:07:52Z
- **Tasks:** 3
- **Files modified:** 22 (2 created, 20 modified)

## Accomplishments
- `Invoice` is now the full DANFE-visible target of D-01..D-04: `operation_nature`, `issuer` (`cnpj`, `name`, `ie`, `uf`), `recipient` (`tax_id`, `tax_id_kind`, `name`, `ie`, `uf`), `items` (14 columns), `totals` (11 boxes) and `installments`. Every field is required; only the two `ie` members are nullable. The Phase 1 top-level amount is gone; the total is `totals.invoice_total`.
- The model-facing schema counts exactly 0 optional and 2 union properties (budget 24/16), asserted by `The_v2_invoice_schema_fits_the_structured_output_budget`; `just schema` is reproducible and `just schema-check` is green.
- `Decimal4` (four decimals, no sign) and `Rate` (two decimals, no sign) follow the `Money` discipline (pattern first, invariant `TryParse`, exceptions wrapped); `Money.RoundHalfUp` rounds half away from zero and never returns negative zero.
- `Invoice.PatternViolations()` is a reflection walker emitting dotted snake_case paths with list indices (`items[1].ncm`); a schema-driven drift test walks the model-facing schema through `$ref` and array items and proves all 28 patterned paths are enforced.
- One hand-checked fixture, `data/vectors/valid-invoice.json`, is accepted strictly by .NET and by the generated Pydantic model, and now drives the extractor, endpoint, model and e2e tests.
- Python: `invoice_from_xml` implements the documented XML-to-DANFE mapping (Simples and Normal regime, any ICMS group, IPI, CPF recipient, `ie`, `cobr/dup`); the grader grades 27 fields (`grader-002`, summary version 2); `just check` (all offline gates, including the e2e over real HTTP) is green.

## Task Commits

Each task was committed atomically:

1. **Task 1 (tracer): the shared v2 fixture crosses records, schemas and Pydantic** - `3eacfb5` (feat)
2. **Task 2: .NET consumers, prompt extract-002, budget assertion, spike tool** - `7070a7e` (feat)
3. **Task 3: Python ground truth, 27-field grader, e2e over HTTP** - `35fed2a` (feat)

**Plan metadata:** committed with this SUMMARY (docs: complete plan)

_Note: the tasks are `tdd="true"` but each ships as one atomic commit (tests and implementation together), see Deviations._

## Files Created/Modified
- `dotnet/src/Carimbo.Domain/Invoice.cs` - v2 records, `TaxIdKind`, new `Patterns`, reflection `PatternViolations`
- `dotnet/src/Carimbo.Domain/Wire.cs` - `Decimal4`, `Rate`, their converters, `Money.RoundHalfUp`
- `dotnet/src/Carimbo.Domain/CanonicalSchema.cs` - pattern nodes for `Decimal4` and `Rate`, typed string enums
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` - prompt `extract-002`
- `data/vectors/valid-invoice.json` - the shared hand-checked fixture (textbook CNPJ, NT 2025.001 example, SINTETICA names)
- `schema/invoice.schema.json`, `schema/invoice.model.schema.json`, `python/src/carimbo_models/generated.py` - regenerated with `just schema`
- `python/src/carimbo_evals/ground_truth.py` - `invoice_from_xml`
- `python/src/carimbo_evals/grader.py`, `summary.py` - 27 fields, `GroundTruth` over the wire-form invoice, `grader-002`
- the .NET and Python test files listed in the frontmatter - migrated to the v2 target

## Decisions Made
- Removed the top-level amount without an alias (planner decision D-02, to be recorded as D-22 by plan 02-02).
- Kept `tax_id_kind` free of a `Description` so its schema node is exactly `{type, enum}`; the prompt carries the cpf/cnpj rule.
- Object-typed members carry their `Description` on the hoisted `$defs` entry (the property itself is a bare `$ref`); the descriptions test exempts `$ref` and enum nodes accordingly.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Codegen freshness test broke once schema descriptions exceeded 88 columns**
- **Found during:** Task 3 (`pytest python/tests`)
- **Issue:** `test_generated_models_are_fresh` generated into `tmp_path`, where ruff found no project config and wrapped at the default 88 columns, while the committed `generated.py` is produced inside `python/` at line-length 100. It passed in Phase 1 only because every generated line was short.
- **Fix:** the test copies `python/pyproject.toml` into `tmp_path` so the formatters see the project config.
- **Files modified:** `python/tests/test_models.py`
- **Verification:** `pytest python/tests -q` and `just schema-check` pass
- **Committed in:** `35fed2a`

**2. [Rule 3 - Blocking] `test_datagen.py` read the removed `GroundTruth` attributes**
- **Found during:** Task 3
- **Issue:** `test_xml_ground_truth_reads_back_the_spec` used `truth.issuer_cnpj`, `truth.recipient_cnpj`, `truth.total_amount`, which no longer exist on the v2 `GroundTruth`. The file is not in the plan's `files_modified`.
- **Fix:** switched to `truth.expected("issuer.cnpj")`, `truth.expected("recipient.tax_id")` and `truth.invoice_total`.
- **Files modified:** `python/tests/test_datagen.py`
- **Verification:** `just py-check` and `just datagen-check` pass
- **Committed in:** `35fed2a`

**3. [Rule 3 - Blocking] `carimbo_evals/cli.py` help text named the removed `total_amount` field**
- **Found during:** Task 3
- **Issue:** the `--tolerance` help said "Allowed total_amount difference."; tolerance now applies to every `totals.*` box.
- **Fix:** help text changed to "Allowed totals.* difference."
- **Files modified:** `python/src/carimbo_evals/cli.py`
- **Committed in:** `35fed2a`

**4. [Plan wording] TDD gate shape**
- Each `tdd="true"` task shipped as one commit with tests and implementation together, as the plan's "commit each task atomically" asks. For Tasks 1 and 2 the new API did not exist, so the "red" state was a compile failure rather than an assertion failure (and one real red, `Export_equals_the_committed_schema_file`, before `just schema`). No RED evidence record was produced or run through `check tdd-red-evidence`; the plan type is `execute`, so the plan-level TDD gate does not apply.

**5. [Plan wording] e2e edge case**
- The Phase 1 grouped-access-key edge reply on case-002 was replaced by a lowercase `issuer.cnpj` (case-002 has an alphanumeric issuer CNPJ), as the plan states. The grouped key form is still covered by `ExtractorTests` (`access_key` with spaces).

---

**Total deviations:** 3 auto-fixed (1 Rule 1, 2 Rule 3), 2 plan-wording notes
**Impact on plan:** All fixes were required for a green `just check`; no scope creep.

## Issues Encountered
- A pattern variable named `type` clashed with a local in `CanonicalSchema.TransformSchemaNode` (CS0136); renamed.
- `ruff format python` also formats `generated.py` (only `ruff check` excludes it); it was already in canonical form so nothing changed.

## Known Stubs
None. No hardcoded empty values reach any rendering path; `installments: []` is the legitimate value for a DANFE without a FATURA block.

## Threat Flags
None. No new network endpoint, auth path or trust boundary was added. T-02-01..T-02-04 are mitigated (reflection walker plus drift test; linear patterns; pattern-first wire decimals with JsonException wrapping; synthetic-only fixture). No package was added or changed (`git diff --exit-code main -- python/uv.lock dotnet/Directory.Packages.props` is clean).

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Ready for 02-02: the v2 records, the shared fixture and `invoice_from_xml` are the base for the validators, the vector file and `docs/DANFE-MAPPING.md`; D-22 (the `total_amount` to `totals.invoice_total` change) still needs its record in `docs/DECISIONS.md`.
- Open until plan 02-03 (live): provider acceptance of the alternation `TaxId` pattern, the typed enum and the 14-field item array (RESEARCH A1/A2). `docs/spikes/01-llm-gateway.md` still names `extract-001` as the Phase 1 historical result.
- The skeleton dataset still has no IPI, CPF, `dup` or Normal-regime content; plan 02-05 reworks the datagen (the reader already handles those groups, covered by a synthetic XML test).

## Self-Check: PASSED

- Created files exist: `data/vectors/valid-invoice.json`, `python/src/carimbo_evals/ground_truth.py` and the regenerated schema and model files (checked with `[ -f ]`).
- Task commits `3eacfb5`, `7070a7e`, `35fed2a` are ancestors of HEAD; `gsd check evaluation-scope --plan 02-01 --commits-only` resolves all three.
- Acceptance criteria of all three tasks re-run: PASS.
- Plan verification: `devenv shell -- just check` exits 0 (dotnet-check 278 tests, py-check 190 tests, schema-check, datagen-check, e2e 4 tests, docs-check, secrets-check); `SchemaExport --check` reports up to date.

---
*Phase: 02-validated-extraction*
*Completed: 2026-10-08*
