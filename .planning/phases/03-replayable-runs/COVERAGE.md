# API Coverage — Anthropic Messages API (official `Anthropic` .NET SDK 12.53.0) and OTLP trace export, Phase 3

> Full coverage by default. Opt-outs are explicit, reasoned decisions.
> Scope: the Messages API surface used by the extraction path (`Carimbo.Llm` gateway behind `POST /eval/extractions`) after Phase 3 adds the response cache (D-27), the single SDK retry owner (D-28), per-request overrides (D-29), the prompt catalog (D-30) and model-call spans (D-31); plus the OpenTelemetry OTLP export to the local Jaeger viewer. Every row was re-decided from the full-coverage baseline; the Phase 2 matrix (`.planning/phases/02-validated-extraction/COVERAGE.md`) was the precedent, not a carry-over. The reason column of an INTEGRATE row names the plan that implements or proves it.

| capability | decision | reason |
|---|---|---|
| messages create (non-streaming) | INTEGRATE | |
| structured outputs via output_config.format (v2 model-facing schema) | INTEGRATE | |
| PDF document content block (base64) | INTEGRATE | |
| text content block with catalogued prompts and repair-001 | INTEGRATE | prompt catalog extract-002 and extract-003 (03-08) |
| multi-turn repair conversation (assistant then user turns) | INTEGRATE | each turn hashed into the response-cache key (03-01) |
| prompt caching: ephemeral cache_control on the PDF block | INTEGRATE | part of the response-cache key as cache_document (03-01) |
| usage per attempt: input, output, cache read, cache write 5m and 1h | INTEGRATE | stored in cache entries, span attributes and incurred cost (03-01, 03-06, 03-07) |
| stop reasons and stop_details per attempt | INTEGRATE | end_turn-only cache eligibility and the finish_reasons span attribute (03-01, 03-07) |
| typed API errors (401, 400, 429, 529, 5xx, timeout, network) | INTEGRATE | mapped to failure kinds and span error.type (03-07) |
| SDK retry policy: MaxRetries, backoff, retry-after, x-should-retry | INTEGRATE | default 2, bounded 0 to 5, the single retry owner (03-05) |
| per-attempt SDK timeout and per-attempt HTTP visibility | INTEGRATE | Llm:Anthropic:TimeoutSeconds and http_attempts |
| model selection per request among priced models | INTEGRATE | claude-haiku-4-5, its dated snapshot and claude-sonnet-5-5 through overrides.model (03-08) |
| response model id and message id | INTEGRATE | also span attributes gen_ai.response.model and gen_ai.response.id (03-07) |
| request-id capture on failures | INTEGRATE | |
| max_tokens 16000 without streaming | INTEGRATE | |
| OTLP trace export (gRPC) to Jaeger v2 | INTEGRATE | only when OTEL_EXPORTER_OTLP_ENDPOINT is set (03-07, 03-10) |
| Jaeger query API /api/traces/{id} | INTEGRATE | live trace lookup (03-10) |
| prompt-cache TTL 1h (cache_control ttl) | OPT-OUT | not needed — repair attempts follow within seconds so the 5-minute entry covers them; reruns are served by the local response cache |
| effort and adaptive thinking settings | OPT-OUT | not in the API-02 override set — requests send no thinking or effort field; adding one must add a cache-key field (the coverage test forces it); revisit when Phase 6 picks the models |
| streaming responses | OPT-OUT | not needed — the endpoint is synchronous and the 16000-token non-streaming cap was accepted live in Phase 2 |
| message batches | OPT-OUT | not needed yet — repair needs the previous answer within one request chain; reconsider for cost in Phase 6 |
| token counting endpoint | OPT-OUT | not needed — response usage is authoritative and the runner reserve covers pre-flight budgeting |
| tool use and tool_choice | OPT-OUT | not in this phase — extraction uses structured outputs; agent tool turns arrive in Phase 3.1 (D-26), which must extend LlmRequest and the cache key |
| Files API (uploaded file references) | OPT-OUT | not needed — skeleton PDFs are small and inline; the PDF SHA-256 already identifies the document in the cache key |
| models list endpoint | OPT-OUT | not needed — the versioned pricing table is the allowlist for model overrides, so an unlisted model is rejected before any call |
| sampling parameters (temperature, top_p, top_k) | OPT-OUT | explicitly out of scope — 400 on Sonnet 5.5 and Opus 5.5; reproducibility comes from the response cache (D-27) |
| assistant prefill | OPT-OUT | explicitly out of scope — 400 on current models; the gateway refuses an assistant-final conversation |
| system prompt parameter | OPT-OUT | not needed — catalogued prompts travel as user text blocks; moving them would change every cache key for no measured gain |
| citations, web search, code execution, computer use, MCP connector | OPT-OUT | explicitly out of scope — not part of document extraction; MCP arrives as our own Tools host in Phase 3.1 |
| Admin, usage and cost reporting API | OPT-OUT | not needed — cost is computed per attempt from the versioned pricing table and incurred versus notional cost is in the contract (D-29) |
| extended or 1M context options | OPT-OUT | not needed — the skeleton DANFEs are one or two pages |
| OTLP metrics and logs export | OPT-OUT | not needed — the requirement is one span per model call; latency and cost aggregates live in the eval summaries |
| HTTP client instrumentation (one span per HTTP attempt) | OPT-OUT | rejected by D-31 — it would add spans next to the one span per model call; retries show as the http_attempts attribute |
