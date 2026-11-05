---
phase: 05-comparable-measurement
plan: 11
subsystem: evals
tags: [live-run, dev-split, spend-approval, d-19]
status: blocked
---

# Phase 5 Plan 11: the one live run of the dev split Summary

Blocked on purpose: the spend is approved and recorded; the human-run paid step (Task 2) and the publication (Task 3) are still to come. This file carries `status: blocked` until Task 3 completes it, because the plan index reads any other status as a finished plan.

## Spend approval

- **Reply (verbatim):** `approve-spend`
- **Recorded at (UTC):** 2026-11-05T23:57:57Z
- **CHECK_SHA:** `127c922c2875ab11fae73a1df56c0d23df9ebb8f` (from 05-10-SUMMARY.md, `just check` exit 0)
- **Task 1 checks, both passed with that SHA:**
  - `git diff --quiet 127c922c2875ab11fae73a1df56c0d23df9ebb8f HEAD -- . ':(exclude).planning'` exit 0
  - `test -z "$(git status --porcelain -- . ':(exclude).planning')"` exit 0
- **Budget line:** Phase 5 live spend so far US$0. Per-run cap US$2.50 (`just dev-split`), phase cap US$5. Estimate US$1.2 to 2.2 on `claude-haiku-4-5`, rough, scaled from 3 skeleton cases. The test split is not run.
