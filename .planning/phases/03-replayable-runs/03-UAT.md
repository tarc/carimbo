---
status: testing
phase: 03-replayable-runs
source: [03-VERIFICATION.md]
started: 2026-10-15T10:44:06Z
updated: 2026-10-15T10:44:06Z
---

Re-test round after gap closure 03-11 (G-03-1, G-03-2). The previous round (3 passed, 2 issues, diagnosed) is in git history at 67f032d; tests 3 to 5 passed there and are not repeated.

## Current Test

number: 1
name: Jaeger UI labels repair calls (G-03-1 re-test)
expected: |
  With Jaeger up (`docker compose up -d`), run the free replay
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 just skeleton 1.00 2 read_only`
  (zero provider calls) and open a trace with repair attempts at
  http://localhost:16686/trace/<trace_id>. The first chat span shows
  carimbo.llm.prompt_version extract-003 and no repair_prompt_version; each repair
  span shows carimbo.llm.repair_prompt_version repair-001 beside extract-003, agreeing
  with attempts[].prompt_version in the eval record. No prompt or answer content on any span.
awaiting: user response

## Tests

### 1. Jaeger UI labels repair calls (G-03-1 re-test)
expected: First span extract-003 only; each repair span carries carimbo.llm.repair_prompt_version repair-001 beside extract-003, matching attempts[].prompt_version; no content on spans.
result: [pending]

### 2. README cache-key list and trace-lookup paragraph read clearly (G-03-2 re-test)
expected: README line 31 (Response cache, D-27) lists the real cache-key fields (incl. max tokens and replicate salt, no effort) and the trace-lookup paragraph (~line 192) explains how to tell a repair call, in clear, honest wording.
result: [pending]

## Summary

total: 2
passed: 0
issues: 0
pending: 2
skipped: 0
blocked: 0

## Gaps
