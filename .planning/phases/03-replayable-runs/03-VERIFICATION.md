---
phase: 03-replayable-runs
verified: 2026-10-15T11:05:00Z
status: human_needed
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
covered_digest: "v3:sha256:0b20b35ac276a7a0e725903b1beb860b645fff06c59792db42b15f01966d3b9e"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: human_needed
  previous_score: 5/5
  gaps_closed:
    - "G-03-1: repair-call spans now carry carimbo.llm.repair_prompt_version (D-33)"
    - "G-03-2: README cache-key list matches LlmCacheKey.CanonicalJson"
  gaps_remaining: []
  regressions: []
gaps: []
human_verification:
  - test: "Re-check the Jaeger UI after the 03-11 fix. With the stack up (`docker compose up -d`), run the free replay `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 just skeleton 1.00 2 read_only` (cache hits only, zero provider calls, key unchanged by 03-11). Open the trace for a case that had repair attempts (case-001 or case-002) at http://localhost:16686/trace/<trace_id> and open the span Details of each `chat claude-haiku-4-5` span."
    expected: "The first span shows carimbo.llm.prompt_version extract-003 and no carimbo.llm.repair_prompt_version. Each repair span shows carimbo.llm.repair_prompt_version repair-001 beside carimbo.llm.prompt_version extract-003. This agrees with attempts[i].prompt_version in cases.jsonl. No prompt, answer or PDF content appears."
    why_human: "UAT test 1 (the Jaeger UI check) is the test that produced G-03-1. The fix is proven on the real OpenTelemetry pipeline with an in-memory exporter, but no test asserts what the Jaeger UI renders. The earlier UI check was a person-delegated browser run."
  - test: "Re-read README.md line 31 (Response cache D-27) and the trace-lookup paragraph (line 192) as a reviewer would."
    expected: "The cache-key list reads clearly and the repair-call explanation is understandable."
    why_human: "Wording clarity is a judgment. The factual accuracy of the list is verified in code (Truth 7); only readability remains. The earlier README judgment (UAT test 2) was passed except for the effort line, which is now fixed."
---

# Phase 3: Replayable Runs Verification Report

**Phase Goal:** As a developer, I want to replay eval reruns from a request-hash cache and see every model call as a costed span in a local trace viewer, so that I can iterate on prompts and graders without paying twice and can trace any case back to its model calls.
**Verified:** 2026-10-15T11:05:00Z
**Status:** human_needed
**Re-verification:** Yes, after gap closure (plan 03-11 closing UAT gaps G-03-1 and G-03-2)

Both UAT gaps are closed in the code. No gaps remain and none of the five roadmap criteria regressed. The status stays `human_needed` only because the Jaeger UI rendering of the new repair label has not been looked at by a person, which is exactly the check that raised G-03-1. The three policy confirmations and the 14 judgment-tier prohibitions from the previous report were accepted in UAT tests 3, 4 and 5 (pass) and are no longer open.

## Goal Achievement

### Observable Truths (ROADMAP success criteria, regression check)

Quick regression pass: the phase's earlier verification evidence still stands, and the 03-11 delta did not touch the files these truths rest on.

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | read_only rerun makes zero provider calls and reports original latency and cost; refresh calls the model; different model, prompt version or salt misses; truncated and refused answers never cached; production runs with the cache off | VERIFIED | `git diff 67f032d -- dotnet/src/Carimbo.Llm/Cache` is empty, so the cache key and store are byte-unchanged by 03-11. `LlmCacheKey.CanonicalJson` still hashes model, max_tokens, cache_document, prompt_version, repair_prompt_version, prompt/schema/document hashes, follow_ups and replicate_salt. Committed live replay evidence unchanged. Orchestrator: `just check` exit 0 at HEAD e6a6763 (846 .NET tests, 399 pytest, 7 e2e). |
| 2 | One `docker compose` command starts a local trace viewer; looking up a trace ID shows one span per model call with model, tokens, cost and no content | VERIFIED (UI visual pending, human item 1) | `TracingLlmGateway` still creates one `chat <model>` span per call; the only tag keys it sets are `LlmTelemetry` constants, and `AllowedTags` grew by exactly one name (`carimbo.llm.repair_prompt_version`). Content sentinel test now runs with RepairPromptVersion set (TracingGatewayTests, passing). |
| 3 | Transient provider errors retried by exactly one policy | VERIFIED | `AnthropicLlmGateway.cs` and the retry tests are untouched by 03-11; the tracing decorator still adds no retry (it only observes). |
| 4 | Per-request overrides take effect and the response echoes the effective configuration; prompt version in key and result metadata | VERIFIED | Unchanged files. The new `A_prompt_version_override_labels_every_span_with_the_selected_prompt` test additionally proves that with `overrides.prompt_version` extract-002 the span labels follow the per-request prompt and match `attempts[i].prompt_version`. |
| 5 | pytest parses golden eval-response fixtures emitted by the .NET tests | VERIFIED | `git diff 67f032d -- data schema python/src/carimbo_models` is empty; golden fixtures byte-identical. `just check` exit 0 (orchestrator evidence). |

**Score:** 5/5 roadmap truths verified, plus both 03-11 gap truths below (7/7 total), 0 present and behavior-unverified.

### Plan 03-11 must-haves (gap closure)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 6 | G-03-1: repair-call spans identify their prompt; `carimbo.llm.prompt_version` stays the extraction version and `carimbo.llm.repair_prompt_version` is set exactly when `LlmRequest.RepairPromptVersion` is non-null, matching `attempts[i].prompt_version` | VERIFIED | Code: `LlmTelemetry.cs` line 45 defines `RepairPromptVersion = "carimbo.llm.repair_prompt_version"` and lists it in `AllowedTags`. `TracingLlmGateway.cs` sets `LlmTelemetry.RepairPromptVersion` from `request.RepairPromptVersion` only (pattern `is { } repairPromptVersion`), before the inner call, so an error span carries it too; the `PromptVersion` tag line is unchanged. Source of the value: `InvoiceExtractor.cs` line 277 `RepairPromptVersion = index == 0 ? null : effective.RepairPromptVersion` and the record label at line 261 `index == 0 ? PromptVersion : RepairPromptVersion` (the rule the tests reproduce in `CallLabel`). Behavior: I ran the two named test classes myself. `TracingWiringTests` 8/8 passed (three-attempt trace: span 0 extract-003 with no repair tag, spans 1 and 2 repair-001 plus extract-003; extract-002 override: all spans extract-002, repair spans repair-001; call label equals `attempts[i].prompt_version`). `TracingGatewayTests` 11/11 passed (initial vs repair, failing repair is an error span with the label, content sentinels with the label set, name pin and allowlist membership). The SUMMARY's mutation check (removing the SetTag block fails 3 tests) is plausible from the test content; the tests assert literal `repair-001`. Not behavior-unverified: a passing behavioral test exercises it. |
| 7 | G-03-2: README Response cache (D-27) bullet lists the key inputs `CanonicalJson` actually hashes and no field it lacks; D-33 recorded without deleting earlier lines; docs-check requires D-18 to D-33 | VERIFIED | README line 31 reads "(model, max tokens, the document cache flag, prompt and repair prompt versions, hashes of the prompt text and schema, the document's hash and media type, the repair conversation and the replicate salt)"; compared field by field against `CanonicalJson` (model, max_tokens, cache_document, prompt_version, repair_prompt_version, prompt_sha256, schema_sha256, document_media_type, document_sha256, follow_ups, replicate_salt): all present, "effort" absent. `docs/DECISIONS.md` has `## D-33 Repair prompt version on model-call spans (refines D-31)` and `git diff --numstat 67f032d -- docs/DECISIONS.md` is 17 added, 0 deleted. `justfile` docs-check loop includes D-33 (line 57) and its comment says "D-18 to D-33"; AGENTS.md line 51 and README lines 22 and 175 say D-33. README line 35 and line 192 explain the repair attribute. `Directory.Packages.props` still has 7 PackageVersion entries. |

### Prohibitions (plan 03-11, test tier)

Both are `verification: test` and have wired enforcement, so they are not unverified.

| Prohibition | Verdict | Evidence |
|-------------|---------|----------|
| LLM-03: no span attribute from prompt text, follow-ups, schema, document or stop detail; the new attribute only from `LlmRequest.RepairPromptVersion` | PASS | `TracingLlmGateway.cs` reads only `request.RepairPromptVersion` for the new tag; `No_prompt_answer_schema_document_or_base64_reaches_a_name_a_tag_or_an_event` passes with the repair label set. |
| LLM-04: cache key, response contract and generated files byte-unchanged from 67f032d | PASS | `git diff --stat 67f032d -- data schema python/src/carimbo_models dotnet/src/Carimbo.Llm/Cache` prints nothing. |

The 14 judgment-tier prohibitions of the earlier report were accepted in UAT test 5; the 03-11 delta did not touch the code they cover.

### Required Artifacts (03-11)

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs` | constant plus allowlist entry | VERIFIED | Present, in `AllowedTags`, remark documents D-33 |
| `dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs` | tag mirrored from request before the inner call | VERIFIED | Contains `request.RepairPromptVersion`; wired as the outermost decorator (unchanged from 03-07) |
| `dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs` | per-span labels vs attempts, default and extract-002 | VERIFIED | Contains `LlmTelemetry.RepairPromptVersion` and `extract-002`; 8/8 pass |
| `dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs` | initial vs repair, error span, sentinels, name pin | VERIFIED | 11/11 pass |
| `docs/DECISIONS.md` | D-33 | VERIFIED | Heading present, "refines D-31", mentions the attribute |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `InvoiceExtractor.cs` | `TracingLlmGateway.cs` | `RepairPromptVersion = index == 0 ? null : ...` mirrored into the span | WIRED | Pattern at line 277; tag set at gateway line 31 |
| `TracingLlmGateway.cs` | `LlmTelemetry.cs` | `LlmTelemetry.RepairPromptVersion` constant in `AllowedTags` | WIRED | Confirmed in both files |
| `justfile` | `docs/DECISIONS.md` | docs-check requires `## D-33 ` | WIRED | Loop lists `D-32 D-33`; heading present |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| Span tag `carimbo.llm.repair_prompt_version` | `request.RepairPromptVersion` | `effective.RepairPromptVersion` from `PromptCatalog` via `InvoiceExtractor` | Yes, `repair-001`, asserted on the exported trace against the response's `attempts[]` | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Repair span labels end to end | `dotnet test --project tests/Carimbo.Api.Tests -- --filter-class Carimbo.Api.Tests.TracingWiringTests` | 8 passed, 0 failed | PASS |
| Gateway span labels, error span, sentinels, name pin | `dotnet test --project tests/Carimbo.Llm.Tests -- --filter-class Carimbo.Llm.Tests.TracingGatewayTests` | 11 passed, 0 failed | PASS |
| Cache, schema, data untouched | `git diff --stat 67f032d -- data schema python/src/carimbo_models dotnet/src/Carimbo.Llm/Cache` | empty | PASS |
| Whole offline gate | orchestrator `devenv shell -- just check` at e6a6763 | exit 0 (not repeated) | PASS |
| Debt markers | grep TBD/FIXME/XXX/TODO/HACK in the 03-11 source, test and justfile files | none | PASS |

No paid recipe ran. Commits after e6a6763 (HEAD 0f719df) change only `.planning/` documents.

### Probe Execution

No probes: the repository has no `scripts/` directory and no plan declares one. Step 7c skipped.

### Requirements Coverage

Every ID in the phase header appears in at least one plan's `requirements:` frontmatter and in REQUIREMENTS.md; 03-11 additionally claims LLM-03, EXT-05 and LLM-04. No orphaned Phase 3 requirement.

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|--------------|-------------|--------|----------|
| LLM-01 | 03-05 | Single non-stacked retry policy | SATISFIED | Unchanged; SDK `MaxRetries` is the only owner |
| LLM-03 | 03-07, 03-10, 03-11 | One OTel span per call with model, tokens, cost, no content; each span identifies its call | SATISFIED | Spans plus the repair label (Truth 6); allowlist grows by one non-content name |
| LLM-04 | 03-01, 03-03, 03-04, 03-06, 03-10, 03-11 | SHA-256 request-hash cache, on in dev/eval, off in production; documented key accurate | SATISFIED | Key unchanged; README now matches (Truth 7) |
| LLM-05 | 03-01, 03-03, 03-04, 03-06, 03-10 | Cache modes, original latency and cost on hits, truncated or refused never cached | SATISFIED | Unchanged |
| EXT-05 | 03-01, 03-03, 03-08, 03-11 | Prompt version in cache key and result metadata (and trace agrees) | SATISFIED | Span label equals `attempts[i].prompt_version` |
| REPO-05 | 03-02, 03-07, 03-10 | Trace viewer with one docker compose command | SATISFIED | `compose.yaml` unchanged |
| API-02 | 03-04, 03-08 | Per-request overrides, effective configuration echoed | SATISFIED | Unchanged; override path additionally covered by the extract-002 trace test |
| API-04 | 03-09 | Golden fixtures parsed by pytest | SATISFIED | Fixtures byte-identical |

### Anti-Patterns Found

No debt markers, stubs or hardcoded empty data in the 03-11 changes. The open warnings WR-01 to WR-05 from 03-REVIEW (all non-blocking, none touched by 03-11) remain recorded in 03-REVIEW-DISPOSITION.md; the README effort inaccuracy previously listed as an Info item is now fixed. The 03-11 code review found 0 critical, 0 warning, 2 info (orchestrator evidence).

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs` | 221-257 | JSON null in a required member accepted as a hit (WR-01) | Warning | Carried from the previous report; needs a hand-damaged cache file, not on the goal path |
| `dotnet/src/Carimbo.Api/EvalEndpoint.cs` | 288 | `effective.pricing_version` is the host's current version for a replay (WR-04) | Warning | Carried; true today |

### Human Verification Required

1. **Jaeger UI re-check of the repair label.** Run the free replay against the local Jaeger and open a trace with repair attempts. Expected: first span extract-003 only, repair spans carry `carimbo.llm.repair_prompt_version` repair-001, agreeing with `attempts[i].prompt_version`, with no content. Why human: no test asserts what the Jaeger UI renders; this is the check that produced G-03-1.
2. **README readability** of line 31 and the trace-lookup paragraph. Why human: wording judgment; accuracy is verified.

Out of scope and not raised as gaps (UAT test 1 observations): the "Incomplete" trace flag from the runner's unexported parent span, the cached-token difference in `gen_ai.usage.input_tokens`, and `GET /healthz` traces.

### Gaps Summary

None. G-03-1 and G-03-2 are closed in the code and docs, evidenced by passing behavioral tests run in this verification and by field-by-field comparison of the README list with the cache key. The phase goal is achieved; only the Jaeger UI re-look by a person remains.

---

_Verified: 2026-10-15T11:05:00Z_
_Verifier: Claude (gsd-verifier)_
