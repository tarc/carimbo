---
phase: "04"
slug: "synthetic-dataset"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: validated
nyquist_compliant: true
wave_0_complete: true
created: "2026-10-22"
---

# Phase 04 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | pytest 9.1.1 + pytest-asyncio 1.4.0 (Python); xUnit v3 4.0.1 on Microsoft.Testing.Platform (.NET) |
| **Config file** | `python/pyproject.toml` (markers `e2e`, `live`, deselected by default), `global.json` (MTP runner) |
| **Quick run command** | `devenv shell -- uv run --project python pytest python/tests/test_dataset_*.py -q` |
| **Full suite command** | `devenv shell -- just check` |
| **Estimated runtime** | ~300 seconds (full, includes `dataset-check` regeneration), ~30 seconds (quick) |

---

## Sampling Rate

- **After every task commit:** Run the quick command for the module touched, plus `just datagen-check` whenever `spec.py`, `nfe_xml.py` or `danfe.py` change
- **After every dataset plan (04-04 to 04-07):** the full byte-identical `just dataset-check` runs once, in the plan's last task (after that task's `just dataset`); earlier tasks run their targeted pytest modules and the .NET dataset gate class only
- **After every plan wave:** Run `just py-check` and the .NET dataset gate class
- **Before `/gsd-verify-work`:** Full suite must be green, and one green CI run on the PR before any image PDF is committed as final
- **Max feedback latency:** 300 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 04-02-01 | 02 | 1 | DATA-03 | T-04-SC, T-04-03 | Lock resolved, nothing new installed before review | lock check | `devenv shell -- uv lock --project python --check` | ✅ | ✅ green |
| 04-02-02 | 02 | 1 | DATA-03 | T-04-SC, T-04-04 | Human approves numpy, reportlab and runtime pins (wheels listed for each) | checkpoint (blocking-human) | manual: PyPI pages and lock diff | — | ✅ approved 2026-10-29 |
| 04-02-03 | 02 | 1 | DATA-03 | T-04-SC | Approved lock installs, suite green | import check + suite | `devenv shell -- just py-check` | ✅ | ✅ green |
| 04-01-01 | 01 | 2 | DATA-02, DATA-04 | T-04-01, T-04-02 | Skeleton bytes unchanged; CST 10 XSD-valid | unit + CLI check | `devenv shell -- uv run --project python pytest python/tests/test_tax_groups.py -q` and `devenv shell -- just datagen-check` | ✅ | ✅ green |
| 04-01-02 | 01 | 2 | DATA-02, DATA-04 | T-04-01, T-04-02 | Every CST/CSOSN XSD-valid, exact arithmetic | unit | `devenv shell -- uv run --project python pytest python/tests/test_tax_groups.py -q` | ✅ | ✅ green |
| 04-03-01 | 03 | 2 | DATA-03 | T-04-05, T-04-06 | Twin image-only, deterministic, no identifiers | unit | `devenv shell -- uv run --project python pytest python/tests/test_scan.py -q` | ✅ | ✅ green |
| 04-03-02 | 03 | 2 | DATA-03 | T-04-05 | All 8 renderings, barcode flags, frozen parameters | unit | `devenv shell -- uv run --project python pytest python/tests/test_scan.py -q` | ✅ | ✅ green |
| 04-04-01 | 04 | 3 | DATA-05, DATA-06 | T-04-11, T-04-22 | Manifest v2 (generator.scan), build by `just dataset`, check wired into `just check` and CI | CLI check | `devenv shell -- just dataset` and `devenv shell -- uv run --project python pytest python/tests/test_repo_layout.py -q` | ✅ | ✅ green |
| 04-04-02 | 04 | 3 | DATA-06 | T-04-22 | Comparison reports missing, extra and changed files; command exits 1; the plan's one byte-identical check | unit + CLI check | `devenv shell -- uv run --project python pytest python/tests/test_dataset_check.py -q` and `devenv shell -- sh -c 'just dataset-check && just datagen-check'` | ✅ | ✅ green |
| 04-09-01 | 09 | 4 | DATA-04 | T-04-08, T-04-09, T-04-10 | Zero validator errors and zero schema errors over every manifest case | .NET theory + unit | `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/Carimbo.Validation.Tests -- --filter-class Carimbo.Validation.Tests.DatasetGroundTruthGateTests'` and `devenv shell -- uv run --project python pytest python/tests/test_dataset_ground_truth.py -q` | ✅ | ✅ green |
| 04-09-02 | 09 | 4 | DATA-04 | T-04-08, T-04-09 | Expected blocks recomputed from the quota plan, file set equals manifest, oracle untouched | .NET theory + unit | `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/Carimbo.Validation.Tests -- --filter-class Carimbo.Validation.Tests.DatasetGroundTruthGateTests --filter-class Carimbo.Validation.Tests.GroundTruthGateTests'` and `devenv shell -- uv run --project python pytest python/tests/test_dataset_ground_truth.py python/tests/test_ground_truth.py -q` | ✅ | ✅ green |
| 04-05-01 | 05 | 5 | DATA-02, DATA-05 | — | Strata counts, declared bands (mixed slots at least 2 items and 2 codes), literal slot table, page bands, split | unit | `devenv shell -- uv run --project python pytest python/tests/test_dataset_spec.py -q` and `devenv shell -- uv run --project python pytest python/tests/test_dataset_ground_truth.py -q` | ✅ | ✅ green |
| 04-05-02 | 05 | 5 | DATA-02 | T-04-12, T-04-13 | Synthetic-only, unique identifiers; per-item discount and freight sum exactly to the header (SEFAZ 535/537); the plan's one byte-identical check | unit + CLI check | `devenv shell -- uv run --project python pytest python/tests/test_dataset_spec.py python/tests/test_synthetic_only.py python/tests/test_dataset_ground_truth.py -q` and `devenv shell -- sh -c 'just dataset-check && just datagen-check'` | ✅ | ✅ green |
| 04-06-01 | 06 | 6 | DATA-02 | T-04-14 | Printed text equals XML (D-05); long-text band 26-30 and length tags per stratum | unit | `devenv shell -- uv run --project python pytest python/tests/test_dataset_text_equals_xml.py python/tests/test_dataset_spec.py -q` | ✅ | ✅ green |
| 04-06-02 | 06 | 6 | DATA-02, DATA-04 | T-04-15 | Value caps, exact arithmetic (header vDesc and vFrete equal the item sums); the plan's one byte-identical check | unit + CLI check | `devenv shell -- uv run --project python pytest python/tests/test_dataset_spec.py python/tests/test_dataset_ground_truth.py -q` and `devenv shell -- sh -c 'just dataset-check && just datagen-check'` | ✅ | ✅ green |
| 04-07-01 | 07 | 7 | DATA-02, DATA-03, DATA-06 | T-04-17, T-04-18 | No text layer, flags, no leak, size ≤ 20 MB (above: DATA-06-SIZE stop, nothing committed, D-12 fallback re-planned as 04-10) | unit + size | `test "$(du -sb data/dataset \| cut -f1)" -le 20000000` and `devenv shell -- uv run --project python pytest python/tests/test_dataset_scans.py -q` | ✅ | ✅ green |
| 04-07-02 | 07 | 7 | DATA-02, DATA-05, DATA-06 | T-04-16, T-04-19, T-04-23, T-04-24 | Dev-only CI subset, invariants, order independence; slot tuples change only after approval at the CI-SUBSET-EMPTY-CELL stop; no evals/results file cites dataset-001 (D-17 qualification); the plan's one byte-identical check | unit + CLI check | `devenv shell -- uv run --project python pytest python/tests/test_dataset_manifest.py python/tests/test_dataset_scans.py python/tests/test_dataset_ground_truth.py -q`, `devenv shell -- sh -c 'just dataset-check && just datagen-check'` and `test -z "$(git grep -l 'dataset-001' -- evals/results)"` | ✅ | ✅ green |
| 04-08-01 | 08 | 8 | DATA-05, DATA-06 | T-04-21 | D-40 recorded | CLI check | `devenv shell -- just docs-check` | ✅ | ✅ green |
| 04-08-02 | 08 | 8 | DATA-05, DATA-06 | T-04-20 | Full offline gate | full suite | `devenv shell -- just check` | ✅ | ✅ green |
| 04-10-01 | 10 | 9 | DATA-06 | G-04-2 | Twins built in a subprocess with SOURCE_DATE_EPOCH unset or set to 1700000000 have the committed hashes (RED before the pin) | subprocess unit | `devenv shell -- uv run --project python pytest python/tests/test_dataset_manifest.py -q -k source_date_epoch` | ✅ | ✅ green |
| 04-10-02 | 10 | 9 | DATA-06 | G-04-2 | Twin bytes equal across SOURCE_DATE_EPOCH values in-process; the variable is restored, also when writing raises; no byte under data/ changes | unit + CLI check | `devenv shell -- uv run --project python pytest python/tests/test_scan.py -q -k source_date_epoch` and `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check` | ✅ | ✅ green |
| 04-11-01 | 11 | 10 | DATA-03 | T-04-28 | Preview renders through the production `render_twin` path (2.4 reproduces the committed blur-severe bytes, 1.2 the blur-mild bytes); nothing written in the repo | tracer (scratch script, outside the repo) | one-off: `blur_severe_preview.py` in the session scratchpad | — | ✅ green (not repeatable by design) |
| 04-11-02 | 11 | 10 | DATA-03 | T-04-28 | User chooses the blur-severe sigma on native 220 DPI previews | checkpoint (blocking-human) | manual: see Manual-Only | — | ✅ chose 1.8 on 2026-10-29 |
| 04-11-03 | 11 | 10 | DATA-03, DATA-06 | T-04-28 | DEGRADATION pinned literally (RED at 2.4); only the eight blur-severe twins and the manifest change; nfe-0095 equals the approved preview hash; size under the cap | unit + CLI check + size | `devenv shell -- uv run --project python pytest python/tests/test_scan.py -q -k frozen_values`, `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check` and `test "$(du -sb data/dataset \| cut -f1)" -le 20000000` | ✅ | ✅ green |
| 04-12-01 | 12 | 11 | DATA-05, DATA-06 | — | D-41 recorded, docs-check range extended to D-41 in justfile, AGENTS.md and README | CLI check + drift test | `devenv shell -- just docs-check` and `devenv shell -- uv run --project python pytest python/tests/test_readme_drift.py -q` | ✅ | ✅ green |
| 04-12-02 | 12 | 11 | DATA-05, DATA-06 | G-04-2 | Full offline gate with SOURCE_DATE_EPOCH unset | full suite | `devenv shell -- env -u SOURCE_DATE_EPOCH just check` | ✅ | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `python/tests/test_tax_groups.py` — every CST/CSOSN group XSD-valid, printed and mapped (DATA-02, DATA-04; plan 04-01)
- [x] `python/tests/test_scan.py` — twin renderer: image-only, determinism, barcode flags, leak (DATA-03; plan 04-03)
- [x] `python/tests/test_dataset_manifest.py` — counts, strata, split, CI subset, hashes, size budget (DATA-02, DATA-05, DATA-06; plan 04-07)
- [x] `python/tests/test_dataset_spec.py` — strata, literal slot table, split, tax-code arithmetic and trait coverage from specs (DATA-02, DATA-05; plans 04-05, 04-06)
- [x] `python/tests/test_dataset_scans.py` — no text layer, barcode flags, leak check (DATA-03; plan 04-07)
- [x] `python/tests/test_dataset_text_equals_xml.py` — D-05 graded-string presence (DATA-02; plan 04-06)
- [x] `python/tests/test_dataset_ground_truth.py` — jsonschema + Pydantic over the manifest (DATA-04; plan 04-09)
- [x] `dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs` on the shared `GroundTruthManifest` helper (DATA-04; plan 04-09)
- [x] `justfile` recipes `dataset`, `dataset-check`, `check` wiring; `.github/workflows/ci.yml` step (DATA-06; plan 04-04)
- [x] `python/tests/test_dataset_check.py` — missing, extra and changed files make the byte-identical check fail (DATA-06; plan 04-04)

*Framework install: none (pytest and xUnit exist).*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Byte-identical image PDFs on the CI runner | DATA-06 | Determinism proven only on this machine (research assumption A2); CI run 40915848246 failed on the twins' SOURCE_DATE_EPOCH dependence (G-04-2), now reproduced and closed locally by plan 04-10 | Push the PR, confirm the `dataset-check` CI step is green before treating the committed image PDFs as final (UAT re-test 3) |
| Blur severe is hard but readable (CONTEXT D-08) | DATA-03 | Legibility is a human judgement | Plan 04-11 Task 2: read nfe-0095's item table at native 220 DPI bottom-up without the key; on 2026-10-29 the user read every code, NCM and quantity at 1.8 and missed one quantity digit at 2.0. UAT re-test 1 re-reads the committed nfe-0095 |
| Supply-chain review of the new pins (numpy, reportlab, runtime pypdfium2/zxing-cpp) | DATA-03 | Lock diff needs a human look (Phase 1 precedent) | Review the `uv.lock` diff at the checkpoint before `uv lock` is committed |
| Renderer fidelity against a reference DANFE layout | DATA-02 | The D-05 test proves printed text, not layout (ROADMAP Phase 4 planning note) | Plan 04-08 Task 2: open one committed text DANFE side by side with BrazilFiscalReport's own sample output or an nfelib sample XML rendered by the same library outside the repo (structural reference only, never committed, DECISIONS D-14); same blocks, labels, item columns and barcode position |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 300s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** validated 2026-10-29 (validate-phase audit: 0 gaps; `just check` green at ceaa2c4, 2805 pytest passed, .NET 0 failed). Re-audited after gap closure 04-10 to 04-12: 0 gaps; `env -u SOURCE_DATE_EPOCH just check` green at 0cd063c (2810 pytest passed, .NET 2173/2173, e2e 13 passed) on the second run; the first run lost one timing-sensitive .NET test (`AgentToolboxFaultTests.A_tool_that_outlives_the_call_timeout_is_a_timeout_on_the_real_session`, untouched by this phase), which then passed three isolated reruns

## Validation Audit 2026-10-29

| Metric | Count |
|---|---|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
