# LLM-06 spike: gateway shape and retry ownership

Recorded by `dotnet/tools/LlmSpike --live` against the live Anthropic API (plan 01-10, decision D-11).
This record is sanitised: it holds shapes, counts, status codes and parse outcomes only. It contains no provider key,
no request header, no request body, no PDF bytes and no model text (model text is reduced to a parse outcome).

- Run date (UTC): 2026-10-07
- Contract: prompt `extract-001`, model-facing schema sha256 `7f8aaf7662ae3564170930373f1e959861b7cb133fabd9829296b02a1e4f00e2`
- Dataset: skeleton cases (case-001: 1 page, case-003: 2 pages), ground truth from the case XML
- Spike budget: US$1.00 (the Phase 1 cap is US$5.00, D-09)

## Step 1: Reachability, model snapshot, message id and usage keys (A2)

- tiny request without document: requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001`, stop_reason `end_turn`, input=14 output=4 cache_creation=0 (5m=0, 1h=0) cache_read=0 thinking_tokens=n/a, text blocks 1, thinking blocks 0, latency 0.8s, attempts=1, http statuses=[200], cost US$0.0000
- message id shape: `msg_...` (28 chars); `request-id` response header present: True
- raw `usage` keys returned by the API: [cache_creation.ephemeral_1h_input_tokens, cache_creation.ephemeral_5m_input_tokens, cache_creation_input_tokens, cache_read_input_tokens, inference_geo, input_tokens, output_tokens, service_tier]
- alias `claude-haiku-4-5` resolved to snapshot `claude-haiku-4-5-20251001` (A2 confirmed: a dated snapshot is returned).

## Step 2: Haiku 4.5 with the model-facing schema and a PDF document block (A1, A5)

The schema keywords present in the contract: `$defs`/`$ref`, `pattern`, `format: date`, `title`, `description`, `additionalProperties: false`.

- case-001 as sent (full contract schema): requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001`, stop_reason `end_turn`, input=3574 output=116 cache_creation=0 (5m=0, 1h=0) cache_read=0 thinking_tokens=n/a, text blocks 1, thinking blocks 0, latency 3.4s, attempts=1, http statuses=[200], cost US$0.0042
- schema acceptance: accepted as is. Parse outcome: parsed: true, fields equal to ground truth: 8/9, model text 301 chars (not recorded)
- input tokens for the one-page request (prompt, schema and document): 3574

## Step 3: Cache-token confirmation on the multi-page case (Pitfall 6)

Two identical requests per model with `cache_control` on the document and a byte-identical schema, on case-003 (2 pages), max_tokens 1024. The document title carries a per-run nonce so the first call is a cold cache write.
Haiku 4.5 needs a 4096-token prefix to cache; Sonnet 5.5 needs 512.

- haiku call 1: requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001`, stop_reason `end_turn`, input=201 output=116 cache_creation=10310 (5m=10310, 1h=0) cache_read=0 thinking_tokens=n/a, text blocks 1, thinking blocks 0, latency 2.5s, attempts=1, http statuses=[200], cost US$0.0137
- haiku call 2: requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001`, stop_reason `end_turn`, input=201 output=116 cache_creation=0 (5m=0, 1h=0) cache_read=10310 thinking_tokens=n/a, text blocks 1, thinking blocks 0, latency 2.1s, attempts=1, http statuses=[200], cost US$0.0018
- haiku: first call cache_creation=10310 (5m=10310, 1h=0), second call cache_read=10310; cache write then read observed: True
- approximate input tokens per extra page (case-003 minus case-001 total input): 6937 (A5). Case-003 is item-dense, so this is an upper-end figure for the skeleton, not a per-page constant.
- sonnet call 1: requested `claude-sonnet-5-5`, returned `claude-sonnet-5-5`, stop_reason `end_turn`, input=276 output=160 cache_creation=11770 (5m=11770, 1h=0) cache_read=0 thinking_tokens=0, text blocks 1, thinking blocks 0, latency 3.0s, attempts=1, http statuses=[200], cost US$0.0316
- sonnet call 2: requested `claude-sonnet-5-5`, returned `claude-sonnet-5-5`, stop_reason `end_turn`, input=276 output=159 cache_creation=0 (5m=0, 1h=0) cache_read=11770 thinking_tokens=0, text blocks 1, thinking blocks 0, latency 2.8s, attempts=1, http statuses=[200], cost US$0.0045
- sonnet: first call cache_creation=11770 (5m=11770, 1h=0), second call cache_read=11770; cache write then read observed: True

## Step 4: Sonnet 5.5 with explicit effort, then two deliberately invalid requests (A3, A4, A6)

- case-001, `OutputConfig.Format` plus `Effort.Low`, no thinking parameter, no sampling parameters, max_tokens 8192: requested `claude-sonnet-5-5`, returned `claude-sonnet-5-5`, stop_reason `end_turn`, input=4380 output=158 cache_creation=0 (5m=0, 1h=0) cache_read=0 thinking_tokens=0, text blocks 1, thinking blocks 0, latency 7.0s, attempts=1, http statuses=[200], cost US$0.0103; parsed: true, fields equal to ground truth: 9/9, model text 301 chars (not recorded)
- A3: the API reports thinking_tokens=0 inside output_tokens=158 (output_tokens includes thinking: True).
- invalid request A: `thinking: disabled` on Sonnet 5.5: AnthropicBadRequestException, HTTP 400, error type `invalid_request_error`; attempts=1, http statuses=[400]
- invalid request B: non-default `temperature` (0.5) on Sonnet 5.5: AnthropicBadRequestException, HTTP 400, error type `invalid_request_error`; attempts=1, http statuses=[400]

## Step 5: Truncation (max_tokens) and refusal paths

- case-001 with max_tokens 50: requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001`, stop_reason `max_tokens`, input=3574 output=50 cache_creation=0 (5m=0, 1h=0) cache_read=0 thinking_tokens=n/a, text blocks 1, thinking blocks 0, latency 2.6s, attempts=1, http statuses=[200], cost US$0.0038; parsed: false, fields equal to ground truth: 0/9, model text 134 chars (not recorded)
- observed stop_reason `max_tokens`; a truncated answer is not valid JSON, so strict parsing yields a typed schema failure unless the stop reason is checked first.
- refusal: it cannot be forced reliably, so it was not attempted live. The offline self-test line (e) covers the mapping with a canned payload.

## Step 6: Direct SDK versus IChatClient with the raw factory, same request

Both calls use Haiku 4.5, case-003, `cache_control` on the document, the contract schema and max_tokens 4096.

- direct SDK: stop_reason `end_turn`, input=201 output=116 cache_creation=0 (5m=0, 1h=0) cache_read=10291 thinking_tokens=n/a, message id and model reachable (28 chars, `claude-haiku-4-5-20251001`), request id reachable through `WithRawResponse`: True; parsed: true, fields equal to ground truth: 9/9, model text 301 chars (not recorded)
- IChatClient raw factory: FinishReason `stop`, ModelId `claude-haiku-4-5-20251001`, ResponseId reachable (28 chars), Usage.InputTokenCount=10492 CachedInputTokenCount=10291 OutputTokenCount=116 AdditionalCounts=[none]; parsed: true, fields equal to ground truth: 9/9
- raw `usage` from the API for the IChatClient call: input=201 cache_creation=0 (5m=0, 1h=0) cache_read=10291 output=116
- request bodies byte-identical between the two paths (compared by SHA-256 in memory, not recorded): False
  - structurally identical (same members and same values, compared in memory); the byte difference is serialisation order or whitespace only
  - the output schema is identical between the two requests: True
- usage decomposition from the ChatResponse alone (live): adapter input count equals uncached+creation+read=True; uncached input recoverable by subtraction=True; cache read matches=True. Cache creation was 0 here (the cache entry was already warm), so the creation case and the 5m/1h split come from the canned body of self-test line (d): the adapter sums creation into the input count=True and exposes the 5m/1h split=False.
- refusal mapping (canned payload, offline): direct SDK stop_reason `refusal` with stop_details category `cyber`; adapter FinishReason `content_filter`, stop_details in the response properties: False, reachable only through the raw representation: True
- request id: the HTTP `request-id` header was present on the wire: True; reachable from the direct path via `WithRawResponse`: True; visible in the ChatResponse: False

## Step 7: Retry ownership

A counting handler wraps the real handler and the client is configured with `MaxRetries = 2`. Both requests below are invalid and are rejected before inference.

- `thinking: disabled` on Sonnet 5.5: AnthropicBadRequestException, HTTP 400, error type `invalid_request_error`; attempts=1, http statuses=[400] (not retried: True)
- unknown model id: AnthropicNotFoundException, HTTP 404, error type `not_found_error`; attempts=1, http statuses=[404] (not retried: True)
- 5xx and 529 behaviour is taken from the offline self-test line (f): with `MaxRetries = 2` a 529, 529, 200 sequence makes 3 attempts and succeeds; with `MaxRetries = 0` it makes 1 attempt and throws `Anthropic5xxException`. A live 529 cannot be provoked on demand.
- The SDK hides the extra attempts unless a counting handler is installed through `AnthropicClient.HttpClient`; with it, per-attempt visibility needs no Polly.

## Step 8: Sanitised golden response bodies

Written under `dotnet/tests/Carimbo.Llm.Tests/Fixtures/` for the pricing and mapping tests in plan 01-12. Every `content[]` text, thinking and signature value is replaced by `<redacted: N chars>`; ids, model, stop_reason and usage are kept as returned.

- `messages-haiku-end-turn.json`: written
- `messages-max-tokens.json`: written
- `messages-cache-usage.json`: written

## Spend

Actual spend is computed from the returned usage with the research pricing table (US$ per MTok: Haiku 4.5 input 1.00, 5m write 1.25, 1h write 2.00, read 0.10, output 5.00; Sonnet 5.5 input 2.00, 5m write 2.50, 1h write 4.00, read 0.20, output 10.00).
The estimate is the conservative pre-call guard: every input token at US$4.00/MTok (pages x 3000 + 2000 tokens) and max_tokens at US$10.00/MTok.

| Call | Model | Worst-case estimate | Actual |
|------|-------|---------------------|--------|
| step 1 | claude-haiku-4-5 | US$0.0082 | US$0.0000 |
| step 2 | claude-haiku-4-5 | US$0.0610 | US$0.0042 |
| step 3 haiku 1 | claude-haiku-4-5 | US$0.0422 | US$0.0137 |
| step 3 haiku 2 | claude-haiku-4-5 | US$0.0422 | US$0.0018 |
| step 3 sonnet 1 | claude-sonnet-5-5 | US$0.0422 | US$0.0316 |
| step 3 sonnet 2 | claude-sonnet-5-5 | US$0.0422 | US$0.0045 |
| step 4 sonnet | claude-sonnet-5-5 | US$0.1019 | US$0.0103 |
| invalid request A: `thinking: disabled` on Sonnet 5.5 | claude-sonnet-5-5 | US$0.0082 | US$0.0000 |
| invalid request B: non-default `temperature` (0.5) on Sonnet 5.5 | claude-sonnet-5-5 | US$0.0082 | US$0.0000 |
| step 5 | claude-haiku-4-5 | US$0.0205 | US$0.0038 |
| step 6 direct | claude-haiku-4-5 | US$0.0730 | US$0.0018 |
| step 6 IChatClient | claude-haiku-4-5 | US$0.0730 | US$0.0018 |
| step 7 `thinking: disabled` on Sonnet 5.5 | claude-sonnet-5-5 | US$0.0082 | US$0.0000 |
| step 7 unknown model id | claude-sonnet-5-5 | US$0.0082 | US$0.0000 |

- Estimated (sum of worst cases for the calls made): US$0.5391
- Actual: US$0.0735
- Budget: US$1.00; within budget: True
- This counts toward the US$5.00 Phase 1 cap (D-09).
- Total spike spend across all four runs of the tool: US$0.2551 (US$0.0735, US$0.0346, US$0.0735 and this run, US$0.0735). The first three runs were repeated after tool fixes: a request-shape comparison in Step 6, a cache-write fixture choice with a per-run nonce in Step 3, and removing explicit null members from direct requests. They produced the same findings. This document and the fixtures are from the last run.

## Recommendation

Decision rule from the research: choose the direct SDK unless the IChatClient path is lossless on usage decomposition and on stop details.

- Usage decomposition lossless through the adapter (live, Step 6): false
- Stop details lossless through the adapter (canned refusal, Step 6): false
- Default `ChatResponseFormat.ForJsonSchema` path rewrites the committed schema (offline self-test line (c)); the raw factory keeps it verbatim.
- "Lossless" is judged on the ChatResponse itself. The adapter also exposes the provider `Message` through `RawRepresentation` (self-test line (e)), so the full detail can be recovered there, but that is the direct SDK type again: the adapter then adds a mapping layer and a provider-type cast without adding information.

**Recommended bottom adapter: `direct-sdk`** behind `ILlmGateway`. The IChatClient path is not lossless on at least one measure, so the rule selects the direct SDK.

Supporting evidence:

- Schema acceptance on Haiku 4.5: the API accepted the full model-facing schema unchanged (`$defs`/`$ref`, `pattern`, `format: date`, `title`); no fallback is needed.
- Sonnet 5.5 accepts `OutputConfig.Format` together with `Effort`: true.
- Haiku 4.5 wrote then read cache on the 2-page case: true; Sonnet 5.5: true.
- Approximate input tokens per page: 6937.

**Phase 1 retry ownership: `MaxRetries = 0` on the SDK client.** One HTTP attempt per call keeps cost and attempt counts exactly attributable under the US$5 cap, and the SDK does not retry a 4xx (live, Step 7: not retried = true). The offline self-test line (f) shows that a transient 529 would be retried only when `MaxRetries` is raised. A counting handler passed through `AnthropicClient.HttpClient` gives per-attempt visibility if retries are switched on later. Phase 3 (LLM-01) should own the single retry policy, not stack a second one on top.

**Model id pinning.** The alias `claude-haiku-4-5` resolved to `claude-haiku-4-5-20251001`. Send the alias during development, always record both `model_requested` and `model_returned` (the gateway response already carries both), and pin the dated snapshot for the published eval runs so a moved alias cannot change results silently.

Not edited here: `docs/DECISIONS.md`. D-21 is written in plan 01-12 after the developer's decision at the Task 3 checkpoint.
