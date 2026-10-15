---
status: complete
phase: 03-replayable-runs
source: [03-VERIFICATION.md]
started: 2026-10-15T10:44:06Z
updated: 2026-10-15T13:29:22.342Z
---

Re-test round after gap closure 03-11 (G-03-1, G-03-2). The previous round (3 passed, 2 issues, diagnosed) is in git history at 67f032d; tests 3 to 5 passed there and are not repeated.

## Current Test

[testing complete]

## Tests

### 1. Jaeger UI labels repair calls (G-03-1 re-test)
expected: First span extract-003 only; each repair span carries carimbo.llm.repair_prompt_version repair-001 beside extract-003, matching attempts[].prompt_version; no content on spans.
result: pass
note: "Run by Claude on user request: read_only replay run 20261015T130529Z-067fc6 (US$0 incurred, 7/7 cache hits). Jaeger API traces e32e1e5d (case-001) and 278659c9 (case-002): span 1 prompt_version extract-003 only, spans 2-3 add repair_prompt_version repair-001; case-003 (47405018) single extract-003 span. Matches attempts[].prompt_version (extract-003, repair-001, repair-001). No message/content attributes or span logs."

### 2. README cache-key list and trace-lookup paragraph read clearly (G-03-2 re-test)
expected: README line 31 (Response cache, D-27) lists the real cache-key fields (incl. max tokens and replicate salt, no effort) and the trace-lookup paragraph (~line 192) explains how to tell a repair call, in clear, honest wording.
result: pass
note: "User found 'the document cache flag' unclear (it conflated Anthropic prompt caching with the D-27 response cache) and asked for a direct fix: README line 31 now reads 'whether repair has marked the PDF for Anthropic prompt caching'. Phrase appeared nowhere else outside historical planning records; no gate asserts it. Line 192 followed by Claude on user request for case-002: trace_id 278659c9... identical in summary.md and cases.jsonl; /trace/<id> served HTTP 200; trace (via Jaeger API) has 3 chat claude-haiku-4-5 spans with model, token counts and carimbo.llm.cost_usd; span 1 extract-003 only, spans 2-3 add repair_prompt_version repair-001, matching attempts[].prompt_version; no content attributes or span logs."

## Summary

total: 2
passed: 2
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps
