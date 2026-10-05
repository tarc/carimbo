# Roadmap: carimbo

## Overview

Milestone 1, "Extraction, measured", gets to measured extraction with vertical slices. Phase 1 is a walking skeleton. It scaffolds both stacks and the C# → JSON Schema → Pydantic contract chain, generates a few seeded synthetic DANFEs, extracts them live through the .NET eval endpoint, and grades the stored results offline from Python. The whole measurement loop runs on real code before any breadth is added. Each later phase widens one part of that loop. Phase 2 adds the full DANFE-visible target, deterministic validators and bounded repair. Phase 3 makes reruns replayable and traceable. Phase 4 grows the dataset to 150+ hard cases with validated ground truth. Phase 5 adds the full grader set, statistics and run comparison. Phase 6 closes the milestone with a fork-safe PR gate and a published two-model results table that a reviewer can reproduce without an API key. Milestones 2 and 3 (agent, durable execution) are not part of this roadmap.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Walking Skeleton** - A few synthetic DANFEs extracted live through the eval endpoint, graded offline from Python, summarized
- [ ] **Phase 2: Validated Extraction** - Full DANFE-visible target, deterministic validators, bounded repair loop with every attempt recorded
- [ ] **Phase 3: Replayable Runs** - Request-hash cache with replay modes, single retry policy, costed spans in a local trace viewer, per-request overrides
- [ ] **Phase 4: Synthetic Dataset** - 150+ seeded, varied and degraded cases with validated ground truth, manifest and fixed CI subset
- [ ] **Phase 5: Comparable Measurement** - Full graders, confidence intervals, silent-error rate, failure taxonomy and run comparison
- [ ] **Phase 6: Gated CI and Results** - Fork-safe PR gate from committed fixtures, budgeted live tier, published two-model results and failure analysis

## Phase Details

### Phase 1: Walking Skeleton

**Goal:** As a developer, I want to generate a few synthetic DANFEs, extract them live through the .NET eval endpoint and grade the stored results from Python, so that the whole measurement loop runs end to end on real code before any breadth is added.
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: REPO-01, REPO-02, REPO-03, REPO-04, CI-01, DOM-01, DOM-05, DOM-06, DOM-07, DOM-08, LLM-02, LLM-06, EXT-01, EXT-02, API-03, DATA-01, DATA-07, EVAL-01, EVAL-02, RES-03
**Success Criteria** (what must be TRUE):
  1. On a fresh clone, the developer (devenv on NixOS-WSL) and a reviewer (documented non-Nix path) can each build, lint, type-check and test the .NET solution and the Python project with one documented command per stack. The repo works under both Claude Code and OpenCode from one canonical agent instruction file, and GitHub Actions builds and tests both stacks on every PR.
  2. The pure C# domain records export a committed canonical JSON Schema, a model-facing schema and generated Pydantic models. A test keeps the model-facing schema within ≤24 optional and ≤16 union properties, and CI fails when any of the three is stale. `docs/DECISIONS.md` records the accepted D-03, D-07 and D-15 refinements as superseding entries.
  3. Running the generator twice with the same seed produces byte-identical pairs of `{case}.xml` ground truth and `{case}.pdf` DANFE (with a Code 128 access-key barcode) for a few cases. The cases use only synthetic parties, CNPJs and addresses.
  4. With the static API key, `POST /eval/extractions` sends a generated PDF through the gateway and returns either a schema-valid `Invoice` or a typed refusal, truncation or infrastructure failure. The response includes tokens, cost from a versioned pricing table, latency and trace ID. A recorded live spike settles the gateway shape and retry ownership, confirming PDF document blocks, raw output schema and cache-token usage. Without the key, or outside dev/eval, the endpoint is unavailable.
  5. One documented command runs the Python runner over those cases (bounded concurrency, cost cap, resumable) and writes one JSONL record per case, including raw model output. It then grades the stored run offline, with no model calls, and writes a run summary with at least one field-level grade per case plus tokens, cost and latency.

**Plans:** 7/13 plans executed

Plans:
**Wave 1**
- [x] 01-01-PLAN.md — Pinned Python toolchain behind a blocking supply-chain review (wave 1)
- [x] 01-02-PLAN.md — .NET solution (root global.json, CPM) and pure Domain records with Wire.Options (wave 1)

**Wave 2** *(blocked on Wave 1 completion)*
- [x] 01-03-PLAN.md — Tracer: one PDF over HTTP → typed Invoice → JSONL → graded summary, scripted model (wave 2)

**Wave 3** *(blocked on Wave 2 completion)*
- [x] 01-04-PLAN.md — Domain tests, canonical JSON Schema export with snapshot gate, DECISIONS D-18..D-20 (wave 3)
- [x] 01-06-PLAN.md — Seeded synthetic CNPJ/access-key math, NF-e XML and DANFE with Code 128 (wave 3)

**Wave 4** *(blocked on Wave 3 completion)*
- [x] 01-05-PLAN.md — Model-facing schema projector and ≤24/≤16 budget test; extractor sends the committed schema (wave 4)
- [x] 01-07-PLAN.md — carimbo-datagen build/check and the three committed skeleton cases (wave 4)

**Wave 5** *(blocked on Wave 4 completion)*
- [ ] 01-08-PLAN.md — Typed outcome tests and a locked-down eval endpoint (404/401/400/413, trace id, concurrency) (wave 5)
- [ ] 01-09-PLAN.md — Generated Pydantic models, budgeted resumable runner, offline grader and summary CLIs (wave 5)
- [ ] 01-10-PLAN.md — LLM-06 spike: offline self-test, live evidence within US$1, gateway-shape decision checkpoint (wave 5)

**Wave 6** *(blocked on Wave 5 completion)*
- [ ] 01-11-PLAN.md — Versioned pricing table and cost on every eval response (wave 6)

**Wave 7** *(blocked on Wave 6 completion)*
- [ ] 01-12-PLAN.md — Chosen Anthropic adapter, D-21, D-10 key resolution and a live smoke extraction (wave 7)

**Wave 8** *(blocked on Wave 7 completion)*
- [ ] 01-13-PLAN.md — just recipes, reviewer and devenv paths, AGENTS.md, PR CI, and the live `just skeleton` phase gate (wave 8)

### Phase 2: Validated Extraction

**Goal:** As a developer, I want to have every extraction target the full DANFE-visible invoice, pass deterministic validators and be repaired within a bounded budget, so that extracted invoices carry hard guarantees before anything is measured at scale.
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: DOM-02, DOM-03, DOM-04, VAL-01, VAL-02, VAL-03, VAL-04, VAL-05, VAL-06, API-01, EXT-03, EXT-04
**Success Criteria** (what must be TRUE):
  1. The `Invoice` target holds exactly the DANFE-visible fields, and the XML→DANFE mapping is documented. CNPJ and access key accept both numeric and alphanumeric forms. Money crosses the wire as pattern-constrained decimal strings and rounds half-up the same way in C# and Python.
  2. One hand-curated vector file passes under both xUnit and pytest. It holds known-valid and known-invalid CNPJs (numeric and alphanumeric), access keys and rounding cases.
  3. For known-bad invoices, validators never throw. They return structured errors (field, rule ID, expected, actual, severity) for wrong CNPJ check digits, a wrong access-key check digit, key-vs-field mismatches (issuer CNPJ, year-month, model, series, number), line items that disagree with totals, tax amounts inconsistent with bases and rates for the regime, and implausible dates.
  4. When an attempt fails validation, extraction retries with the structured errors fed back, under a prompt that forbids fabricating values. It stops at the configured maximum (default 2) and then returns a typed failure. Scripted-model tests cover first-try success, successful repair and budget exhaustion.
  5. `POST /eval/extractions` returns validator outcomes and every attempt with its output, validator results, tokens, cost and latency, alongside the result and trace ID.

**Plans**: TBD

### Phase 3: Replayable Runs

**Goal:** As a developer, I want to replay eval reruns from a request-hash cache and see every model call as a costed span in a local trace viewer, so that I can iterate on prompts and graders without paying twice and can trace any case back to its model calls.
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: LLM-01, LLM-03, LLM-04, LLM-05, EXT-05, REPO-05, API-02, API-04
**Success Criteria** (what must be TRUE):
  1. Rerunning the Phase 1 cases in read-only (replay) mode makes zero provider calls and reports the original latency and cost. Refresh mode calls the model again. A different model, prompt version or replicate salt misses the cache. Truncated or refused responses are never cached, and the production configuration runs with the cache off.
  2. One `docker compose` command starts a local trace viewer. Looking up the trace ID from any eval response shows one span per model call, carrying model, tokens and cost but no prompt, response or PDF content.
  3. Transient provider errors are retried with backoff by exactly one retry policy, never SDK retries stacked on custom ones. Tests with a failing fake transport show this.
  4. Per-request overrides (model, prompt version, max repairs, cache mode, replicate salt) take effect, and the response echoes the effective configuration. The prompt version appears in both the cache key and the result metadata.
  5. pytest parses golden eval-response fixtures emitted by the .NET tests, so a breaking change to the response contract fails CI.

**Plans**: TBD

### Phase 4: Synthetic Dataset

**Goal:** As a developer, I want to evaluate against a seeded dataset of at least 150 varied and deliberately hard invoices whose ground truth is proven valid, so that measured accuracy reflects realistic difficulty instead of a few clean cases.
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: DATA-02, DATA-03, DATA-04, DATA-05, DATA-06
**Success Criteria** (what must be TRUE):
  1. The manifest lists at least 150 cases. They cover short invoices and invoices spanning several pages, many line items and several tax regimes, and at least 10% of cases use alphanumeric CNPJs.
  2. Rotated, blurred and low-DPI variants, and barcode-unreadable variants, are image-only PDFs with no text layer.
  3. CI fails if any ground-truth record fails the exported JSON Schema or the .NET validators.
  4. The versioned manifest records per-case tags, split, content hashes, seed and generator version, and defines a fixed stratified CI subset. If the dataset is at most ~20 MB it is committed. Otherwise only the manifest and CI subset are committed, and regenerating the data counts as a new dataset version.

**Plans**: TBD

### Phase 5: Comparable Measurement

**Goal:** As a developer, I want to grade full-dataset runs field by field with confidence intervals, a silent-error rate and tagged failures, and compare any two runs case by case, so that I can tell real quality changes from noise.
**Mode:** mvp
**Depends on**: Phase 3, Phase 4
**Requirements**: EVAL-03, EVAL-04, EVAL-05, EVAL-06, EVAL-07, EVAL-08
**Success Criteria** (what must be TRUE):
  1. Graders score schema validity, exact match (IDs, CNPJ, access key) and numeric tolerance (amounts and taxes, configurable tolerance). Line items are matched by optimal assignment, not by position. Self-tests show ground truth scoring perfect and known corruptions scoring wrong.
  2. A full-dataset run summary reports per-field and per-slice accuracy with n and confidence intervals, plus cost, cache hit rate and latency percentiles. Infrastructure failures are counted separately from wrong answers.
  3. The summary reports the silent-error rate (values that passed validators but are wrong). Failures are auto-tagged by taxonomy (e.g. digit transposition, party swap, dropped or merged row) and counted.
  4. `compare` lists per-field deltas and regressed cases between two runs, and refuses runs whose dataset or grader versions differ.

**Plans**: TBD

### Phase 6: Gated CI and Results

**Goal:** As a reviewer, I want to see every PR gated on eval quality at no cost and reproduce the published two-model results table without an API key, so that the project's quality claims rest on evidence I can check myself.
**Mode:** mvp
**Depends on**: Phase 5
**Requirements**: EVAL-09, CI-02, CI-03, CI-04, RES-01, RES-02
**Success Criteria** (what must be TRUE):
  1. Every PR, fork PRs included, replays the eval CI subset from committed cache fixtures with no secrets, and fails when any grader falls below its configured threshold.
  2. A live eval tier runs only for same-repo changes or manual dispatch, and aborts when it exceeds its budget.
  3. Each published run commits `summary.json` and a compact per-case score table under `evals/reports/`; raw JSONL stays out of git. A reviewer without an API key can replay the published run from committed fixtures and get the same scores.
  4. A results table compares two models (or two prompt versions) with n, confidence intervals, cost per correct invoice and p95 latency. A written failure analysis explains the main failure categories using the taxonomy.

**Plans**: TBD

## Planning Notes

Inputs for discuss and plan phases. They are not extra scope.

- **Phase 1:** The planner treats this phase as the walking skeleton. Run the spikes first:
  - the live SDK contract (LLM-06)
  - JSON Schema exporter `$defs` hoisting (fallback: NJsonSchema)
  - datamodel-codegen determinism
  - xUnit v3 runner opt-in
  - DANFE rendering and hybrid Code 128 for a few cases
  - confirming which package `httpx2` is

  The first `Invoice` may be a subset that Phase 2 completes. Fix the wire conventions here (snake_case, string enums, money as decimal strings) so that later phases only add fields.
- **Phase 2:** Before fixing the target, check the DANFE printed fields, the `vNF` formula and tolerances against the current MOC, and the alphanumeric CNPJ rules against NT 2025.001.
- **Phase 3:** Pin the OpenTelemetry GenAI semconv version, and check the per-model prompt-cache thresholds.
- **Phase 4:** This phase depends only on Phase 2, so it can run alongside Phase 3. Check renderer fidelity against reference samples. Whether the dataset bytes reproduce on NixOS-WSL, in CI and on a reviewer machine decides DATA-06 (commit or regenerate).
- **Phase 5:** Grader code can start early, because it needs only the generated models. The line-item assignment algorithm and the taxonomy rules still need design.
- **Phase 6:** Pick the model pair after a capability check; research suggests Sonnet 5.5 vs Haiku 4.5. Calibrate the CI subset size and thresholds from the first baseline before turning the gate on.

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 6

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Walking Skeleton | 7/13 | In Progress | - |
| 2. Validated Extraction | 0/TBD | Not started | - |
| 3. Replayable Runs | 0/TBD | Not started | - |
| 4. Synthetic Dataset | 0/TBD | Not started | - |
| 5. Comparable Measurement | 0/TBD | Not started | - |
| 6. Gated CI and Results | 0/TBD | Not started | - |
