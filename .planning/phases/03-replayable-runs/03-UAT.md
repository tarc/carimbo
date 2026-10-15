---
status: testing
phase: 03-replayable-runs
source: [03-VERIFICATION.md]
started: 2026-10-15T09:33:34Z
updated: 2026-10-15T09:50:59Z
---

## Current Test

number: 2
name: README Phase 3 sections read clearly and honestly
expected: |
  Following `docker compose up -d` and `just skeleton 1.00 2 read_only` as a reviewer would, the prose is clear,
  the replay is labelled as a replay, and the seeding cost (US$0.0970) is presented as the cost of extraction
  rather than the replay's zero. (Verifier note: README line 31 lists "effort" as a cache key field, which the
  key does not contain.)
awaiting: user response

## Tests

### 1. Jaeger UI shows one costed chat span per model call, with no content
expected: Each seeding trace shows the server span with 3, 3 and 1 `chat claude-haiku-4-5` spans carrying model, tokens and carimbo.llm.cost_usd and no content (Jaeger container carimbo-jaeger-1 is still running; a free `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 just skeleton 1.00 2 read_only` regenerates traces if it was restarted)
result: pass
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
result: [pending]

### 3. LLM-04 environment policy is acceptable
expected: You accept that outside Development and Eval a non-blank Llm:Cache:Directory fails startup even when Llm:Cache:Mode is off, and any mode other than off fails startup (Program.cs ResolveCache, pinned by CacheConfigurationTests)
result: [pending]

### 4. D-30 restoring extract-002 as the second catalogued prompt is acceptable
expected: You prefer a real earlier prompt (byte-exact, SHA-pinned, with known label defects G-02-1 and G-02-2) over a test-only second prompt, or you record a superseding decision
result: [pending]

### 5. Judgment-tier prohibitions accepted
expected: The 14 prohibitions in 03-VERIFICATION.md's Prohibitions table, each given a non-authoritative PASS by the verifier with code or test evidence, are accepted (WR-02, WR-03 and WR-05 carry caveats)
result: [pending]

## Summary

total: 5
passed: 1
issues: 0
pending: 4
skipped: 0
blocked: 0

## Gaps
