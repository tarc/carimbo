---
title: Consider a thin MCP-plus-agent slice before Phases 4-6
created: 2026-10-09
source: external review feedback, relayed by the user
review_on: 2026-10-14 (the Wednesday session on the remaining phases)
status: pending
---

## Feedback (verbatim)

> Still no agent. The roadmap's next four phases are all extraction and
> measurement. The posting is about agents that take action with tools, so my
> earlier suggestion still stands: a thin MCP-plus-agent slice soon matters more
> than reaching 150 cases or confidence intervals.

## Where it collides with the current plan

- ROADMAP.md: Milestone 1 is extraction only (Phases 3-6 are replay, dataset,
  measurement, CI gate). "Milestones 2 and 3 (agent, durable execution) are not
  part of this roadmap."
- PROJECT.md Out of Scope: Milestone 2 (MCP tools, agent loop, decision evals)
  is deferred to a later milestone via `/gsd-new-milestone`.
- The audience (PROJECT.md) is reviewers for an "Operational AI / Agents
  Platform" team, so the absence of any agent is the visible gap.

## Decisions already in place that a thin slice must honour

- D-08: hand-written agent loop (call model, run tool, append result, repeat),
  step budget, typed final `Decision`; no agent framework.
- D-09: MCP tools are read-only; the agent outputs approve / reject / escalate
  with reason and evidence; executing the decision is not a tool.
- D-10: uncertainty escalates. D-16: LLM-as-judge is calibrated.
- STACK: `ModelContextProtocol.AspNetCore` 2.2.0, separate `Tools` host,
  Streamable HTTP, stateless, read-only tools.

## Options to weigh on Wednesday (not decided)

1. Insert a thin vertical slice now (e.g. Phase 3.5 or reorder so it precedes
   Phase 4): a `Tools` MCP host with one or two read-only tools over the
   extracted invoice and validator findings, the D-08 loop, a typed `Decision`,
   and a handful of scripted decision cases graded like extraction. Measured,
   small, uses the existing eval endpoint and fake model seam.
2. Keep Phase 3 (replay/cache, single retry, traces) first, since a cached and
   traced gateway makes agent runs reproducible and cheap; then the agent
   slice; then shrink Phases 4-6 (fewer than 150 cases, intervals later).
3. Keep the roadmap as is and start Milestone 2 earlier by cutting Phase 5/6
   scope.

Open questions: which reference data the read-only tools need (Postgres was
deferred to M2), how decision ground truth is labelled for synthetic cases,
and whether the slice needs Phase 3's response cache to stay within budget.

## Next step

Raise in the Wednesday session; if accepted, `/gsd-phase` to insert or reorder,
record a superseding decision in docs/DECISIONS.md, then
`/gsd-discuss-phase` for the slice.
