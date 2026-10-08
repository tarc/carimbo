---
phase: 02-validated-extraction
plan: 10
subsystem: evals
tags: [live-run, repair-loop, skeleton, justfile, runner, claude-haiku-4-5]

requires:
  - phase: 02-validated-extraction
    provides: "02-03 live gateway and probe spend; 02-07 reworked skeleton cases; 02-09 bounded repair loop and effective.max_repairs on the wire"
provides:
  - "`just skeleton [max_cost] [max_repairs]` (default 1.00 and 2) exporting Extraction__MaxRepairs to the Api it starts; `just skeleton 1.00 0` disables repair"
  - "Runner defaults fit a repair chain: 900 s read timeout (5 s connect) and a 0.25 USD default per-case reserve"
  - "Two recorded live runs on claude-haiku-4-5 over the three reworked cases: max_repairs 2 and max_repairs 0"
affects: [Phase 3 response cache and replay fixtures, Phase 6 repair ablation, 02 verification]

actuals:
  tokens: 3500
  tasks: 2
  commits: 2
plan_head_before: 34ca109aa5e9c85d6acde9bb5fb002f8f3ff9658
plan_head_after: 9ed298efd960eee07ec310fbf229e438f86c2c21
commits: 2

tech-stack:
  added: []
  patterns:
    - "Repair budget is configuration of the Api process (Extraction__MaxRepairs), never a per-request override (D-19)"

key-files:
  created: []
  modified:
    - justfile
    - python/src/carimbo_evals/runner.py
    - python/tests/test_runner.py
    - python/tests/test_repo_layout.py
    - README.md
    - AGENTS.md

key-decisions:
  - "Run output stays git-ignored; this SUMMARY is the committed record of the two live runs"
  - "The 2-vs-0 difference on case-001 is run-to-run model variance, not repair: nothing was repaired in either run"

patterns-established:
  - "Tracer first: the recipe parameter and runner timeout were verified by tests and `just --dry-run` before any paid call"

requirements-completed: [EXT-03, EXT-04, API-01]

status: complete
---

# Phase 2 Plan 10: the reworked skeleton runs live with and without repair

**`just skeleton [max_cost] [max_repairs]` now carries the repair budget to the Api, and the full target, validators and bounded repair loop ran live on claude-haiku-4-5 for US$0.1485 across both runs. Repair repaired zero cases: the one case it was given to fix (a misread 44-digit access key) stayed broken across all three attempts.**

## Performance

- Tasks: 2 (1 tracer with RED then GREEN, 1 live-run task)
- Paid calls: 2 recorded runs, 8 provider attempts in total (5 with repair, 3 without)
- Files modified: 6 (no run output committed)

## Accomplishments

- Recipe: `skeleton max_cost="1.00" max_repairs="2"` and `_skeleton-run max_cost max_repairs`; the Api process starts with `Extraction__MaxRepairs={{ max_repairs }}`. `just --dry-run _skeleton-run 1.00 0` prints `Extraction__MaxRepairs=0`, the default prints `=2`. Documented in README.md and AGENTS.md.
- Runner: `DEFAULT_RESERVE_USD = Decimal("0.25")` and `httpx2.Timeout(900.0, connect=5.0)`, so three sequential 300 s provider attempts fit one HTTP read and a three-attempt case cannot slip past the cost cap.
- Live evidence for EXT-03, EXT-04 and API-01 against the real model, with and without repair.

## Task Commits

1. Task 1 RED: `95d0d09` test(02-10): repair-chain timeout, reserve and skeleton repair budget
2. Task 1 GREEN: `9ed298e` feat(02-10): skeleton takes a repair budget; runner fits a full repair chain
3. Task 2: no file change (live runs only; run directories are git-ignored)

## Live runs

Both runs: model requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001`; prompt `extract-002`, repair prompt `repair-001`; schema sha256 `87b9283d...2932098`; dataset `skeleton-002`; grader `grader-002`; pricing `anthropic-2026-10-04`; reference date 2026-10-01; 0 unpriced cases; 0 harness errors. Run directories (git-ignored): `evals/runs/skeleton-20261008T183505Z` (max_repairs 2) and `evals/runs/skeleton-20261008T183611Z` (max_repairs 0).

### Run A: max_repairs 2 (cost US$0.08433365, cap 1.00)

| case | status | attempts | attempt statuses | errors / warnings, rule ids | fields | input | output | cache read | cache write | cost USD | latency ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| case-001 | success | 1 | success | 0 / 0, none | 27/27 | 891 | 612 | 0 | 6326 | 0.01185850 | 8404 |
| case-002 | validation_failed | 3 | validation_failed x3 | 3 / 0, KEY_CHECK_DIGIT, KEY_NUMBER_MISMATCH, KEY_SERIES_MISMATCH | 26/27 (access_key wrong) | 5841 | 2343 | 12994 | 6497 | 0.02697665 | 25395 |
| case-003 | success | 1 | success | 0 / 0, none | 27/27 | 891 | 6093 | 0 | 11314 | 0.04549850 | 49586 |

Totals: input 7623, output 9048, cache read 12994, cache write 24137 (all 5-minute), latency min / median / max 8404 / 25395 / 49586 ms. Attempts: 5 total, max 3, repaired 0. Invoice total delta 0.00 in all cases.

### Run B: max_repairs 0 (cost US$0.06424000, cap 1.00)

| case | status | attempts | attempt statuses | errors / warnings, rule ids | fields | input | output | cache read | cache write | cost USD | latency ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| case-001 | validation_failed | 1 | validation_failed | 1 / 0, KEY_CHECK_DIGIT | 26/27 (access_key wrong) | 7217 | 612 | 0 | 0 | 0.01027700 | 7957 |
| case-002 | validation_failed | 1 | validation_failed | 3 / 0, KEY_CHECK_DIGIT, KEY_NUMBER_MISMATCH, KEY_SERIES_MISMATCH | 26/27 (access_key wrong) | 7388 | 781 | 0 | 0 | 0.01129300 | 9909 |
| case-003 | success | 1 | success | 0 / 0, none | 27/27 | 12205 | 6093 | 0 | 0 | 0.04267000 | 49269 |

Totals: input 26810, output 7486, no cache read or write, latency min / median / max 7957 / 9909 / 49269 ms. Attempts: 3 total, max 1, repaired 0.

### Comparison of max_repairs 2 against 0

- **Cases repaired: 0.** Run A has `attempts.repaired = 0`. Its only multi-attempt case (case-002) failed all three attempts. The first-attempt status of every case in run A is its final status.
- **Cases still caught: 1 of 3 with repair, 2 of 3 without.** The gap is case-001, and it is not an effect of repair. In run A case-001's first attempt was correct (access key `...4198220840`); in run B the same case, same prompt and schema, came back with one wrong digit (`...4199220840`, KEY_CHECK_DIGIT). With no sampling control on these models that is run-to-run variance. Run A's `success` for case-001 must not be read as first-pass accuracy bought by repair; its per-attempt statuses show it needed none. Per-attempt statuses are kept in the records for exactly this reason (EXT-03 transparency).
- **First-pass (attempt 0) view across both runs:** case-001 passed once and failed once, case-002 failed both times with the same three rules, case-003 passed both times. With n=1 per cell this says only that access-key transcription is unstable on Haiku 4.5.
- **Extra cost of repair:** the two extra case-002 attempts cost US$0.00650 and US$0.00756 (first attempt US$0.01292), so US$0.02698 against US$0.01129 for the single attempt without repair: US$0.0157 spent for no gain. Because the repair-enabled Api sets `CacheDocument` (cache write premium on the first request), single-attempt cases are also dearer in run A: case-001 0.01186 against 0.01028 (+15%), case-003 0.04550 against 0.04267 (+7%; different attempts of the same case, so indicative only). Total US$0.0843 against US$0.0642.
- **Extra latency of repair:** case-002 25395 ms with three attempts against 9909 ms with one. Cases that needed no repair were unaffected (case-003 49.6 s against 49.3 s).
- **Cache behaviour:** run B recorded no cache write or read at all; run A wrote cache on every case and read it on the two repair attempts of case-002 (12994 tokens), which is the 02-09 rule `CacheDocument = MaxRepairs > 0` working as designed. The repair attempts' input was served mostly from cache.

### Cumulative Phase 2 live spend

US$0.0526 (02-03 probe, three runs) + US$0.08433 (run A) + US$0.06424 (run B) = **US$0.2011**, under the US$5 phase cap. Each run was far under the US$1.00 per-run cap. A Phase 1-era run directory (`skeleton-20261008T001300Z`, dataset skeleton-001, prompt extract-001, US$0.0195) also exists in `evals/runs`; counting it as well gives US$0.2206, still far under the cap.

## Findings for verification (recorded, not patched here)

1. **Access-key transcription fails on case-002 and the repair loop cannot fix it.** The model reads the 44-digit key from the DANFE with series `000` and a shifted number (`600098396`), against expected series 6 and number 983968; the three attempts produced three different wrong keys (`...550006000983968114303869`, `...899`, `...811430399`) with a different check digit each time. The repair feedback correctly uses a fixed per-rule sentence without values for check-digit and key rules (02-09 disclosure policy), so the model has nothing to anchor on except re-reading the image. Candidates: a barcode-decoded key as an input or cross-check (the Code 128 decode in the stack is planned for Phase 3 and D-05), a stronger transcription instruction, or a different model. This is a model/pipeline defect for the verifier and Phase 3/6, not a harness defect.
2. **case-003 passed with an alphanumeric CNPJ** (`OT350N9B000164`, issuer.cnpj and access key `432608OT350N9B000164...`) and `ISENTO` IE: 27/27, no errors, in both runs. Not truncated at the 16000-token limit (output 6093 tokens, 49 s).
3. **Run-to-run variance is visible at n=1.** case-001 differs between runs on the same inputs; a statement about repair effect needs repeated runs and the cache/replay mechanism (Phase 3) and the Phase 6 ablation.
4. **Single-attempt cost with repair enabled is higher** because of the document cache write; relevant to the Phase 6 cost-quality table.

## Deviations from Plan

None - the plan was executed as written. Neither run needed a recipe fix, both completed on the first try, and no run was retried.

Minor: the new Yoda-style assertion in `test_runner.py` was rewritten to satisfy ruff `SIM300` before the GREEN commit (test style, no behaviour change).

## Issues Encountered

None.

## Verification

- `devenv shell -- just check` exits 0 after Task 1 (.NET 527 tests, pytest 280 passed, schema and datagen checks, e2e 5 passed, docs and secrets checks).
- Task 2 verify: both automated commands passed (`live runs ok`, no `sk-ant-` or `x-api-key` in `evals/runs`); `git status --porcelain evals` prints nothing.
- No key value was printed, logged or written; the key resolved through `_with-provider-key`.

## Known Stubs

None.

## Threat Flags

None. No new network endpoint, auth path or trust boundary; the recipe sets one environment variable for the locally started Api.

## Next Phase Readiness

Phase 2 plans are complete. For verification: finding 1 (case-002 access key) is an open quality gap that the repair loop does not close; the live evidence for EXT-03 shows the loop is bounded and its attempts are visible, not that it improves accuracy.

## Self-Check: PASSED

- `95d0d09` and `9ed298e` are ancestors of HEAD.
- `justfile`, `python/src/carimbo_evals/runner.py`, `python/tests/test_runner.py`, `python/tests/test_repo_layout.py`, `README.md`, `AGENTS.md` exist and carry the changes.
- Both run directories hold `summary.json`, `summary.md`, `cases.jsonl` and `run.json`.
