---
phase: 05-comparable-measurement
reviewed: 2026-11-06T00:00:00Z
depth: standard
files_reviewed: 25
files_reviewed_list:
  - justfile
  - python/src/carimbo_evals/cli.py
  - python/src/carimbo_evals/compare.py
  - python/src/carimbo_evals/corruptions.py
  - python/src/carimbo_evals/grader.py
  - python/src/carimbo_evals/matching.py
  - python/src/carimbo_evals/normalize.py
  - python/src/carimbo_evals/runner.py
  - python/src/carimbo_evals/slices.py
  - python/src/carimbo_evals/stats.py
  - python/src/carimbo_evals/summary.py
  - python/src/carimbo_evals/tags.py
  - python/tests/test_compare.py
  - python/tests/test_e2e_fake.py
  - python/tests/test_e2e_full_dataset.py
  - python/tests/test_grader.py
  - python/tests/test_matching.py
  - python/tests/test_repo_layout.py
  - python/tests/test_runner.py
  - python/tests/test_selftests.py
  - python/tests/test_silent_errors.py
  - python/tests/test_stats.py
  - python/tests/test_summary_v4.py
  - python/tests/test_tags.py
  - python/tests/test_vectors.py
findings:
  critical: 1
  warning: 4
  info: 8
  total: 13
  necessity: 1
status: issues_found
---

# Phase 5: Code Review Report

**Reviewed:** 2026-11-06
**Depth:** standard
**Files Reviewed:** 25
**Status:** issues_found

## Summary

The grading path itself (`grader.py`, `matching.py`, `normalize.py`, `stats.py`, `slices.py`, the row/field aggregation in `summary.py`) holds up. I traced the Wilson, McNemar and percentile-bootstrap arithmetic by hand and against the pinned tests, checked the optimal-assignment cost construction (the `1e6` tie scale dominates for any realistic row count), and checked slice membership against `data/dataset/manifest.json`. I also regraded both published dev-split runs offline into a scratch directory (`carimbo-evals grade --out ...`, nothing under `evals/results` touched, no live recipe, no test split). Both regrades are byte-identical to the committed `summary.json` apart from `meta.graded_at`, so the published numbers reproduce.

Problems cluster at the edges of the grading path, not inside it: how a run's completeness is established, how `compare` describes case movement, and how robust `compare` and `grade` are to unusual input. One of them (CR-01) can make a partial run publish as complete, which matters most for a project whose product is the measured number. I verified it by experiment (below).

Frozen items (D-48): nothing in `tags.py` or the grading path needed a fix. IN-07 records an observation about the tag list (a third of wrong values untagged in the published run) for the record only.

Evidence used: `evals/results/dev-split-20261106T001800Z/` (51 of 54 cases, `stopped_reason: cost_cap`, correctly flagged `incomplete: true`) and its replay `dev-split-20261106T021526Z/` (3 `cache_miss` infrastructure failures). Both are flagged correctly because their `run.json` was written. The defects below concern runs where that file is absent or stale.

## Critical Issues

### CR-01: A run killed before its end, or resumed on a narrower selection, grades as `incomplete: false`

**File:** `python/src/carimbo_evals/grader.py:490-509,537-546` (cause), `python/src/carimbo_evals/runner.py:637-660`, `justfile:297-305`
**Issue:** Completeness has exactly one witness: `run.json`, which `run_cases` writes only on normal exit. Two ways to lose it:

1. **Killed or interrupted run.** `KeyboardInterrupt` or a kill propagates out of `run_cases` before `_write_json_atomic`, so no `run.json` exists. `load_run_state` then returns `{}`, `cases_not_run` is 0 and `stopped_reason` is None, so `incomplete` is False. The justfile recipes do `carimbo-evals run ... || run_rc=$?` and then still run `grade` on whatever `cases.jsonl` holds, so a Ctrl-C produces a clean-looking `summary.json`/`summary.md`. I reproduced this: the first 20 records of the published live run copied into a scratch directory without `run.json` graded to `incomplete: False`, `incomplete_detail = {cases_not_run: 0, ...}`, `cases: 20`, with no INCOMPLETE line in `summary.md`. The `load_run_state` docstring says the infrastructure and harness cases are still marked by status, but the cases that were simply never run (the actual hazard) are not.
2. **Resume with a narrower selection.** `run_cases` rewrites `run.json` on every invocation with `case_ids` of that invocation and `stopped_reason` of that invocation. After a cost-cap stop, `run --resume --case nfe-0144` writes `case_ids: ["nfe-0144"]`, `stopped_reason: null`. The grader then reports `cases_not_run = 0` and `incomplete = false` while the other unrun dev cases are still missing.

Either way the downstream consumers (`compare`, the README tables, anyone reading `summary.json`) see a number on a subset presented as a finished run. Since headline rates and Wilson intervals are computed over whatever cases exist, nothing else in the summary signals it.
**Fix:**
```python
# grader.py: absence of run.json is itself incomplete evidence
state = load_run_state(run_dir)
state_missing = not state
run_meta["run_state_missing"] = state_missing
# summary.incomplete_state(): flagged also when run_meta.get("run_state_missing")
#   detail["run_state"] = "missing"  (and render "run.json missing" in the INCOMPLETE line)
```
and in `runner.run_cases`: write a provisional `run.json` (with `"stopped_reason": "in_progress"`) before dispatching and replace it atomically at the end; on `resume`, union the new `case_ids` with those of an existing `run.json` instead of overwriting them. Add tests for "grade a directory with `cases.jsonl` but no `run.json`" and "resume with a subset".

## Warnings

### WR-01: `compare` lists a case that degraded to refused or truncated as "improved" (and the reverse as "worsened")

**File:** `python/src/carimbo_evals/compare.py:295-304`
**Issue:** For two not-correct cases the classification is by `len(wrong)`. Refused, truncated (and all non-graded) statuses carry `wrong == []` by design, so `validation_failed` with 44 wrong values followed by `refused` has `len(after) < len(before)` and lands in `improved`. I confirmed it by mutating nfe-0038 of the published run to `refused` and comparing: `improved = [('nfe-0038', 'validation_failed', 'refused')]`. The reverse (refused to validation_failed with 4 wrong values) is reported as `worsened`. The McNemar verdict uses the `correct` flag and is unaffected, but the case lists are the part a human reads to find regressions, and a model that starts refusing or truncating would show up as improving.
**Fix:** Only compare wrong-value counts when both cases were graded (`status in GRADED_STATUSES` for both). Otherwise report a status change, for example in a separate `status_changed` list, or put it under `worsened`/`improved` only through an explicit status ordering.
```python
GRADED = ("success", "validation_failed", "schema_invalid")
elif not before["correct"] and not after["correct"]:
    if before["status"] in GRADED and after["status"] in GRADED:
        ...  # compare len(wrong)
    elif before["status"] != after["status"]:
        status_changed.append(_entry(case_id, before, after))
```

### WR-02: `compare` raises raw tracebacks on structurally odd summaries, contradicting its stated contract

**File:** `python/src/carimbo_evals/compare.py:117-131,248-261` (validation gap in `_validate_case`, crash in `_tally`)
**Issue:** `load_summary` promises "anything unusable becomes a `CompareRefusal` rather than a traceback" (T-05-16), but `_validate_case` only checks `case_id`, `status`, `correct` and `wrong`. `_tally` then dereferences `case["items"]["columns"][column]["correct"]` unchecked. Reproduced on a copy of the published summary:
- `items: {"columns": []}` raises `AttributeError`.
- `items: {"columns": {"code": {"correct": Infinity, "n": 1}}}` raises `OverflowError` (`json.loads` accepts `Infinity`/`NaN`).
- `items: {"columns": {"code": {"correct": "x", "n": 1}}}` raises `ValueError`.

`test_a_hostile_or_malformed_summary_is_refused_without_a_traceback` (HOSTILE table, `test_compare.py:254-274`) never exercises `items`, `installments` or `fields`, so the contract is untested where it breaks. Impact is a crash on a hand-edited or corrupted summary, not wrong numbers.
**Fix:** In `_validate_case`, check that `fields` is a dict (when present), that `items`/`installments` are dicts (when present) whose `columns` is a dict of `{"correct": int, "n": int}` (reject bool and non-int), then add those cases to `HOSTILE`. Alternatively wrap `compare_summaries` in `try/except (AttributeError, TypeError, ValueError, OverflowError)` that raises `CompareRefusal`.

### WR-03: `summary.cache.hit_rate` rounds with a different rule than every other rate

**File:** `python/src/carimbo_evals/summary.py:184`
**Issue:** `round(hits / answered, 4)` uses float division and Python's round-half-even, while `stats.rate` (the documented single rounding helper, "4 places, ROUND_HALF_UP, exact") is used for all other rates. They disagree on exact halves: `round(1/32, 4) == 0.0312` but `rate(1, 32) == 0.0313` (the latter is pinned in `test_stats.py`); `round(5/32, 4) == 0.1562` vs `rate(5, 32) == 0.1563`. The published runs (0/125 and 125/125) are unaffected, but any other cache mix can print a hit rate that disagrees with the project's own rounding rule.
**Fix:**
```python
"hit_rate": rate(hits, answered),
```
`rate` already returns `None` for a zero denominator. Update `test_the_hit_rate_is_rounded_to_four_places` accordingly (1 of 3 still gives 0.3333).

### WR-04: `run_cases --resume` truncates the torn tail before it checks the overrides, so "nothing has been written when this raises" is false

**File:** `python/src/carimbo_evals/runner.py:407-409` (truncate in `_load_existing`), `runner.py:517-518` (overrides check afterwards), docstring at `runner.py:427-433`
**Issue:** `_load_existing` removes the torn final line from `cases.jsonl` as soon as the file validates. `_require_same_overrides` runs after it and raises `RunConfigMismatchError` ("Nothing has been written when this raises"). So a resume with the wrong `--cache-mode` / `--model` exits 2 having already modified the run file. The lost bytes are an unusable partial record, so no valid data is lost, but the file was changed by a command that reports it refused to run and the docstring states the opposite.
**Fix:** Split loading from repair: have `_load_existing` return `(records, tail_length)` without writing, run `_require_same_overrides`, then truncate the tail just before opening the sink.

## Info

### IN-01: Stale comment says per-item grading is future work

**File:** `python/src/carimbo_evals/summary.py:47-48`
**Issue:** The comment above `FIELDS` ends "Per-item grading is Phase 5". That is this phase and it is implemented (`matching.py`, `row_block`, `column_intervals`).
**Fix:** Reword to "Item and installment columns are graded separately (`matching.diff_rows`)."

### IN-02: `primary_metric.headline_row` points at a row a dev-only summary does not have

**File:** `python/src/carimbo_evals/summary.py:652-658`
**Issue:** `headline_row: "test.text"` and `second_row: "test.image"` are constants. In the published dev-split summaries `summary["headline"]` has only a `dev` key, so a consumer following `headline_row` gets a `KeyError`/None. The markdown labels are right (`dev text (dev)`), only the machine-readable pointer is wrong for dev runs.
**Fix:** Emit the pointer only when a `test` scope exists, otherwise `null` (or name the scope that holds the primary row).

### IN-03: Protection of the `test` split is by recipe absence only

**File:** `python/src/carimbo_evals/cli.py:55-57,172-175,248-253`, `python/tests/test_repo_layout.py:277`
**Issue:** `SPLITS = ("dev", "test")`, so `carimbo-evals run --split test` (a paid run) is a one-flag command. The only guard is the absence of a recipe and the textual check `assert "--split test" not in justfile`, which a variable, a quoted `"test"` or a different spelling defeats. That matches the stated Phase 5 rule ("nothing here passes it to a live run") but is a convention, not a barrier.
**Fix:** Optional: require an explicit `--allow-test-split` flag (or env var) for `--split test` on `run`, and have the repo-layout test grep for `--split` followed by anything other than `dev`.

### IN-04: Replay summaries print "unpriced" and fold cache misses into infrastructure failures

**File:** `python/src/carimbo_evals/summary.py:129-162,883-885`
**Issue:** In the published replay, the three `cache_miss` records (nfe-0144 to nfe-0146) have `cost_usd: null` and `incurred_cost_usd: "0.00000000"`. `summary.md` prints "US$0.00000000 incurred, US$2.30699480 notional (3 unpriced)", but `unpriced_cases` counts only the notional side (`unpriced_incurred_cases` is 0), so the parenthetical reads as if the incurred figure were uncertain. Separately, a read-only cache miss is counted as `infrastructure_failure`, the same bucket as a real provider outage, so the summary alone cannot tell "the replay lacked an answer" from "the provider failed". The cause is only in `outcome.failure.kind`.
**Fix:** Label the count "unpriced (notional)" and consider surfacing `failure.kind` counts (for example `cache_miss`) in `incomplete_detail`.

### IN-05: `grade` leaks tracebacks for malformed run files and builds a path from the record's `case_id`

**File:** `python/src/carimbo_evals/grader.py:452,530`, `python/src/carimbo_evals/cli.py:349-353`
**Issue:** `_read_records` does `record["case_id"]` with no shape check (a valid JSON line that is not an object, or has no `case_id`, raises `TypeError`/`KeyError`), whereas the runner's `_load_existing` validates exactly this. A malformed XML in the cases directory raises `xml.etree.ElementTree.ParseError`, a `SyntaxError` subclass, which `except (OSError, ValueError)` does not catch. `cases_dir / f"{case_id}.xml"` also joins an unvalidated `case_id` (for example `../../x`), so a crafted `cases.jsonl` makes the grader parse an arbitrary XML file. It is read-only and local, so low risk.
**Fix:** Validate records as `_load_existing` does (raise `ValueError`), catch `ET.ParseError` into a `ValueError` in `invoice_from_xml`/`load_ground_truth`, and require `case_id` to match `^[A-Za-z0-9._-]+$` before building a path.

### IN-06: `compare` does not surface that either run is incomplete

**File:** `python/src/carimbo_evals/compare.py:286-327`
**Issue:** `compare_summaries` never reads `incomplete`/`incomplete_detail`. Comparing the cost-capped live run (51 cases) with a complete one shows only `excluded` and `only_second` case ids, with no statement that one side stopped on the cost cap. The verdict is computed on the shared cases, which is defensible, but a reader of `compare.md` is not told that one run is a prefix (by case id, so biased toward text-layer cases) of the other.
**Fix:** Add `incomplete` and `stopped_reason` for both runs to the `compare.json`/`compare.md` "Same/Changed" sections.

### IN-07: One third of the wrong values in the published dev run are `untagged` (observation only, tags frozen under D-48)

**File:** `python/src/carimbo_evals/tags.py` (frozen); data from `evals/results/dev-split-20261106T001800Z/summary.json`
**Issue:** 265 of 827 wrong values are `untagged`. By column: item `total` 44, `access_key` 30, `issuer.cnpj` 29, `products_total` 22, `invoice_total` 21, item `quantity` 21, `issuer.ie` 19, item `unit_price` 19, `recipient.tax_id` 13, `recipient.ie` 10 (predicted null, which no digit rule covers), `number` 10. The digit rules accept only exact transposition, exactly one substituted character, or a one-character length difference, so multi-character identifier errors (for example access key or CNPJ read with two or more wrong digits) and a null for a present `recipient.ie` fall through. `merged_rows`, `split_rows`, `party_swap`, `schema_invalid` and `thousands_misparse` are all 0. The `untagged` bucket exists to show what the rules miss, so this is the system working as designed. I did not find a rule-ordering bug that explains the 32 percent. No change here: record it for the next tag-list version (`failure-tags-2`, which would bump the grader).
**Fix:** None now. When D-48 is reopened, consider a rule for "identifier differs in more than one character" and for "predicted null for a non-null expected".

### IN-08: Impossible-state guard: re-draw loop for an all-zero trace and span id

**File:** `python/src/carimbo_evals/runner.py:171-176`
**Category:** necessity
**Issue:** `new_traceparent` re-draws while the id parses to 0. `secrets.token_hex(16)` and `token_hex(8)` return all-zero values with probability 2^-128 and 2^-64 respectively, so the loop bodies are never executed in any real run, and no test can reach them without monkeypatching. The W3C invalidity rule is real, but the guard defends against a state the generator cannot practically produce. This rests on judgment, hence Info. Evidence: `secrets.token_hex` draws uniformly from the OS CSPRNG.
**Fix:** Drop the two `while` loops, or keep one line stating the W3C rule if the explicitness is wanted.

---

_Reviewed: 2026-11-06_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
