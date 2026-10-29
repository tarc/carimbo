---
phase: 04-synthetic-dataset
verified: 2026-10-29T16:30:00Z
status: human_needed
score: 4/5 must-haves verified
covered_files:
  - .planning/phases/04-synthetic-dataset/04-01-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-01-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-02-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-02-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-03-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-03-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-04-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-04-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-05-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-05-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-06-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-06-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-07-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-07-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-08-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-08-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-09-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-09-SUMMARY.md
  - data/dataset/manifest.json
  - dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs
  - python/src/carimbo_datagen/dataset.py
  - python/src/carimbo_datagen/dataset_spec.py
  - python/src/carimbo_datagen/quota.py
  - python/src/carimbo_datagen/scan.py
  - python/tests/test_dataset_ground_truth.py
covered_digest: "v4:sha256:112c84746223dfceb7c604c4aea772598b12908ec85f2847ea4aef4702c8b8b9"
behavior_unverified: 0
overrides_applied: 0
human_verification:
  - test: "Open the eight committed twins of one dev curve base (for example nfe-0001, whose twins are in data/dataset/manifest.json with base_case nfe-0001 and rendering other than text) and compare mild with severe."
    expected: "Mild renderings are lightly degraded; severe ones are hard but still human-readable (header fields and the printed access-key digits legible, including on barcode_damaged)."
    why_human: "Legibility is a visual judgement (04-03 truth tagged verification: backstop, CONTEXT D-08). No test can establish it."
  - test: "Open data/dataset/nfe-0001.pdf next to a reference DANFE layout (BrazilFiscalReport sample output, or an nfelib sample XML rendered into a scratch directory outside the repo)."
    expected: "Same blocks, labels, item columns and barcode position (ROADMAP Phase 4 planning note: check renderer fidelity)."
    why_human: "Layout fidelity to a real DANFE is a visual comparison."
  - test: "Push the phase branch, open the PR, and read the CI python job's `just dataset-check` step."
    expected: "Green (byte identity on ubuntu-latest, RESEARCH A2) and duration at most about 180 s; otherwise follow CONTEXT D-13 (move to a path-filtered workflow)."
    why_human: "Needs a push to GitHub and a CI run. The branch has not been pushed. Locally the check passes in about 52 s."
---

# Phase 4: Synthetic Dataset Verification Report

**Phase Goal:** As a developer, I want to evaluate against a seeded dataset of at least 150 varied and deliberately hard invoices whose ground truth is proven valid, so that measured accuracy reflects realistic difficulty instead of a few clean cases.
**Verified:** 2026-10-29
**Status:** human_needed
**Re-verification:** No, initial verification

## Goal Achievement

The goal is achieved in the code and data. `data/dataset` holds 154 cases, byte-reproducible from seed 20261022, with ground truth gated by the .NET validators and the exported JSON Schema in CI. Three visual or CI-hosted checks that no offline test can settle remain, so the status is `human_needed`, not `passed`. No gap is blocking.

### Observable Truths

Roadmap success criteria first (the contract), then the one plan truth marked as non-inferable.

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Manifest lists at least 150 cases: short and multi-page, many line items, several tax regimes, at least 10% alphanumeric CNPJs | VERIFIED | I read `data/dataset/manifest.json` with node: 154 cases (90 text bases and 64 twins). Pages: 134 single-page, 20 two-page (`multi_page` tag 20). Item counts reach 80 (`many_items` 34, `short` 60, `medium` 60). Regimes: `normal` 77, `simples` 77, plus 4 `mei` (CRT 4). Alphanumeric issuer CNPJ: 40 of 154 = 26% (regex `[A-Z]` on `expected.issuer_cnpj`; 24 of the 90 bases), plus 4 alphanumeric recipients. Also `st_deferral` 22, `mixed_tax_codes` 56, `long_text` 18, `confusable` 18. |
| 2 | Rotated, blurred, low-DPI and barcode-unreadable variants are image-only PDFs with no text layer | VERIFIED | My own pypdfium2 script (`python -I`-free uv run, not the repo test): for all 154 cases, every non-`text` rendering PDF has 0 text characters on every page and the page count in the manifest; every text case has a text layer. All 64 image-only twins carry `image_only`. All 44 `barcode_unreadable` cases are image-only, and flag and tag agree in all 154 (`barcode_readable == !barcode_unreadable`). sha256 of all 154 XML and PDF files equals the manifest. Manifest case set equals the file stems on disk. |
| 3 | CI fails if any ground-truth record fails the exported JSON Schema or the .NET validators | VERIFIED | `DatasetGroundTruthGateTests` (read in full) maps each manifest case with `NfeXmlMapper.Load` and asserts zero `Error` findings at the manifest `as_of_date`, with no filtering. I ran it: Carimbo.Validation.Tests passed, 926 succeeded, 0 failed. `python/tests/test_dataset_ground_truth.py` (JSON Schema Draft 2020-12 plus strict `Invoice`): 867 passed. The `just check` recipe lists `dotnet-check` and `py-check`, and CI runs `just dotnet-check` and `just py-check`, so both gates run in CI. The prohibition holds: `git diff` from the merge base with `origin/main` shows no change to `dotnet/src`, `data/skeleton` or `schema`, so the oracle was not touched. |
| 4 | Versioned manifest records per-case tags, split, content hashes, seed and generator version, and a fixed stratified CI subset; committed if at most about 20 MB | VERIFIED | Manifest keys: `dataset_version` "dataset-001", `master_seed` 20261022, `generator` (carimbo 0.1.0, package versions, nested `scan` object), `quota_sha256`, `ci_subset` (20 ids). Per case: tags, split (54 dev, 100 test), xml and pdf sha256, rendering, severity, base_case, barcode_readable. CI subset: all 20 ids are dev and cover the cells regime, multi-page, many items, alphanumeric, CPF, ST, all eight renderings, short, medium, mixed, long text and confusable. `du -sb data/dataset` is 19,368,019 bytes (under the 20,000,000 cap, over the 18,000,000 warning line), all 309 files committed. `just dataset-check` (run by me): "ok: data/dataset matches a regeneration from seed 20261022", 52 s. It is wired into `check` (justfile:101) and `.github/workflows/ci.yml:47`. |
| 5 | Severe renderings are hard but still human-readable (04-03 plan truth, `verification: backstop`) | UNCERTAIN (human) | The text-layer-free renderings exist and are produced with frozen parameters, but legibility to a human is not inferable from code or tests. Routed to human verification item 1. |

**Score:** 4/5 truths verified, 0 present-but-behavior-unverified.

### Requirements Coverage

Every requirement ID in the plan frontmatter (DATA-02 to DATA-06) is accounted for. REQUIREMENTS.md maps no other requirement to Phase 4, so nothing is orphaned.

| Requirement | Source plans | Description | Status | Evidence |
|-------------|--------------|-------------|--------|----------|
| DATA-02 | 04-01, 04-05, 04-06, 04-07 | at least 150 cases, single and multi-page, many items, several regimes, at least 10% alphanumeric CNPJ | SATISFIED | Truth 1. |
| DATA-03 | 04-02, 04-03, 04-07 | degraded and barcode-unreadable variants are image-only | SATISFIED | Truth 2. Scan stack pinned exactly in `pyproject.toml` behind the 04-02 checkpoint. |
| DATA-04 | 04-01, 04-06, 04-09 | every ground-truth record validates against the schema and the .NET validators, checked in CI | SATISFIED in code. Tracking checkbox still open | Truth 3. `.planning/REQUIREMENTS.md` still shows `- [ ] DATA-04` and `Pending` in the traceability table (line 43 and 182), while DATA-02, 03, 05 and 06 are ticked. This is a bookkeeping lag, not a code gap; flip it when the phase is marked complete. |
| DATA-05 | 04-04, 04-05, 04-07, 04-08 | versioned manifest with tags, split, hashes, seed, generator version, stratified CI subset | SATISFIED | Truth 4. |
| DATA-06 | 04-04, 04-07, 04-08 | commit when at most about 20 MB, otherwise manifest plus CI subset | SATISFIED | 19,368,019 bytes, fully committed, `dataset-check` byte identity. |

### Key Links

| From | To | Status | Details |
|------|----|--------|---------|
| `justfile` `check` | `dataset-check` recipe | WIRED | justfile:64-65 and :101. |
| `.github/workflows/ci.yml` | `just dataset-check` | WIRED | ci.yml:47. |
| `dataset.py` | `scan.py` (`render_twin`, `barcode_readable`) | WIRED | Manifest flags are measured from the final bytes, and `dataset-check` regenerates them identically. |
| `quota.py` | `dataset_spec.py` and the manifest | WIRED | `quota_sha256` is in the manifest. The 90 bases follow the quota counts (24 alphanumeric issuers, 4 MEI, 18 and 18 hard traits). |
| manifest | .NET and Python gates | WIRED | Both are parametrized from `manifest.json`, and a test asserts the manifest case set equals the files on disk. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Dataset regenerates byte-identically from seed | `devenv shell -- just dataset-check` | ok, 52 s | PASS |
| Python gate over all manifest cases | `pytest python/tests/test_dataset_ground_truth.py -q` | 867 passed | PASS |
| .NET gate over all manifest cases | `dotnet test --filter-class '*DatasetGroundTruthGateTests'` | Validation.Tests passed, 926 succeeded, 0 failed (other test projects report "zero tests ran" only because of the class filter) | PASS |
| Image-only and hash integrity of all 154 cases | independent pypdfium2 + sha256 script | no discrepancy | PASS |
| Full `just check` | run by the orchestrator at ceaa2c4 (later commits touch only `.planning`) | exit 0, pytest 2805 passed, e2e 13 passed | not re-run by me |

### Anti-Patterns and Review Findings

No TBD/FIXME/XXX debt markers were found to matter for the phase goal. 04-REVIEW.md has 0 critical, 4 warning and 6 info findings, all open. None blocks a roadmap success criterion. How I weighed them:

| Finding | Severity | Effect on must-haves |
|---------|----------|----------------------|
| WR-02: D-05 text-equals-XML test covers names, natOp, item code, description, NCM, unit, quantity, price and total, vNF and access key, with a whitespace-stripped substring match; it does not check dates, parties' tax ids, CFOP, CST, taxes, freight, discount or installments | Warning | The 04-06 plan truth lists exactly the fields the test covers, so the plan is met. But D-40's wording ("every graded string is printed") overstates it. Ground-truth validity (SC3) rests on the schema and validator gates, not on this test, so no roadmap criterion fails. Recommend either extending the test or narrowing the D-40 wording before the dataset is used for the first eval. |
| WR-01: `dataset-build --out DIR` deletes unlisted `.xml` and `.pdf` files in DIR | Warning | Developer-safety hazard (could wipe `data/skeleton`). Does not affect the delivered data. |
| WR-03: docstring claims lazy import that does not exist | Warning | Documentation only. |
| WR-04: twin tags inherit `barcode_unreadable` from the base | Warning | Latent. I confirmed that in the committed manifest flag and tag agree in all 154 cases, and a test guards it. |
| IN-04: nfe-0087 has a fully discounted line (`vProd == vDesc`) | Info | Passes the validators. |

### Deviation Weighed

Plan 04-07 raised the scan and barcode-decode DPI from the 200 frozen in 04-03 to 220. The reason is recorded in D-40 and the `scan.py` comments: at 200 DPI eight text bases (for example nfe-0011) did not decode, while 216 to 224 decode all 90 text bases and all eight `image_clean` twins. The 04-07 plan itself says such a failure is "a generator bug, fixed in scan.py constants, never by changing the flag", so the change follows the plan. The manifest `generator.scan` holds `scan_dpi` 220 and `barcode_decode_dpi` 220, consistent with the committed bytes. It was made before dataset-001 first merged, under the user-approved D-17 qualification, so it created no new dataset version. Accepted.

### Human Verification Required

1. **Twin legibility.** Open the eight twins of one dev curve base. Expected: mild lightly degraded, severe hard but readable, printed access-key digits legible. Why human: visual judgement (backstop truth, D-08).
2. **Renderer fidelity.** Compare `data/dataset/nfe-0001.pdf` with a reference DANFE layout. Expected: same blocks, labels, item columns and barcode position. Why human: visual comparison.
3. **CI byte identity.** Push the branch and read the `just dataset-check` step on ubuntu-latest and its duration (at most about 180 s, otherwise CONTEXT D-13). Why human: needs a push and a CI run, and the branch is not pushed.

### Housekeeping Before `/gsd-ship` (not gaps)

- README.md still shows Phase 4 as `Not started` (line 15) and has no `- Phase 4:` verification bullet. Under AGENTS.md these are written after verification, on the phase branch, and `test_readme_drift.py` fails `just check` until they hold. The narrative section (README line 54) already describes the dataset.
- REQUIREMENTS.md DATA-04 checkbox and traceability row still read Pending.
- ROADMAP.md progress row shows `9/9 In Progress`; mark complete only once the human items pass.

### Gaps Summary

No gaps. The dataset exists, is reproducible, is image-only where required, and its ground truth is gated in CI by the unmodified .NET validators and the exported schema. The only open items are the three human checks above and the bookkeeping listed under housekeeping.

---

_Verified: 2026-10-29_
_Verifier: Claude (gsd-verifier)_
