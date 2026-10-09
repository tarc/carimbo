# Phase 2 schema probe: v2 model-facing schema and a cached two-turn conversation

Recorded by `dotnet/tools/LlmSpike --schema-probe` against the live Anthropic API (plan 02-03).
This record is sanitised: it holds shapes, counts, status codes and parse outcomes only. It contains no provider key,
no request header, no request body, no PDF bytes and no model text (model text is reduced to a parse outcome and a character count).

- Run date (UTC): 2026-10-08
- Model: `claude-haiku-4-5`
- Contract: prompt `extract-002`, model-facing schema sha256 `87b9283dee13bf8d7729e3c81c424fda9f8e323b740943cb3621d2d2f6932098` (10106 bytes)
- Document: case-001.pdf, 5305 bytes, one page
- Probe budget: US$0.2500; the call is skipped when its worst case would pass the budget
- Counts toward the US$5 Phase 2 cap (D-19). The cumulative Phase 2 spend is tracked in the plan summary.

## Request shapes

What each request carries, derived from the same builders the run uses (`+cache` marks an ephemeral `cache_control` breakpoint):

- (a) text probe: one user message with a text block; `output_config.format` = the committed model-facing schema; max_tokens 4096; no sampling, tool, thinking or system fields
- (b1) first call: user[document+cache,text]; max_tokens 16000; CacheDocument True
- (b2) repair call: user[document+cache,text] assistant[text] user[text]; max_tokens 16000; CacheDocument True

## Result

### (a) Grammar acceptance of the v2 model-facing schema (text only)

- v2 schema accepted: **yes** (HTTP 200)
- stop_reason `end_turn`, returned model `claude-haiku-4-5-20251001`
- usage: input=3551 output=369 cache_read=0 cache_write_5m=0 cache_write_1h=0
- latency 19.1s, HTTP attempts 1, cost US$0.0054
- parsed: true, pattern violations: 0, items 1, model text 941 chars (not recorded); schema-valid invoice: yes

### (b) Two-turn conversation over case-001.pdf with a cached document block

- (b1) first call: accepted (HTTP 200), stop_reason `EndTurn`, returned model `claude-haiku-4-5-20251001`
  - usage: input=891 output=607 cache_read=6321 cache_write_5m=0 cache_write_1h=0
  - latency 7.4s, HTTP attempts 1, cost US$0.0046
  - parsed: true, pattern violations: 0, items 3, model text 1501 chars (not recorded); schema-valid invoice: yes
- (b2) repair call: accepted (HTTP 200), stop_reason `EndTurn`, returned model `claude-haiku-4-5-20251001`
  - usage: input=1524 output=607 cache_read=6321 cache_write_5m=0 cache_write_1h=0
  - latency 5.8s, HTTP attempts 1, cost US$0.0052
  - parsed: true, pattern violations: 0, items 3, model text 1501 chars (not recorded); schema-valid invoice: yes
- second text equals the first: no

## Spend

Actual spend is computed from the returned usage with the embedded pricing table (version `anthropic-2026-10-04`). The estimate is the pre-call guard: every input token counted at the 5-minute cache-write rate, tokens estimated at two characters each, and max_tokens counted in full at the output rate.

| Call | Worst-case estimate | Actual | Note |
|------|---------------------|--------|------|
| (a) text probe | US$0.0323 | US$0.0054 | - |
| (b1) first call | US$0.0918 | US$0.0046 | - |
| (b2) repair call | US$0.0928 | US$0.0052 | - |

- Total: US$0.0151
- Budget: US$0.2500; within budget: True
- This run counts toward the US$5.00 Phase 2 cap (D-19).

## Assumptions settled

- A1 (enum without `type`, and the `tax_id` alternation pattern, accepted by the structured-output grammar): yes. Check (a) sends the committed schema, which carries both.
- A2 (14-field item array with about nine patterned strings per row stays under the grammar-complexity limit): yes. A 400 here would name the schema; see check (a).
- A4 (no SDK pre-flight guard against a non-streaming request with max_tokens 16000): yes. The first gateway call was sent once, non-streaming.
- A5 (`Role.Assistant` exists and a text-block list is accepted as an assistant turn): yes. It compiled, and the repair call carried an assistant turn.
- Both gateway turns returned a schema-valid invoice: first yes, second yes.
- Cache: the first call wrote 0 and read 6321 cache tokens; the repair call read 6321; the repair turn read the cached prefix. A first-call read above zero means an earlier run within five minutes had already written the same prefix. The Haiku 4.5 minimum is a 4096-token prefix, and the cached prefix covers the schema and the prompt as well as the document.
- Cold-cache reading, from an earlier run of the same probe (same schema, prompt and document, 2026-10-08): the first call wrote 6321 cache tokens (5m) and read 0, and the repair call read 6321. The run above hit the prefix that run had written, which is why its first call shows a read. Added by hand; the probe cannot force a cold cache.
