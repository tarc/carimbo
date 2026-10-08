---
phase: 01-walking-skeleton
plan: 10
subsystem: llm-gateway
tags: [dotnet, anthropic-sdk, ichatclient, spike, llm-06, prompt-caching, retries]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "ExtractionContract.Default and the model-facing schema (01-05), committed skeleton cases (01-07), pinned Anthropic 12.53.0 (01-02)"
provides:
  - "dotnet/tools/LlmSpike: --offline-selftest (deterministic, no key, no network) and --live --budget-usd --cases --out --fixtures"
  - "docs/spikes/01-llm-gateway.md: sanitised live evidence for steps 1-8, Spend and Recommendation (D-11)"
  - "Three sanitised Messages-API response fixtures for the gateway mapping and pricing tests in 01-12"
  - "Developer decision for D-21: direct-sdk bottom adapter, MaxRetries = 0 in Phase 1, alias in development and dated snapshot for published runs"
affects: [01-11, 01-12, 01-13, 03-extraction-hardening]

actuals:
  tokens: 22439
  tasks: 3
  commits: 3
plan_head_before: 35ac0ac549f464eec4f71254f129ce91268a7871
plan_head_after: 6668c201439f81263567291daab022338a602ee8

tech-stack:
  added: []
  patterns:
    - "Spike tool outside the solution and outside dotnet test; evidence written as a sanitised markdown record plus redacted fixtures"
    - "Live writes go to <name>.tmp and are renamed on success, so an interrupted run leaves committed evidence untouched"
    - "Pre-call worst-case spend guard with a per-run budget; steps skip rather than exceed"
    - "Counting HttpMessageHandler passed through AnthropicClient.HttpClient for per-attempt visibility without Polly"

key-files:
  created:
    - dotnet/tools/LlmSpike/LlmSpike.csproj
    - dotnet/tools/LlmSpike/Program.cs
    - docs/spikes/01-llm-gateway.md
    - dotnet/tests/Carimbo.Llm.Tests/Fixtures/messages-haiku-end-turn.json
    - dotnet/tests/Carimbo.Llm.Tests/Fixtures/messages-max-tokens.json
    - dotnet/tests/Carimbo.Llm.Tests/Fixtures/messages-cache-usage.json
  modified: []

key-decisions:
  - "Gateway bottom adapter is direct-sdk: the direct Anthropic SDK behind ILlmGateway, implemented once in 01-12. The spike's decision rule (docs/spikes/01-llm-gateway.md, Recommendation) picks it because the IChatClient path was not lossless on usage decomposition (adapter sums cache-creation into the input count and does not expose the 5m/1h split) nor on stop details (refusal collapses to content_filter, stop_details only via RawRepresentation). The developer confirmed the recommendation."
  - "Phase 1 retry ownership: SDK MaxRetries = 0. Step 7 showed a 4xx is never retried and offline self-test line (f) showed 529,529,200 makes 3 attempts only when MaxRetries is raised. One attempt per call keeps cost and latency exactly attributable under the US$5 cap. Phase 3 (LLM-01) owns the single retry policy and must not stack a second one."
  - "Model id pinning: send the alias (claude-haiku-4-5) in development, always record both model_requested and model_returned, and pin the dated snapshot (claude-haiku-4-5-20251001) for published eval runs. claude-sonnet-5-5 returns no dated id, so it stays on its alias (Step 1, Step 3)."

patterns-established:
  - "Chosen option id for the checkpoint is recorded in the SUMMARY and in the spike document; docs/DECISIONS.md D-21 is written by 01-12, not here"

requirements-completed: [LLM-06]

coverage:
  - id: D1
    description: "Offline self-test shows without network or key that the direct path and the IChatClient raw-factory path send the committed schema verbatim, the default JSON-schema path rewrites it, how each path reports usage and stop reasons, and the attempt count per MaxRetries setting"
    requirement: "LLM-06"
    verification:
      - kind: command
        ref: "dotnet run --project dotnet/tools/LlmSpike -- --offline-selftest (lines (a)-(f), last line SELFTEST PASS, exit 0; re-run at plan close)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Two consecutive offline self-test runs print identical output and exit 0"
    requirement: "LLM-06"
    verification:
      - kind: command
        ref: "dotnet run ... --offline-selftest twice, cmp of the outputs (run during Task 1)"
        status: pass
    human_judgment: false
  - id: D3
    description: "Live spike recorded whether the API accepts the model-facing schema with a PDF document block on Haiku 4.5, the usage fields including cache read and write, Sonnet 5.5 with explicit effort, and which invalid requests return 400"
    requirement: "LLM-06"
    verification:
      - kind: other
        ref: "docs/spikes/01-llm-gateway.md Steps 1-7 (schema accepted unchanged; cache write then read on both models; Format plus Effort accepted; thinking:disabled and non-default temperature return 400; 404 and 400 not retried)"
        status: pass
    human_judgment: true
  - id: D4
    description: "Spike document and fixtures contain no API key, request headers or PDF bytes, and spend stays within the US$1.00 spike budget"
    requirement: "LLM-06"
    verification:
      - kind: command
        ref: "grep -rlE 'sk-ant-|x-api-key|JVBERi0' docs/spikes dotnet/tests/Carimbo.Llm.Tests/Fixtures prints nothing (exit 1); final run actual US$0.0735 of US$1.00"
        status: pass
    human_judgment: false
  - id: D5
    description: "Gateway shape and retry ownership chosen by the developer at a decision checkpoint informed by the recorded evidence"
    requirement: "LLM-06"
    verification:
      - kind: other
        ref: "Task 3 checkpoint resolved: direct-sdk, MaxRetries = 0, alias in development and dated snapshot for published runs"
        status: pass
    human_judgment: true
  - id: D6
    description: "An interrupted live spike leaves the committed document and fixtures unchanged (write to .tmp then rename) and reports spend so far"
    requirement: "LLM-06"
    verification:
      - kind: backstop
        ref: "Not reproducible deterministically in a test; implemented in Program.cs (temporary name plus rename, running spend printed after every step) and reviewed in the Task 1 follow-up"
        status: pass
    human_judgment: true
---

# Phase 1 Plan 10: LLM-06 spike, gateway shape and retry ownership Summary

**A deterministic offline self-test plus a budgeted live run against the Anthropic API settled the gateway on evidence: direct SDK behind `ILlmGateway`, `MaxRetries = 0` in Phase 1, alias in development and the dated snapshot pinned for published eval runs.**

## Performance

- **Duration:** multi-session (stopped at the Task 3 decision checkpoint and resumed after the developer's answer)
- **Tasks:** 3 of 3 (2 auto, 1 decision checkpoint)
- **Files:** 6 created, 0 modified

## Accomplishments

- `dotnet/tools/LlmSpike` has `--offline-selftest` (no key, no network, canned bodies, identical output on every run, ends `SELFTEST PASS`) and `--live` with the eight research steps, a pre-call worst-case budget guard and tmp-then-rename writes. Self-test lines (a) to (f) show: the direct path and the IChatClient raw factory send the committed schema verbatim with `cache_control` on the document block; the default `ForJsonSchema` path rewrites the schema; the adapter folds cache-creation tokens into the input count; refusal becomes `content_filter` with `stop_details` only in `RawRepresentation`; a 529, 529, 200 sequence makes 3 attempts at `MaxRetries = 2` and 1 attempt then `Anthropic5xxException` at `MaxRetries = 0`.
- The live run (docs/spikes/01-llm-gateway.md) confirmed: the full model-facing schema (`$defs/$ref`, `pattern`, `format: date`, `title`) is accepted on Haiku 4.5 unchanged, so no fallback schema is needed; `claude-haiku-4-5` resolves to `claude-haiku-4-5-20251001` while `claude-sonnet-5-5` returns no dated id; cache write then read is observed on both models on a 2-page case (Haiku 10310 tokens written then read, Sonnet 11770); Sonnet 5.5 accepts `OutputConfig.Format` together with `Effort.Low` and reports `thinking_tokens=0` inside `output_tokens`; `thinking: disabled` and a non-default `temperature` return 400 `invalid_request_error`; a 400 and a 404 are not retried even with `MaxRetries = 2`; `max_tokens` truncation returns `stop_reason: max_tokens` with unparsable output; the HTTP `request-id` header is reachable only through the direct path's `WithRawResponse`.
- Three sanitised response bodies (`messages-haiku-end-turn.json`, `messages-max-tokens.json`, `messages-cache-usage.json`) are in place for the 01-12 mapping and pricing tests. Text, thinking and signature values are replaced by `<redacted: N chars>`; ids, model, stop_reason and usage are as returned.

## Decision Record (input for D-21 in plan 01-12)

Resolved at the Task 3 checkpoint by the developer. Plan 01-12 writes D-21 from this section; `docs/DECISIONS.md` is deliberately not edited here.

- **Chosen option id:** `direct-sdk` (not `ichatclient-raw`, not `direct-sdk-retries`).
- **Gateway bottom adapter:** direct Anthropic SDK behind `ILlmGateway`. Rationale, from docs/spikes/01-llm-gateway.md:
  - Step 6: through `IChatClient` the ChatResponse reports `InputTokenCount=10492` for a request whose API usage was input 201 plus cache read 10291, so uncached input is only recoverable by subtraction. On the canned creation body (self-test line (d)) the adapter sums cache creation into the input count and does not expose the 5m/1h split. Usage decomposition is therefore not lossless.
  - Step 6 and self-test line (e): a refusal maps to `FinishReason.content_filter` and `stop_details` (category and explanation) is not in the response properties, only reachable by casting `RawRepresentation` back to the provider `Message`, which is the direct SDK type again plus a mapping layer. The request id is not visible in the ChatResponse.
  - Recommendation section: the research decision rule selects the direct SDK unless the IChatClient path is lossless on both usage decomposition and stop details; it was lossless on neither.
  - Trade-off accepted: no `UseOpenTelemetry` or MCP tool-type conveniences for free. Phase 3 or Milestone 2 may add an adapter on top; Phase 3's cache and telemetry decorators wrap `ILlmGateway`, so the choice is reversible behind that seam.
- **Phase 1 retry ownership:** `MaxRetries = 0` on the SDK client. Evidence: Step 7 (a 400 and a 404 each make exactly 1 attempt even with `MaxRetries = 2`) and self-test line (f) (transient 5xx/529 is retried only when `MaxRetries` is raised, hidden from the caller unless a counting handler is installed). One attempt per call keeps cost and attempt counts exactly attributable under the US$5 cap (D-09). Phase 3 (LLM-01) owns the single retry policy and must not stack a second one on top; a counting handler through `AnthropicClient.HttpClient` gives per-attempt visibility if retries are enabled later.
- **Model id pinning:** send the alias (for example `claude-haiku-4-5`) in development; always record both `model_requested` and `model_returned`; pin the dated snapshot (`claude-haiku-4-5-20251001`) for published eval runs so a moved alias cannot change results silently. `claude-sonnet-5-5` returns no dated id (Step 3), so it stays on its alias.
- **Other facts 01-11 and 01-12 should take from the spike:** structured output schema needs no fallback on Haiku 4.5; Haiku 4.5 needs a 4096-token cacheable prefix and Sonnet 5.5 needs 512; about 6937 input tokens for the extra page of the item-dense case-003 (upper-end figure, not a constant); never send `thinking: disabled` or `temperature` to Sonnet 5.5.

## Spend

| Scope | Amount |
|-------|--------|
| Final live run (the run that wrote the committed document) | US$0.0735 (worst-case estimate US$0.5391) |
| All four live runs of the tool (US$0.0735, US$0.0346, US$0.0735, US$0.0735) | **US$0.2551** |
| Spike budget (D-09) | US$1.00 |
| Phase 1 cap (D-09) | US$5.00 |

Every run stayed under the US$1.00 budget on its own, and the total across all four is US$0.2551. This counts toward the US$5.00 Phase 1 cap, leaving US$4.7449. The first three runs were repeated after tool fixes (a request-shape comparison in Step 6, cold cache probes with a per-run nonce in Step 3, no explicit null members in direct requests) and produced the same findings.

## Task Commits

1. **Task 1: spike tool with deterministic offline self-test** - `a569e88` (feat)
2. **Task 1 follow-up: request-shape diff, cold cache probes, no explicit nulls** - `5b90fde` (feat)
3. **Task 2: live spike with sanitised evidence and fixtures** - `6668c20` (docs)
4. **Task 3: decision checkpoint** - resolved by the developer (direct-sdk); no code commit

## Files Created/Modified

- `dotnet/tools/LlmSpike/LlmSpike.csproj`, `Program.cs` - the spike tool (not in the solution; 01-12 adds it)
- `docs/spikes/01-llm-gateway.md` - Steps 1-8, Spend and Recommendation
- `dotnet/tests/Carimbo.Llm.Tests/Fixtures/messages-haiku-end-turn.json`, `messages-max-tokens.json`, `messages-cache-usage.json` - redacted golden response bodies

## Decisions Made

See `key-decisions` and the Decision Record above.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Request-shape comparison, cold cache probes and explicit nulls in the live spike**
- **Found during:** Task 2 (live runs one to three)
- **Issue:** The first live runs compared the two request paths without a structural request-body diff, the cache-write probe could hit an already-warm cache and so never show a creation, and direct requests serialised explicit null members that the IChatClient path omitted.
- **Fix:** Added the in-memory request-shape comparison (structural equality, SHA-256 only in memory), a per-run nonce in the document title so the first call is a cold write, and removed explicit nulls from direct requests. Re-ran the live spike; findings unchanged.
- **Files modified:** `dotnet/tools/LlmSpike/Program.cs`
- **Commit:** `5b90fde`

**Total deviations:** 1 auto-fixed (bug in the spike tool itself). **Impact:** three extra live runs (US$0.1816 of the US$0.2551 total), all within the per-run budget and the phase cap.

**Evidence limits worth knowing:** in Step 6 the cache entry was already warm, so the IChatClient creation case and the 5m/1h split come from the canned body (self-test line (d)) rather than a live response. Refusal could not be forced live and is covered by the canned payload only. A live 529 cannot be provoked on demand, so 5xx retry behaviour is offline evidence.

## Verification

- Offline self-test re-run at plan close: ends `SELFTEST PASS`, exit 0 (lines (a)-(f) present; line (c) states the default path changed the schema).
- `grep -rlE 'sk-ant-|x-api-key|JVBERi0' docs/spikes dotnet/tests/Carimbo.Llm.Tests/Fixtures` prints nothing.
- `grep -c '^## Step [1-8]' docs/spikes/01-llm-gateway.md` is 8; `## Spend` and `## Recommendation` are present.
- The determinism check (two runs, `cmp`) and `dotnet build -warnaserror` passed at Task 1.

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. T-01-24 mitigated (key read from `CARIMBO_ANTHROPIC_API_KEY` then `ANTHROPIC_API_KEY`, passed explicitly, never printed; secret-shape grep clean on docs and fixtures); T-01-25 mitigated (pre-call worst-case estimate against `--budget-usd 1.00`, total US$0.2551); T-01-26 mitigated (shapes, counts and parse outcomes only, model text reduced to a parse outcome); T-01-SC no packages added (Anthropic 12.53.0 pinned in 01-02).

## Next Phase Readiness

Plan 01-11 (pricing table) can take Haiku and Sonnet usage field names and the token-class split from Steps 1 and 3. Plan 01-12 implements the single `direct-sdk` adapter with `MaxRetries = 0`, adds `dotnet/tools/LlmSpike` to the solution, uses the three fixtures for mapping and pricing tests, and writes D-21 from the Decision Record above.

## Self-Check: PASSED

- FOUND: dotnet/tools/LlmSpike/LlmSpike.csproj, dotnet/tools/LlmSpike/Program.cs, docs/spikes/01-llm-gateway.md
- FOUND: dotnet/tests/Carimbo.Llm.Tests/Fixtures/messages-haiku-end-turn.json, messages-max-tokens.json, messages-cache-usage.json
- FOUND commits: a569e88, 5b90fde, 6668c20 (all ancestors of HEAD)
