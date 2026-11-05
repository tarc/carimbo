# Phase 5: Comparable Measurement - Research

**Researched:** 2026-11-05
**Domain:** offline grading, statistics and run comparison for a Python eval harness (no new model calls)
**Confidence:** HIGH for everything executed against the committed data; MEDIUM for the tag rules (they are proven on seeded corruptions, not yet on real model failures)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

Carried forward (locked earlier, not re-discussed)
- Grading is offline, deterministic and separate from the run (EVAL-02, DECISIONS D-05). No LLM judge in this milestone (DECISIONS D-16). Python never reimplements the validators (Phase 2 D-09).
- `validation_failed` candidates are graded and counted as caught (DECISIONS D-24). `schema_invalid` is wrong on every field. Refused, truncated, infrastructure and harness outcomes are never counted as wrong answers.
- `test` is the holdout for published numbers, `dev` is for iteration, the CI subset is dev-only, and results are reported for test and full separately from dev (Phase 4 D-14, D-15).
- `dataset-001` is frozen: any byte change to any file is a new `dataset_version` (Phase 4 D-17). Graders adapt to the manifest as committed.
- Live spend goes only through capped `just` recipes. The cap is US$5 per phase. CI has no provider key. All data is synthetic (DECISIONS D-14).
- Published runs under `evals/results/` are verbatim copies. Regrading never rewrites them.

What counts as correct
- **D-01:** The primary metric is the **correct-invoice rate**. An invoice is correct when every graded value is right: all 27 header fields, all 14 item columns on every true row, every installment's number, due date and amount, and no missing or extra rows or installments. It is declared here, before any number exists. — **Reversibility:** costly — Phase 6's results table and cost per correct invoice divide by it; changing it after publication changes every published figure, though regrading stored runs is free.
- **D-02:** The denominator is every case except infrastructure failures and harness errors. Refused, truncated and `schema_invalid` cases count as not correct; refused and truncated are still not wrong answers. Infrastructure failures and harness errors leave the denominator, are reported apart and mark the run incomplete, so a model can't improve its rate by truncating on 80-item invoices.
- **D-03:** Numeric grading is **exact by default**: tolerance 0.00 for amounts, quantities and rates, still configurable with `--tolerance`. The summary also counts values off by R$0.01 or less, so both views come from one grading. This replaces the current default of 0.01, so it needs a grader version bump.
- **D-04:** Text is **normalized exact**, using the existing `normalize_name` rule (NFKC, casefold, punctuation to spaces, whitespace collapsed, accents kept), for names, descriptions, item codes and units. Nothing fuzzy ever counts as correct. A near miss gets the `text_near_miss` tag so cosmetic failures are visible.

Silent errors
- **D-05:** The headline silent-error rate is **invoices**: `success` cases with at least one wrong graded value, divided by `success` cases.
- **D-06:** Silent invoices are split into those touching a **payment value** and the rest. Payment values are the access key, issuer CNPJ, recipient tax id, issue date, invoice total, item totals, and installment due dates and amounts. A dropped or invented row counts as a wrong item total. A wrong total must never be blended with a description typo.
- **D-07:** Per-field counts of wrong values on `success` cases show what the validators can't see.

Line items
- **D-08:** Rows are matched with `scipy.optimize.linear_sum_assignment`, pinned exactly, behind the same blocking supply-chain review as plans 01-01 and 04-02. Installments use the same matching.
- **D-09:** Row order never affects a pass (EVAL-03).

Failure taxonomy
- **D-10:** **One tag per wrong value**, from an ordered first-match rule list versioned with the grader, the same shape as the decision labelling rule. Tag counts add up to the wrong-value total. An `untagged` bucket shows what the rules miss, and each case lists its tags. Rule-based only, from the graded diff.
- **D-11:** The draft list, roughly in match order:
  - Parties: `party_swap`
  - Rows: `trailing_rows_lost`, `dropped_row`, `invented_row`, `merged_rows`, `split_rows`, `value_from_other_row`
  - Numbers: `value_from_other_box`, `thousands_misparse`, `zero_for_blank`
  - Ids and digits: `digit_transposition`, `digit_substitution`, `digit_count`
  - Text and codes: `text_near_miss` (similarity 0.9 or more), `text_wrong`, `tax_code_wrong`, `date_wrong`
  - Fallback: `untagged`
- **D-12:** The list may be extended from the live dev run's failures, then it is **frozen, with its grader version, before the `test` split is ever run**. A later change is a grader version bump. — **Reversibility:** one-way — once test results are seen, the rules can't be re-fitted without leaking the holdout.

Headline, slices and intervals
- **D-13:** A run reports **two headline rows and text is primary**:
  - primary: correct-invoice rate on text-layer cases of `test` (60 cases, 60 independent invoices)
  - second row: image-only cases of `test` (40 cases from 5 invoices), with the degradation curve under it

  A pooled row is available but is not the headline. The same rows are computed for `dev` and `full`, and a dev figure is always labelled dev. — **Reversibility:** costly — the published table and the `compare` verdict are built on this row.
- **D-14:** Every case-level rate uses a 95% **Wilson interval with n set to the distinct invoices in the row** (`base_case`). Each row shows both counts, for example 40 cases, 5 invoices. With no twins in the row this is plain Wilson on the case count. It errs wide because it treats twins as fully correlated, and it needs no random seed. A seeded bootstrap over invoices (about 10,000 resamples, seed recorded in the summary) is kept only for pooled row-level metrics such as item column accuracy, because rows within an invoice are correlated.
- **D-15:** Slices are a **declared list** fixed in code and versioned with the grader: regime, length band, multi-page, alphanumeric CNPJ, CPF recipient, ST/deferral, mixed tax codes, long text, confusable, rendering by severity, barcode readable. Each is computed for test, dev and full. Slices are descriptive; only D-16 is a significance test. Other tags stay in the per-case records.

compare
- **D-16:** `compare` runs one test: an **exact McNemar test on the correct-invoice flag over text-layer cases**, with verdict `significant`, `inconclusive` or `no change`, at alpha 0.05. Field deltas stay descriptive. The verdict says it covers case sampling only, not run-to-run model variation. This moves one slice of MEAS-03 into the phase, and REQUIREMENTS.md records that.
- **D-17:** Case lists:
  - **regressed:** correct in the first run, not correct in the second
  - **worsened:** not correct in both, more wrong values in the second
  - **improved:** the mirror of both
  - **not comparable:** an infrastructure failure or harness error in either run, never in regressed

  Each listed case names the values that flipped and their tags.
- **D-18:** `compare` **refuses, with no override**, a different dataset manifest hash, grader version or tolerance, and the message names the fix: regrade both runs with the current grader. Model, prompt, repair budget and cache mode may differ and are printed as what changed. Different case sets compare on the intersection and print how many cases were excluded. — **Reversibility:** costly — Phase 6's gate builds on this contract.

Live spend and D-26
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

### Deferred Ideas (OUT OF SCOPE)

- Repair ablation and first-pass grades (MEAS-01), replicates and a noise floor (MEAS-02), and the rest of paired significance (MEAS-03): v2, per D-21 and D-16.
- A payment-values pass rate beside the primary metric: not chosen as primary. D-06's list makes it a free regrade if Phase 6 wants it.
- Slice deltas in `compare`: Phase 6's results table reads slices from each run's summary.
- `compare` on decision runs: revisit with decision evals at scale in Milestone 2.
- Thresholds, the CI gate, `compare`'s gate exit code, charts, the model pair and the budget for test and full runs: Phase 6.
- The dc-004 recipient-mismatch question from Phase 3.1 (PROJECT.md Active list): still undecided and not part of extraction measurement.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| EVAL-03 | Graders cover schema validity, exact match (IDs, CNPJ, access key), numeric tolerance (amounts, taxes; configurable) and line-item matching by optimal assignment (not index) | "Row matching" (integer 14-point score over 7 identity columns, minimum 6, `linear_sum_assignment` with an explicit tie-break) run over all 154 cases: ground truth self-matches in 154/154, delete, duplicate, shuffle and reverse tests pass in 154/154, the 36 confusable pairs match their partners. Exact tolerance 0.00 default needs the `test_vectors.py` coupling removed. |
| EVAL-04 | Graders have self-tests (ground truth perfect; known corruptions wrong) and infra failures stay distinct from wrong answers | Prototype diff + tags: ground truth gives 0 wrong values in 154/154 cases; 18 corruption kinds each produce exactly their one tag (table in "Failure tags"). Timing and the split between pytest and `just e2e` measured. |
| EVAL-05 | Per-field and per-slice accuracy with n and CIs, cost, cache hit rate, latency percentiles | Wilson (Decimal), exact McNemar (Fraction), seeded bootstrap (stdlib `random.random`) all stdlib-only and cross-checked against scipy; slice n table for dev/test/full; nearest-rank percentiles; no stats dependency beyond scipy for assignment. |
| EVAL-06 | Silent-error rate: values that passed validators but are wrong | Definition pinned to D-05/D-06 with the exact payment-value path set; which validator rules make a corruption silent vs caught (relevant to the scripted full-dataset proof). |
| EVAL-07 | Failures auto-tagged with a failure taxonomy | Ordered first-match rule list `failure-tags-1`, which tags need the graded diff vs the row assignment, and four findings about the D-11 draft (order, `zero_for_blank`, count fields, `schema_invalid`). |
| EVAL-08 | `compare` lists per-field deltas and regressed cases and refuses mismatched dataset or grader versions | Refusal contract, McNemar verdict rule, inputs read from `summary.json`, what in `summary.json` must therefore be per case, and what the published runs under `evals/results/` can and cannot be compared to. |
</phase_requirements>

## Summary

The phase is almost entirely pure-Python, offline code: a graded diff (header fields, row assignment, per-column wrongness), an ordered tag list, statistics that need nothing beyond the standard library, a `compare` command, and a new `summary.json` shape. Every algorithmic question in CONTEXT was answered by running code against the committed `data/dataset`. The one dependency is scipy 1.18.1, which resolves against the existing lock by adding only itself (`numpy` 2.5.3 satisfies `numpy<2.8,>=2.0.0`), imports and type-checks cleanly, and must still go through the locked blocking human review (the supply-chain seam rates it SUS because it could not read PyPI download and repository metadata, not because of a real signal).

Three findings change how the plans should be written, and one of them touches the headline metric. First, the dataset prints some values wrapped mid-token on the DANFE: the unit `PAR` wraps as `PA` / `R` in 62 rows of 43 cases, and every one of the 18 `confusable` cases prints its many-digit line with a unit price and totals broken across lines (for example `685.457,072` then `0`). That is 31 of the 60 `test` text-layer cases (12 of 30 on `dev`) that carry at least one wrap hazard. Second, `python/tests/test_vectors.py` pins the grader's `DEFAULT_TOLERANCE` to the validator tolerance vector, so D-03 breaks a test that must be rewritten, not just bumped. Third, `grade` writes `summary.json` next to `cases.jsonl`, so regrading a verbatim run under `evals/results/` would rewrite it unless `grade` gains an output option.

The costs are small. Matching all 154 ground truths takes 0.28 s, a scripted 154-case run over HTTP takes 1.3 s plus 0.7 s to grade, and a 10,000-resample bootstrap over 90 invoices with 30 columns takes 0.7 s. The existing suite is 2,810 pytest tests in 43 s and 13 e2e tests in 61 s, so the new self-tests and the full-dataset proof fit in `just check` without a timing problem if the corruption battery stays near 15 s.

**Primary recommendation:** Build `failure-tags-1`, the 7-column integer match score (minimum 6 of 14) and the stdlib statistics as pure modules first, prove them over all 154 cases in pytest, then add the summary v4 shape, `compare`, `--split` and `grade --out`, with the scipy lock review as the gate plan before any install.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Run records (`cases.jsonl`) | Python runner (`carimbo_evals.runner`) | .NET eval endpoint | Unchanged; the grader only reads stored records (EVAL-02). |
| Field, row and installment grading, tags | Python grader (`carimbo_evals`, offline) | none | D-05 and Phase 2 D-09: Python grades, never reimplements the validators. |
| Row assignment | Python grader (scipy `linear_sum_assignment`) | none | D-08. Lazy import keeps CLI start-up fast. |
| Statistics (Wilson, McNemar, bootstrap, percentiles) | Python, stdlib only | none | Deterministic, seed-free except the bootstrap; no stats dependency needed. |
| Slices and dataset metadata | Python grader reading `data/dataset/manifest.json` | none | Per-case `split`, `base_case`, `rendering`, `severity`, `barcode_readable`, `tags` already in the manifest; skeleton manifests lack them and must still grade. |
| `compare` | Python CLI reading two `summary.json` files | none | Never reads raw JSONL (CONTEXT), so it works on Phase 6's committed artifacts. |
| Scripted full-dataset proof | .NET `Carimbo.ScriptedHost` + Python runner/grader | pytest `e2e` marker | Real HTTP path; answers keyed by PDF SHA-256, twins have distinct PDF bytes. |
| Dev live run | `just` recipe (runner, Api host) | provider (paid, capped) | Same shape as `_skeleton-run`; one explicit approval. |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| scipy | 1.18.1 (2026-08-21) | `scipy.optimize.linear_sum_assignment` for rows and installments | Locked by D-08. Requires Python >=3.12 and `numpy<2.8,>=2.0.0`; resolves with the project's numpy 2.5.3 and changes no other pin. [VERIFIED: PyPI JSON API, `uv add --no-sync` against a scratch copy of the lock] |
| numpy | 2.5.3 (already pinned) | scipy's array type for the cost matrix | Already a runtime dependency (`python/pyproject.toml`). |
| Python stdlib | 3.12.14 | `decimal`, `fractions`, `math.comb`, `random.Random`, `difflib.SequenceMatcher` | Wilson, exact McNemar, bootstrap, percentiles and the similarity for `text_near_miss` all need nothing else. Verified equal to scipy over a full grid (below). |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| typer | 0.27.2 (pinned) | `compare` command, `--split`, `grade --out` | Existing CLI pattern in `cli.py`. |
| pytest | 9.1.1 (pinned) | self-tests; markers `e2e` and `live` | Existing. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| stdlib Wilson/McNemar | `scipy.stats.binomtest(...).proportion_ci(method="wilson")`, `binomtest(...).pvalue` | Same numbers (0 mismatches over every k for n 1..100, 154, 300 and all b, c under 40), but the Wilson-with-n-set-to-invoices of D-14 has a non-integer k, which `binomtest` cannot take. Use scipy only as a test-time cross-check. |
| `random.Random.random()` bootstrap | `numpy.random.default_rng(seed)` | numpy's Generator streams are not guaranteed identical across numpy versions; the Python docs guarantee only `random()` after the compatible seeder. Use `random()`. |
| pandas | none | CONTEXT notes pandas is not a dependency; nothing here needs it. |

**Installation (after the blocking checkpoint, not before):**
```bash
# resolve only, no install (the 04-02 pattern)
devenv shell -- uv lock --project python
# after human approval of the lock diff
devenv shell -- uv sync --project python --locked
```
with `"scipy==1.18.1"` added to `[project] dependencies` in `python/pyproject.toml`.

**Version verification:** `scipy` 1.18.1 is the latest stable on PyPI (previous 1.18.0 on 2026-06-19, 1.17.1 on 2026-02-23), `requires_python >=3.12`. [VERIFIED: PyPI JSON API, 2026-11-05]

## Package Legitimacy Audit

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| scipy 1.18.1 | PyPI | project since 2010-07 (earliest file upload on PyPI), this release 2026-08-21 | 85.6M last week, 292M last month (pypistats) | github.com/scipy/scipy | SUS (seam) | Approved by D-08, gated by the locked blocking checkpoint |

- The seam (`gsd-tools query package-legitimacy check --ecosystem pypi scipy`) returned `SUS` with reasons `unknown-downloads` and `no-repository`: its PyPI adapter returned `weeklyDownloads: null` and `repoUrl: null`. Both are gaps in the seam's metadata, not findings: PyPI's own JSON lists `project_urls.source = https://github.com/scipy/scipy`, pypistats lists 85,644,470 downloads for the last week, and PyPI's integrity API shows a build provenance attestation for the cp312 wheel from publisher `{'kind': 'GitHub', 'repository': 'scipy/scipy-release', 'workflow': 'wheels.yml'}`. [VERIFIED: PyPI JSON API, pypistats.org, PyPI integrity API]
- `scipy` is named in the locked decision D-08 and is the official SciPy package, so the name is not slopsquat-suspect. The `SUS` verdict still requires `checkpoint:human-verify` before install; D-08 already requires exactly that, so do not collapse the gate.
- Lock diff, from `uv add --project <scratch copy> "scipy==1.18.1" --no-sync`: `Resolved 68 packages`; the only new package is `scipy 1.18.1` (dependency `numpy`), with an sdist (`scipy-1.18.1.tar.gz`, sha256 `52c4b7422442aba924d03ad4019852b08a92e64ea187b933135687bfe2747307`) and 61 hashed file URLs, including the Linux cp312 manylinux wheel `scipy-1.18.1-cp312-cp312-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl` (35.3 MB, sha256 `f55fa87b6c612ecd6b058f167c53231b1d14e412efe361d3d6e38b3631c73218`). No other package version changed. [VERIFIED: executed in a scratch directory; the project's `uv.lock` and `pyproject.toml` were not touched, `git status` clean]
- Installed in a separate scratch venv only: unpacked size of `scipy` is 81 MB; `from scipy.optimize import linear_sum_assignment` takes about 0.37 s wall (numpy alone 0.13 s), so import it lazily inside the matching function. pyright 1.1.414 in `standard` mode reports `0 errors` for a file using `linear_sum_assignment` with scipy 1.18.1 installed. [VERIFIED: executed]
- Packages removed (SLOP): none. Packages flagged SUS: scipy (checkpoint required).
- No postinstall concept applies to a PyPI wheel; the review must confirm that the lock lists a wheel (not only the sdist) for the platforms CI and the developer use, as in 04-02.

## Architecture Patterns

### System Architecture Diagram

```
                      data/dataset/manifest.json  (per-case split, base_case, rendering, severity,
                                |                   barcode_readable, tags; sha256 of the file)
                                v
  cases.jsonl --> _read_records --> grade_case (per record) ---------------------+
  (stored run,        |              status, validator block, cache, latency,    |
   never changed)     |              cost (unchanged)                            |
                      |                                                          v
  data/dataset/*.xml -+--> invoice_from_xml (ground truth)         graded diff per case
                                                                    1. 27 header fields (existing _field_grade)
                                                                    2. rows: score matrix -> LSA -> pairs,
                                                                       unmatched truth, unmatched pred
                                                                    3. per matched pair: 14 column grades
                                                                    4. installments: same, 3 columns
                                                                    5. wrong-value list (path, expected, predicted)
                                                                              |
                                                                              v
                                                    ordered first-match tag rules (failure-tags-1)
                                                    one tag per wrong value; case lists its tags
                                                                              |
                          +-----------------------------------+---------------+
                          v                                   v
                 per-case grade (embedded in            build_summary (v4)
                 summary.json "cases")                  counts, headline rows (Wilson on invoices),
                          |                             slices, field and item stats (bootstrap),
                          |                             silent errors (payment / other), tags,
                          |                             cost, cache, latency p50/p95, incomplete flag
                          |                                       |
                          |                                       v
                          |                            summary.json + summary.md
                          |                                       |
                          +------------------>  compare A B  <---+   (reads two summary.json only)
                                                 refuse: manifest hash | grader version | tolerance  (exit 2)
                                                 intersect case ids; McNemar on text-layer correct flag;
                                                 regressed / worsened / improved / not comparable lists
```

### Recommended Project Structure

```
python/src/carimbo_evals/
├── grader.py        # extend: per-case graded diff; DEFAULT_TOLERANCE = Decimal("0.00"); keep _load_manifest and _read_records signatures (decision_grader imports them)
├── matching.py      # NEW: identity-column score, assignment (lazy scipy import), tie-break; rows and installments
├── tags.py          # NEW: failure-tags-1 ordered rules; pure over the graded diff
├── stats.py         # NEW: wilson, mcnemar_exact, bootstrap, nearest_rank; stdlib only
├── slices.py        # NEW: declared slice list (versioned), manifest per-case metadata loader
├── summary.py       # extend: SUMMARY_VERSION = 4, GRADER_VERSION = "grader-003", new blocks; keep the 27-field FIELDS
├── compare.py       # NEW: refusal contract, McNemar verdict, case lists, text/markdown output
├── cli.py           # add: compare, run --split, grade --out
└── corruptions.py   # NEW (recommended): seeded corruption catalogue keyed by tag, shared by pytest and the e2e proof
python/tests/        # no conftest.py and no shared helper module exists (importlib import mode); put shared helpers in src
```

`python/tests` has neither `conftest.py` nor `__init__.py`, and `addopts` uses `--import-mode=importlib`, so one test file cannot import another. A corruption catalogue used by both a pytest self-test and the e2e proof belongs under `src/carimbo_evals/` (stdlib only, no HTTP client) rather than in a test file. [VERIFIED: `ls python/tests`, `pyproject.toml` `[tool.pytest.ini_options]`]

### Pattern 1: ordered first-match rules with a version string and a rule number

The decision labelling rule is the shape to copy. [VERIFIED: dotnet/src/Carimbo.Decisions/DecisionRules.cs:16-19 and docs/DECISIONS.md:740] Its constant and comment read:

```csharp
public const string LabellingVersion = "decision-labels-1";
/// Applies the five ordered rules; the first match wins:
```

and it returns `DecisionLabel(Outcome, Cause, Rule)` with the rule number. For tags, use a version constant (for example `TAGS_VERSION = "failure-tags-1"`), an ordered tuple of `(tag, predicate)` and record the rule number next to each wrong value, so a reviewer can see which rule fired. A test that pins the ordered tag names makes a later change a visible grader version bump (D-12).

### Pattern 2: row matching as an integer score plus an explicit tie-break

```python
# Source: executed prototype, /tmp/.../scratchpad/tag_proto.py (not project code)
MATCH_W = {"code": 3, "description": 3, "ncm": 1, "unit": 1, "quantity": 2, "unit_price": 2, "total": 2}  # sum 14
MATCH_MIN = 6   # score of 14 needed for a pair to count as matched

def assign(pred, truth, weights, minimum):
    S = np.array([[score(p, t, weights) for t in truth] for p in pred], dtype=np.int64)
    credit = np.where(S >= minimum, S, 0)            # below the minimum earns nothing
    ii, jj = np.indices(S.shape)
    cost = (sum(weights.values()) - credit) * 10**6 + np.abs(ii - jj)   # integer costs, index-distance tie-break
    r, c = linear_sum_assignment(cost)               # lazy import inside the function in real code
    pairs = [(int(a), int(b)) for a, b in zip(r, c, strict=True) if credit[a, b] > 0]
```

`score` adds a column's weight when the normalized text (`normalize_name`) or the `Decimal` value is equal. It is independent of `--tolerance`, so a regrade at a different tolerance never changes which rows were matched.

### Anti-Patterns to Avoid

- **Scoring all 14 columns for matching.** Shared values (zero taxes, the same CFOP, the same unit) make unrelated rows look alike: with all 14 columns the best non-self similarity between two unrelated truth rows reaches 0.714, above the 0.5 that confusable partners score. The 7 identity columns separate them (below).
- **Float costs with an implicit tie-break.** The scipy page does not say how ties are broken or whether the output is stable across versions. [CITED: docs.scipy.org linear_sum_assignment] Use integer costs and the index-distance term so the result is a pure function of the matrix.
- **Altering `_load_manifest`.** `decision_grader.py` imports `_load_manifest` and `_read_records` from `grader.py`; add a new loader for per-case dataset metadata instead.
- **Adding keys to `field_accuracy[...]` entries.** Two e2e assertions compare the entries to `{"correct": 2, "n": 2}` exactly (see Pitfall 4). Put intervals in a new block.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Optimal row assignment | a greedy or position-based matcher | `scipy.optimize.linear_sum_assignment` (D-08) | Greedy fails on the confusable pairs when a row is dropped or duplicated; the LSA optimum plus the index tie-break passes every probe in 154/154 cases. |
| Wilson interval | a normal-approximation interval | the Decimal Wilson in "Code Examples" | Normal approximation is wrong near 0 and 1, where these rates sit. |
| McNemar p-value | a chi-square approximation | exact binomial on the discordant pairs with `Fraction` | Discordant counts are small (a handful), so an approximation is wrong; the exact value is a few lines. |
| Percentiles | `statistics.quantiles` or `numpy.percentile` interpolation | nearest-rank on sorted ints | No interpolation, so an integer-millisecond input gives an integer-millisecond output that regrades identically. |
| Random stream for the bootstrap | numpy `default_rng` | `random.Random(seed).random()` | The only stream the Python docs guarantee across versions. |
| Text similarity for `text_near_miss` | an edit-distance dependency | `difflib.SequenceMatcher(None, a, b, autojunk=False).ratio()` | Stdlib, deterministic; `autojunk=False` removes the long-string heuristic. |

**Key insight:** every statistic here has a closed form over small integers, so the whole summary can be computed with `Decimal`, `Fraction` and `math.comb` and serialized as fixed-precision numbers, which is what makes regrades byte-stable.

## Runtime State Inventory

Not a rename or migration phase: omitted. One related fact: the committed runs under `evals/results/` are grader-001 and grader-002 outputs at tolerance 0.01 against datasets `skeleton-001`/`skeleton-002`, summary versions 1, 2 and 3 (decision runs use `decision-summary-1`). They will not be comparable with grader-003 output until regraded; see "What `summary.json` consumers pin".

## Common Pitfalls

### Pitfall 1: the dataset prints some values wrapped mid-token (the wrapping question, answered)

**What goes wrong:** CONTEXT assumed wrapping happens at word boundaries only (checked on `nfe-0006`). Running a whole-token check over all 90 text-layer DANFEs finds two real mid-token wrap families. A correct answer is still correct under D-01 and D-04 only if the model reassembles the value; a model that copies the broken text fails the invoice.

1. **Descriptions, names, operation nature and item codes: no mid-word wrap.** For every text-layer case, every word of every description, party name, `natOp` and item code appears as a whole whitespace-delimited token, and each description appears contiguously after whitespace collapse. This includes all 18 `long_text` cases (`nfe-0008`, `nfe-0040`, `nfe-0042`, `nfe-0044` among them). Collapsing whitespace is sufficient for these. [VERIFIED: executed over `data/dataset`, 90 text-layer PDFs read with pypdfium2]
2. **Unit `PAR` wraps as `PA` / `R`.** The narrow `UN.` column breaks the three letters; the text layer reads `... 6101 PA` then `R` on its own line, and the rendered page shows `PA` over `R` (looked at the image of `nfe-0005`). Only `PAR` does it; `UN`, `M`, `LT`, `CX`, `PCT`, `RM` print whole. 62 rows in 43 cases carry `PAR`: 35 of the 90 text-layer cases plus their 8 image-only twins; on `dev` 10 of 30 text-layer cases, on `test` 25 of 60. [VERIFIED: executed]
3. **Large unit prices and item totals wrap mid-number in every `confusable` case.** Example, `nfe-0009` item 1: the text layer reads `685.457,072` then `0` (unit price `685.457,0720`), `1.539.546.2` then `48,66` (total `1.539.546.248,66`), and the ICMS base and amount wrap too. 18 cases are affected (38 values): `nfe-0009 0017 0022 0025 0030 0032 0034 0036 0045 0055 0062 0067 0069 0075 0077 0079 0081 0090`, which are exactly the 18 cases tagged `confusable`; none has an image-only twin. The cause is the generator's deliberate many-digit line (`_BIG_QUANTITY = (2_000, 9_999)`, `_BIG_UNIT_PRICE = (500_000, 999_999)`, invoice total up to 10^9). [VERIFIED: executed; dataset_spec.py:162-164 read]
4. **Combined effect on the headline row.** 31 of the 60 `test` text-layer cases carry at least one hazard (25 have a `PAR` row, 14 a wrapped big number, 29 have neither) and 12 of the 30 `dev` text-layer cases do (10 with `PAR`, 4 with a wrapped big number, 18 with neither). [VERIFIED: executed]

**How to avoid:** do not change the grader to forgive these (D-04 says nothing fuzzy is correct, and `dataset-001` is frozen under Phase 4 D-17). Do three things instead: (a) keep `text_wrong`/`text_near_miss` honest for `unit` (similarity of `PA` to `PAR` is 0.8, so it tags `text_wrong`), (b) when the dev run's failures arrive, check whether `unit == PAR` and the confusable many-digit line dominate them before reading them as model weakness (the confusable slice and `PAR` rows are then a rendering confound, to be named in the README rather than hidden), and (c) the README must not describe the confusable slice as measuring row matching alone. A cheap, grader-side way to keep the confound visible without touching the dataset is a per-case boolean computed from the ground truth XML (`has_par_unit`, `has_wrapped_big_number`) stored in the case record; the planner decides whether that is worth a slice. [ASSUMED: that a model sees the printed layout and the Anthropic PDF text extraction like pdfium; the model may well reassemble the wrapped values. Only the dev run can say.]

**Warning signs:** `unit` failures concentrated on `PAR` rows; `value_from_other_box`, `thousands_misparse` or `digit_count` tags concentrated on the `confusable` slice's one many-digit row.

### Pitfall 2: `test_vectors.py` pins the grader's default tolerance

`python/tests/test_vectors.py:107-108` reads:

```python
def test_grader_default_tolerance_matches_the_vector_file() -> None:
    assert Decimal(VECTORS["tolerance"]["tolerance"]) == DEFAULT_TOLERANCE
```

and `data/vectors/validator-vectors.json` holds `"tolerance": {"tolerance": "0.01", "sum_cap": "1.00"}`, which .NET also reads (`ValidationOptions().Tolerance`, `dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs:354-358`). That coupling made sense when the grader reused the validator tolerance; D-03 deliberately separates them. Setting `DEFAULT_TOLERANCE = Decimal("0.00")` fails this test. The test must be deleted or rewritten to say the grader default is independent of the validator tolerance (do not touch the vector file; .NET reads it). [VERIFIED: files read this session; `python/tests/test_vectors.py:107-108` and `data/vectors/validator-vectors.json` tolerance block quoted verbatim above]

### Pitfall 3: `grade` writes into the run directory

`cli.py` `grade` ends with `write_summary(run, summary)` and prints `(run / "summary.md")`. Regrading a verbatim copy under `evals/results/<run>/` would overwrite its `summary.json` and `summary.md`, which CONTEXT says never happens. `compare` also needs both runs regraded with the same grader. Add `grade --out <dir>` (write the summary there, leave the run untouched), or copy `cases.jsonl` into `evals/runs/` first; `--out` is the safer contract. [VERIFIED: python/src/carimbo_evals/cli.py `grade` command]

### Pitfall 4: tests pin the current summary shape

Pins that the plans must update or keep green (all read this session):

| Pin | Where | What to do |
|-----|-------|------------|
| `assert GRADER_VERSION == "grader-002"` and `assert SUMMARY_VERSION == 3` | `python/tests/test_grader.py:343-346` and `:760-762` | bump to `"grader-003"` and `4` |
| `assert summary["tolerance"] == "0.01"` for `grade_run` default | `python/tests/test_grader.py:765` | becomes `"0.00"` when `DEFAULT_TOLERANCE = Decimal("0.00")`; `str(Decimal("0.00"))` is `"0.00"` |
| `assert len(ALL_FIELDS) == 27` and `ALL_FIELDS[-2:] == ["item_count", "installment_count"]` | `python/tests/test_grader.py:339-346` | keep `FIELDS` at the 27 header fields; add item and installment grades elsewhere |
| `field_accuracy["access_key"] == {"correct": 2, "n": 2}` and `["totals.invoice_total"] == {"correct": 1, "n": 2}` | `python/tests/test_e2e_fake.py:325-326`, `:698` | do not add keys to these entries; put CIs in a new block |
| `recorded["summary_version"] == replayed_summary["summary_version"] == 3` | `python/tests/test_e2e_fake.py:449` | `4` |
| `graded["case-001"]["fields"] == dict.fromkeys(ALL_FIELDS, True)` | `python/tests/test_e2e_fake.py:308`, `:690-692`, `:797` | keep `fields` as the 27 header booleans |
| `test_grader_default_tolerance_matches_the_vector_file` | `python/tests/test_vectors.py:107-108` | see Pitfall 2 |
| `build_summary(grades, {"run_id": "r", "tolerance": TOLERANCE}, None)` called directly | `python/tests/test_grader.py:883, 1060, 1091, 1104, 1123` | keep the `(case_grades, run_meta, manifest)` call shape working with `manifest=None` and no per-case dataset metadata |

`test_readme_drift.py` checks only that each `evals/results/<run>` directory is linked from README.md and that links resolve; it does not read any `summary.json`. No code outside Python reads `summary.json`, and the golden fixtures under `data/contract/` hold extraction and decision responses, not summaries, so regenerating `grader-003` summaries touches no golden file. [VERIFIED: `grep` over the repo and `python/tests/test_readme_drift.py:407-450`]

### Pitfall 5: the D-11 draft order makes two tags unreachable

D-11 lists `dropped_row` and `invented_row` before `merged_rows` and `split_rows`. In a first-match list the generic rules would always win: a merge leaves one truth row unmatched, which `dropped_row` would already tag. Order refinements first: `merged_rows`, `split_rows`, then `trailing_rows_lost`, `dropped_row`, `invented_row`, then `value_from_other_row`. The prototype uses this order and produces exactly one tag for each seeded corruption. This is a finding about the draft, not a reopening of D-10 or D-11 (the list is "roughly in match order").

### Pitfall 6: counts, double counting and `schema_invalid`

- `item_count` and `installment_count` are graded header values (they are in the 27). A dropped row therefore yields two wrong values: the missing row (counted as a wrong item total per D-06) and a wrong `item_count`. Both are wrong values and both need a tag so that tags sum to the total (D-10). Give the count the tag of the first row event in index order (`trailing_rows_lost`, `dropped_row`, `invented_row`, `merged_rows` or `split_rows`); `untagged` only if no row event explains it, which cannot happen with the matcher above.
- `schema_invalid` is "wrong on every field" (D-01/D-02 and the existing `grade_case`), so all 27 header fields plus every row would land in `untagged` and swamp the bucket D-10 wants to show the rule gaps. Recommend one rule at the top, `schema_invalid`, that tags every wrong value of a `schema_invalid` case. It is an addition to D-11's list that the planner can adopt or replace by excluding `schema_invalid` wrong values from the tag counts and reporting them apart.
- `zero_for_blank` (D-11) is ambiguous by name. In this dataset an absent tax tag prints `0,00` (ground truth `_fixed(None)` yields `0.00`), so "blank" is never printed. Define it as: predicted value is numerically zero and the expected value is not. The mirror (expected zero, predicted not) falls through to `value_from_other_box` or `untagged`; if the dev run shows it often, name it then.
- Dates are excluded from the digit rules in the prototype (they go straight to `date_wrong`), otherwise a one-digit date error is tagged `digit_substitution`. A day/month swap in dates is a likely real failure and a candidate extra tag after the dev run.

### Pitfall 7: Decimal context and float output

Use `decimal.localcontext()` rather than mutating the global context, quantize with an explicit rounding (`ROUND_HALF_UP`), and serialize rates and bounds as numbers rounded to 4 places, like `hit_rate` today (`round(hits / answered, 4)`). `"0"` and `"0.00"` are the same tolerance: normalize the stored string (a new default of `Decimal("0.00")` prints `"0.00"`, while `--tolerance 0` prints `"0"`), and make `compare` compare `Decimal` values, not strings.

### Pitfall 8: the manifests do not all have per-case dataset metadata

`data/skeleton/manifest.json` has `case_id`, `expected`, `pages`, `pdf`, `pdf_sha256`, `tags`, `xml`, `xml_sha256` only: no `split`, `base_case`, `rendering`, `severity` or `barcode_readable`. Several `test_grader.py` fixtures write ad hoc manifests or none. The loader must treat all per-case metadata as optional (`base_case` defaults to `case_id`; no split means no headline rows; slices omitted), or `grade --cases data/skeleton` and the existing tests break. [VERIFIED: `data/skeleton/manifest.json` and `data/dataset/manifest.json` read]

### Pitfall 9: the live dev run

- The recipe must pass `--split dev` through the runner; `discover_cases` globs every `*.pdf` of the directory and has no split or subset option today. [VERIFIED: runner.py `discover_cases`]
- The runner defaults are `DEFAULT_MAX_COST_USD = Decimal("1.00")` and `DEFAULT_RESERVE_USD = Decimal("0.25")` (runner.py:37-38), so the recipe needs `--max-cost-usd 2.50` explicitly and AGENTS.md's "Live paid steps" list and caps need the new recipe.
- If the cap stops the run (exit 3), the summary must say `incomplete`; D-02 already marks infrastructure and harness cases as incomplete, and a cap stop leaves ungraded cases that are in neither the numerator nor the denominator.
- The recipe runs `dataset-check` (46 s measured) like `_skeleton-run` runs `datagen-check`; keep it.
- `data/dataset` is 19,562,414 bytes against a 20,000,000-byte hard limit (Phase 4 D-12, warning at 18,000,000 in `test_dataset_manifest.py:197`). The dataset is frozen; nothing in Phase 5 may add a file there.

## Code Examples

All executed in a scratch directory this session against scipy 1.18.1, numpy 2.5.3 and Python 3.12.14.

### Wilson interval with an effective n (D-14)

```python
from decimal import Decimal, localcontext, ROUND_HALF_UP
Z = Decimal("1.959963984540054")   # 97.5th percentile of the normal; fixed constant, never computed

def wilson(correct: int, cases: int, invoices: int) -> tuple[Decimal, Decimal] | None:
    """95% Wilson score interval for the rate correct/cases with n set to the distinct invoices."""
    if cases == 0 or invoices == 0:
        return None
    with localcontext() as ctx:
        ctx.prec = 28
        p, n, z2 = Decimal(correct) / Decimal(cases), Decimal(invoices), Z * Z
        d = 1 + z2 / n
        centre = (p + z2 / (2 * n)) / d
        half = Z * ((p * (1 - p) / n + z2 / (4 * n * n)).sqrt()) / d
        q = Decimal("0.0001")
        return (max(Decimal(0), centre - half).quantize(q, ROUND_HALF_UP),
                min(Decimal(1), centre + half).quantize(q, ROUND_HALF_UP))
```
Checked against `scipy.stats.binomtest(k, n).proportion_ci(method="wilson")` for every k with n in 1..100, 154 and 300: 0 mismatches at 5e-5 tolerance, and the float and Decimal versions agree after rounding for the same grid. Examples: `wilson(30, 40, 5) = (0.3360, 0.9468)` (40 cases, 5 invoices, 30 correct); `wilson(45, 60, 60) = (0.6277, 0.8422)`; `wilson(0, 60, 60) = (0.0000, 0.0602)`; `wilson(60, 60, 60) = (0.9398, 1.0000)`.

### Exact McNemar on the correct flag (D-16)

```python
import math
from fractions import Fraction

def mcnemar_exact(b: int, c: int) -> Fraction:
    """Two-sided exact p-value; b, c = cases correct in one run only."""
    n = b + c
    if n == 0:
        return Fraction(1)
    tail = sum(math.comb(n, i) for i in range(min(b, c) + 1))
    return min(Fraction(1), Fraction(2 * tail, 2 ** n))
# significant  <=>  p < 1/20, tested as 20 * numerator < denominator (no float compare)
```
Equal to `scipy.stats.binomtest(min(b, c), b + c, 0.5).pvalue` for all b, c below 40 (to 1e-12). With 5 discordant cases all one way p = 0.0625 (not significant); with 6 all one way p = 0.03125 (significant). Recommended verdict rule: `significant` when p < 0.05; `no change` when there are no discordant text-layer cases (`b = c = 0`); `inconclusive` otherwise. Print b, c, n and the exact p so the number, not the label, is the evidence. [VERIFIED: executed]

### Seeded bootstrap over invoices (D-14), stable stream

```python
import random
def bootstrap_sums(vectors: list[list[int]], seed: int, resamples: int) -> list[list[int]]:
    """vectors: one list of integer counts per invoice (base_case), sorted by base_case id."""
    rnd, n, k = random.Random(seed).random, len(vectors), len(vectors[0])
    out = []
    for _ in range(resamples):
        sums = [0] * k
        for _ in range(n):
            row = vectors[int(rnd() * n)]
            for j in range(k):
                sums[j] += row[j]
        out.append(sums)
    return out
# interval = nearest-rank 2.5th and 97.5th percentile of each ratio: rank = ceil(q * resamples), 1-based
```
`random.Random.random()` is the one generator output the Python docs guarantee unchanged ("The generator's random() method will continue to produce the same sequence when the compatible seeder is given the same seed"); `randrange` and `choices` are not covered. [CITED: docs.python.org/3/library/random.html] Timing: 10,000 resamples x 90 invoices x 30 counters took 0.7 s in pure Python. The resampling unit is the `base_case`: a base and its image-only twins move together. Record `{seed, resamples, unit: "base_case", method: "percentile", rng: "random.Random.random"}` in the summary.

### Nearest-rank percentiles

```python
import math
def nearest_rank(sorted_values: list[int], pct: int) -> int | None:
    if not sorted_values:
        return None
    return sorted_values[max(0, math.ceil(pct * len(sorted_values) / 100) - 1)]
```
p50 and p95 over `model_latency_ms` (the original model latency, so a free replay reports the same numbers). Keep `None` for no data, as `_latency` does now. The current `_latency` returns `min`, `median` (statistics.median, which interpolates for an even count) and `max`; with the summary version bump replace `median` by `p50` and add `p95` and `n`.

### Row matching result on the real data

| Check (over all 154 cases unless stated) | Result |
|---|---|
| Ground truth matched against itself (diagonal, every row) | 154/154 (0.28 s total, including XML parsing) |
| Shuffled rows, reversed rows | 154/154, 154/154 |
| One row deleted: n-1 pairs, exactly that truth row unmatched | 141/141 (the cases with at least 2 rows) |
| One row duplicated: n pairs, exactly one unmatched predicted row | 154/154 |
| 36 confusable pairs (2 in each of the 18 confusable cases): drop one partner, shuffle, partner slot filled with the other's values, a digit transposed in the differing value | 36/36 each |
| Score between the two partners of a confusable pair (of 14) | exactly 7 in all 36 (same description, NCM, unit and one of quantity or unit price; different code and total) |
| Score between any two other truth rows in the same invoice | at most 5 (906 pairs reach 5: same description, NCM and unit; 12,875 score 1, 19,665 score 0, 169 score 2) |

So a minimum of 6 of 14 sits between the other-row ceiling (5) and the confusable-partner score (7). The earlier all-14-column variant fails that separation (unrelated rows reach 0.714), and a minimum above 0.5 would turn a model that copies the first partner into the second slot into an `invented_row` plus `dropped_row` instead of a mismatched row tagged `value_from_other_row`. The confusable partner differs in `code`, which is why code carries weight 3 as does description. Installments: score `number` 2, `due_date` 2, `amount` 1, minimum 2; 54 of the 58 multi-installment cases have equal installment amounts, so number and due date must carry the match. [VERIFIED: executed over `data/dataset`; `data/dataset` has 0 twin XML files that differ from their base XML, so twins share ground truth]

### Failure tags: what each needs, and the proof

Inputs: **diff** = the graded wrong-value list (path, expected, predicted); **assign** = the row assignment (matched pairs, unmatched truth rows, unmatched predicted rows).

| Order | Tag | Rule (first match wins per wrong value) | Needs |
|---|---|---|---|
| 0 | `schema_invalid` (addition) | the case status is `schema_invalid` | status |
| 1 | `party_swap` | the case shows a party crossover (predicted issuer CNPJ or name equals the expected recipient tax id or name, or the reverse), and the wrong value is under `issuer.` or `recipient.` | diff |
| 2 | `merged_rows` | an unmatched truth row `u` and a matched pair (p, t) with `p.total == t.total + u.total` or `p.quantity == t.quantity + u.quantity` (non-zero): tags the missing row `u` and the matched pair's `total`/`quantity` values | assign + diff |
| 3 | `split_rows` | an unmatched predicted row `q` and a matched pair (p, t) with `p.total + q.total == t.total` (or quantity): tags `q` and the matched pair's value | assign + diff |
| 4 | `trailing_rows_lost` | the unmatched truth row and every truth row after it are unmatched | assign |
| 5 | `dropped_row` | any other unmatched truth row (also unmatched installment) | assign |
| 6 | `invented_row` | any other unmatched predicted row (also installment) | assign |
| 7 | `value_from_other_row` | a matched row's wrong item value (code, description, NCM, quantity, unit price, total, ICMS base, ICMS amount, IPI amount) equals the same column of a different truth row, non-zero | assign + diff |
| 8 | `value_from_other_box` | a header total equals a different expected totals box (non-zero), or an item money column equals another money column of the same truth row | diff |
| 9 | `thousands_misparse` | numeric, same significant digits, different value (decimal point or thousands separator misread) | diff |
| 10 | `zero_for_blank` | predicted is numerically zero and expected is not | diff |
| 11 | `digit_transposition` | id-like or numeric strings, separators removed, same length, exactly two adjacent positions swapped | diff |
| 12 | `digit_substitution` | same length, exactly one position differs | diff |
| 13 | `digit_count` | one character inserted or deleted | diff |
| 14 | `text_near_miss` | text column, `SequenceMatcher(autojunk=False).ratio()` of the normalized strings is at least 0.9 | diff |
| 15 | `text_wrong` | any other text column or enumerated value (names, `operation_nature`, description, unit, UF, `tax_id_kind`) | diff |
| 16 | `tax_code_wrong` | `cst_csosn`, `cfop` or `ncm` that missed the digit rules | diff |
| 17 | `date_wrong` | `issue_date` or an installment `due_date` | diff |
| 18 | `untagged` | everything else | |

Counts (`item_count`, `installment_count`) take the tag of the first row event. Only rules 2 to 7 need the row assignment; every other rule is a pure function of one wrong value (or the case's header). Prototype results over the real data: ground truth gives 0 wrong values in 154 of 154 cases; for 18 corruption kinds, each producing a different expected tag, every applicable case produced a wrong-value set tagged by exactly the expected tag, no failures: `party_swap` 154/154, `trailing_rows_lost` 117/117 (needs at least 4 rows), `dropped_row` 117/117, `invented_row` 154/154, `merged_rows` 128/128, `split_rows` 154/154, `value_from_other_row` 128/128, `value_from_other_box` 154/154, `thousands_misparse` 154/154, `zero_for_blank` 41/41 (needs a non-zero ICMS amount), `digit_transposition`, `digit_substitution`, `digit_count` 154/154 each, `text_near_miss` 154/154, `text_wrong` 154/154, `tax_code_wrong` 154/154, `date_wrong` 154/154, `untagged` 154/154. The whole battery (2,772 corruptions plus 154 ground-truth diffs) took 14.4 s in an unoptimized prototype; budget it, see Validation Architecture. The shape for the decision rule (version string, numbered rules, first match, the rule number returned) is `DecisionRules.Label` above.

### Payment values and silent errors (D-05, D-06)

A wrong value is a **payment value** when its path is one of: `access_key`, `issuer.cnpj`, `recipient.tax_id`, `issue_date`, `totals.invoice_total`, any item `total` (including a missing or extra row, which counts as a wrong item total), and any installment `due_date` or `amount` (a missing or extra installment counts as a wrong installment amount). Silent invoice = status `success` with at least one wrong graded value; `silent_payment` = the subset with at least one payment-value wrong; `silent_other` = the rest. The rate is `silent / success`. Per-field wrong counts on `success` cases are D-07.

Which seeded corruptions the real validators let through matters for the scripted proof (D-20). The rule ids are `CNPJ_FORMAT`, `CNPJ_CHECK_DIGIT`, `KEY_FORMAT`, `KEY_CHECK_DIGIT`, `KEY_ISSUER_CNPJ_MISMATCH`, `KEY_NUMBER_MISMATCH`, `ITEM_ARITH`, `TOTAL_SUM_PRODUCTS`, `TOTAL_VNF_FORMULA`, `DUP_SUM`, among others (`dotnet/src/Carimbo.Validation/Findings.cs:55-85`). So silent corruptions (status stays `success`) are changes the checks cannot see: description, unit, code, operation nature, party names, NCM, CFOP, installment number, a valid-but-swapped party id. Corruptions that break arithmetic or the key (a transposed access-key digit, an invoice total times 100, a dropped row that breaks the sum) end `validation_failed` after the repair attempts, which is the "caught" half the proof also needs. The pytest self-tests do not run the validators; they build records with status forced to `success`.

### What `summary.json` consumers pin, and how to bump

Recommended v4 shape (additive; every existing key keeps its meaning): `summary_version: 4`, `grader_version: "grader-003"`, new `tags_version`, `slices_version`, `stats` (z, bootstrap seed, resamples, method), `primary_metric` (name `correct_invoice_rate` plus the D-02 denominator rule), `incomplete` (true when any infrastructure failure, harness error or a cap stop), `headline` rows (text-layer and image-only rows for `test`, `dev` and `full`, each with cases, invoices, correct, rate, interval), `slices`, `field_stats` (header fields with intervals; `field_accuracy` unchanged), `items` and `installments` blocks (precision, recall, F1, per-column accuracy over matched pairs with bootstrap interval, all-values-correct row rate, order-preserved flag), `silent_errors`, `failure_tags` (counts, `untagged`), `within_cent` count, latency `p50`/`p95`. Per case (for `compare`): `correct`, `split`, `base_case`, `rendering`, `severity`, `barcode_readable`, manifest `tags`, and a `wrong` list with path, tag, rule number, payment flag, expected and predicted. `compare` needs nothing else from raw JSONL.

Existing published runs: `skeleton-20261008T001300Z` (summary 1, grader-001, `skeleton-001`), the two 2026-10-08 runs (summary 2, grader-002) and the two 2026-10-15 runs (summary 3, grader-002, `skeleton-002`, manifest sha256 `1ddbdddbaeb3b0a876f4e94862a29ad1431637b3a215af3df0e0234f95a17a77`, which still equals `sha256sum data/skeleton/manifest.json`); decisions use `decision-summary-1`. [VERIFIED: summary.json files read] None of them can be compared to a grader-003 summary, by design. They can be regraded (`grade --out`) because skeleton-002 still matches. They carry no `split` or `rendering`, so they yield no headline rows; that is a loader requirement (Pitfall 8), not a reason to touch them.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Tolerance 0.01 default, field-level header grading only (27 fields) | Tolerance 0.00 default, all item columns and installments, optimal row matching | this phase (D-03, D-01) | `grader-003`, summary version 4 |
| `hit_rate` as the only float, min/median/max latency | Rates and intervals rounded to 4 places, p50/p95 nearest-rank | this phase | one rounding rule for every float |
| `httpx` 0.28.1 | `httpx2` | already in the stack | not touched here |

**Deprecated/outdated:** none in scope.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The model's view of a wrapped DANFE value matches pdfium's text and the rendered image, so `PA`/`R` and the split big numbers are real hazards for it | Pitfall 1 | If the model reassembles them reliably the confound is small; if it copies them, the confusable slice and `PAR` rows dominate the failures. The dev run shows which. |
| A2 | Bootstrap for every headline and slice row stays near 10 s per regrade (extrapolated from 0.7 s for one 90 x 30 row set) | Code Examples | Slower regrades; mitigated by a `--resamples` knob and fewer resamples in tests |
| A3 | The D-19 estimate of US$1.2 to 2.2 holds (CONTEXT numbers; reconciled only against the 3 skeleton cases) | Pitfall 9 | The US$2.50 cap stops the run early and leaves the dev summary `incomplete`; per-attempt costs scale with items because a repair re-emits the whole invoice |
| A4 | `normalize_id` (strip spaces, dots, slashes, hyphens, uppercase) is acceptable for `ncm`, `cfop` and `cst_csosn`, while D-04 names only names, descriptions, item codes and units for `normalize_name` | "Row matching" and tags | If a stricter text rule is wanted for those columns, `NCM 7318.15.00` would be wrong rather than right |
| A5 | The `unit` `PAR` and many-digit hazards are the only mid-token wraps; the whole-token check covered descriptions, names, `natOp`, item code, unit, NCM, CFOP, item quantity, unit price and total, and the invoice-level `vNF`, `vProd`, `vICMS`, `vBC` totals only | Pitfall 1 | Other printed values (other totals boxes, installment lines, access key groups) were checked only by the existing whitespace-stripped test |

## Open Questions

1. **Which of the three hazards gets a slice or a per-case flag?**
   - What we know: 31 of 60 test text-layer cases are affected; the confusable slice is entirely wrapped.
   - What's unclear: whether the maintainer wants the confound visible in `summary.json` (a derived per-case flag) or only in the README.
   - Recommendation: add a derived per-case `render_hazards` list from the ground truth XML (no dataset change), do not make it a headline slice; decide in the plan, and re-read after the dev run.

2. **Alias or dated snapshot for the dev run model (DECISIONS D-21).**
   - Recommendation: the dated snapshot recorded in `model_returned` is already stored per case, so either works for provenance; use the default `claude-haiku-4-5`, as CONTEXT defaults, and keep `read_only` replay the proof of reproducibility.

3. **`validation_failed` and `correct`.** Resolved by the CONTEXT default (correct requires `success`). Nothing further.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Python | grader, tests | yes | 3.12.14 (`python/.venv`) | none needed |
| uv | lock, sync | yes | 0.12.11 (pyproject requires `>=0.8.17`) | none needed |
| .NET SDK | scripted host, e2e, dev run | yes | 10.0.400 (nix) | none needed |
| just | recipes | yes | 1.58.0 | none needed |
| scipy | row assignment | no (not in the lock) | 1.18.1 resolves and installs in a scratch venv | blocked until the review checkpoint |
| docker | Jaeger (optional trace viewer) | yes | present | optional |
| Provider key (`CARIMBO_ANTHROPIC_API_KEY`, `ANTHROPIC_API_KEY`) | the single live dev run | no (both unset in this shell; `secretspec` is present) | n/a | the live run waits for the key and the explicit spend approval; everything else is offline |

**Missing dependencies with no fallback:** none for the offline work.
**Missing dependencies with fallback:** scipy (gate plan first), the provider key (only the live step needs it).

## Validation Architecture

Framework: pytest 9.1.1 with markers `e2e` and `live`, selected by `addopts = ["--import-mode=importlib", "-m", "not e2e and not live"]`. Config: `python/pyproject.toml`. Baselines measured this session: `pytest python/tests -q` is 2,810 passed, 13 deselected, 43 s; `pytest -m e2e` is 13 passed in 61 s (4.6 s host start-up per test, 1 to 2 s per test body); `carimbo-datagen dataset-check` takes 46 s.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | pytest 9.1.1 (+ pytest-asyncio 1.4.0), `asyncio_mode = "auto"` |
| Config file | `python/pyproject.toml` `[tool.pytest.ini_options]` |
| Quick run command | `uv run --project python pytest python/tests/test_<module>.py -q` |
| Full suite command | `just py-check` (sync, ruff, ruff format, pyright, pytest), `just e2e`, `just check` for everything |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| EVAL-03 | Exact match for IDs/CNPJ/key, tolerance 0.00 default and configurable | unit | `uv run --project python pytest python/tests/test_grader.py -q` | extend existing |
| EVAL-03 | Row assignment: ground truth self-matches, shuffled, reversed, delete, duplicate, confusable partners, score thresholds 5 / 6 / 7, tie-break determinism | unit over all 154 cases | `uv run --project python pytest python/tests/test_matching.py -q` | no, Wave 0 |
| EVAL-04 | Ground truth scores perfect with no tags for all 154; every tag has a seeded corruption producing exactly that tag; delete/duplicate/swap precision and recall exact | unit over all 154 cases | `uv run --project python pytest python/tests/test_selftests.py -q` | no, Wave 0 |
| EVAL-04 | Infrastructure failure and harness error stay out of the denominator, mark `incomplete` | unit | `uv run --project python pytest python/tests/test_grader.py -q -k "infrastructure or harness or incomplete"` | extend existing |
| EVAL-05 | Wilson, exact McNemar, bootstrap, nearest-rank against pinned numbers (hand-computed constants such as `wilson(30, 40, 5) = (0.3360, 0.9468)`; cross-check against scipy where k is an integer) | unit | `uv run --project python pytest python/tests/test_stats.py -q` | no, Wave 0 |
| EVAL-05 | Summary v4: headline rows, slices (n and invoices), field and item stats, cost, cache hit rate, p50/p95; a regrade is byte-identical apart from `meta.graded_at` | unit + tmp run dir | `uv run --project python pytest python/tests/test_summary_v4.py -q` | no, Wave 0 |
| EVAL-05 | Full-dataset summary over all 154 cases through the real HTTP path | e2e | `uv run --project python pytest python/tests/test_e2e_full_dataset.py -q -m e2e` | no, Wave 0 |
| EVAL-06 | Silent-error rate and payment/other split on seeded records; per-field wrong counts on `success` only | unit | `uv run --project python pytest python/tests/test_silent_errors.py -q` | no, Wave 0 |
| EVAL-07 | Tag rules (one test per tag), tag counts sum to the wrong-value total, `untagged` bucket, case tag lists, tag list order pinned (a change is a grader bump) | unit | `uv run --project python pytest python/tests/test_tags.py -q` | no, Wave 0 |
| EVAL-08 | `compare`: refusals (manifest hash, grader version, tolerance, decision summary) exit 2 with the fix named; case-set intersection and excluded count; regressed/worsened/improved/not comparable; McNemar verdicts `significant`, `inconclusive`, `no change`; exit 0 with regressions | unit + CLI runner | `uv run --project python pytest python/tests/test_compare.py -q` | no, Wave 0 |
| Run selection | `run --split dev` selects the 54 dev cases; `grade --out` leaves the run directory unchanged | unit/CLI | `uv run --project python pytest python/tests/test_runner.py python/tests/test_grader.py -q -k "split or out"` | extend existing |

### Sampling Rate

- **Per task commit:** the single new test module for the task plus `ruff check python` and `pyright -p python`.
- **Per wave merge:** `just py-check` (about 1 min now; plan for 1.5 min).
- **Phase gate:** `just check` green, then the live step after approval, then `just py-check` again with the committed result files and the README Status row.

### Time budget (D-20, and the discretion item on how `check` splits)

| Piece | Measured or estimated | Where it should run |
|---|---|---|
| Match all 154 ground truths | 0.28 s | pytest (`py-check`) |
| Diff and tag battery, 18 corruptions x 154 cases | 14.4 s unoptimized prototype | pytest; keep the full battery on all 154 cases only if it stays under about 15 s, otherwise run every corruption on the 54 dev cases plus a seeded sample of test cases and keep ground truth, delete, duplicate and swap on all 154 (EVAL-04 text requires ground truth on all 154) |
| Bootstrap, 10,000 resamples, 90 invoices, 30 counters | 0.7 s | pytest uses 200 to 500 resamples except one pinned test |
| Scripted host start-up | about 4.6 s | `just e2e` |
| Scripted run, 154 cases over HTTP at concurrency 2, ground-truth answers | 1.3 s, then `grade` 0.7 s | `just e2e`, one new test with corrupted answers; whole addition about 10 s |
| `dataset-check` | 46 s | already in `just check` |

Recommendation: the pure self-tests and the byte-stable regrade test in pytest (no .NET), and the scripted 154-case proof as one `e2e` test, built from the same `corruptions.py` catalogue so the two cannot drift. The proof answers every case from `<responses>/<sha256 of the PDF>.json`; twins have their own PDF hash in the manifest, so all 154 cases can carry different answers. Use a script with an `attempts` array only where a repair path is wanted; a root answer repeats on each attempt, which ends a case `validation_failed` after the repair budget.

### Wave 0 Gaps

- [ ] `python/tests/test_matching.py`, `test_selftests.py`, `test_stats.py`, `test_tags.py`, `test_silent_errors.py`, `test_summary_v4.py`, `test_compare.py` (new)
- [ ] `python/tests/test_e2e_full_dataset.py` (new, `e2e` marker)
- [ ] `python/src/carimbo_evals/corruptions.py` shared catalogue (new)
- [ ] Remove or rewrite `test_grader_default_tolerance_matches_the_vector_file`
- [ ] Update the pinned versions and tolerance string in `test_grader.py`, `test_e2e_fake.py`
- [ ] scipy added to the lock and installed only after the blocking checkpoint

## Security Domain

`security_enforcement` is enabled (`.planning/config.json`, `security_asvs_level: 1`).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no (the eval endpoint's key handling is unchanged) | existing `CARIMBO_EVAL_API_KEY` ephemeral key in the recipes |
| V3 Session Management | no | n/a |
| V4 Access Control | no | n/a |
| V5 Input Validation | yes | `compare` reads two JSON files that may come from a download or another machine: validate shape and types, treat any malformed or hostile file as a refusal with exit 2, never `eval`, no pickle, size sanity; graded `cases.jsonl` is parsed with the existing record reader |
| V6 Cryptography | no (SHA-256 of the manifest file only) | `hashlib.sha256` as `_load_manifest` does today |
| V14 Configuration / supply chain | yes | scipy pinned exactly, resolve-only lock review at a blocking, never auto-approved checkpoint, `uv sync --locked`, as 01-01 and 04-02 |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Compromised or typosquatted package (scipy) | Tampering | exact pin, lock diff shows scipy as the only new package, provenance attestation checked, human checkpoint |
| Crafted `summary.json` fed to `compare` (wrong types, huge lists, missing keys) | Tampering / DoS | schema-light validation up front, bounded work (intersection of case ids), exit 2 on anything unexpected |
| Provider key leakage in logs or committed results | Information disclosure | existing `secrets-check` recipe and `_with-provider-key`; presence-only checks (this research printed only set or unset) |
| Spend overrun on the live run | Denial of wallet | per-run cap US$2.50 in the recipe, runner stops at the cap, US$5 per phase, `read_only` replays are free |
| Holdout leakage (tag rules re-fitted on test) | Repudiation of results | D-12: freeze the list and its grader version before any `test` run; a pinned-order test makes a later change a visible bump |
| Real taxpayer data | Information disclosure | all data synthetic; unchanged (`test_synthetic_only.py`) |

## Sources

### Primary (HIGH confidence)
- Executed this session against the committed repo: `data/dataset/manifest.json` (154 cases), `data/dataset/*.xml` and `*.pdf` (pypdfium2 text extraction, a rendered page image), `python/src/carimbo_evals/{grader,summary,cli,ground_truth,decision_grader,runner}.py`, `python/tests/{test_grader,test_e2e_fake,test_vectors,test_readme_drift}.py`, `dotnet/src/Carimbo.Decisions/DecisionRules.cs`, `dotnet/src/Carimbo.Validation/Findings.cs`, `data/vectors/validator-vectors.json`, `evals/results/*/summary.json`, `justfile`, `AGENTS.md`.
- PyPI JSON API for scipy (version, release dates, `requires_dist`, `requires_python`, file hashes) and PyPI's integrity API (provenance attestation), queried 2026-11-05.
- `uv add --no-sync` against a scratch copy of `pyproject.toml` and `uv.lock`; install of scipy 1.18.1 and numpy 2.5.3 in a scratch venv; pyright 1.1.414 against a scipy-using file; timings of all the prototype runs.
- https://docs.python.org/3/library/random.html (Notes on Reproducibility).
- https://docs.scipy.org/doc/scipy/reference/generated/scipy.optimize.linear_sum_assignment.html (signature, rectangular behaviour, algorithm reference; says nothing on ties, dtype or determinism).

### Secondary (MEDIUM confidence)
- pypistats.org `recent` endpoint for scipy download counts.
- `gsd-tools query package-legitimacy check` seam result (SUS from missing metadata).

### Tertiary (LOW confidence)
- none. The prototypes are scratch code, not committed, and live under the session scratchpad.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. scipy resolved, installed, imported and type-checked in isolation; no stats dependency needed.
- Architecture: HIGH for the diff, matching, statistics and summary shape (all run on the real data); MEDIUM for the tag rules until real model failures exist (D-12 anticipates extending them).
- Pitfalls: HIGH. The wrapping, test-pin and `grade` output findings were each reproduced directly.

**Research date:** 2026-11-05
**Valid until:** 2026-12-05 for scipy and PyPI facts (scipy releases about every two months); the dataset findings stay valid until `dataset-001` changes.

## RESEARCH COMPLETE
