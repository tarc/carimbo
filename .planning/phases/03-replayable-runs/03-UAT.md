---
status: testing
phase: 03-replayable-runs
source: [03-VERIFICATION.md]
started: 2026-10-15T09:33:34Z
updated: 2026-10-15T09:33:34Z
---

## Current Test

number: 1
name: Jaeger UI shows one costed chat span per model call, with no content
expected: |
  At http://localhost:16686, service carimbo-api, traces f720b1d530a4adac18a1ad9ce440ff95 (case-001) and
  d65bb5551a0bd401f1d5eb7958d8b0ab (case-002) each show a POST /eval/extractions server span with three
  `chat claude-haiku-4-5` child spans; e1408a0541e6c473885d513c65c2d63e (case-003) shows one. Each span
  carries model, token counts and carimbo.llm.cost_usd, and no prompt, answer or PDF text.
awaiting: user response

## Tests

### 1. Jaeger UI shows one costed chat span per model call, with no content
expected: Each seeding trace shows the server span with 3, 3 and 1 `chat claude-haiku-4-5` spans carrying model, tokens and carimbo.llm.cost_usd and no content (Jaeger container carimbo-jaeger-1 is still running; a free `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 just skeleton 1.00 2 read_only` regenerates traces if it was restarted)
result: [pending]

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
passed: 0
issues: 0
pending: 5
skipped: 0
blocked: 0

## Gaps
