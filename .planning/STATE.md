---
gsd_state_version: "1.0"
current_phase: 01
current_phase_name: Walking Skeleton
status: verifying
stopped_at: "Completed 01-13-PLAN.md (human checks pending: first PR CI run, Claude Code/OpenCode discovery)"
last_updated: "2026-10-08T00:14:41.276Z"
last_activity: 2026-10-05
last_activity_desc: Phase 01 execution started
state_head: 0af93f4cb45970345344d98c4a676b476e465da3
progress:
  total_phases: 6
  completed_phases: 0
  total_plans: 13
  completed_plans: 13
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-10-03)

**Core value:** Measured quality: a reproducible eval suite that runs the real pipeline, compares runs, fails CI on regressions and publishes a results table.
**Current focus:** Phase 01 — Walking Skeleton

## Current Position

Phase: 01 (Walking Skeleton) — EXECUTING
Plan: 13 of 13
Status: Phase complete — ready for verification
Last activity: 2026-10-05 — Phase 01 execution started

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: -
- Total execution time: 0.0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

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

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 1]: The live gateway spike (LLM-06) and the walking skeleton need `CARIMBO_ANTHROPIC_API_KEY` (locally via `secretspec run` or an export, see D-10) and a small API budget. Locally the key is stored with secretspec.
- [Phase 1]: It is still unconfirmed which package `httpx2` is; confirm before building the Python runner.
- [Phase 6]: The model pair, CI subset size and thresholds are still open. Calibrate them from the first baseline run.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 261005-nle | Document local secretspec setup for the Anthropic API key | 2026-10-05 | d44f31a | [261005-nle-document-local-secretspec-setup-for-the-](./quick/261005-nle-document-local-secretspec-setup-for-the-/) |

## Deferred Items

Items acknowledged and deferred at milestone close, most recent first:

| Category | Item | Status | Deferred At | Milestone |
|----------|------|--------|-------------|-----------|
| *(none)* | | | | |

## Session Continuity

Last session: 2026-10-08T00:14:41.247Z
Stopped at: Completed 01-13-PLAN.md (human checks pending: first PR CI run, Claude Code/OpenCode discovery)
Resume file: None
