---
gsd_state_version: "1.0"
current_phase: 03
current_phase_name: Replayable Runs
status: executing
stopped_at: Completed 03-09-PLAN.md
last_updated: "2026-10-15T09:04:20.216Z"
last_activity: 2026-10-15
last_activity_desc: Phase 03 execution started
state_head: 2fe9a1d58c8f0066f4d77cdbdafdc18bf2992856
progress:
  total_phases: 7
  completed_phases: 2
  total_plans: 39
  completed_plans: 38
  percent: 29
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-10-08)

**Core value:** Measured quality: a reproducible eval suite that runs the real pipeline, compares runs, fails CI on regressions and publishes a results table.
**Current focus:** Phase 03 — Replayable Runs

## Current Position

Phase: 03 (Replayable Runs) — EXECUTING
Plan: 10 of 10
Status: Ready to execute
Last activity: 2026-10-15 — Phase 03 execution started

Progress: [███░░░░░░░] 29%

## Performance Metrics

**Velocity:**
- Total plans completed: 29
- Average duration: -
- Total execution time: 0.0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01 | 16 | - | - |
| 02 | 13 | - | - |

**Recent Trend:**
- Last 5 plans: -
- Trend: -

*Updated after each plan completion*
**Per-Plan Metrics:**

| Plan | Duration | Tasks | Files |
|------|----------|-------|-------|
| Phase 01 P02 | 3 min | 2 tasks | 9 files |
| Phase 01 P01 | multi-session | 3 tasks | 9 files |
| Phase 01 P03 | 9 min | 1 tasks | 10 files |
| Phase 01 P04 | 5min | 3 tasks | 9 files |
| Phase 01 P06 | 25min | 2 tasks | 6 files |
| Phase 01 P05 | 6 min | 2 tasks | 9 files |
| Phase 01 P07 | 4 min | 2 tasks | 9 files |
| Phase 01 P08 | 7 min | 2 tasks | 6 files |
| Phase 01 P09 | 11 min | 3 tasks | 10 files |
| Phase 01 P10 | multi-session | 3 tasks | 6 files |
| Phase 01 P11 | 4 min | 2 tasks | 9 files |
| Phase 01 P12 | 15min | 2 tasks | 7 files |
| Phase 1 P13 | 13 min | 3 tasks | 10 files |
| Phase 01 P14 | 6 min | 3 tasks | 6 files |
| Phase 01 P15 | 6 min | 3 tasks | 5 files |
| Phase 01 P16 | 3 min | 2 tasks | 3 files |
| Phase 02 P01 | 15 min | 3 tasks | 22 files |
| Phase 02 P02 | 7 min | 2 tasks | 8 files |
| Phase 02 P03 | 35 min | 3 tasks | 11 files |
| Phase 02 P04 | 11 min | 3 tasks | 12 files |
| Phase 02 P05 | 11 min | 3 tasks | 15 files |
| Phase 02 P06 | 5 min | 2 tasks | 3 files |
| Phase 02 P07 | 12 min | 2 tasks | 8 files |
| Phase 02 P08 | 8 min | 3 tasks | 10 files |
| Phase 02 P09 | 25 min | 3 tasks | 9 files |
| Phase 02 P10 | 25min | 2 tasks | 6 files |
| Phase 02 P11 | 15 min | 3 tasks | 11 files |
| Phase 02 P12 | 5 min | 3 tasks | 8 files |
| Phase 02 P13 | 5 min | 3 tasks | 10 files |
| Phase 03 P01 | 25 min | 2 tasks | 7 files |
| Phase 03 P02 | 4 min | 2 tasks | 6 files |
| Phase 03 P03 | 5 min | 3 tasks | 7 files |
| Phase 03 P04 | 8 min | 3 tasks | 6 files |
| Phase 03 P05 | 5min | 2 tasks | 6 files |
| Phase 03 P06 | 7 min | 2 tasks | 9 files |
| Phase 03 P07 | 11 min | 2 tasks | 9 files |
| Phase 03 P08 | 12 min | 3 tasks | 9 files |
| Phase 03 P09 | 9 min | 3 tasks | 20 files |

## Accumulated Context

### Decisions

Decisions are logged in the PROJECT.md Key Decisions table and in docs/DECISIONS.md.
Recent decisions affecting current work:

- [Roadmap]: The roadmap covers Milestone 1 only. Phases are vertical MVP slices. Phase 1 is a walking skeleton: a few generated cases, live extraction through the eval endpoint, offline grading and a run summary. Breadth comes after.
- [Roadmap]: The D-03, D-07 and D-15 refinements are accepted. Phase 1 records them in docs/DECISIONS.md as superseding entries (RES-03).
- [Roadmap]: The full validators land in Phase 2, before Phase 4 checks dataset ground truth against them. The gateway spike (LLM-06) runs first in Phase 1.
- [Phase 01]: global.json lives at repo root so the SDK pin and Microsoft.Testing.Platform opt-in apply to every command run from the root
- [Phase 01]: Money.Parse validates the invariant two-decimal pattern before decimal.Parse, so pt-BR 12,34 is rejected; Wire.Options is built explicitly (never the Web defaults preset)
- [Phase 01]: 01-01: Python supply chain approved as committed (httpx2 2.13.1, brazilfiscalreport 1.2.0, hatchling 1.32.4); no replacements
- [Phase 01]: 01-01: Python tooling runs via nix shell nixpkgs#uv nixpkgs#python312 with UV_PYTHON_DOWNLOADS=never; zxingcpp import needs libstdc++ on LD_LIBRARY_PATH (devenv follow-up)
- [Phase 01]: Eval endpoint authenticates before body binding; handler typed Func<HttpContext, Task<IResult>> (ASP0016: lone HttpContext lambda becomes a RequestDelegate that drops the IResult)
- [Phase 01]: outcome.failure is populated for every non-success outcome (refused detail, truncation, schema parse error, infrastructure failure); schema_invalid is graded wrong on all graded fields, other typed failures never count as wrong answers
- [Phase 01]: 01-04: Canonical schema exporter builds the Money node before any object guard and hoists titled objects into $defs with fresh nodes; SchemaExport --check and the snapshot test byte-compare the committed schema
- [Phase 01]: 01-04: DOM-08 left open; only the canonical-schema staleness layer is done, generated Pydantic staleness belongs to 01-09/01-13
- [Phase 01]: 01-06: MULTIPAGE_ITEMS stays 80 (case-003 renders 2 pages, 50 items already overflow); party names use diacritic business words plus the SINTETICA marker; cMun is a documented UF-code placeholder
- [Phase 01]: 01-05: title, description, pattern and format:date stay in the model-facing schema; live API acceptance is assumed until the 01-10 spike (fallback owned by 01-12)
- [Phase 01]: 01-05: projector rejects dangling/non-local $ref, recursive $defs and allOf with $ref; budget counts reachable definitions per $ref use
- [Phase 01]: 01-07: carimbo-datagen check regenerates all three cases and compares the whole directory, so extra or missing files fail it; byte identity on a uv-managed interpreter (A9) stays unverified until the first CI py-check run
- [Phase 01]: 01-08: IInvoiceExtractor is registered by factory so a Development host without an ILlmGateway starts; the eval route stays unmapped (404) instead of the host crashing on build-time DI validation
- [Phase 01]: 01-08: eval endpoint 400s use ValidationProblem keyed by field and never echo the payload; the 10 MB cap is RequestSizeLimit metadata plus an explicit Content-Length 413 check; typed failures stay HTTP 200
- [Phase 01]: 01-09: unpriced cases are charged at reserve_usd against the cost cap but excluded from spent_usd and reported in unpriced_cases — spent_usd stays only money the endpoint priced; the cap stays conservative
- [Phase 01]: 01-09: a non-empty cases.jsonl without --resume is refused (exit 2); resume repairs a torn last line and counts prior spend against the cap — a case can never get a second record by accident
- [Phase 01]: 01-09: grader field names are dotted (issuer.cnpj); schema validity is graded separately as schema_valid_jsonschema and schema_valid_pydantic; grade_run returns the summary and write_summary writes the files — matches the D-04 field list; typed failures carry no field grades
- [Phase 01]: [Plan 01-10] Gateway bottom adapter is direct-sdk (direct Anthropic SDK behind ILlmGateway); SDK MaxRetries = 0 in Phase 1, Phase 3 (LLM-01) owns the single retry policy; send the model alias in development, record model_requested and model_returned, pin the dated snapshot (claude-haiku-4-5-20251001) for published eval runs. Spike spend US$0.2551 of the US$5 cap. D-21 is written in 01-12.
- [Phase 01]: 01-11: cost is priced by a CostAccountingLlmGateway decorator over the versioned pricing.json (decimal only, 5 token classes, alias map); unknown models give null cost plus unpriced_model warning, never zero — Applied by CarimboApi.CreateApp to whichever ILlmGateway is registered so scripted, stub and real gateways price identically; effective.pricing_version always comes from the table
- [Phase 01]: D-21: direct Anthropic SDK behind ILlmGateway, MaxRetries 0 in Phase 1, Phase 3 (LLM-01) owns the single retry policy, alias in development and dated snapshot pinned for published runs — docs/spikes/01-llm-gateway.md: IChatClient path not lossless on usage or stop details; live API accepted the schema unchanged; smoke case-001 success for USD 0.0042, phase spend USD 0.2593 of 5
- [Phase 1]: 01-13: secrets-check matches real key shapes (sk-ant-<kind><NN>-<8+ chars) — The broader sk-ant-<8 chars> pattern flagged the synthetic word in a 01-12 test string
- [Phase 1]: 01-13: devenv.lock committed; devenv.nix adds Node (pyright) and LD_LIBRARY_PATH (libstdc++) for NixOS — Found by running devenv shell -- just check on NixOS-WSL
- [Phase 01]: 01-14: WR-03 resolved by enforcement. Invoice.PatternViolations() checks access_key, issuer.cnpj and recipient.cnpj against the Patterns constants the schema is exported from; no check digits (D-03), Domain stays BCL-only.
- [Phase 01]: 01-14: InvoiceExtractor catches only JsonException, FormatException and OverflowException; no catch-all, so Carimbo bugs surface as 5xx and cancellation propagates.
- [Phase 01]: 01-14: Patterns are matched over the full value length because the .NET $ anchor accepts a trailing newline that JSON Schema (ECMA-262) rejects.
- [Phase 01]: 01-15: WR-02 resolved by charging. may_have_reached_provider charges reserve_usd for every harness error unless provably pre-provider (HTTP 400/401/404/413/415, or ConnectError/ConnectTimeout/PoolTimeout), live and for every prior record on resume; assumed_usd is reported apart from spent_usd — The cost cap (D-09) must bound money that may have been paid, and a 5xx or timeout can follow a provider call; older records without http.error_type stay charged
- [Phase 01]: 01-15: grader splits cases.jsonl on newline only; resume validates every line before the torn-tail truncation and raises CorruptRunError (ValueError subclass) mapped to CLI exit 2 — ensure_ascii=False leaves U+2028/U+2029/U+0085 raw in strings, and the CLI exit codes 0/2/3/4 must hold on a damaged run directory
- [Phase 01]: 01-16: class-level responses_dir fixture override feeds the module host fixture; ledger Source cells cite fix commits (plan id then shas)
- [Phase 02]: Invoice total lives only at totals.invoice_total; the Phase 1 top-level total_amount is removed with no alias (D-22, recorded by 02-02)
- [Phase 02]: Recipient is a separate record from Party: tax_id plus tax_id_kind (cnpj|cpf); the issuer stays CNPJ-only
- [Phase 02]: D-22 to D-24 recorded (02-02): Invoice v2 target, validation and bounded repair, eval contract 2 — Fixes the target, repair semantics and eval contract before the code that implements them
- [Phase 02]: 02-02: validator-vectors.json is the shared cross-stack oracle; all-identical CNPJ and CPF are rejected by project rule; sum_tolerance is an object with cap_cases and within arrays — Expected values come from published examples and hand computation, never the code under test
- [Phase 02]: 02-03: provider accepted the committed v2 model-facing schema as is; no projector fallback and no D-25
- [Phase 02]: 02-03: one-page Haiku requests reuse the cached prefix (schema and prompt count toward the 4096-token minimum); price cache reads for repairs
- [Phase 02]: 02-04: recipient identifier rules are routed by identifier shape (CNPJ pattern, CPF pattern, else declared kind's FORMAT rule); KEY_* findings sit on access_key with Expected from the extracted fields and Actual from the key
- [Phase 02]: 02-04: validators are total; SafeMath scopes catch (OverflowException) to one decimal operation and the check becomes ARITH_OVERFLOW; no top-level catch
- [Phase 02]: 02-05: PartySpec.cnpj renamed tax_id (case-003 recipient is a CPF); totals, freight, discount and installments computed in CaseSpec so XML cannot drift from the validator formulas; manifest expected blocks come from the spec, never from the XML
- [Phase 02]: 02-06: validation_failed candidates are graded (field and schema denominators) and flagged caught, counted separately from success; rule_counts count cases per rule id
- [Phase 02]: 02-07: NfeXmlMapper lives in the src project Carimbo.GroundTruth (BCL-only, Domain reference) so Phase 4 reuses the D-17 gate; access key is Id minus the NFe prefix only, no normalisation
- [Phase 02]: 02-08: reference_date bound as JsonElement? with strict yyyy-MM-dd parsing so any wrongly typed value is a 400 keyed reference_date
- [Phase 02]: 02-08: validation_failed returns outcome.failure null with the candidate in outcome.invoice; top-level cost is null (warning from the first unpriced answered attempt) never a partial sum
- [Phase 02]: 02-09: repair feedback reveals expected/actual only for the 13 arithmetic and date rules, and only when validator-shaped; identifier, check-digit and key rules get fixed sentences
- [Phase 02]: 02-09: Extraction:MaxRepairs bounded 0..5 at startup (default 2), MaxTokens default 16000, provider timeout default 300 s per attempt
- [Phase 02]: 02-10: skeleton repair budget is Api configuration (Extraction__MaxRepairs via just skeleton [max_cost] [max_repairs]); live Haiku 4.5 runs repaired 0 cases, so the case-002 access-key misread is a finding for Phase 3/6
- [Phase 02]: Null list elements are rejected at the parse boundary by Invoice.NullViolations (Domain reflection walker) as schema_invalid; the validator entry guard returns NULL_VALUE errors as defence in depth (closes CR-01). — System.Text.Json accepts null collection elements that the schema forbids; the verifier reproduced a NullReferenceException and HTTP 500 that lost paid attempts.
- [Phase 02]: D-25: parse boundary accepts only what the committed schema allows (NULL_VALUE, null element rejection, exact-name enums via StrictEnumJsonConverter) — Success must mean schema-valid so .NET outcomes and Python schema_valid grades agree
- [Phase 02]: Prompt extract-003 and the model-facing schema descriptions quote the labels the skeleton DANFEs print; each prompt text is pinned by SHA-256 to its version (02-13) — G-02-1 and G-02-2: the model reads both prompt and schema descriptions, so both were corrected (description-only); extract-003 is unmeasured live until the next paid run.
- [Roadmap]: D-26 (2026-10-15): a thin MCP-plus-agent slice joins Milestone 1 as Phase 3.1, after Phase 3 (Replayable Runs); Phases 4-6 are scoped down when each is discussed; requirements AGT-01 to AGT-06. The phase is inserted separately with /gsd-phase --insert 3.
- [Phase 03]: 03-01: cache hit replays original latency/usage/cost but reports HttpAttempts 0; only end_turn is cached; cache write failure returns the paid answer with Stored false
- [Phase 03]: D-27 to D-32 recorded in docs/DECISIONS.md: response cache on by default in Development and Eval (D-27), SDK sole retry owner with MaxRetries 2 (D-28), eval contract 3 (D-29), prompt catalog (D-30), model-call spans with AlwaysOn sampler (D-31), golden eval-response fixtures (D-32) — Fix the Phase 3 contract before the implementing plans run; docs-check enforces D-18 to D-32
- [Phase 03]: 03-03: response cache registered once in Program.cs ResolveCache; Development/Eval default read_write at .cache/llm under the content root, any cache config elsewhere fails startup (even Directory with Mode off)
- [Phase 03]: 03-03: scripted host reports HttpAttempts 1 per call so a cache replay (0) is distinguishable; TestHost seeds the cache off unless a test opts in with its own directory or mode
- [Phase 03]: 03-04: a replicate salt is accepted on a host without a cache and still echoed; only cache_mode other than off needs a host cache
- [Phase 03]: 03-04: resume compares request.overrides of every existing record with the current flags (missing, null and empty all mean none) and refuses a mismatch with exit 2
- [Phase 03]: D-28 applied (03-05): the Anthropic SDK is the single retry owner, MaxRetries default 2 bounded 0..5, enforced by a static guard against resilience packages and extra DelegatingHandlers
- [Phase 03]: 03-06: top-level incurred_cost_usd is null when any attempt's incurred is unknown (a provider failure after an answered attempt included), so the runner charges the reserve
- [Phase 03]: 03-06: money in run.json, the CLI run line and summary totals is rendered with format(x, 'f') so a scaled zero never prints as 0E-8
- [Phase 03]: 03-07: AlwaysOnSampler set in code makes every eval response trace_id name a recorded trace; OTEL_TRACES_SAMPLER is ineffective by design, export only when OTEL_EXPORTER_OTLP_ENDPOINT is set
- [Phase 03]: 03-07: TracingLlmGateway is outermost, so a cache hit and the miss that stored it are two spans (hit has http_attempts 0)
- [Phase 03]: 03-08: overrides.model is validated with LlmPricingTable.CanPrice (aliases accepted); overrides.max_repairs accepts only a JSON integer 0..5 (null counts as absent); ExtractionSettings.MaxRepairsLimit=5 is shared by startup, endpoint and extractor — Keeps every override's spend priced and capped (T-03-21, T-03-23) and the 400 field-specific without echoing the value
- [Phase 03]: 03-08: PromptCatalog holds extract-002 (restored byte-exact) and extract-003 (default), SHA-pinned in ExtractorTests; the CLI leaves model and prompt-version validation to the endpoint — D-30: a real second prompt keeps the prompt_version override, the cache key and comparisons honest; flagged assumption A4 for verification
- [Phase 03]: The golden scenarios use their own traceparent trace id, because TracingWiringTests counts exported spans per trace id and test classes run in parallel
- [Phase 03]: Golden update mode deletes only files outside the scenario set; the exact file-set assertion runs in compare mode only, so test order cannot fail a first regeneration
- [Phase 03]: Golden JSON is rendered with the relaxed encoder, two-space indent, LF, one trailing newline and no BOM so accents stay readable in review

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 6]: The model pair, CI subset size and thresholds are still open. Calibrate them from the first baseline run.
- [Phase 2]: Code review warnings WR-02 to WR-06 and info items IN-01 to IN-08 stay open (02-REVIEW-DISPOSITION.md); none blocked verification. WR-02 (recipient UF "EX" for exports flagged UF_UNKNOWN) and WR-06 (schema-check cannot see new or staged artifacts) are the ones most likely to matter later.
- [Phase 2]: The 02-13 re-review (02-REVIEW.md, now scoped to the 02-13 delta) adds WR-01 (test_danfe_labels totals-label substring checks cannot fail for VALOR DO ICMS, BASE DE CÁLCULO DO ICMS, DESCONTO) and WR-02 (the recipient-name rule truncates a name containing " - "). Also: RepairFeedback REGIME_CODE_MISMATCH still says "CST column" (a fix needs a new repair-prompt version), and extract-003 is unmeasured live until the next paid run.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 261005-nle | Document local secretspec setup for the Anthropic API key | 2026-10-05 | d44f31a | [261005-nle-document-local-secretspec-setup-for-the-](./quick/261005-nle-document-local-secretspec-setup-for-the-/) |
| 261009-0r6 | Refresh README for the state after Phase 2 and show the measured results so far | 2026-10-09 | 6d5263e | [261009-0r6-refresh-readme-for-the-state-after-phase](./quick/261009-0r6-refresh-readme-for-the-state-after-phase/) |
| 261015-0u0 | Record the Milestone 1 agent-slice decision (D-26, PROJECT, REQUIREMENTS, close todo) | 2026-10-15 | 557bb8b | [261015-0u0-record-the-milestone-1-agent-slice-decis](./quick/261015-0u0-record-the-milestone-1-agent-slice-decis/) |

### Roadmap Evolution

- Phase 3.1 inserted after Phase 3: Thin Agent Slice: read-only MCP tools, hand-written agent loop, typed approve/reject/escalate Decision, graded decision cases (D-26, AGT-01..AGT-06); Phase 3 stays next

## Deferred Items

Items acknowledged and deferred at milestone close, most recent first:

| Category | Item | Status | Deferred At | Milestone |
|----------|------|--------|-------------|-----------|
| *(none)* | | | | |

## Session Continuity

Last session: 2026-10-15T09:04:20.108Z
Stopped at: Completed 03-09-PLAN.md
Resume file: None
