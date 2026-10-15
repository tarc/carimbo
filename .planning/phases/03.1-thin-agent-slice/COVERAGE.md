# API Coverage — Anthropic Messages API (official `Anthropic` .NET SDK 12.53.0) and the MCP C# SDK (`ModelContextProtocol` / `ModelContextProtocol.AspNetCore` 2.2.0), Phase 3.1

> Full coverage by default. Opt-outs are explicit, reasoned decisions.
> Scope: the agent path added in Phase 3.1 (D-26): agent model calls through the `Carimbo.Llm` gateway behind `POST /eval/decisions`, the stateless MCP Tools host (`Carimbo.Tools`) and the MCP client in `Carimbo.Agent`. Every row was re-decided from the full-coverage baseline; the Phase 3 matrix (`.planning/phases/03-replayable-runs/COVERAGE.md`) was the precedent, not a carry-over. Extraction rows that Phase 3.1 does not touch keep their Phase 3 decision and are listed once for completeness. The reason column of an INTEGRATE row names the plan that implements or proves it.

| capability | decision | reason |
|---|---|---|
| messages create (non-streaming) | INTEGRATE | agent steps and extraction share one adapter (03.1-04) |
| client tool definitions (name, description, input_schema) | INTEGRATE | the four MCP tools mapped to provider tools (03.1-04, 03.1-08) |
| tool_use and tool_result turns, several tool_use blocks in one answer | INTEGRATE | results returned in one user message in block order (03.1-04, 03.1-08) |
| tool_result is_error | INTEGRATE | mapped by the adapter (03.1-04); the loop itself escalates on a tool failure instead of sending an error back (phase decision D-11) |
| structured outputs via output_config.format combined with tools | INTEGRATE | the Decision schema on every agent step, confirmed live by the probe (03.1-04, 03.1-07, D-38) |
| system prompt parameter | INTEGRATE | pinned agent-001 instructions (03.1-13, 03.1-04); extraction still sends none |
| thinking and redacted_thinking blocks echoed with signature | INTEGRATE | lets a priced thinking model chosen by override continue a tool conversation (D-37, 03.1-04); checked live by the two-step Sonnet run of the probe (03.1-07) or recorded as unproven in D-38 |
| stop reasons including tool_use, stop_details on refusal | INTEGRATE | tool_use drives the loop; refusal is a typed failure (03.1-02, 03.1-08) |
| usage per call (input, output, cache read, cache write 5m and 1h) | INTEGRATE | per step in decision-1 and on spans (03.1-09) |
| typed API errors (401, 400, 429, 529, 5xx, timeout, network) | INTEGRATE | unchanged mapping; agent steps report them as infrastructure_failure (03.1-08) |
| SDK retry policy (MaxRetries, backoff, retry-after, x-should-retry) | INTEGRATE | unchanged single retry owner (D-28); no retry in the loop |
| model selection per request among priced models | INTEGRATE | overrides.model on /eval/decisions, default claude-haiku-4-5 (03.1-09) |
| response model id and message id | INTEGRATE | per step in decision-1 (03.1-09) |
| PDF document content block | INTEGRATE | extraction only, unchanged; agent requests carry no document (03.1-02) |
| prompt caching: ephemeral cache_control | OPT-OUT | not needed for agent steps — the prefix stays under Haiku 4.5's 4096-token cache minimum (RESEARCH A8); reruns use the local cache (D-37); extraction keeps its breakpoint |
| tool_choice any or tool (forced tool use) | OPT-OUT | explicitly out of scope — 400 on the current Sonnet, Opus and Fable models; the request omits tool_choice (auto) |
| tool_choice none and disable_parallel_tool_use | OPT-OUT | not needed — tools stay available on every step and parallel calls are executed sequentially in block order |
| strict tool use (strict: true) | OPT-OUT | not needed while D-38 records the structured-output path — tool inputs are one case_id validated by the Tools host; strict use returns only with the submit_decision fallback of phase decision D-13 |
| effort and adaptive thinking settings | OPT-OUT | not in the decision-1 override set — requests send no thinking or effort field; adding one must add a cache-key field (the coverage test forces it) |
| server-side refusal fallbacks (fallbacks parameter) | OPT-OUT | explicitly out of scope — a fallback would switch the model mid-run and blur the measurement; a refusal is a typed failure |
| streaming responses | OPT-OUT | not needed — agent steps are short and non-streaming within the per-attempt timeout |
| message batches | OPT-OUT | not needed — each step depends on the previous step's answer |
| token counting endpoint | OPT-OUT | not needed — usage on each response is authoritative; the probe budgets with a character estimate and the runner with its reserve |
| SDK tool runner (beta) | OPT-OUT | explicitly out of scope — the loop is hand-written by decision (D-08) |
| MCP connector (mcp_servers on the Messages API) | OPT-OUT | explicitly out of scope — it would run the tool loop inside the API and hide it (D-08); our own MCP client reaches the Tools host |
| server tools (web search, web fetch, code execution), computer use | OPT-OUT | explicitly out of scope — the agent may only read the case through the read-only tools (D-09) |
| citations on documents | OPT-OUT | not needed — incompatible with output_config.format, and evidence is our own structured citation list checked by the grader (D-36) |
| compaction, context editing, mid-conversation system messages | OPT-OUT | not needed — at most 10 steps of small tool results |
| Files API, models list, sampling parameters, assistant prefill | OPT-OUT | unchanged Phase 3 decisions — inline content, the pricing table is the model allowlist, sampling and prefill are 400s |
| MCP server: tools/list and tools/call | INTEGRATE | four read-only case tools (03.1-06) |
| MCP Streamable HTTP transport, stateless session mode | INTEGRATE | HttpServerSessionMode.Stateless at /mcp (03.1-06) |
| MCP tool annotations (readOnly, idempotent, destructive, openWorld) | INTEGRATE | declared on every tool and enforced by the client (03.1-06, 03.1-08) |
| MCP tool error results (isError) | INTEGRATE | unknown or invalid case ids (03.1-06); mapped to tool_failed (03.1-08) |
| MCP client: initialize, list tools, call tools | INTEGRATE | one McpClient per agent run over HttpClientTransport (03.1-08) |
| MCP resources | OPT-OUT | not needed — the four tools are the whole data surface, and resources would expose the same data twice |
| MCP prompts | OPT-OUT | not needed — the agent prompt is pinned in code (agent-001) and part of the cache key |
| MCP sampling (server-initiated model calls) | OPT-OUT | explicitly out of scope — the Tools host must never call a model or spend |
| MCP elicitation | OPT-OUT | explicitly out of scope — no human in the loop during a run |
| MCP logging, progress and list-changed notifications | OPT-OUT | not needed — reads complete in milliseconds and the tool list is fixed and pinned |
| MCP completions and roots | OPT-OUT | not needed — case ids come from the user turn, not from completion |
| MCP stdio transport | OPT-OUT | not needed — the separate HTTP host is the AGT-01 boundary; a stdio entry point is not required by any criterion |
| MCP legacy SSE transport and stateful sessions | OPT-OUT | not needed — stateless Streamable HTTP by decision (D-35) |
| MCP authorization (OAuth) | OPT-OUT | not needed — loopback bind plus AllowedHosts on a read-only local host (D-35, threat T-03.1-18) |
| MCP client tools as Microsoft.Extensions.AI AIFunction | OPT-OUT | explicitly out of scope — the gateway is Carimbo-owned (D-21); tool definitions are mapped to LlmToolDefinition |
