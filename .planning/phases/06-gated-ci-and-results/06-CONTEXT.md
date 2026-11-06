# Phase 6: Gated CI and Results - Context

**Gathered:** 2026-11-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Every PR, fork PRs included, replays the 20-case CI subset from committed fixtures with no secrets and fails on any change from the committed baseline. A manually dispatched live tier runs under a budget abort. Published `test` runs of two models are committed under `evals/reports/` with their replay fixtures, so a reviewer with no API key gets the same scores. `docs/RESULTS.md` holds the results table and the written failure analysis.

Requirements: EVAL-09, CI-02, CI-03, CI-04, RES-01, RES-02. Barcode intake, the recipient check and prompt tuning are outside this phase.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked earlier, not re-discussed)
- The primary metric is the correct-invoice rate; text-layer `test` cases are the headline row and image-only `test` cases the second row (Phase 5 D-01, D-13). Case-level intervals are Wilson over distinct invoices (Phase 5 D-14).
- `compare` refuses a different manifest, grader version or tolerance, with no override, and exits 0 even with regressions (Phase 5 D-18). The gate is built here.
- The tag list is frozen at `failure-tags-1` / `grader-003` before any `test` run (DECISIONS D-48).
- `carimbo-evals run` refuses `test` cases without `--allow-test-split`, and running `test` is a human decision (D-48, D-49). D-11 below refines this for read-only replay only.
- `dataset-001` is frozen; all data is synthetic (Phase 4 D-17, DECISIONS D-14). The CI subset is the 20 dev cases in the manifest.
- Published `evals/results/` directories are verbatim copies and are never rewritten.

### Measurement scope
- **D-01:** Keep `dataset-001`. Before the first `test` run, declare an **additive image row that excludes `recipient.name`**, beside the unchanged primary rows. Reason: the homologation notice makes `recipient.name` wrong on all 21 answered dev image cases. It does not explain the 0% dev image row (every one of those cases has at least 16 wrong values), but a stronger model could otherwise be scored 0% for this one field. — **Reversibility:** one-way — a row declared after `test` results are seen would be fitted to the holdout.
- **D-02:** **Publish the baseline first.** Phase 6 measures the pipeline as it is. Barcode-decoded access key and the dc-004 recipient check are built in a later phase. Dev evidence: 12 of the 18 failing text cases are wrong only in `access_key` and/or `issuer.cnpj`, and all 30 text cases have a readable barcode, so the dev text row could go from 12/30 to as high as 24/30.
- **D-03:** **One more `test` run is pre-declared for the barcode and dc-004 phase, and that change is designed from dev evidence only.** Record this in DECISIONS with the Phase 6 results, so the second look at the holdout is declared before the first one. That phase measures the change with `compare` and McNemar. Its spend is not part of the Phase 6 cap. — **Reversibility:** one-way — a holdout look can't be un-taken, and a change designed from `test` failures would leak it.

### Models and paid runs
- **D-04:** The published pair is **Haiku 5.5 (`claude-haiku-5-5`) vs Sonnet 5.5 (`claude-sonnet-5-5`)**. If Haiku 5.5 fails the probe, the fallback pair Haiku 4.5 vs Sonnet 5.5 (about US$13.60 by list-price scaling) does not fit D-05, so stop and ask. — **Reversibility:** one-way — paid `test` spend and holdout exposure can't be undone.
- **D-05:** A **one-time US$12 cap for Phase 6**, recorded in a new DECISIONS entry. The US$5 per-phase rule stands for other phases.
- **D-06:** Run order, with no `test` run until the steps before it pass:
  1. **Haiku 5.5 probe** (capped). It confirms PDF plus structured output and **checks thinking and effort behavior**.
  2. **Full 54-case Haiku 5.5 dev run** (estimate about US$0.25). It measures cost per case, compares like for like with Haiku 4.5, and supplies the CI fixtures.
  3. **10-case Sonnet 5.5 dev check** (estimate about US$0.90), before its `test` run, so its per-case cost is measured.
  4. **Each `test` cap is set from measured cost.** If the measured costs do not fit US$12, **stop and ask before the `test` runs**.
  5. The two `test` runs, each with a separate explicit spend approval and the typed `--allow-test-split` flag.

  Estimates scale the dev run's US$0.051 per text case and US$0.037 per image case by list price and assume the same tokens, so they are rough. The dev run already overran its estimate. — **Reversibility:** one-way — live spend.
- **D-07:** The `test` runs use **`extract-003` with repair budget 2**, the dev baseline's configuration. Effort is set explicitly, recorded and part of the cache key. **The published table states each model's thinking and effort setting.** Each model is pinned as tightly as it allows (DECISIONS D-21) with `model_returned` stored per case.

### PR gate
- **D-08:** The gate is an **exact baseline**: gated metrics must equal a committed thresholds file. Replay is deterministic, so any change is a real code effect, and an intended change updates that file in the same PR. It checks a small per-grader set (correct-invoice count, field accuracy, item F1, status counts, silent errors, tag counts) **plus each case's correct flag**, so offsetting flips can't hide. Cost and latency are not gated.
- **D-09:** **Any cache miss fails the gate**, with a message naming the fix (re-record with a capped recipe). There is no waiver label and no skip. Consequence accepted: a fork PR that changes the prompt or schema needs a maintainer to record fixtures.
- **D-10:** CI fixtures come from the **Haiku 5.5 dev run** (D-06 step 2), on the existing 20-case subset, which resolves `nfe-0146` having no answer in the Haiku 4.5 run. If a subset case still has no cached answer, it is a declared exclusion in the thresholds file, never a manifest edit (the manifest is frozen). **Declared exclusions are counted and shown in the CI output.**

### Replay safety
- **D-11:** A new recipe replays published runs keyless from committed fixtures, always `read_only`. A superseding DECISIONS entry lets the runner accept `test` cases without `--allow-test-split` **only** in that mode with live calls impossible; paid `test` runs still need the typed flag. The layout test is updated to match. — **Reversibility:** costly — it refines the D-49 barrier, which a layout test enforces.
- **D-12:** **Replay never passes the provider key, and a cache miss fails instead of calling the provider.** `just dev-split` and `just skeleton` currently refuse to run without a key even in `read_only`; the replay path must not need one. Tests must prove that no provider call can happen with a key present in the environment too.
- **D-13:** Fixtures also **store refused and truncated outcomes**, so replay reproduces them and the rate matches exactly (success criterion 3). This supersedes D-46's limit. **Stored refused and truncated outcomes replay in `read_only` but are not served to new `read_write` runs**, so a new run still asks the model. — **Reversibility:** costly — cache entry format and D-46 change; entry version bump.

### Live tier
- **D-14:** The live tier runs on **manual dispatch only**.
- **D-15:** The default is the **20 dev cases on Haiku 5.5 with a US$0.25 cap**. A dispatch input can override the model or lower the cap. No workflow input can reach the `test` split. Over budget, the existing cost cap stops the run and marks it incomplete.
- **D-16:** The live tier is **report only**: it fails on budget abort or infrastructure failure, never on scores, and never blocks a merge. Scores go to the job summary and an artifact, with `compare` against the committed baseline for reading.
- **D-17:** **The key sits in a GitHub environment with the user as required reviewer, and it is a dedicated carimbo-workspace key with a Console spend limit.** This supersedes "CI uses no provider key" for this one environment; PR jobs stay secret-free. Creating the key, the Console limit and the environment are manual steps for the user, so plan them as a human checkpoint, and never print or log the value. — **Reversibility:** costly — a repository secret boundary changes.

### Published artifacts
- **D-18:** New published runs go to `evals/reports/<run>/` as `summary.json`, `summary.md`, `run.json` and a per-case score table (CSV), with **no raw JSONL**. Existing `evals/results/` stays frozen, so README links hold.
- **D-19:** Fixtures live in `evals/fixtures/<run>/`, one directory per published run: both `test` runs and the Haiku 5.5 dev run, roughly 13 MB in total by estimate. `.cache/llm` stays git-ignored.
- **D-20:** `docs/RESULTS.md` holds the full tables, slices and the hand-written failure analysis, using the failure taxonomy, and the README shows the headline table and links to it. Table numbers are built by a tool from committed summaries.
- **D-21:** RES-01's table shows n, confidence intervals, cost per correct invoice and p95 latency. **The headline also shows silent errors split into payment and other fields, and names the image-row confound and the wrap hazard** (D-47: 31 of the 60 headline cases carry a hazard).
- **D-22:** **The docs check covers numbers in prose, not only in tables.** Every measured figure in `docs/RESULTS.md` and the README's results must trace to a committed summary, and drift fails `just check`.

### Claude's Discretion
- The thresholds file's format and name, the recipe names, and whether the replay gate is a new CI job or a step in `contract`.
- How the additive image row of D-01 is versioned, provided existing grades don't change and it is declared before the first `test` run.
- How prose numbers are marked or extracted for D-22.
- The cache entry change for D-13.
- Whether `pricing.json` entries and the default model change together. `claude-haiku-5-5` is missing from `pricing.json`, and the extractor and agent default to `claude-haiku-4-5`.
- Numbering of new DECISIONS entries (next free is D-50), and extending the `just docs-check` range.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Scope and requirements
- `.planning/ROADMAP.md` § Phase 6 — goal, success criteria, planning note
- `.planning/REQUIREMENTS.md` — EVAL-09, CI-02, CI-03, CI-04, RES-01, RES-02

### Decisions this phase builds on or supersedes
- `docs/DECISIONS.md` D-15, D-20 — eval artifacts and gating
- `docs/DECISIONS.md` D-21 — snapshot pinning and retries
- `docs/DECISIONS.md` D-27 — response cache
- `docs/DECISIONS.md` D-44 — statistics, headline and primary-metric rows
- `docs/DECISIONS.md` D-46 — replay limit (superseded in part by D-13 here) and the US$5 cap
- `docs/DECISIONS.md` D-47 — wrap hazards
- `docs/DECISIONS.md` D-48, D-49 — frozen tags and the `test` barrier (refined by D-11 here)
- `.planning/phases/05-comparable-measurement/05-CONTEXT.md` — metric definitions and `compare` contract

### Evidence
- `evals/results/dev-split-20261106T001800Z/` — live Haiku 4.5 dev run (51 of 54 cases)
- `evals/results/dev-split-20261106T021526Z/` — its replay, showing the `cache_miss` behavior
- `data/dataset/manifest.json` — `ci_subset`, splits, `base_case`

### Repository rules
- `AGENTS.md` — live-spend rules, generated files, README and ship rules

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `python/src/carimbo_evals/compare.py`: reads two `summary.json` files; suitable for reading live runs against a baseline.
- `python/src/carimbo_evals/summary.py`, `stats.py`, `slices.py`: the numbers the tables need.
- `python/src/carimbo_evals/cli.py`: `run` with `--split`, `--case`, `--cache-mode`, `--model`, `--allow-test-split`.
- `dotnet/src/Carimbo.Llm/Cache/` and `LlmCacheMode`: the cache that D-12 and D-13 change.
- `dotnet/src/Carimbo.Llm/pricing.json`: needs `claude-haiku-5-5`, whose listed price is US$0.10/$0.50 per MTok (up to 100K-token prompts); confirm against the pricing page.
- `dotnet/tools/` probes and `just schema-probe`: a pattern for the Haiku 5.5 probe.

### Established Patterns
- Every CI step is a `just` recipe, and `.github/workflows/ci.yml` uses a read-only token with no credentials.
- Live spend goes through capped recipes, with explicit approval first. `python/tests/test_readme_drift.py` is the pattern for D-22.
- A layout test rejects `--allow-test-split` and any `--split` other than `dev` in the justfile; D-11 must change it deliberately.

### Integration Points
- `.github/workflows/ci.yml`: the gate; a separate manually dispatched workflow for the live tier.
- `justfile`: `dev-split`, `skeleton` and `decisions` check the provider key before replay, which D-12 must remove from the replay path.
- `README.md` Status, Verification and results sections; `just docs-check`.

</code_context>

<specifics>
## Specific Ideas

- The user set the D-06 Sonnet check, D-17's key arrangement and the safety rules of D-12 and D-13 directly; treat their wording as the requirement.
- Haiku 5.5 runs thinking on by default with `medium` effort per the bundled API reference (cached 2026-10-06). That is unverified in this project, which is why the probe checks it.

</specifics>

<deferred>
## Deferred Ideas

- Barcode-decoded access key at intake, and the dc-004 recipient check: a later phase with one pre-declared `test` run (D-03). The roadmap has no such phase yet.
- Prompt iteration on `dev`: not chosen for the published runs.
- Live tier on same-repo PRs or a nightly schedule: not chosen.
- A floor on live scores: needs a noise floor (MEAS-02, v2).
- `dataset-002` without the homologation notice: not chosen.
- Repair budget 0 runs: repair ablation stays MEAS-01, v2.

</deferred>

---

*Phase: 06-gated-ci-and-results*
*Context gathered: 2026-11-06*
