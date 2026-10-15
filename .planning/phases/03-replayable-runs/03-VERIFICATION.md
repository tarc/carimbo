---
phase: 03-replayable-runs
verified: 2026-10-15T13:45:00Z
status: passed
score: 7/7 must-haves verified
covered_files:
  - ".planning/phases/03-replayable-runs/03-01-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-01-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-02-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-02-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-03-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-03-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-04-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-04-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-05-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-05-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-06-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-06-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-07-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-07-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-08-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-08-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-09-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-09-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-10-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-10-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-11-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-11-SUMMARY.md"
  - "AGENTS.md"
  - "README.md"
  - "compose.yaml"
  - "docs/DECISIONS.md"
  - "dotnet/src/Carimbo.Api/EvalEndpoint.cs"
  - "dotnet/src/Carimbo.Api/Program.cs"
  - "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
  - "dotnet/src/Carimbo.Extraction/PromptCatalog.cs"
  - "dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs"
  - "dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs"
  - "dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs"
  - "dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs"
  - "dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs"
  - "dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs"
  - "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs"
  - "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs"
  - "justfile"
  - "python/src/carimbo_evals/contract.py"
  - "python/src/carimbo_evals/runner.py"
  - "python/src/carimbo_evals/summary.py"
covered_digest: "v3:sha256:e9aeff65579b46fbd4fedc37b3b9f93fa856556e9f6d3d502aef771cfc83f049"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: human_needed
  previous_score: 7/7
  gaps_closed:
    - "Human item 1 (Jaeger UI repair labels): passed in UAT test 1 against a live read_only replay"
    - "Human item 2 (README readability): passed in UAT test 2; line 31 reworded afterwards in 48f235f and re-checked here"
  gaps_remaining: []
  regressions: []
gaps: []
---

# Phase 3: Replayable Runs Verification Report

**Phase Goal:** As a developer, I want to replay eval reruns from a request-hash cache and see every model call as a costed span in a local trace viewer, so that I can iterate on prompts and graders without paying twice and can trace any case back to its model calls.
**Verified:** 2026-10-15T13:45:00Z
**Status:** passed
**Re-verification:** Yes. The previous report (human_needed, after gap closure 03-11) went stale because README.md changed in commit 48f235f. The two human items it listed were then completed in 03-UAT.md (status complete, 2 passed, 0 issues, commit 70aaf5b).

## What changed since the previous report

`git diff e6a6763 HEAD` outside `.planning/` is one line: README.md line 31. Every source, test, schema, data and justfile file the previous report verified is byte-identical to what it checked, so its code evidence stands. Only `.planning/` documents (review, security, validation, UAT) changed otherwise.

### The reworded README claim is accurate

README line 31 now reads: "The Api caches model answers by a request hash (model, max tokens, whether repair has marked the PDF for Anthropic prompt caching, prompt and repair prompt versions, hashes of the prompt text and schema, the document's hash and media type, the repair conversation and the replicate salt)."

Checked against code:

- `LlmCacheKey.cs` line 53 writes `cache_document` (`request.CacheDocument`) into `CanonicalJson`, so the flag is part of the hash. The remaining fields (model, max_tokens, prompt_version, repair_prompt_version, prompt_sha256, schema_sha256, document_media_type, document_sha256, follow_ups, replicate_salt) were matched field by field in the previous report and the file is unchanged. "effort" is absent from both the key and the list.
- `InvoiceExtractor.cs` line 272 sets `CacheDocument = maxRepairs > 0`, with the comment that the PDF block is cached only when a repair can follow. "Whether repair has marked the PDF" is therefore faithful: the flag is on exactly when repair is enabled.
- `AnthropicLlmGateway.cs` lines 131 to 133 turn the flag into `CacheControl = new CacheControlEphemeral()` on the document block, which is Anthropic's prompt-cache breakpoint. "For Anthropic prompt caching" is correct, and it removes the earlier conflation with the D-27 response cache (the `LlmContracts.cs` doc comment says the same).

No gate asserts the old phrase (UAT note, and the `justfile` docs-check only checks decision headings).

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | read_only rerun makes zero provider calls and reports original latency and cost; refresh calls the model; different model, prompt version or salt misses; truncated and refused answers never cached; production runs with the cache off | VERIFIED | Cache sources unchanged since the previous verification (`LlmCacheKey.cs` hashes the full field list above). Live read_only replay in UAT test 1: run 20261015T130529Z-067fc6, US$0 incurred, 7/7 cache hits. Orchestrator: `just check` exit 0 at HEAD 70aaf5b. |
| 2 | One docker compose command starts a local trace viewer; looking up a trace ID shows one span per model call with model, tokens, cost and no content | VERIFIED | UAT tests 1 and 2 on the real Jaeger: `/trace/<id>` served HTTP 200 for case-002 (trace 278659c9), 3 `chat claude-haiku-4-5` spans with model, token counts and `carimbo.llm.cost_usd`, no content attributes or span logs. The trace_id matches between summary.md and cases.jsonl. `TracingLlmGateway`/`LlmTelemetry` unchanged; content-sentinel test passes in the orchestrator's full run. The previous human item (UI rendering) is now closed by this live check. |
| 3 | Transient provider errors retried by exactly one policy | VERIFIED | `AnthropicLlmGateway` retry configuration and tests unchanged; the tracing and caching decorators add no retry. |
| 4 | Per-request overrides take effect and the response echoes the effective configuration; prompt version in key and result metadata | VERIFIED | Unchanged files; `TracingWiringTests` covers the extract-002 override path and agreement with `attempts[i].prompt_version`. |
| 5 | pytest parses golden eval-response fixtures emitted by the .NET tests | VERIFIED | `data`, `schema` and `python/src/carimbo_models` unchanged; orchestrator: 399 pytest and 7 e2e passed, pyright 0 errors. |
| 6 | Plan 03-11 G-03-1: repair-call spans carry `carimbo.llm.repair_prompt_version` exactly when `LlmRequest.RepairPromptVersion` is set, matching `attempts[i].prompt_version` | VERIFIED | Code verified in the previous report (`InvoiceExtractor.cs` line 277 `RepairPromptVersion = index == 0 ? null : ...`; tag set in `TracingLlmGateway`; constant in `AllowedTags`). Real-pipeline confirmation now added by UAT test 1: Jaeger traces e32e1e5d (case-001) and 278659c9 (case-002) show span 1 with `prompt_version` extract-003 only and spans 2 and 3 adding `repair_prompt_version` repair-001, equal to attempts[] (extract-003, repair-001, repair-001); case-003 (47405018) is a single extract-003 span. Passing behavioral tests (`TracingWiringTests` 8/8, `TracingGatewayTests` 11/11) as recorded in the previous report. |
| 7 | Plan 03-11 G-03-2: README cache-key list matches `CanonicalJson`; D-33 recorded additively; docs-check requires D-18 to D-33 | VERIFIED | The list, including the line-31 rewording above, matches the key's fields. `docs/DECISIONS.md` D-33 and the `justfile` docs-check loop are in the covered set and unchanged. UAT test 2 passed. |

**Score:** 7/7 truths verified, 0 present and behavior-unverified.

### Prohibitions

The 2 test-tier prohibitions of plan 03-11 (no content in span attributes; cache key, response contract and generated files unchanged) have wired tests that pass in the orchestrator's `just check`; the cache, schema and data directories are unchanged since. The 14 judgment-tier prohibitions were accepted in the earlier UAT round (tests 3 to 5) and no code they cover changed.

### Key Links

All links from the previous report (extractor to tracing gateway to telemetry allowlist; justfile to DECISIONS.md) are WIRED; the files are unchanged. The Jaeger traces in UAT confirm the end-to-end wiring on the running stack.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Whole offline gate | orchestrator `just check` at HEAD 70aaf5b | exit 0: all .NET suites, 399 pytest, 7 e2e, pyright 0 errors (not repeated here) | PASS |
| Only README changed in code-adjacent files since previous report | `git diff --stat e6a6763 HEAD -- . ':!.planning'` | `README.md | 2 +-` | PASS |
| README wording vs code | read `LlmCacheKey.cs`, `InvoiceExtractor.cs`, `AnthropicLlmGateway.cs`, `LlmContracts.cs` | accurate (see above) | PASS |

No paid recipe ran. Probes: the repository has no probe scripts; Step 7c skipped.

### Requirements Coverage

Every ID in the phase header is declared in at least one plan's `requirements:` frontmatter and defined in REQUIREMENTS.md; no Phase 3 requirement is orphaned.

| Requirement | Source Plans | Status | Evidence |
|-------------|--------------|--------|----------|
| LLM-01 | 03-05 | SATISFIED | Single SDK retry owner, unchanged |
| LLM-03 | 03-07, 03-10, 03-11 | SATISFIED | One span per call with model, tokens, cost, no content; verified live in Jaeger (UAT 1, 2) |
| LLM-04 | 03-01, 03-03, 03-04, 03-06, 03-10, 03-11 | SATISFIED | Request-hash cache; README key list now accurate |
| LLM-05 | 03-01, 03-03, 03-04, 03-06, 03-10 | SATISFIED | Cache modes; replay US$0 with original latency and cost |
| EXT-05 | 03-01, 03-03, 03-08, 03-11 | SATISFIED | Prompt version in key, result metadata and span labels |
| REPO-05 | 03-02, 03-07, 03-10 | SATISFIED | `compose.yaml` Jaeger, UI served on the local stack |
| API-02 | 03-04, 03-08 | SATISFIED | Per-request overrides echoed |
| API-04 | 03-09 | SATISFIED | Golden fixtures parsed by pytest |

### Anti-Patterns Found

No new findings; the only change is a one-line README rewording. The non-blocking warnings WR-01 to WR-05 from 03-REVIEW (dispositioned in 03-REVIEW-DISPOSITION.md) are unchanged and not on the goal path. No debt markers introduced.

### Human Verification Required

None. Both previously listed items were completed in 03-UAT.md (2 passed, 0 issues). Out of scope and not raised (UAT observations): the "Incomplete" trace flag from the runner's unexported parent span, the cached-token difference in `gen_ai.usage.input_tokens`, and `GET /healthz` traces.

### Gaps Summary

None. The phase goal is achieved: eval reruns replay from the request-hash cache at zero provider cost, and every model call, including repair attempts, appears as a costed, content-free span in the local Jaeger viewer, traceable from a case's trace_id.

---

_Verified: 2026-10-15T13:45:00Z_
_Verifier: Claude (gsd-verifier)_
