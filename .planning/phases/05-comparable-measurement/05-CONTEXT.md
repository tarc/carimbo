# Phase 5: Comparable Measurement - Context

**Gathered:** 2026-11-05
**Status:** Ready for planning

<domain>
## Phase Boundary

The offline grader grows from 27 header fields to the whole invoice: every line-item column, installments by value, and line items matched by optimal assignment. Each run summary reports a declared primary metric, per-field and per-slice accuracy with n and confidence intervals, a silent-error rate, failure tags, cost, cache hit rate and latency percentiles. A new `compare` command diffs two graded extraction runs case by case and refuses runs that aren't comparable. Self-tests prove that ground truth scores perfect and that known corruptions score wrong and get the expected tag.

A full-dataset summary is proven offline with the scripted model over all 154 cases. The one paid step is a single live run of the `dev` split, which shows the taxonomy real failures before the tag list is frozen.

Not in this phase: thresholds, gating or CI fixtures, `evals/reports/`, charts, the two-model results table and the written failure analysis (all Phase 6); any live run of the `test` split; replicates, repair ablation and paired significance beyond the one test in D-16 (v2).

Numbering: D-01 to D-22 below are local to this phase. Project decisions are written as "DECISIONS D-xx" (`docs/DECISIONS.md`).

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked earlier, not re-discussed)
- Grading is offline, deterministic and separate from the run (EVAL-02, DECISIONS D-05). No LLM judge in this milestone (DECISIONS D-16). Python never reimplements the validators (Phase 2 D-09).
- `validation_failed` candidates are graded and counted as caught (DECISIONS D-24). `schema_invalid` is wrong on every field. Refused, truncated, infrastructure and harness outcomes are never counted as wrong answers.
- `test` is the holdout for published numbers, `dev` is for iteration, the CI subset is dev-only, and results are reported for test and full separately from dev (Phase 4 D-14, D-15).
- `dataset-001` is frozen: any byte change to any file is a new `dataset_version` (Phase 4 D-17). Graders adapt to the manifest as committed.
- Live spend goes only through capped `just` recipes. The cap is US$5 per phase. CI has no provider key. All data is synthetic (DECISIONS D-14).
- Published runs under `evals/results/` are verbatim copies. Regrading never rewrites them.

### What counts as correct
- **D-01:** The primary metric is the **correct-invoice rate**. An invoice is correct when every graded value is right: all 27 header fields, all 14 item columns on every true row, every installment's number, due date and amount, and no missing or extra rows or installments. It is declared here, before any number exists. — **Reversibility:** costly — Phase 6's results table and cost per correct invoice divide by it; changing it after publication changes every published figure, though regrading stored runs is free.
- **D-02:** The denominator is every case except infrastructure failures and harness errors. Refused, truncated and `schema_invalid` cases count as not correct; refused and truncated are still not wrong answers. Infrastructure failures and harness errors leave the denominator, are reported apart and mark the run incomplete, so a model can't improve its rate by truncating on 80-item invoices.
- **D-03:** Numeric grading is **exact by default**: tolerance 0.00 for amounts, quantities and rates, still configurable with `--tolerance`. The summary also counts values off by R$0.01 or less, so both views come from one grading. This replaces the current default of 0.01, so it needs a grader version bump.
- **D-04:** Text is **normalized exact**, using the existing `normalize_name` rule (NFKC, casefold, punctuation to spaces, whitespace collapsed, accents kept), for names, descriptions, item codes and units. Nothing fuzzy ever counts as correct. A near miss gets the `text_near_miss` tag so cosmetic failures are visible.

### Silent errors
- **D-05:** The headline silent-error rate is **invoices**: `success` cases with at least one wrong graded value, divided by `success` cases.
- **D-06:** Silent invoices are split into those touching a **payment value** and the rest. Payment values are the access key, issuer CNPJ, recipient tax id, issue date, invoice total, item totals, and installment due dates and amounts. A dropped or invented row counts as a wrong item total. A wrong total must never be blended with a description typo.
- **D-07:** Per-field counts of wrong values on `success` cases show what the validators can't see.

### Line items
- **D-08:** Rows are matched with `scipy.optimize.linear_sum_assignment`, pinned exactly, behind the same blocking supply-chain review as plans 01-01 and 04-02. Installments use the same matching.
- **D-09:** Row order never affects a pass (EVAL-03).

### Failure taxonomy
- **D-10:** **One tag per wrong value**, from an ordered first-match rule list versioned with the grader, the same shape as the decision labelling rule. Tag counts add up to the wrong-value total. An `untagged` bucket shows what the rules miss, and each case lists its tags. Rule-based only, from the graded diff.
- **D-11:** The draft list, roughly in match order:
  - Parties: `party_swap`
  - Rows: `trailing_rows_lost`, `dropped_row`, `invented_row`, `merged_rows`, `split_rows`, `value_from_other_row`
  - Numbers: `value_from_other_box`, `thousands_misparse`, `zero_for_blank`
  - Ids and digits: `digit_transposition`, `digit_substitution`, `digit_count`
  - Text and codes: `text_near_miss` (similarity 0.9 or more), `text_wrong`, `tax_code_wrong`, `date_wrong`
  - Fallback: `untagged`
- **D-12:** The list may be extended from the live dev run's failures, then it is **frozen, with its grader version, before the `test` split is ever run**. A later change is a grader version bump. — **Reversibility:** one-way — once test results are seen, the rules can't be re-fitted without leaking the holdout.

### Headline, slices and intervals
- **D-13:** A run reports **two headline rows and text is primary**:
  - primary: correct-invoice rate on text-layer cases of `test` (60 cases, 60 independent invoices)
  - second row: image-only cases of `test` (40 cases from 5 invoices), with the degradation curve under it

  A pooled row is available but is not the headline. The same rows are computed for `dev` and `full`, and a dev figure is always labelled dev. — **Reversibility:** costly — the published table and the `compare` verdict are built on this row.
- **D-14:** Every case-level rate uses a 95% **Wilson interval with n set to the distinct invoices in the row** (`base_case`). Each row shows both counts, for example 40 cases, 5 invoices. With no twins in the row this is plain Wilson on the case count. It errs wide because it treats twins as fully correlated, and it needs no random seed. A seeded bootstrap over invoices (about 10,000 resamples, seed recorded in the summary) is kept only for pooled row-level metrics such as item column accuracy, because rows within an invoice are correlated.
- **D-15:** Slices are a **declared list** fixed in code and versioned with the grader: regime, length band, multi-page, alphanumeric CNPJ, CPF recipient, ST/deferral, mixed tax codes, long text, confusable, rendering by severity, barcode readable. Each is computed for test, dev and full. Slices are descriptive; only D-16 is a significance test. Other tags stay in the per-case records.

### compare
- **D-16:** `compare` runs one test: an **exact McNemar test on the correct-invoice flag over text-layer cases**, with verdict `significant`, `inconclusive` or `no change`, at alpha 0.05. Field deltas stay descriptive. The verdict says it covers case sampling only, not run-to-run model variation. This moves one slice of MEAS-03 into the phase, and REQUIREMENTS.md records that.
- **D-17:** Case lists:
  - **regressed:** correct in the first run, not correct in the second
  - **worsened:** not correct in both, more wrong values in the second
  - **improved:** the mirror of both
  - **not comparable:** an infrastructure failure or harness error in either run, never in regressed

  Each listed case names the values that flipped and their tags.
- **D-18:** `compare` **refuses, with no override**, a different dataset manifest hash, grader version or tolerance, and the message names the fix: regrade both runs with the current grader. Model, prompt, repair budget and cache mode may differ and are printed as what changed. Different case sets compare on the intersection and print how many cases were excluded. — **Reversibility:** costly — Phase 6's gate builds on this contract.

### Live spend and D-26
- **D-19:** Phase 5 makes **one live run of the `dev` split**: 54 cases, a new capped `just` recipe with a per-run cap of US$2.50 for that recipe only, and a free `read_only` replay afterwards. The estimate is about US$1.2 to 2.2 on Haiku 4.5, scaled from the three published skeleton cases, so treat it as rough. The `test` split isn't run. — **Reversibility:** one-way — the spend can't be undone, so it runs after an explicit approval, like the earlier live proofs.
- **D-20:** ROADMAP criterion 2's full-dataset summary is proven **offline**: the scripted model answers all 154 cases, with seeded corruptions, and the run is graded to a full summary.
- **D-21:** The D-26 review for Phase 5 cuts: the paid full-dataset run, `compare` on decision runs (it refuses them with a clear message), slice deltas in `compare`, repair ablation metrics (MEAS-01) and replicates and a noise floor (MEAS-02). None of EVAL-03 to EVAL-08 is cut. Record this so the D-26 obligation is visibly discharged for Phase 5, and D-40's open budget question with it.
- **D-22:** Dataset-scale live budget beyond the dev run stays a Phase 6 question.

### Claude's Discretion
Defaults agreed in passing or left open. The first four are defaults unless research finds a problem.
- Latency p50 and p95 come from the original model latency (`model_latency_ms`), so a free replay reports the same numbers. Wall-clock replay time never mixes in. The degradation curve is the rendering rows of the summary, with no chart.
- A correct invoice requires status `success`. A `validation_failed` candidate with every value right is nearly impossible at tolerance 0.00, and would stay caught, not correct.
- EVAL-04 self-tests run over all 154 cases in `just py-check`: ground truth scores perfect with no tags; every tag has at least one seeded corruption that must produce exactly that tag; and delete, duplicate and swap row tests assert exact precision and recall.
- `compare` reads two `summary.json` files, never raw JSONL, so it will work on the artifacts Phase 6 commits. It exits 0 whenever the comparison ran, even with regressions, and 2 on a refusal; the gate is Phase 6's.
- Match scoring: the cost matrix, the minimum similarity for a row to count as matched, and deterministic tie-breaking. Constraint: confusable rows (same description, different quantity or price) must match the right partner.
- Item metrics, within this floor: precision, recall and F1, per-column accuracy over matched pairs, and an all-values-correct row rate. An order-preserved flag may be reported but never gates.
- The exact rule for each tag, and which draft tags merge or split once real failures are in.
- How the field-by-slice view is shaped in `summary.json`, and how much of it reaches `summary.md`.
- The `compare` output format and the CLI shape, including a `--split` selector on `run` (today only repeated `--case` exists).
- The recipe name for the dev run, and how the scripted full-dataset proof splits between `just e2e` and pytest, watching the time of `just check`.
- Which row `compare`'s test uses when two runs share no `test` cases: the shared text-layer cases, labelled with their split.
- Whether the dev run uses the alias or the dated snapshot under DECISIONS D-21. Default model `claude-haiku-4-5`, prompt `extract-003`, repair budget 2.
- The numbering of new DECISIONS entries (next free is D-42), and the bump to `grader-003` and summary version 4.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Scope and requirements
- `.planning/ROADMAP.md` § Phase 5: the goal, the four success criteria, and the Phase 5 planning note (the assignment algorithm and taxonomy rules still need design; both are shaped here)
- `.planning/REQUIREMENTS.md`: EVAL-03 to EVAL-08, MEAS-01 to MEAS-03 (v2, what stays out), and the Out of Scope table (no LLM judge, no generic eval framework or results database)
- `.planning/PROJECT.md`: core value (measured quality) and the Active list

### Decisions
- `docs/DECISIONS.md` D-02 (evals call the real pipeline), D-05 (deterministic before probabilistic), D-14 (synthetic and versioned), D-15 and D-20 (what gets committed), D-16 (no judge), D-21 (model alias vs dated snapshot), D-24 and D-29 (eval contract, incurred vs notional cost), D-26 (the scope-down obligation; D-21 here discharges it), D-36 (decision grading, which stays separate), D-40 and D-41 (dataset-001, its splits, twins and the open budget question)
- `docs/DANFE-MAPPING.md`: what each graded value is, its printed precision (quantity and unit price have 4 decimals) and the XML source

### Research
- `.planning/research/FEATURES.md`: the grader table, "Silent-error rate", "Paired run comparison", "Failure taxonomy auto-tagging", "Cost and latency reporting", "Run comparison and regression detection" and the statistics table
- `.planning/research/PITFALLS.md`: Pitfall 11 (lenient or strict graders, denominators), Pitfall 12 (line-item alignment and its row tests), Pitfall 13 (small-sample noise, overfitting to dev), Pitfall 14 (comparing mismatched runs)
- `.claude/CLAUDE.md` § Technology Stack: version-pinning rules. pandas is listed there but isn't a dependency today; add it only if a plan justifies it.

### Prior phase artifacts
- `.planning/phases/04-synthetic-dataset/04-CONTEXT.md`: D-06 (twins), D-09 (barcode flags), D-14 to D-17 (splits, CI subset, manifest)
- `.planning/phases/02-validated-extraction/02-CONTEXT.md`: D-10 (finding shape), D-14 (`validation_failed`), D-18 (the grader Phase 5 extends)
- `.planning/phases/03.1-thin-agent-slice/03.1-CONTEXT.md`: D-09 (the ordered first-match rule shape the taxonomy copies), D-17 (separate decision grading)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `python/src/carimbo_evals/grader.py`: `grade_case`, `_field_grade`, `normalize_id`, `normalize_name`, `GroundTruth`, `grade_run`, `_read_records`. `DEFAULT_TOLERANCE` is 0.01 today (D-03 changes it). `_load_manifest` returns only name, version and hash, and must expose each case's tags, rendering, severity, split, base case and barcode flag.
- `python/src/carimbo_evals/summary.py`: `FIELDS`, `GRADED_STATUSES`, `build_summary`, `render_markdown`, `GRADER_VERSION`, `SUMMARY_VERSION`. `_latency` gives min, median and max only. `summary.json` already embeds per-case grades, which is what `compare` reads.
- `python/src/carimbo_evals/ground_truth.py`: `invoice_from_xml` already returns items and installments in wire form, so no XML work is needed.
- `python/src/carimbo_evals/cli.py`: Typer commands and exit codes; `compare` joins here. `python/src/carimbo_evals/runner.py`: `discover_cases` has no split or subset selection.
- `python/tests/test_e2e_fake.py` and `dotnet/tests/Carimbo.ScriptedHost`: scripted answers keyed by PDF SHA-256. Twins have their own PDF bytes, so all 154 cases can each get their own answer.
- `python/tests/test_grader.py`: 52 tests, some pinned to a tolerance of 0.01.
- `data/dataset/manifest.json`: `base_case`, `rendering`, `severity`, `split`, `barcode_readable`, `tags`, `ci_subset`.

### Established Patterns
- `Decimal` for all money, never float; `format(x, "f")` when rendering. `hit_rate` is the only float in a summary today, rounded to 4 places, so rates and intervals need the same fixed rounding for byte-stable regrades.
- `meta.graded_at` is the only volatile summary value. Keys are sorted, indent 2.
- Exact pins in `python/pyproject.toml` with `uv.lock` committed; a new dependency goes through a blocking review.
- Versions as constants, stamped into each summary. Tests pin outputs, and golden fixtures fail stale.
- Live proofs are human-run after a spend approval, with results copied verbatim to `evals/results/`.

### Integration Points
- `justfile`: the new capped dev recipe, the `check` chain, and the `docs-check` loop and comment, which end at D-41.
- `AGENTS.md`: the commands table, the "Live paid steps" list and caps (D-19 adds one recipe at US$2.50), and the `docs-check` range.
- `README.md`: the Status row, verification bullet, results and spend, `docs-check` range. `python/tests/test_readme_drift.py` enforces the mechanical parts, and the last plan of the phase carries the README task.
- `docs/DECISIONS.md` and `.planning/REQUIREMENTS.md`: the new decision entries and the MEAS-03 note (D-16).

</code_context>

<specifics>
## Specific Ideas

### What was measured during this discussion
- **Twins:** test is 100 cases but 60 invoices (60 text, 40 image-only from 5 invoices); dev is 54 cases and 30 invoices; full is 154 and 90. Slices that mix in twins include alphanumeric CNPJ on test (24 cases, 16 invoices) and CPF recipient on test (21 cases, 13 invoices).
- **Rendering rows:** n = 5 per rendering on test, 3 on dev, 8 on full. These intervals will be wide, and that is honest.
- **Phase 4 WR-04 doesn't bite:** the committed manifest has zero contradictions between the `barcode_unreadable` tag and `barcode_readable`, with 44 cases unreadable. Slice on the manifest flag.
- **Wrapping:** the three longest descriptions in `nfe-0006` wrap at word boundaries on the DANFE, so collapsing whitespace handles them. That's one invoice only. `test_dataset_text_equals_xml.py` strips all whitespace, so it doesn't prove this. Research should check all long-text bases, because a mid-word wrap would fail correct answers under D-01 and D-04.
- **Cost basis:** `evals/results/skeleton-20261015T090949Z` gives about US$0.012 for a first attempt on a 3-item page, about US$0.006 for each repair, and about 122 output tokens per item on the 50-item case. Dev holds 900 items, test 1,561, the CI subset 381. The range in D-19 depends on how many cases spend both repairs.

### Expected consequence
- On the skeleton, the access key alone left 2 of 3 cases `validation_failed`. If that carries over, the correct-invoice rate under D-01 may be low on Haiku. That's a finding, not a reason to loosen the metric; the silent-error split, per-field table and failure tags stay informative either way, and `worsened` in D-17 exists for exactly this.

</specifics>

<deferred>
## Deferred Ideas

- Repair ablation and first-pass grades (MEAS-01), replicates and a noise floor (MEAS-02), and the rest of paired significance (MEAS-03): v2, per D-21 and D-16.
- A payment-values pass rate beside the primary metric: not chosen as primary. D-06's list makes it a free regrade if Phase 6 wants it.
- Slice deltas in `compare`: Phase 6's results table reads slices from each run's summary.
- `compare` on decision runs: revisit with decision evals at scale in Milestone 2.
- Thresholds, the CI gate, `compare`'s gate exit code, charts, the model pair and the budget for test and full runs: Phase 6.
- The dc-004 recipient-mismatch question from Phase 3.1 (PROJECT.md Active list): still undecided and not part of extraction measurement.

</deferred>

---

*Phase: 05-comparable-measurement*
*Context gathered: 2026-11-05*
