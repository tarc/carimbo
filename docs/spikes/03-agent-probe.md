# Agent probe (phase decision D-13)

Recorded by `dotnet/tools/LlmSpike --agent-probe` (plan 03.1-07). This record is sanitised: it holds shapes, counts, status codes,
tool names and parse outcomes only. It contains no provider key, no prompt, no tool result and no model text.

## Setup

- Run date (UTC): 2026-10-22
- Mode: live (real AnthropicLlmGateway, MaxRetries 0, no cache)
- Runs: `claude-haiku-4-5` on `dc-001` (max 6 steps, MaxTokens 8192); `claude-haiku-4-5` on `dc-003` (max 6 steps, MaxTokens 8192); `claude-sonnet-5-5` on `dc-001` (max 2 steps, MaxTokens 2048, thinking-echo check)
- Agent prompt `agent-001` sha256 `6264c308385b65655ed541818a0a64c3956e1a368e2f9ddbcfbe17a6632e8e3a`
- Decision model-facing schema sha256 `34c5ef7869984da2491b8ad907ee014abec6db7368120bdb24b87e41c97a7bde` (equals the committed schema/decision.model.schema.json)
- Tools: get_extraction_status, get_invoice, get_validator_findings, get_policy
- Budget: US$0.1000; a call is skipped when the spend so far plus its pre-flight estimate (input characters / 3 as input tokens plus MaxTokens as output tokens, at the embedded pricing table `anthropic-2026-10-04`) would pass it
- Counts toward the US$5.00 Phase 3.1 cap.

## Calls

| Model | Case | Step | Outcome | Stop reason | Block kinds | Tools called | Input | Output | Cache read | Cache write | Estimate | Cost |
|-------|------|------|---------|-------------|-------------|--------------|-------|--------|------------|-------------|----------|------|
| claude-haiku-4-5 | dc-001 | 1 | HTTP 200 | tool_use | tool_use,tool_use,tool_use,tool_use | get_extraction_status,get_invoice,get_validator_findings,get_policy | 1858 | 161 | 0 | 0 | US$0.0424 | US$0.0027 |
| claude-haiku-4-5 | dc-001 | 2 | HTTP 200 | end_turn | text | - | 2826 | 193 | 0 | 0 | US$0.0431 | US$0.0038 |
| claude-haiku-4-5 | dc-003 | 1 | HTTP 200 | tool_use | tool_use,tool_use,tool_use,tool_use | get_extraction_status,get_invoice,get_validator_findings,get_policy | 1858 | 161 | 0 | 0 | US$0.0424 | US$0.0027 |
| claude-haiku-4-5 | dc-003 | 2 | HTTP 200 | end_turn | text | - | 3054 | 152 | 0 | 0 | US$0.0433 | US$0.0038 |
| claude-sonnet-5-5 | dc-001 | 1 | HTTP 200 | tool_use | tool_use,tool_use,tool_use,tool_use | get_extraction_status,get_invoice,get_validator_findings,get_policy | 2059 | 184 | 0 | 0 | US$0.0234 | US$0.0060 |
| claude-sonnet-5-5 | dc-001 | 2 | HTTP 200 | end_turn | thinking,text | - | 3271 | 331 | 0 | 0 | US$0.0248 | US$0.0099 |

## Result

path: structured_output

- `claude-haiku-4-5` on `dc-001`: first request accepted (HTTP 200); tools before answering: yes; final answer: parsed: true, outcome Approve, cause None, 6 citations; every evidence source was a tool it called: yes
- `claude-haiku-4-5` on `dc-003`: first request accepted (HTTP 200); tools before answering: yes; final answer: parsed: true, outcome Escalate, cause PolicyWarning, 4 citations; every evidence source was a tool it called: yes
- `claude-sonnet-5-5` on `dc-001` (echo check, not part of the path): first request accepted (HTTP 200); final answer: parsed: true, outcome Approve, cause None, 6 citations

thinking_echo: unverified (no_thinking_block)

- Total spend: US$0.0287 of US$0.1000; within budget: yes

## Recommendation

Keep the planned loop: tools on every step and the Decision schema as `output_config.format`; an answer without tool calls is the final answer.
