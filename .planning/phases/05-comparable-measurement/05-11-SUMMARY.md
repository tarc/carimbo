---
phase: 05-comparable-measurement
plan: 11
subsystem: evals
tags: [live-run, dev-split, spend-approval, d-19, cost-cap, replay, published-runs]

requires:
  - phase: 05-comparable-measurement
    provides: the dev-split recipe and run --split (05-09), the offline full-dataset proof and CHECK_SHA (05-10), summary v4, grader-003 and compare (05-05 to 05-08)
provides:
  - evals/results/dev-split-20261106T001800Z, the live dev run, verbatim (cost cap stop, 51 of 54 dev cases)
  - evals/results/dev-split-20261106T021526Z, its read_only replay, verbatim
  - README "Phase 5 live dev run" section and the Phase 5 spend figure
affects: [05-12, 05-13, phase-06]

actuals:
  tokens: 9000
  tasks: 3
  commits: 2
plan_head_before: 09d7a0320d7d29700fc3172ea1f58d1df9ed4aaa
plan_head_after: 017cd9847267a049ac1c553b9d059583f5c71c8b

tech-stack:
  added: []
  patterns:
    - "A capped paid run that the cap stops is published as it is, marked incomplete, with the unrun cases named"

key-files:
  created:
    - evals/results/dev-split-20261106T001800Z/
    - evals/results/dev-split-20261106T021526Z/
  modified:
    - README.md

key-decisions:
  - "The replay-equality verify was corrected for a cap stop: the replay's extra cases must be exactly the selected cases the live run never recorded, each an infrastructure_failure with failure.kind cache_miss at US$0"
  - "The partial run is the published evidence; a second paid run needs a new explicit approval"

requirements-completed: []

coverage:
  - id: D1
    description: "One approved, capped live run of the dev split exists and is published verbatim; the cap stopped it at 51 of 54 dev cases"
    requirement: EVAL-05
    verification:
      - kind: integration
        ref: "diff -r evals/runs/dev-split-20261106T001800Z evals/results/dev-split-20261106T001800Z (empty); run.json spent_usd 2.30699480 <= 2.50, stopped_reason cost_cap"
        status: pass
    human_judgment: false
  - id: D2
    description: "The read_only replay made zero provider calls and reproduced all 51 recorded cases with the same statuses and invoices; its 3 extra cases are cache_miss infrastructure failures"
    requirement: EVAL-06
    verification:
      - kind: integration
        ref: "verify-replay.py dev-split-20261106T001800Z dev-split-20261106T021526Z -> live dev replay ok ... 51 plus 3 never run live"
        status: pass
    human_judgment: false
  - id: D3
    description: "compare of live against replay exits 0 and reports the cache mode as the only change; verdict no change"
    requirement: EVAL-07
    verification:
      - kind: integration
        ref: "carimbo-evals compare --first evals/runs/dev-split-20261106T001800Z --second evals/runs/dev-split-20261106T021526Z (exit 0)"
        status: pass
    human_judgment: false
  - id: D4
    description: "README links both runs, labels every figure dev, states the incomplete reason, the replay limit and the cache_miss count; secrets and synthetic-only gates pass"
    verification:
      - kind: integration
        ref: "just secrets-check (secrets ok); pytest test_readme_drift.py test_synthetic_only.py (58 passed)"
        status: pass
    human_judgment: false

duration: about 2h20m of wall time, almost all of it the human-run paid step
completed: 2026-11-06
status: complete
---

# Phase 5 Plan 11: the one live run of the dev split Summary

**The cap-stopped live run of the dev split (51 of 54 dev cases, US$2.30699480) and its free read_only replay are published verbatim under `evals/results/`, and the README states the dev figures, the incomplete reason and the replay limit.**

## Spend approval

- **Reply (verbatim):** `approve-spend`
- **Recorded at (UTC):** 2026-11-05T23:57:57Z
- **CHECK_SHA:** `127c922c2875ab11fae73a1df56c0d23df9ebb8f` (from 05-10-SUMMARY.md, `just check` exit 0)
- **Task 1 checks, both passed with that SHA:**
  - `git diff --quiet 127c922c2875ab11fae73a1df56c0d23df9ebb8f HEAD -- . ':(exclude).planning'` exit 0
  - `test -z "$(git status --porcelain -- . ':(exclude).planning')"` exit 0
- **Budget line:** Phase 5 live spend so far US$0. Per-run cap US$2.50 (`just dev-split`), phase cap US$5. Estimate US$1.2 to 2.2 on `claude-haiku-4-5`, rough, scaled from 3 skeleton cases. The test split is not run.

The approval time (2026-11-05T23:57:57Z) is earlier than the live run's `started_at` (2026-11-06T00:18:00Z).

## Runs (named by the human at Task 2, taken verbatim from the `summary:` lines)

- **LIVE_RUN:** `dev-split-20261106T001800Z`, run_id `20261106T001800Z-9e906a`, cache mode `read_write`, `started_at` 2026-11-06T00:18:00Z, `finished_at` 2026-11-06T00:38:37Z, `spent_usd` 2.30699480, `stopped_reason` `cost_cap` (the recipe exited 3), 51 cases recorded, 125 answered attempts, 0 harness errors, 0 unpriced. `incomplete` True with `incomplete_detail.cases_not_run` 3.
- **REPLAY_RUN:** `dev-split-20261106T021526Z`, run_id `20261106T021527Z-a8a10f`, cache mode `read_only`, `spent_usd` 0.00000000, 54 cases, 125 of 125 answered attempts from the cache (hit rate 1.0), notional cost US$2.30699480, `incomplete` True with 3 `infrastructure_failure`.
- **Cases not run live:** nfe-0144, nfe-0145, nfe-0146. The cap stopped the run before they were dispatched, so their answers were never stored.
- **Phase 5 spend:** US$2.30699480 of the US$5 phase cap (the replay spent nothing). The test split was not run, and no second paid run was made. A second one needs a new explicit approval.

## Verification

1. **Replay check (deviation, see below).** `uv run --project python python <scratchpad>/verify-replay.py dev-split-20261106T001800Z dev-split-20261106T021526Z` printed `live dev replay ok dev-split-20261106T001800Z dev-split-20261106T021526Z 51 plus 3 never run live` (exit 0). Every one of the 51 live cases that reached `success`, `validation_failed` or `schema_invalid` replayed with the same status and an equal invoice, and every attempt with usage had `http_attempts` 0 and `cache.status` `hit`.
2. **compare.** `carimbo-evals compare --first evals/runs/dev-split-20261106T001800Z --second evals/runs/dev-split-20261106T021526Z` exited 0. Same in both runs: dataset `dataset-001`, grader `grader-003`, tolerance 0.00. **Changed:** `cache_modes: ['read_write'] to ['read_only']` and nothing else. Cases: shared 51 (excluded: 3), only in the first run: none, **only in the second run: nfe-0144, nfe-0145, nfe-0146**. **Verdict:** exact McNemar test on the correct-invoice flag, row dev text (30 cases): **no change** (b 0, c 0, n 0, exact p 1, alpha 0.05). Field deltas: none. regressed 0, worsened 0, improved 0, **not comparable 0**.
3. **Refused and truncated count: 0.** In the live `cases.jsonl` all 125 attempts across the 51 cases have `stop_reason` `end_turn` and `cache.stored` True, and the live `summary.json` counts refused 0 and truncated 0. `compare` agrees: its not-comparable list is empty. So no live case replays as `cache_miss` for that reason. The replay's 3 `cache_miss` cases are the 3 unrun ones, a different cause.
4. **Published copies.** `cp -r` into `evals/results/` under the same names, then `diff -r evals/runs/<run> evals/results/<run>` printed nothing for each. Sizes (the sum of the four files; `du -sh` shows 3.3M each):
   - `dev-split-20261106T001800Z`: 3,429,000 bytes (cases.jsonl 2,567,754; run.json 1,406; summary.json 850,578; summary.md 9,262)
   - `dev-split-20261106T021526Z`: 3,442,476 bytes (cases.jsonl 2,573,586; run.json 1,399; summary.json 858,089; summary.md 9,402)
5. **Gates.** `devenv shell -- just secrets-check` printed `secrets ok` (run after staging the published files). `pytest python/tests/test_readme_drift.py python/tests/test_synthetic_only.py -q`: 58 passed. `git status --porcelain evals/runs` prints nothing (the run output stays git-ignored).

## Measured dev figures (from the live summary.json, all dev)

| Dev row | Cases | Invoices | Excluded | Correct | Rate | 95% interval |
|---------|-------|----------|----------|---------|------|--------------|
| dev text | 30 | 30 | 0 | 12 | 0.4000 | [0.2459, 0.5768] |
| dev image | 21 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev pooled | 51 | 30 | 0 | 12 | 0.2353 | [0.1193, 0.4114] |

Statuses over the 51 dev cases: success 16, validation_failed 35. Dev silent errors: 4 of 16 success cases (0.2500, [0.1018, 0.4950]), payment 0, other 4. The failure analysis and the wrap-hazard paragraph are plan 05-13's.

## Deviations from Plan

**1. [Rule 1 - Verify command] The plan's replay check cannot hold after a cost-cap stop**
- **Found during:** Task 3, the first verify command.
- **Issue:** the plan asserts `set(b) == set(a)`. The cap stopped the live run at 51 of the 54 selected dev cases; the replay spends 0, never reaches the cap and records all 54, so the sets differ by exactly the 3 unrun cases.
- **Fix:** the orchestrator ran a corrected script, otherwise identical to the plan's inline one (the scratchpad file is not kept). The line `assert a and set(a) <= dev and set(b) == set(a), (len(a), len(b))` became:
  ```python
  assert a and set(a) <= dev, len(a)
  assert set(a) <= set(b), sorted(set(a) - set(b))
  extras = set(b) - set(a)
  if live_state.get("stopped_reason") == "cost_cap":
      assert extras == dev - set(a), (sorted(extras), sorted(dev - set(a)))
      assert extras == set(live_state["case_ids"]) - set(a), sorted(extras)
      for case_id in sorted(extras):
          response = b[case_id]["response"]
          outcome = response["outcome"]
          assert outcome["status"] == "infrastructure_failure", (case_id, outcome["status"])
          assert outcome["failure"]["kind"] == "cache_miss", (case_id, outcome["failure"])
          assert D(response["incurred_cost_usd"]) == 0, case_id
  else:
      assert not extras, sorted(extras)
  ```
  The final print adds the count of extras (`... 51 plus 3 never run live`). A run the cap did not stop keeps the exact-equality check. The PLAN was not edited.
- **Files modified:** none (a scratchpad script).

**2. [Rule 1 - Stale claims] Three README sentences contradicted the new section**
- **Found during:** Task 3, README edit.
- **Issue:** the intro ("no model has been run on it yet"), "Not measured yet" ("No extraction run on the 154-case dataset yet ... nothing in this README is measured on it") and the "Spend" cap sentence ("Every live run is capped at US$1.00") were false once the section existed.
- **Fix:** each was changed minimally to say 51 of the 54 dev cases have a live run, the 100 test cases have none, and the dev split's cap is US$2.50. The rest of the narrative (Status row, Verification bullet, results interpretation) is left for plan 05-13 and the phase's close-out.
- **Files modified:** README.md. **Commit:** 017cd98.

## Auth gates

None for the agent: the paid step is a human-run checkpoint by design, and neither provider key was in the agent's shell.

## Task Commits

1. **Task 1: spend approval recorded** - `a2926aa` (docs)
2. **Task 2: human-run live run and replay** - no commit (run output is git-ignored)
3. **Task 3: publish both runs verbatim and the README section** - `017cd98` (feat)

## Known Stubs

None.

## Threat Flags

None. The published files hold no key (`secrets ok`) and only synthetic data (`test_synthetic_only.py` passes).

## Self-Check: PASSED

- evals/results/dev-split-20261106T001800Z and evals/results/dev-split-20261106T021526Z exist with cases.jsonl, run.json, summary.json and summary.md each, byte-identical to their evals/runs originals.
- README.md contains `### Phase 5 live dev run`, `cache_miss` and the refused-or-truncated count 0.
- Commits `a2926aa` and `017cd98` are ancestors of HEAD.
