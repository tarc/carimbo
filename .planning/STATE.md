---
gsd_state_version: "1.0"
current_phase: 01
current_phase_name: Walking Skeleton
status: executing
stopped_at: Completed 01-05-PLAN.md
last_updated: "2026-10-05T18:25:18.432Z"
last_activity: 2026-10-05
last_activity_desc: Phase 01 execution started
state_head: b0ce22f520082c7c734c897cae578a12dbf38b0e
progress:
  total_phases: 6
  completed_phases: 0
  total_plans: 13
  completed_plans: 6
  percent: 0
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-10-03)

**Core value:** Measured quality: a reproducible eval suite that runs the real pipeline, compares runs, fails CI on regressions and publishes a results table.
**Current focus:** Phase 01 — Walking Skeleton

## Current Position

Phase: 01 (Walking Skeleton) — EXECUTING
Plan: 7 of 13
Status: Ready to execute
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

Last session: 2026-10-05T18:25:18.403Z
Stopped at: Completed 01-05-PLAN.md
Resume file: None
