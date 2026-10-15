---
title: Consider a thin MCP-plus-agent slice before Phases 4-6
created: 2026-10-09
source: external review feedback, relayed by the user
review_on: 2026-10-14 (the Wednesday session on the remaining phases)
status: completed
completed: 2026-10-15
resolution: option 2, recorded as D-26 in docs/DECISIONS.md
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

## Outcome (2026-10-15)

Option 2 chosen on 2026-10-15 and recorded as D-26 in `docs/DECISIONS.md`.
Phase 3 (Replayable Runs) stays first, because its response cache and costed
traces make agent runs reproducible and cheap. A thin MCP-plus-agent slice
follows as an inserted Phase 3.1. Phases 4-6 are scoped down later; the exact
cuts are decided when each of those phases is discussed.

- Requirements AGT-01 to AGT-06 in `.planning/REQUIREMENTS.md` (Phase 3.1,
  Pending); the scope change is in `.planning/PROJECT.md`.
- The open questions above: the slice's tools read the extracted invoice and
  its validator findings, so it needs no Postgres reference data; each
  decision case carries one expected decision labelled by a documented rule,
  and the Phase 3.1 discussion designs the labelling; the slice runs after
  Phase 3, so it uses the response cache.
- Still to do outside this todo: `/gsd-phase --insert 3` for Phase 3.1
  (including the ROADMAP overview sentence that keeps agent work out of the
  roadmap), then `/gsd-discuss-phase 3.1`.
