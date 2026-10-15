---
status: diagnosed
phase: 03-replayable-runs
source: [03-VERIFICATION.md]
started: 2026-10-15T09:33:34Z
updated: 2026-10-15T10:21:14Z
---

## Current Test

[testing complete]

## Tests

### 1. Jaeger UI shows one costed chat span per model call, with no content
expected: Each seeding trace shows the server span with 3, 3 and 1 `chat claude-haiku-4-5` spans carrying model, tokens and carimbo.llm.cost_usd and no content (Jaeger container carimbo-jaeger-1 is still running; a free `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 just skeleton 1.00 2 read_only` regenerates traces if it was restarted)
result: issue
reported: "from Test 1, I want to fix that repair calls being mislabelled in Jaeger (item 2.)"
severity: major
source: automated browser check (Playwright + headless Chrome against Jaeger 2.22 UI, delegated by the user, who had also checked it)
notes: |
  All 6 traces (3 seeding, 3 replay) render one POST /eval/extractions server span with 3/3/1 chat claude-haiku-4-5
  spans. Each span's Details tab shows the 22 attributes, including gen_ai.request.model claude-haiku-4-5,
  gen_ai.response.model claude-haiku-4-5-20251001, gen_ai.usage.* and carimbo.llm.cost_usd. Every cost equals the
  run record. Seeding spans show cache.status miss with http_attempts 1; replay spans show hit with 0. A scan of
  581 25-character chunks of the raw model output and of PDF base64 against all rendered UI text found 0 hits.
  The GenAI tab shows operation, provider, model and tokens. Observations, non-blocking:
  (a) every eval trace is flagged "Incomplete" and the search list labels it by trace id, because the server
  span's parent is the runner's traceparent span, which is never exported;
  (b) repair-call spans carry carimbo.llm.prompt_version extract-003, while the record says repair-001.
  TracingLlmGateway tags only LlmRequest.PromptVersion, so Jaeger cannot tell a repair call from the first
  call except by order;
  (c) gen_ai.usage.input_tokens follows OTel semconv (includes cached tokens, e.g. 7359), while the record's
  usage.input_tokens is uncached only (998). The difference is by design but undocumented;
  (d) GET /healthz requests also produce traces in the search list.

### 2. README Phase 3 sections read clearly and honestly
expected: Following `docker compose up -d` and `just skeleton 1.00 2 read_only` as a reviewer would, the prose is clear, the replay is labelled as a replay, and the seeding cost (US$0.0970) is presented as the cost of extraction rather than the replay's zero. (Verifier note: README line 31 lists "effort" as a cache key field, which the key does not contain.)
result: issue
reported: "fix the effort line"
severity: minor

### 3. LLM-04 environment policy is acceptable
expected: You accept that outside Development and Eval a non-blank Llm:Cache:Directory fails startup even when Llm:Cache:Mode is off, and any mode other than off fails startup (Program.cs ResolveCache, pinned by CacheConfigurationTests)
result: pass

### 4. D-30 restoring extract-002 as the second catalogued prompt is acceptable
expected: You prefer a real earlier prompt (byte-exact, SHA-pinned, with known label defects G-02-1 and G-02-2) over a test-only second prompt, or you record a superseding decision
result: pass

### 5. Judgment-tier prohibitions accepted
expected: The 14 prohibitions in 03-VERIFICATION.md's Prohibitions table, each given a non-authoritative PASS by the verifier with code or test evidence, are accepted (WR-02, WR-03 and WR-05 carry caveats)
result: pass

## Summary

total: 5
passed: 3
issues: 2
pending: 0
skipped: 0
blocked: 0

## Gaps

- gap_id: G-03-1
  truth: "In Jaeger, each chat span identifies which prompt it used: repair calls are distinguishable from the initial extraction call"
  status: failed
  reason: "User reported: from Test 1, I want to fix that repair calls being mislabelled in Jaeger (item 2.) -- repair-call spans carry carimbo.llm.prompt_version extract-003 while the eval record says repair-001; TracingLlmGateway tags only LlmRequest.PromptVersion, never RepairPromptVersion"
  severity: major
  test: 1
  root_cause: "TracingLlmGateway sets carimbo.llm.prompt_version from LlmRequest.PromptVersion only. On a repair call the extractor sends PromptVersion = the extraction prompt (extract-003) and RepairPromptVersion = repair-001 (InvoiceExtractor.cs:276-277), while the eval record labels the attempt index == 0 ? PromptVersion : RepairPromptVersion (InvoiceExtractor.cs:261). The span never reads RepairPromptVersion, so every repair span says extract-003."
  artifacts:
    - path: "dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs"
      issue: "lines 31-34 tag only request.PromptVersion; RepairPromptVersion is ignored"
    - path: "dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs"
      issue: "allowlist (lines 50-72) has no attribute for the repair prompt version"
    - path: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs"
      issue: "no test pins the prompt attribute on a repair request"
    - path: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs"
      issue: "the three-span validation-failure test does not assert per-span prompt versions"
  missing:
    - "A repair-call span reports the prompt actually used for that call, matching attempts[].prompt_version in the eval record (repair-001 on repair calls, the extraction version on the first call), via an allowlisted attribute"
    - "Keep the extraction prompt version visible on repair spans too (e.g. carimbo.llm.prompt_version stays the extraction version and a new allowlisted carimbo.llm.repair_prompt_version is set when RepairPromptVersion is non-null), or switch prompt_version to the per-call value; pick one and record it against D-31"
    - "Unit test in TracingGatewayTests for a request with RepairPromptVersion set, and a wiring assertion that the 3-span validation-failure trace has the first span labelled with the extraction version and the two repair spans with repair-001"
  debug_session: "inline diagnosis during /gsd-verify-work 03 (Playwright check of Jaeger UI + code read), 2026-10-15"


- gap_id: G-03-2
  truth: "README.md line 31 lists the response-cache key fields accurately"
  status: failed
  reason: "User reported: fix the effort line (README says the key includes 'effort'; LlmCacheKey has no effort field and omits max_tokens and replicate_salt from the listed fields)"
  severity: minor
  test: 2
  root_cause: "Plan 03-10 wrote README line 31 from planning prose (STACK research listed effort/thinking as key fields). carimbo never sends effort or thinking (D-21), and LlmCacheKey.CanonicalJson (LlmCacheKey.cs:50-70) hashes key_version, model, max_tokens, cache_document, prompt_version, repair_prompt_version, prompt, schema, document media type and bytes, follow_ups and replicate_salt."
  artifacts:
    - path: "README.md"
      issue: "line 31 lists 'effort' as a cache-key input and omits max_tokens and replicate_salt"
  missing:
    - "Rewrite the parenthesis on README.md line 31 to the real key inputs: model, max tokens, prompt and repair prompt versions, schema hash, document hash, the repair conversation and the replicate salt"
  debug_session: "inline diagnosis during /gsd-verify-work 03 (code read of LlmCacheKey.cs), 2026-10-15"

