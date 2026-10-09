# API Coverage — Anthropic Messages API (official `Anthropic` .NET SDK 12.53.0), Phase 2

> Full coverage by default. Opt-outs are explicit, reasoned decisions.
> Scope: the Messages API surface used by the production extraction path (`Carimbo.Llm` gateway behind `POST /eval/extractions`) after Phase 2 adds validation and bounded repair. Every row was re-decided from the full-coverage baseline; Phase 1 opt-outs were not carried over silently. `dotnet/tools/LlmSpike --schema-probe` (plan 02-03) records live evidence for the rows marked with a plan id.

| capability | decision | reason |
|---|---|---|
| messages create (non-streaming) | INTEGRATE | |
| structured outputs via output_config.format (v2 model-facing schema) | INTEGRATE | |
| PDF document content block (base64) | INTEGRATE | |
| text content block (versioned prompts extract-002 and repair-001) | INTEGRATE | |
| multi-turn repair conversation: assistant then user turns (02-03, 02-09) | INTEGRATE | |
| prompt caching: ephemeral cache_control on the PDF block (02-03, 02-09) | INTEGRATE | |
| usage per attempt: input, output, cache read, cache write 5m and 1h | INTEGRATE | |
| stop reasons and stop_details per attempt | INTEGRATE | |
| typed API errors (401, 400 schema, 429, 529, 5xx, timeout, network) | INTEGRATE | |
| SDK retry setting (MaxRetries) and per-attempt HTTP visibility | INTEGRATE | |
| response model id (alias requested vs snapshot returned) and message id | INTEGRATE | |
| request-id capture on failures | INTEGRATE | |
| max_tokens 16000 without streaming (02-03 probe) | INTEGRATE | |
| prompt-cache TTL 1h (cache_control ttl) | OPT-OUT | not needed — repair attempts follow within seconds, the default 5-minute ephemeral entry covers them; a 1h write costs more |
| effort and adaptive thinking settings | OPT-OUT | not needed yet — the phase runs claude-haiku-4-5 (D-19); model and effort overrides arrive with API-02 in Phase 3 |
| streaming responses | OPT-OUT | not needed — the endpoint is synchronous and the 02-03 probe checks that a non-streaming 16000-token cap is accepted; revisit only if that probe fails |
| message batches | OPT-OUT | not needed yet — repair needs the previous answer within the same request chain and per-case latency; reconsider for cost in Phase 6 |
| token counting endpoint | OPT-OUT | not needed — response usage is authoritative for cost; the runner reserve covers pre-flight budgeting |
| tool use and tool_choice | OPT-OUT | explicitly out of scope — extraction and repair use structured outputs; forced tool_choice returns 400 on current Sonnet/Opus; agent tools are Milestone 2 |
| Files API (uploaded file references) | OPT-OUT | not needed — skeleton PDFs are small and sent inline; prompt caching already avoids re-paying the document on repair |
| models list endpoint | OPT-OUT | not needed — model ids are configured explicitly and the returned snapshot is recorded per attempt |
| sampling parameters (temperature, top_p, top_k) | OPT-OUT | explicitly out of scope — 400 on Sonnet 5.5 and Opus 5.5; reproducibility comes from the Phase 3 response cache |
| assistant prefill | OPT-OUT | explicitly out of scope — 400 on current models; the repair conversation always ends on a user turn and the gateway refuses an assistant-final conversation |
| system prompt parameter | OPT-OUT | not needed — the versioned extraction and repair prompts travel as text blocks in user turns; revisit with prompt versioning in Phase 3 (EXT-05) |
| citations, web search, code execution, computer use, MCP connector | OPT-OUT | explicitly out of scope — not part of document extraction |
| Admin, usage and cost reporting API | OPT-OUT | not needed — cost is computed per attempt from the versioned pricing table (LLM-02) |
| extended or 1M context options | OPT-OUT | not needed — the skeleton DANFEs are one or two pages |
