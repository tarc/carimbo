---
phase: 03-replayable-runs
plan: 10
subsystem: live-proof
tags: [live-run, response-cache, replay, jaeger, traces, justfile, readme]

requires:
  - phase: 03-replayable-runs
    provides: plans 03-01..03-09 (response cache, single retry owner, contract 3 with incurred cost, prompt catalog, model-call spans, Jaeger, golden fixtures)
provides:
  - "just skeleton [max_cost] [max_repairs] [cache_mode]: Llm__Cache__Directory pinned to ${PWD}/.cache/llm, --cache-mode passed to the runner, traces hint line when OTEL_EXPORTER_OTLP_ENDPOINT is set"
  - "just skeleton 1.00 2 read_only as the documented zero-model-call replay"
  - "evals/results/skeleton-20261015T090949Z (live seeding run) and evals/results/skeleton-20261015T091111Z (read_only replay), copied verbatim"
  - "README Phase 3 results: first live measurement of extract-003, the replay at zero incurred cost beside the seeding cost, trace lookup"
affects: [03-verification, 04-synthetic-dataset, 05-comparable-measurement]

actuals:
  tokens: 14000
  tasks: 3
  commits: 2
plan_head_before: a982791c5a86e4a149f1bf9c64e72c578febe38b
plan_head_after: 99caa4e713661bd291fea5b1058e2327f27d60b4

tech-stack:
  added: []
  patterns:
    - "Live proof = one capped seeding run, then a read_only replay of the same command, then a trace lookup by the trace_id carried in the eval response"
    - "Published evidence is a verbatim copy of the git-ignored run directory (diff -r clean)"

key-files:
  created:
    - evals/results/skeleton-20261015T090949Z/cases.jsonl
    - evals/results/skeleton-20261015T090949Z/run.json
    - evals/results/skeleton-20261015T090949Z/summary.json
    - evals/results/skeleton-20261015T090949Z/summary.md
    - evals/results/skeleton-20261015T091111Z/cases.jsonl
    - evals/results/skeleton-20261015T091111Z/run.json
    - evals/results/skeleton-20261015T091111Z/summary.json
    - evals/results/skeleton-20261015T091111Z/summary.md
  modified:
    - justfile
    - python/tests/test_repo_layout.py
    - README.md
    - AGENTS.md

key-decisions:
  - "The seeding run used the default read_write mode: .cache/llm did not exist (0 entries) when Task 3 started, so no refresh was needed and every answered attempt was a real model call"
  - "The README labels the replay as a replay and reports the seeding run's US$0.0970 as the cost of extracting the three cases; the replay's US$0 incurred is never presented as the cost of extraction (LLM-05 prohibition)"
  - "Model and pipeline results (two validation_failed cases) are recorded as findings, not patched"

patterns-established:
  - "A replay's summary latency_ms is wall time of the replay; model_latency_ms keeps the original model latency"

requirements-completed: [LLM-04, LLM-05, LLM-03, REPO-05]

coverage:
  - id: D1
    description: "The documented live command records into the persistent git-ignored .cache/llm and the same command with read_only replays it: justfile skeleton takes cache_mode, pins Llm__Cache__Directory and passes --cache-mode"
    requirement: "LLM-04"
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_skeleton_carries_the_repair_budget_to_the_api_by_configuration"
        status: pass
      - kind: other
        ref: "just --dry-run _skeleton-run 1.00 2 read_only (Llm__Cache__Directory= and --cache-mode read_only present)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Live seeding run: every answered attempt called the model (http_attempts 1, cache.status miss, mode read_write), 7 attempts, US$0.09700510 against the US$1.00 cap, all 7 end_turn answers stored in .cache/llm (7 entries)"
    requirement: "LLM-03"
    verification:
      - kind: other
        ref: "Task 3 automated verify script (live replay ok skeleton-20261015T090949Z skeleton-20261015T091111Z)"
        status: pass
    human_judgment: false
  - id: D3
    description: "read_only replay made zero provider calls: 7/7 answered attempts cache.status hit and http_attempts 0, run.json spent_usd 0.00000000, notional_usd 0.09700510 equal to the seeding cost, summary cache.hit_rate 1.0 (success criterion 1, live)"
    requirement: "LLM-05"
    verification:
      - kind: other
        ref: "Task 3 automated verify script (live replay ok)"
        status: pass
    human_judgment: false
  - id: D4
    description: "Each seeding-run trace_id resolves in Jaeger with one 'chat claude-haiku-4-5' span per attempt (3, 3, 1) carrying gen_ai.request.model, gen_ai.usage.output_tokens and carimbo.llm.cost_usd, no attribute holding the case's raw output (success criterion 2, live)"
    requirement: "LLM-04"
    verification:
      - kind: other
        ref: "Task 3 automated trace script (traces ok)"
        status: pass
    human_judgment: false
  - id: D5
    description: "Run files and README hold no key; both run directories identical to evals/runs originals"
    requirement: "REPO-05"
    verification:
      - kind: other
        ref: "just secrets-check (secrets ok); diff -r evals/runs/<run> evals/results/<run> clean for both"
        status: pass
    human_judgment: false
  - id: D6
    description: "A reviewer can open a seeding-run trace in the Jaeger UI and see the server span with one costed child per attempt and no prompt, answer or PDF text"
    requirement: "LLM-04"
    verification:
      - kind: other
        ref: "Task 3 human-check (Jaeger UI, service carimbo-api)"
        status: pending
    human_judgment: true
    rationale: "Visual inspection of the trace in the UI is left to the end-of-phase human check; Jaeger was left running with the three seeding traces in memory. The API-level lookup (D4) already asserts span count, attributes and absence of raw output."

duration: 10min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 10: live proof: a paid seeding run, a free replay and a trace lookup Summary

**One approved live seeding run of the three skeleton cases (first live run of prompt extract-003, US$0.0970) and a read_only replay that made zero provider calls (7/7 cache hits, US$0 incurred, notional equal to the seeding cost), with every seeding trace resolved in a local Jaeger as one costed, content-free `chat` span per attempt.**

## Performance

- **Duration:** 10 min (plan start 09:04:24Z at the plan-start base commit; continuation for Task 3 ran 09:08Z to 09:14Z)
- **Started:** 2026-10-15T09:04:24Z
- **Completed:** 2026-10-15T09:14:00Z
- **Tasks:** 3 (one tracer, one decision checkpoint approved by the human, one auto)
- **Files modified:** 12 (4 source and doc files in Task 1; README plus 8 copied result files in Task 3)

## Accomplishments

- `just skeleton 1.00 2 [cache_mode]` now records into the persistent git-ignored `.cache/llm` and the identical command with `read_only` replays it.
- Live seeding run `skeleton-20261015T090949Z` (read_write, `.cache/llm` empty beforehand, so no `refresh` was needed): 3 cases, 7 model calls, US$0.09700510 incurred and notional, 12764 input and 10272 output tokens, 0 harness errors, 0 unpriced. Cache hit rate 0/7. All 7 answers stored (7 files in `.cache/llm`).
- Replay `skeleton-20261015T091111Z` (read_only): 7/7 answered attempts hits (hit rate 1.0), `http_attempts` 0 everywhere, `spent_usd` 0.00000000, `notional_usd` 0.09700510, identical statuses and field results.
- Trace lookup (`http://127.0.0.1:16686/api/traces/<trace_id>`): each seeding trace has the `POST /eval/extractions` server span and exactly one `chat claude-haiku-4-5` span per attempt.

### Per-case results (seeding run, prompt extract-003, repair budget 2, model requested claude-haiku-4-5, returned claude-haiku-4-5-20251001)

| Case | Status | Attempt statuses | Fields | Incurred = notional | Latency (model) | Trace id | Spans in Jaeger (total / chat) |
|------|--------|------------------|--------|---------------------|-----------------|----------|--------------------------------|
| case-001 | validation_failed | validation_failed x3 | 26/27, access_key wrong, KEY_CHECK_DIGIT | US$0.02375545 | 25783 ms | f720b1d530a4adac18a1ad9ce440ff95 | 4 / 3 |
| case-002 | validation_failed | validation_failed x3 | 26/27, access_key wrong, KEY_CHECK_DIGIT, KEY_SERIES_MISMATCH, KEY_NUMBER_MISMATCH | US$0.02760040 | 31614 ms | d65bb5551a0bd401f1d5eb7958d8b0ab | 4 / 3 |
| case-003 | success | success | 27/27 | US$0.04564925 | 48521 ms | e1408a0541e6c473885d513c65c2d63e | 2 / 1 |

Replay trace ids (spans were exported for the replay too; not asserted): case-001 4d4e04a198763c1557909c425ecb5077, case-002 a5d72a5ef161e6ee06c0aec7d9ecab99, case-003 6d0aede5b071a5ee134375d447d97ba2. Replay wall time per case 48, 48 and 8 ms; `model_latency_ms` keeps the original (25646, 31514, 48514).

### Spend

- Seeding run: US$0.09700510 (cap US$1.00; expected about US$0.10). Replay: US$0.
- **Cumulative Phase 3 live spend: US$0.0970 of the US$5 cap.** No earlier Phase 3 plan made a live call (03-09 states "No live or paid call was made"; 03-02 and 03-07 record nothing live). Phases 1 and 2 are accounted separately in the README Spend section.

## Task Commits

1. **Task 1 (tracer): `just skeleton` takes a cache mode and keeps a persistent cache; replay documented** - `eb68fce` (feat)
2. **Task 2: decision checkpoint** - resolved by the human to option-a (approve one seeding run, cap US$1.00); no commit
3. **Task 3: live seeding run, replay, trace lookup, evidence and README** - `99caa4e` (feat)

**Plan metadata:** recorded by the docs(03-10) commit that adds this file.

## Findings (recorded, not patched)

1. **Access-key transcription is still the failure mode, and extract-003 did not fix it on this sample.** Case-001 (truth `...568011198220840`) was a success under extract-002 in Phase 2 and is a validation_failed here: attempt 0 returned `...568011199220840` (one digit wrong), attempts 1 and 2 returned `...568011199822084` (the digits shifted and the last one dropped). Case-002 (truth `33260485431315000146550060009839681143038699`) came back with series and number digits shifted (`...550006000983968...`) in attempts 0 and 1, and in attempt 2 additionally with a changed ending (`...114303699`). With n=1 per case, run-to-run variance and the prompt change cannot be separated. Every wrong key was flagged; no case reported `success` with a wrong field (0 silent errors in 3 extractions, an observation and not a rate).
2. **Repair repaired 0 cases again.** The four repair attempts on case-001 and case-002 cost US$0.0263 and changed nothing useful: case-001's second and third answers are identical, case-002's second repeats the first. This matches the Phase 2 note; feedback withholds values for check-digit and key rules by design.
3. **Cost of cached repair reads works as designed:** repair attempts read the PDF from the prompt cache (6361 and 6532 cache-read tokens) and show lower per-attempt cost than the first attempt (US$0.0055 against US$0.0120 on case-001).
4. **case-003** (50 items, 6093 output tokens) took 48.5 s, consistent with Phase 2 (49.6 s and 49.3 s).
5. Remedies for the access-key misread (barcode-decoded key as cross-check, stronger transcription instruction, different model) remain unbuilt and out of the Phase 3 roadmap.

## Deviations from Plan

None - plan executed exactly as written. `.cache/llm` was empty so the seeding run used the read_write default; no environment retry was needed and the seeding step ran exactly once.

**Total deviations:** 0
**Impact on plan:** none.

## Issues Encountered

- Two README wording slips were caught by checking the numbers against the run data before the commit (the extra repair cost was US$0.0263, not US$0.0239; a replay's summary `latency_ms` is real wall time while `model_latency_ms` carries the original latency) and fixed.

## Authentication Gates

None. The provider key was resolved by the recipe through secretspec; only its presence was checked, and the value was never printed, logged or committed (`just secrets-check` passes).

## Known Stubs

None.

## Threat Flags

None. No new network endpoint, auth path or schema; the Api still binds loopback and Jaeger is bound to loopback. T-03-26 (wallet): human approval before spend, one seeding run, US$0.0970 against US$1.00 and US$5. T-03-27: `just secrets-check` passed after copying the run directories. T-03-28: the trace check asserted no span attribute contains the first 40 characters of the case's raw output. T-03-SC: no package added.

## User Setup Required

None beyond the provider key and a Docker engine, both already available for this run. Jaeger (`carimbo-jaeger-1`) was left running for the end-of-phase human check; its storage is in memory, so `just skeleton 1.00 2 read_only` with `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317` regenerates traces for free if it is restarted.

## Next Phase Readiness

- All ten Phase 3 plans are complete; the phase is ready for verification. Live parts of success criteria 1 and 2 are proven (D2 to D4); the UI inspection (D6) is the remaining human check.
- Phase 4 inherits a working replay: with `.cache/llm` seeded, reruns of the skeleton cases cost nothing.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*

## Self-Check: PASSED

- Files present: both `evals/results/skeleton-20261015T0909*` and `skeleton-20261015T0911*` directories with cases.jsonl, run.json, summary.json and summary.md; README.md, justfile, AGENTS.md modified.
- Commits present on the branch: eb68fce, 99caa4e.
- `just check` exits 0; `just secrets-check` prints "secrets ok"; "live replay ok" and "traces ok" both printed.
