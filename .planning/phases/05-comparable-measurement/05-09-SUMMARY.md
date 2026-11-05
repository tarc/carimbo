---
phase: 05-comparable-measurement
plan: 09
subsystem: evals
tags: [cli, split, regrade, published-run-guard, just-recipe, d-19, eval-05]

requires:
  - phase: 05-comparable-measurement
    provides: grade_run(resamples=) and summary v4 (05-07), compare.py and its regrade hint (05-08), the dev/test split in data/dataset/manifest.json (04)
provides:
  - runner.read_case_splits and `carimbo-evals run --split dev|test`
  - `carimbo-evals grade --out DIR` and `--resamples N`, with a guard that refuses a regrade into a published summary under evals/results
  - the `just dev-split [max_cost] [cache_mode]` recipe (default cap US$2.50), its layout pins and its AGENTS.md / README.md rows
  - compare's REGRADE_HINT restored to the planned sentence ending in `--out DIR`
affects: [05-10, 05-11, 05-13, phase-06]

actuals:
  tokens: 14000
  tasks: 3
  commits: 3
plan_head_before: 8a101e9dff039ef61c7b64e89f46acf54f767b8d
plan_head_after: 1e016af07b2da726080ada2cd471dabcd108d864

tech-stack:
  added: []
  patterns:
    - "A published run is a directory under evals/results that already holds summary.json; the CLI refuses to write there, and regrades go to a new --out directory"
    - "The one live path for a split is a justfile recipe; a layout test pins its arguments and that `--split test` appears nowhere in the justfile"

key-files:
  created: []
  modified:
    - python/src/carimbo_evals/runner.py
    - python/src/carimbo_evals/cli.py
    - python/src/carimbo_evals/compare.py
    - python/tests/test_runner.py
    - python/tests/test_grader.py
    - python/tests/test_repo_layout.py
    - justfile
    - AGENTS.md
    - README.md

key-decisions:
  - "--split is validated against SPLITS = (dev, test) with a BadParameter before any request; a manifest with no splits exits 2 naming manifest.json"
  - "The published-run guard checks the output directory (--out, or the run directory when --out is absent), so it covers both a plain regrade and a --out pointing onto a published summary"
  - "The regrade hint's flags are pinned against the real options of the grade command, so the hint cannot drift from the CLI again"

patterns-established:
  - "Tests hash every file of a run directory (SHA-256) before and after a regrade to prove it is verbatim"

requirements-completed: []

coverage:
  - id: D1
    description: "run --split dev selects exactly the manifest's dev cases (54 of 154), combines with --case, and exits 2 on a manifest without splits or an unknown split"
    requirement: EVAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_runner.py#test_read_case_splits_of_the_dataset_has_54_dev_and_100_test_cases"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_cli_split_dev_runs_only_the_dev_cases"
        status: pass
      - kind: unit
        ref: "python/tests/test_runner.py#test_cli_split_over_a_directory_without_splits_exits_2_naming_the_manifest"
        status: pass
    human_judgment: false
  - id: D2
    description: "grade --out and --resamples; a stored run keeps its bytes; a published summary under evals/results is never rewritten"
    requirement: EVAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_grader.py#test_grade_out_writes_the_summary_elsewhere_and_leaves_the_run_untouched"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_grade_refuses_to_rewrite_a_published_run_and_names_out"
        status: pass
      - kind: unit
        ref: "python/tests/test_grader.py#test_grade_resamples_is_recorded_in_the_summary"
        status: pass
    human_judgment: false
  - id: D3
    description: "the capped just dev-split recipe, with its replay, pinned by a layout test and documented; no live call made"
    requirement: EVAL-05
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_dev_split_recipe_is_capped_to_the_dev_cases"
        status: pass
      - kind: integration
        ref: "devenv shell -- just py-check"
        status: pass
    human_judgment: true
    rationale: "The recipe itself (Api start, health wait, live run) is verified by reading and by its static pins only; it is deliberately not run in this plan, and plan 05-11 holds the paid run"

duration: 11min
completed: 2026-11-05
status: complete
---

# Phase 5 Plan 09: run a split, regrade without touching a published run, and the capped dev recipe Summary

**`carimbo-evals run --split dev` takes the 54 dev cases from the dataset manifest, `grade --out` and `--resamples` regrade a stored run into a new directory with a guard that refuses to rewrite a published run under evals/results, and `just dev-split` (cap US$2.50, replayable with `read_only`) is the single pinned and documented live path for the dev split; nothing was spent.**

## Performance

- **Duration:** 11 min (22:13Z to 22:24Z)
- **Tasks:** 3 (one tracer, two auto; the tracer and Task 2 were tdd)
- **Files:** 9 modified, none created

## Accomplishments

- Tracer: `read_case_splits(data/dataset)` returns 154 entries, 54 `dev` and 100 `test`; `data/skeleton`, a missing manifest, malformed JSON and entries with non-string fields return `{}`. Over a mock transport `--split dev` ran only the dev ids and `run.json` `case_ids` lists them; `--split dev --case <test id>` exits 2 with the unknown-id message; a directory with no splits exits 2 naming `manifest.json`; `--split bogus` exits 2. The 100 pre-existing test_runner tests pass unchanged. The tracer's `<verify>` is automated only, so it was re-run (107 passed) before expansion.
- `grade --out DIR` creates DIR and writes `summary.json` and `summary.md` there; a test hashes every file of the run directory with SHA-256 before and after. `grade --resamples N` (min 1) reaches `grade_run` and is recorded as `stats.bootstrap.resamples`.
- Guard: `_is_published_summary(target)` is true for a directory with the consecutive path parts `evals/results` that already holds `summary.json`. `grade --run <published>` exits 2 and names `--out`; `--out <published>` exits 2; `--out <new sibling>` exits 0; `summary.json` keeps its bytes. `git status --porcelain evals/results` is empty after the suite.
- `just dev-split max_cost="2.50" cache_mode="read_write"` goes through `_with-provider-key` to `_dev-split-run`, a copy of `_skeleton-run` with `dataset-check`, no `Extraction__MaxRepairs`, run directory `evals/runs/dev-split-<UTC stamp>`, `run --cases data/dataset --split dev --max-cost-usd --cache-mode`, and `grade --run ... --cases data/dataset`. `just --dry-run dev-split` shows the `_dev-split-run` call, `just --list` shows `dev-split` and hides the helper, and no `dev-split-*` directory exists under `evals/runs`.
- `test_dev_split_recipe_is_capped_to_the_dev_cases` pins each argument once, that `--split test` occurs nowhere in the justfile, and the `just dev-split 2.50 read_only` mention in both docs; `dev-split` joined the live-recipe key-echo test.
- AGENTS.md and README.md: two command rows each, the live-recipe lists, the US$2.50 dev-split cap beside the US$5 phase cap, a `compare` and `grade --out` note under the raw commands (AGENTS.md) and a `just dev-split` paragraph (README.md).

## Task Commits

1. **Task 1 (tracer): `run --split` selects from the manifest** - `c945d5d` (feat)
2. **Task 2: `grade --out`, `--resamples` and the published-run guard** - `443004f` (feat)
3. **Task 3: the capped `just dev-split` recipe, its pins and its docs** - `1e016af` (feat)

## Verification

- `devenv shell -- just py-check`: exit 0, 3231 passed, 13 deselected (3217 baseline plus 14 new); `ruff check`, `ruff format --check` and `pyright -p python` clean. The one warning (dataset size above the 18 MB line) pre-dates this plan.
- `devenv shell -- just --dry-run dev-split | grep -c _dev-split-run` prints 1.
- Acceptance: `run --help` lists `--split`; `grade --help` lists `--out` and `--resamples` (test pins it); `read_case_splits` exists and cli.py calls it; AGENTS.md's Live paid steps sentence contains `just dev-split` and `US$2.50`.
- No paid call, no live recipe, no provider contact in this plan.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking/consistency] compare.py's `REGRADE_HINT` restored to the planned sentence, outside this plan's files_modified**
- **Found during:** Task 2 (orchestrator-required addition)
- **Issue:** plan 05-08 dropped the trailing `--out DIR` from `REGRADE_HINT` because `grade --out` did not exist. Now that it does, and the guard refuses a regrade into a published run without `--out`, the shorter hint would send a user straight into a refusal.
- **Fix:** `REGRADE_HINT` is again `regrade both runs with the current grader: carimbo-evals grade --run RUN --cases CASES --out DIR`. `test_the_regrade_hint_is_the_planned_sentence_and_its_flags_are_grade_options` (in test_grader.py, beside the other grade CLI tests) pins the sentence and proves `--run`, `--cases` and `--out` are real options of the `grade` command. The three assertions in test_compare.py check the sentence prefix and still pass unchanged, so test_compare.py was not edited.
- **Files modified:** python/src/carimbo_evals/compare.py (outside files_modified, as the orchestrator expected), python/tests/test_grader.py (inside)
- **Verification:** `pytest python/tests/test_grader.py python/tests/test_compare.py` 145 passed; `just py-check` green
- **Commit:** `443004f`

**Total deviations:** 1 (the orchestrator-required hint restoration). **Impact:** none on behavior beyond the hint text; the printed advice works when followed.

## Issues Encountered

- `ruff` and `pyright` are not on the devenv shell's PATH; they run through `uv run --project python`, as `just py-check` does. A first pyright pass flagged `typer.main.get_command(app).commands` in the new hint test; `typer.main.get_group(app)` is the typed form.

## Known Stubs

None.

## Threat Flags

None. T-05-18 is mitigated (`--max-cost-usd {{ max_cost }}` with default 2.50 in the recipe, pinned by the layout test; the runner's exit 3 at the cap is plan 05-06's). T-05-19 is mitigated (`_is_published_summary` refuses with exit 2, `--out` writes elsewhere, tests hash the run directory). T-05-20 is mitigated (`dev-split` goes through `_with-provider-key`, has no echo of the key, and joined the key-echo test; the eval key is ephemeral per run). T-05-SC: no package added.

## Requirements

EVAL-05 is delivered in part by this plan and not marked complete: `requirements.ready-ids` reports it blocked, because sibling plans 05-10, 05-11 and 05-13 also declare it and have no SUMMARY yet. Nothing was marked.

## Next Phase Readiness

Plan 05-11 can run `just dev-split` (a human step, US$2.50 cap) and replay it with `just dev-split 2.50 read_only`, then keep the run verbatim under `evals/results/` and regrade it only through `grade --out`. The phase's last plan (05-13) still owes the README narrative; the commands rows and cap are in place.

## Self-Check: PASSED

- Files exist: runner.py, cli.py, compare.py, test_runner.py, test_grader.py, test_repo_layout.py, justfile, AGENTS.md, README.md (checked by `git show --stat` of the three commits).
- Commits `c945d5d`, `443004f` and `1e016af` are ancestors of HEAD; `git rev-list --count` from the ledger base gives 3.
