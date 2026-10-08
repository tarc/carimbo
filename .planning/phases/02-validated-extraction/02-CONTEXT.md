# Phase 2: Validated Extraction - Context

**Gathered:** 2026-10-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 2 widens the Phase 1 loop. Every extraction targets the full DANFE-visible `Invoice`. Deterministic .NET validators return structured errors and never throw. A bounded repair loop feeds those errors back to the model. `POST /eval/extractions` reports the validator outcomes and every attempt. The phase ends with a live run of the reworked skeleton cases, graded by the extended Python grader.

Out of this phase: the response cache, the retry policy, per-request overrides (model, prompt version, max repairs, cache mode, salt) and golden contract fixtures, which all belong to Phase 3. Dataset breadth and degradation belong to Phase 4. Line-item assignment grading, confidence intervals, the silent-error rate and the taxonomy belong to Phase 5.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked earlier, not re-discussed)
- Wire conventions: snake_case, string enums, money as `^-?[0-9]+\.[0-9]{2}$` strings, `decimal`/`Decimal` in code. Fields are additive.
- `Patterns.AccessKey` (`^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$`) and `Patterns.Cnpj` (`^[A-Z0-9]{12}[0-9]{2}$`) already admit the alphanumeric form. Phase 2 adds check-digit validation (VAL-02/03).
- The model-facing schema must stay within ≤24 optional and ≤16 union properties. `SchemaBudget.EnsureWithin` enforces this, and every new field must fit.
- Structured outputs only. No `tool_choice`, `Temperature`/`TopP` or prefill.
- D-21: direct Anthropic SDK behind `ILlmGateway`, `MaxRetries` 0. Phase 3 owns the retry policy.
- Typed failures (refused, truncated, schema_invalid, infrastructure) are never wrong answers.
- `Domain` stays BCL-only (D-04). The validators may live in a new `Carimbo.Validation` project or in `Domain` if they stay pure. Placement is the planner's choice.

### Target breadth (DOM-02)
- **D-01:** Line items carry the full DANFE item row: `code`, `description`, `ncm`, `cst_csosn`, `cfop`, `unit`, `quantity`, `unit_price`, `total`, `icms_base`, `icms_rate`, `icms_amount`, `ipi_rate`, `ipi_amount`. When a tax column is blank on the DANFE, the model returns `"0.00"`, so the fields stay required and spend no union budget. The planner verifies how BrazilFiscalReport renders blank or zero tax columns and writes the rule into the prompt. — **Reversibility:** costly — committed schema, generated Pydantic models, prompt and grader all follow the field set; renames break stored runs.
- **D-02:** The totals block (CÁLCULO DO IMPOSTO) is fully required: `icms_base`, `icms_amount`, `icms_st_base`, `icms_st_amount`, `products_total`, `freight`, `insurance`, `discount`, `other_expenses`, `ipi_amount`, `invoice_total`. The DANFE prints 0,00 for absent totals. `total_amount` from Phase 1 becomes `invoice_total` inside the totals record, or stays as an alias. The planner decides, and the change must be recorded.
- **D-03:** `Party` gains `ie` (state registration, nullable for exempt or individual) and `uf`. The recipient identifier becomes `tax_id` plus `tax_id_kind` (string enum `cnpj` | `cpf`). The issuer stays CNPJ-only. A CPF check-digit rule joins the validators, and CPF vectors join the vector file. The street address is not in the target.
- **D-04:** The target adds `operation_nature` (natureza da operação) and an `installments` list (fatura/duplicatas: `number`, `due_date`, `amount`), required and possibly empty. Transport (transportador/volumes) and complementary info are not in the target.
- **D-05:** The XML→DANFE field mapping is documented (DOM-02) in a committed doc and implemented once in .NET (XML → `Invoice`) for the ground-truth gate (D-14). The Python grader's XML reader follows the same documented mapping.

### Regimes and tax rules (VAL-04)
- **D-06:** Rules apply to Simples Nacional CSOSN 101/102/103/300/400/500/900 and Regime Normal CST 00/20/40/41/50/60/90:
  - `icms_amount = icms_base × icms_rate`, with the reduced base for CST 20.
  - Exempt or non-taxed codes (40/41/50, and CSOSN 102/103/300/400) carry no ICMS amount.
  - CSOSN 101 credit is not tax due.
  - `ipi_amount = base × ipi_rate`.
  - ST codes 10/30/70, deferral 51 and any other code produce a *warning* (`TAX_CODE_UNSUPPORTED`), never an error.
- **D-07:** The regime is inferred, not extracted. A 3-digit `cst_csosn` (origin + CST) means Normal, and a 4-digit one (origin + CSOSN) means Simples. Mixed families on one invoice are an error (`REGIME_CODE_MISMATCH`). No `tax_regime` field exists in the target.
- **D-08:** Arithmetic tolerance is ±R$0.01 for each single computed value (qty × unit_price, base × rate). Sums over n items allow ±R$0.01 × n, capped at R$1.00. Totals checks cover the item sums vs the totals block and the `vNF` formula. One configurable tolerance setting covers all of it, and the vector file also exposes it. The planner verifies the `vNF` formula against the current MOC (ROADMAP planning note).
- **D-09:** The vector file (VAL-06) is hand-curated JSON. xUnit runs all of it against the .NET validators. pytest runs the parts Python already owns: the datagen generators (CNPJ, CPF, access-key check digits, numeric and alphanumeric) and the evals money/rounding module (half-up). Python never reimplements the cross-field, totals or tax validators.

### Validator output (VAL-01)
- **D-10:** Each finding is `{field, rule_id, expected, actual, severity}`. Severity has two levels, `error` and `warning`. Rule IDs are stable, UPPER_SNAKE strings (eval artifacts will reference them). Research FEATURES.md has a starting catalogue (`KEY_CHECK_DIGIT`, `TOTAL_SUM_*`, `REGIME_CODE_MISMATCH`, `DATE_PLAUSIBLE`, `DUP_SUM`, …). — **Reversibility:** costly — rule IDs end up in committed summaries and per-case tables from Phase 5 on.
- **D-11:** Date plausibility uses an injected `TimeProvider` / reference date. Production uses the wall clock. The eval endpoint takes an optional reference date in the request, defaulting to the dataset's as-of date (a manifest field), and echoes it in the effective config. Rules: issue date ≤ reference + 1 day, issue date ≥ 2006-04-01, installment due dates ≥ issue date. This is a request field needed for reproducible validation, not one of the Phase 3 API-02 overrides.

### Repair loop (EXT-03, EXT-04)
- **D-12:** Only `error` findings trigger repair. Warnings are reported and never repaired.
- **D-13:** A repair attempt continues the conversation. It sends the original request, the previous assistant output and a user turn listing the structured errors, under a repair prompt that forbids inventing values: re-read the document, and if a value really is printed that way, return it unchanged. The repair prompt has its own version string. A `cache_control` breakpoint on the PDF block lets repairs read it from the prompt cache. The planner confirms that `ILlmGateway` can express multi-turn plus cache_control. (Phase 1 checked cache-token usage fields in the spike.)
- **D-14 (exhaustion):** When `max_repairs` (default 2, from configuration `Extraction:MaxRepairs`, not per request) is spent and errors remain, the outcome is a new typed status `validation_failed`. It carries the last schema-valid candidate invoice and its remaining findings. Production treats it as failure. Evals grade the candidate's fields and count it as "caught" (input to EVAL-06 later). Success means schema-valid with zero `error` findings, and success may carry warnings. — **Reversibility:** costly — outcome statuses are on the wire and in grading logic.
- **D-15:** Every attempt is recorded and returned: output (raw and parsed), validator findings, tokens, cost, latency, attempt kind (`initial` | `repair`), and prompt version. The response totals sum over the attempts.

### Fixtures and live proof
- **D-16:** The 3 skeleton cases are reworked in place:
  - case-001: Simples, CSOSN 101 with credit.
  - case-002: Regime Normal, CST 00/20 + IPI, with freight, discount and 2 installments.
  - case-003: the multi-page case, with an alphanumeric issuer CNPJ and a CPF recipient.

  Datagen grows only what these need (Normal-regime tax groups, IPI, fatura/dup, alphanumeric CNPJ, CPF, a hybrid Code 128 for the alphanumeric key). Byte-identical regeneration (`just datagen-check`) still holds. The committed PDF bytes change, which is acceptable because the Phase 3 cache does not exist yet. — **Reversibility:** costly — once Phase 3 keys the cache on PDF SHA-256, changing these bytes again invalidates fixtures.
- **D-17:** Ground-truth gate: an xUnit test maps each committed skeleton XML to `Invoice` (D-05 mapping) and asserts zero validator `error` findings, so the mapping and the validators agree before any model is involved. Phase 4 reuses the test at scale.
- **D-18:** The Python grader is extended to header + totals + counts. It grades the new scalar fields (party `ie`/`uf`/`tax_id_kind`, `operation_nature`, all totals with tolerance), plus `item_count` and `installment_count`, and records the validator outcome and attempt count per case. Per-item matching waits for Phase 5 (optimal assignment, no positional grading now).
- **D-19:** The live proof is `just skeleton` on `claude-haiku-4-5` with `max_repairs` 2, plus one run with `max_repairs` 0 as a first look at what repair buys. Max repairs is set through configuration (env var), because per-request overrides are Phase 3. Caps: US$1.00 per run, US$5 for the phase. Scripted-model tests stay the CI proof: first-try success, successful repair, budget exhaustion.

### Claude's Discretion
- The full rule-ID catalogue and naming beyond the examples above, and the exact field names within D-01..D-04 (keep snake_case).
- Validator project placement, rule composition and how tolerance config is bound.
- The exact attempt record and response shape for D-15, provided it holds everything EXT-04/API-01 list.
- Prompt wording for extraction v2 and repair v1. Bump `PromptVersion` (`extract-002` or similar).
- Vector file location (e.g. `data/vectors/` or `schema/`) and format.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project decisions and requirements
- `docs/DECISIONS.md` — D-04 (pure Domain), D-05 (deterministic before probabilistic), D-06 (structured errors, bounded repair), D-18 (canonical vs model-facing schema), D-21 (gateway shape, retry ownership)
- `.planning/REQUIREMENTS.md` — DOM-02..04, VAL-01..06, EXT-03, EXT-04, API-01
- `.planning/ROADMAP.md` — Phase 2 success criteria and planning note (verify DANFE printed fields, `vNF` formula and tolerances against the current MOC; alphanumeric CNPJ against NT 2025.001)

### Domain research
- `.planning/research/FEATURES.md` — DANFE-visible fields table, access-key cDV algorithm (ASCII−48 mod 11), tax groups and regimes, `vNF` formula, validator rule catalogue
- `.planning/research/PITFALLS.md` — rounding, alphanumeric CNPJ, repair loops that teach the model to cheat
- `.planning/research/SUMMARY.md` — critical pitfalls 1 (alphanumeric CNPJ) and 2 (money/rounding)
- `.planning/research/STACK.md` — structured-output schema limits

### Phase 1 artifacts
- `.planning/phases/01-walking-skeleton/01-CONTEXT.md` — Phase 1 decisions (Invoice subset, skeleton cases, live budget)
- `docs/spikes/01-llm-gateway.md` — live API shapes, usage fields and finish reasons from the gateway spike

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `dotnet/src/Carimbo.Domain/Invoice.cs`: `Patterns` (explicit `[0-9]` classes, `IsFullMatch` against the .NET `$` newline quirk) and `Invoice.PatternViolations()`. Extend both for new pattern fields (CPF, UF, NCM, CFOP).
- `dotnet/src/Carimbo.Domain/Wire.cs`: `Money` with `MoneyJsonConverter` and `Wire.Options`. Half-up rounding helpers go next to it.
- `dotnet/src/Carimbo.Domain/CanonicalSchema.cs`: hoists titled objects into `$defs`, so new records (`LineItem`, `Totals`, `Installment`) get titles and must not collide.
- `dotnet/src/Carimbo.Extraction/ModelSchemaProjector.cs` + `SchemaBudget.cs`: the budget gate for the wider schema.
- `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs`: `ExtractionContract` (prompt version, schema SHA) and the `ExtractionOutcome` union, which gains `ValidationFailed` and the repair loop.
- `dotnet/src/Carimbo.Llm/LlmPricing.cs` + the cost-accounting decorator: each attempt is priced the same way.
- `dotnet/tests/Carimbo.ScriptedHost`: the scripted-model host for repair-loop e2e tests.
- `python/src/carimbo_datagen/ids.py`: `make_cnpj` and `make_access_key`. Extend for alphanumeric CNPJ and CPF, and test against the vector file.
- `python/src/carimbo_evals/money.py` and `grader.py`: rounding and field grading to extend (D-18).

### Established Patterns
- Generated artifacts are never hand-edited. Run `just schema` after any record change and `just datagen` after spec changes. `schema-check` and `datagen-check` gate CI.
- Typed failures are HTTP 200 with `outcome.status` plus `outcome.failure`, and `InvoiceExtractor` catches only parse exceptions (no catch-all).
- Datagen is Simples-only today (CRT 1, CSOSN 102, `PISNT`/`COFINSNT`, zeroed `ICMSTot`, `modFrete` 9, no `cobr`).

### Integration Points
- `dotnet/src/Carimbo.Api/EvalEndpoint.cs`: `EvalResponse`/`EvalOutcome` gain attempts, validator findings, the `validation_failed` status and an optional reference date. `EvalEffective` echoes max_repairs, the repair prompt version and the reference date.
- `python/src/carimbo_evals/runner.py`: the JSONL record stores the wider response unchanged (raw output per attempt).
- `data/skeleton/manifest.json`: gains the dataset as-of date (D-11).

</code_context>

<specifics>
## Specific Ideas

- The repair prompt must make "the value really is printed that way" a legitimate answer. A DANFE can be genuinely inconsistent, and repair must never bend true values to silence a validator.
- The `max_repairs` 0 vs 2 comparison on the skeleton is a preview of the Phase 6 repair ablation (research FEATURES.md differentiator).
- The `validation_failed` candidate is what later separates "wrong and caught" from "wrong and silent" (EVAL-06).

</specifics>

<deferred>
## Deferred Ideas

- ST arithmetic (CST 10/30/70: MVA, pICMSST) and deferral (51) rules. They give warnings now and could be revisited once Phase 4 adds ST-heavy cases.
- Transport block and complementary info (infCpl) as extraction fields.
- Per-item positional grading. Not planned: Phase 5 does optimal-assignment matching.
- Barcode-first access key at intake (D-05 bullet). Not in Phase 2 requirements.

</deferred>

---

*Phase: 02-validated-extraction*
*Context gathered: 2026-10-08*
