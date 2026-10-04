# Phase 1: Walking Skeleton - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-04
**Phase:** 01-walking-skeleton
**Areas discussed:** First Invoice subset, Skeleton cases, Live runs & budget, Repo layout & commands

---

## First Invoice subset

| Question | Options | Selected |
|---|---|---|
| Invoice width | Header + parties + totals / Header + parties only / Include line items | Header + parties + totals |
| Decision record | Stub record only / Skip until M2 | Stub record only |
| Grading | Per-field exact + money tolerance / Exact match only | Per-field exact + money tolerance |
| Access key strictness | 44-char pattern only / Plain string | 44-char pattern only |

## Skeleton cases

| Question | Options | Selected |
|---|---|---|
| Case count | 3 / 5 / 1 | 3 |
| Variety | Clean, one regime, 1 multi-page / All clean single-page | Clean, one regime, 1 multi-page |
| Commit data | Commit them / Regenerate only | Commit them |

## Live runs & budget

| Question | Options | Selected |
|---|---|---|
| Model | Haiku 4.5 default + Sonnet 5.5 once / Sonnet 5.5 only / Haiku 4.5 only | Haiku 4.5 default + Sonnet 5.5 once |
| Budget | US$5 / US$2 / US$20 | US$5 |
| Who runs live calls | Add key to cloud env / I run them locally / Fake model until later | Add key to cloud env |
| Spike log | docs/spikes/ + DECISIONS entry / .planning only | docs/spikes/ + DECISIONS entry |

**Notes:** `ANTHROPIC_API_KEY` was not set in the cloud session at discussion time. The user was pointed to the environment settings to add it.

## Repo layout & commands

| Question | Options | Selected |
|---|---|---|
| Layout | dotnet/ + python/ / src/ + tests/ mixed | dotnet/ + python/ |
| Task runner | just / make / plain scripts | just |
| Non-Nix path | Documented manual install / Devcontainer too | Documented manual install |
| Agent file | AGENTS.md + CLAUDE.md imports it / You decide | AGENTS.md + CLAUDE.md imports it |

## Claude's Discretion

Recipe names, project names, JSONL fields, summary format, runner CLI flags, output paths.

## Deferred Ideas

None.
