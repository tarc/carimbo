# API Coverage — Anthropic Messages API (official `Anthropic` .NET SDK 12.53.0)

> Full coverage by default. Opt-outs are explicit, reasoned decisions.
> Scope: the Messages API surface relevant to carimbo's PDF-to-typed-invoice extraction in Phase 1.
> "Production path" = `Carimbo.Llm` gateway used by `POST /eval/extractions`. The LLM-06 spike tool (`dotnet/tools/LlmSpike`, plan 01-10) exercises additional capabilities to record evidence.

| capability | decision | reason |
|---|---|---|
| messages create (non-streaming) | INTEGRATE | |
| structured outputs via output_config.format json_schema | INTEGRATE | |
| PDF document content block (base64) | INTEGRATE | |
| text content block (versioned prompt) | INTEGRATE | |
| usage accounting: input, output, cache read, cache write 5m and 1h | INTEGRATE | |
| stop reasons end_turn, max_tokens, refusal, stop_sequence and stop_details | INTEGRATE | |
| typed API errors (401, 400, 429, 529, 5xx, timeout, network) | INTEGRATE | |
| SDK retry setting (MaxRetries) and per-attempt visibility | INTEGRATE | |
| response model id (alias requested vs snapshot returned) and message id | INTEGRATE | |
| request-id capture on failures | INTEGRATE | |
| prompt caching (cache_control on the document block) | OPT-OUT | not needed yet — one attempt per case, so a cache write only adds cost; the spike confirms the usage fields; the Phase 2 repair loop adopts it |
| effort and adaptive thinking settings | OPT-OUT | not needed yet — default model is Haiku 4.5 (D-08); Sonnet 5.5 effort/thinking is recorded by the spike only; model overrides arrive with API-02 in Phase 3 |
| streaming responses | OPT-OUT | explicitly out of scope — the eval endpoint is synchronous and returns small structured JSON; streaming adds no measurable value |
| message batches | OPT-OUT | not needed yet — eval runs need per-case latency and trace IDs now; batches return results asynchronously; reconsider for cost in Phase 6 |
| token counting endpoint | OPT-OUT | not needed — response usage is authoritative for cost; three cases need no pre-flight sizing |
| tool use and tool_choice | OPT-OUT | explicitly out of scope — extraction uses structured outputs; forced tool_choice returns 400 on current Sonnet/Opus; agent tools are Milestone 2 |
| Files API (uploaded file references) | OPT-OUT | not needed — skeleton PDFs are small and sent inline; file lifecycle would add state to a stateless endpoint |
| models list endpoint | OPT-OUT | not needed — model ids are configured explicitly and the returned snapshot is recorded per call |
| sampling parameters (temperature, top_p, top_k) | OPT-OUT | explicitly out of scope — they return 400 on Sonnet 5.5 and Opus 5.5; reproducibility comes from the Phase 3 response cache |
| assistant prefill | OPT-OUT | explicitly out of scope — returns 400 on current models; structured outputs replace it |
| system prompt parameter | OPT-OUT | not needed — the versioned prompt travels as one text block next to the document; revisit with prompt versioning in Phase 3 |
| citations, web search, code execution, computer use, MCP connector | OPT-OUT | explicitly out of scope — not part of document extraction |
| Admin, usage and cost reporting API | OPT-OUT | not needed — cost is computed per call from the versioned pricing table (LLM-02) |
| extended or 1M context options | OPT-OUT | not needed — skeleton DANFEs are one to a few pages |
