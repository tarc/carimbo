---
phase: 04-synthetic-dataset
verified: 2026-10-29T20:30:00Z
status: passed
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
  - .planning/phases/04-synthetic-dataset/04-10-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-10-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-11-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-11-SUMMARY.md
  - .planning/phases/04-synthetic-dataset/04-12-PLAN.md
  - .planning/phases/04-synthetic-dataset/04-12-SUMMARY.md
  - data/dataset/manifest.json
  - docs/DECISIONS.md
  - dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs
  - python/src/carimbo_datagen/dataset.py
  - python/src/carimbo_datagen/dataset_spec.py
  - python/src/carimbo_datagen/quota.py
  - python/src/carimbo_datagen/scan.py
  - python/tests/test_dataset_ground_truth.py
  - python/tests/test_dataset_manifest.py
  - python/tests/test_scan.py
covered_digest: "v4:sha256:3138ab28f5c5452cac99bbf5e87e8bd622dfdd63e35cb19bd9f42b2270fb10c4"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: human_needed
  previous_score: 4/5
  gaps_closed:
    - "G-04-1: blur severe sigma 2.4 made item-table digits misreadable (now 1.8, user-chosen on native-resolution previews)"
    - "G-04-2: twin PDF bytes depended on SOURCE_DATE_EPOCH (date now pinned in code)"
  gaps_remaining: []
  regressions: []
human_verification:
  - test: "UAT re-test 1. Open the committed data/dataset/nfe-0095.pdf (blur severe twin of nfe-0001) at native 220 DPI next to the text base, and read the item-table code, NCM and quantity cold. Optionally repeat on nfe-0127 and read the printed access-key digits (the access key was not re-checked at 1.8)."
    expected: "Item-table digits are transcribable with effort (CONTEXT D-08: hard but readable), the access-key digits are legible, and the other renderings are as judged fine in UAT test 1."
    why_human: "Legibility is a visual judgement (backstop truth, CONTEXT D-08). The user's 1.8 pick at the 04-11 checkpoint was one transcription of one invoice (nfe-0095), and 1.2 (mild) was transcribed just as correctly (IN-07). The optional nfe-0127 / access-key cross-check was not done."
  - test: "UAT re-test 3. With the user's approval, push gsd/phase-04-synthetic-dataset and run CI (workflow_dispatch or PR). Read the python job: `just py-check` and `just dataset-check`."
    expected: "Both green on ubuntu-latest (no SOURCE_DATE_EPOCH), and the dataset-check step at most about 180 s (CONTEXT D-13); otherwise D-13's path-filtered move applies."
    why_human: "Needs a push and a hosted CI run; nothing has been pushed. Locally the same condition (SOURCE_DATE_EPOCH unset) passes, which is local evidence only."
---

# Phase 4: Synthetic Dataset Verification Report

**Phase Goal:** As a developer, I want to evaluate against a seeded dataset of at least 150 varied and deliberately hard invoices whose ground truth is proven valid, so that measured accuracy reflects realistic difficulty instead of a few clean cases.
**Verified:** 2026-10-29
**Status:** human_needed
**Re-verification:** Yes, after gap closure (plans 04-10 to 04-12 for UAT gaps G-04-1 and G-04-2). The earlier report was `human_needed` with 4/5.

## Goal Achievement

The goal is achieved in code and data, and both UAT gaps are closed as far as offline evidence can show. I did not rely on the summaries or the orchestrator's numbers: I re-ran the byte-identity gate in the two environments that matter, re-hashed and re-inspected all 154 cases with an independent script, and diffed the data against the pre-gap-closure commit. Two human-only items remain (re-read of the committed blur severe twin, and the hosted CI run). Neither is a gap.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Manifest lists at least 150 cases: short and multi-page, many line items, several tax regimes, at least 10% alphanumeric CNPJs | VERIFIED | Unchanged since the first report and not touched by the gap closure. `manifest.json` has 154 cases (90 text bases, 64 twins), 20 two-page cases, 4 MEI plus normal and simples regimes, 40 of 154 with alphanumeric issuer CNPJ (26%). The data diff below proves the gap-closure plans changed nothing that affects these counts. |
| 2 | Rotated, blurred, low-DPI and barcode-unreadable variants are image-only PDFs with no text layer | VERIFIED | My script (`uv run python -I`, pypdfium2 plus zxing-cpp, not the repo tests) over all 154 cases: every non-`text` rendering has 0 text characters on all pages, every text case has a text layer, every `xml_sha256` and `pdf_sha256` equals the file on disk, and the `barcode_readable` flag equals an independent Code 128 decode at 220 DPI in all 154 cases, including the eight regenerated blur-severe twins (all false). Result: "cases 154 twins 64 bad 0". 309 files on disk. 44 cases carry `barcode_unreadable`, as before. |
| 3 | CI fails if any ground-truth record fails the exported JSON Schema or the .NET validators | VERIFIED | `dotnet test --filter-class '*DatasetGroundTruthGateTests'`: Carimbo.Validation.Tests 926 succeeded, 0 failed. `test_dataset_ground_truth.py` passed in the 1168-test Python run below. Both gates are parametrized from the manifest and run inside `just dotnet-check` and `just py-check`. `git diff ceaa2c4 HEAD` shows no change under `dotnet/`, `schema/` or `data/skeleton`, so the oracle was not touched by the gap closure. The ground-truth XML is unchanged (see data diff). |
| 4 | Versioned manifest records tags, split, hashes, seed, generator version and a fixed stratified CI subset; committed if at most about 20 MB, regenerated byte-identically | VERIFIED | `du -sb data/dataset` = 19,562,414 bytes (under 20,000,000; the size test warns above 18,000,000, as designed). `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check` printed "ok: data/dataset matches a regeneration from seed 20261022" in 47 s. The same recipe with `SOURCE_DATE_EPOCH=1700000000` passed in 47 s. The devenv value (315532800) passed in the first report and in the orchestrator's `just check`. All three environments therefore regenerate the committed bytes. |
| 5 | Severe renderings are hard but still human-readable (04-03 plan truth, `verification: backstop`, CONTEXT D-08) | UNCERTAIN (human) | Evidence moved but did not settle it. UAT test 1 found blur severe at 2.4 unreadable in the item table. The user then chose 1.8 at the 04-11 blocking checkpoint after transcribing the nfe-0095 item table (2.0 failed by one quantity digit, 1.8 all correct), and the committed nfe-0095.pdf has the approved preview's hash per the 04-11 SUMMARY. Remaining weakness, recorded as IN-07: one reader, one invoice, and the mild blur (1.2) was transcribed just as correctly, so the severe/mild contrast is thin; the access key was not re-read at 1.8 and the optional nfe-0127 cross-check was skipped. Legibility cannot be inferred from code. Routed to human item 1. |

**Score:** 4/5 truths verified, 0 present-but-behavior-unverified.

### Gap Closure Verification

| Gap | Claim | What I checked | Status |
|-----|-------|----------------|--------|
| G-04-2 (blocker) | Twin bytes independent of SOURCE_DATE_EPOCH | Read `scan.py`: `PDF_EPOCH = 315532800` and `_pinned_source_date_epoch()` wrap the reportlab canvas in `write_image_only_pdf`; it restores the previous value or absence in a `finally`. Ran `just dataset-check` with the variable removed (the CI condition) and with 1700000000: both ok. `test_scan.py` (in-process bytes across values, environment restoration) and `test_dataset_manifest.py` (separate processes with the variable removed and set differently, compared with committed hashes) exist and pass. PDF_EPOCH is not in the manifest, as planned. | CLOSED locally. Hosted CI re-proof is human item 2. |
| G-04-1 (major) | Blur severe re-tuned, only eight twins and the manifest change | `scan.py` has `"blur": {"mild": 1.2, "severe": 1.8}` with the UAT reason in the comment. `git diff --stat ceaa2c4 HEAD -- data` lists exactly nine files: the eight blur-severe twins (nfe-0095, 0103, 0111, 0119, 0127, 0135, 0143, 0151) and `manifest.json`. A structural JSON diff of the manifest against `ceaa2c4` shows exactly nine differences: `generator/scan/degradation/blur/severe` 2.4 to 1.8 and the eight `pdf_sha256` values at indices 94, 102, 110, 118, 126, 134, 142, 150. Nothing else differs, so XML, text PDFs and the other 56 twins keep their bytes, and the tags, barcode flags, 44 `barcode_unreadable` count and `ci_subset` are unchanged. `test_scan.py` pins the whole DEGRADATION table literally (mild below severe for every type). | CLOSED in data. Legibility is human item 1. |
| Docs | D-41 and counts match the files | `docs/DECISIONS.md` has `## D-41` (line 913) and the pointer line under D-40 (883) with 0 lines deleted from D-40; figures in D-41 and README (sigma 1.8, 19,562,414 bytes, 437,586 headroom, 44 `barcode_unreadable`) match the manifest and `du -sb`. `just docs-check` prints "docs ok", `just secrets-check` prints "secrets ok", and the README drift test passes. No stale 2.4 or 19,368,019 remains outside D-40's own superseded text and D-41's explanation. | VERIFIED |

### Required Artifacts and Key Links

| Artifact or link | Status | Details |
|------------------|--------|---------|
| `python/src/carimbo_datagen/scan.py` (PDF_EPOCH pin, DEGRADATION) | VERIFIED | Substantive, used by `dataset.py::_write_twin` through `render_twin` and by `scan_parameters()` into `generator.scan`. |
| `python/tests/test_scan.py`, `python/tests/test_dataset_manifest.py` | VERIFIED | Contain the environment-independence and DEGRADATION tests; 1168 passed across scan, manifest, ground-truth and readme-drift test files with SOURCE_DATE_EPOCH removed. |
| `data/dataset/manifest.json` and 154 case pairs | VERIFIED | Level 4: hashes and flags recomputed independently from the bytes on disk. |
| `justfile` `check` to `dataset-check`; `.github/workflows/ci.yml` to `just dataset-check`; manifest to .NET and Python gates | WIRED | Unchanged since the first report; the 04-12 edit moved only the docs-check range (D-18 to D-41) in justfile, AGENTS.md and README. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Regeneration is byte-identical without SOURCE_DATE_EPOCH (CI condition) | `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check` | ok, 47 s | PASS |
| Regeneration is byte-identical with another value | `devenv shell -- env SOURCE_DATE_EPOCH=1700000000 just dataset-check` | ok, 47 s | PASS |
| Python dataset gates | `env -u SOURCE_DATE_EPOCH pytest test_scan.py test_dataset_manifest.py test_readme_drift.py test_dataset_ground_truth.py` | 1168 passed, 1 warning (size above the 18 MB warning line, designed) | PASS |
| .NET validator gate over every manifest case | `dotnet test --filter-class '*DatasetGroundTruthGateTests'` | 926 succeeded, 0 failed | PASS |
| Hash, image-only and barcode-flag integrity of all 154 cases | independent pypdfium2 + zxingcpp + sha256 script | bad 0 | PASS |
| Docs and secrets gates | `just docs-check`, `just secrets-check` | docs ok, secrets ok | PASS |
| Full `just check` (all gates) | run by the orchestrator at 0cd063c with SOURCE_DATE_EPOCH unset: .NET 2173, pytest 2810, e2e 13 | exit 0; not re-run by me (later commits touch only `.planning`; I ran the phase-relevant gates above) | Not re-run |

### Probe Execution

SKIPPED: no `probe-*.sh` is declared by this phase's plans.

### Requirements Coverage

Every requirement ID in the plan frontmatter (DATA-02 to DATA-06; 04-10 to 04-12 add DATA-03, DATA-05 and DATA-06) appears in `REQUIREMENTS.md`. REQUIREMENTS.md maps no other requirement to Phase 4, so nothing is orphaned.

| Requirement | Source plans | Status | Evidence |
|-------------|--------------|--------|----------|
| DATA-02 (at least 150 cases, varied, 10% alphanumeric CNPJ) | 04-01, 04-05, 04-06, 04-07 | SATISFIED | Truth 1. |
| DATA-03 (degraded and barcode-unreadable variants are image-only) | 04-02, 04-03, 04-07, 04-11 | SATISFIED | Truth 2, re-checked on the regenerated blur-severe twins. Readability of the severe level is truth 5. |
| DATA-04 (ground truth validated against schema and .NET validators in CI) | 04-01, 04-06, 04-09 | SATISFIED in code, tracking lag | Truth 3. `.planning/REQUIREMENTS.md` still has `- [ ] DATA-04` (line 43) and `Pending` (line 182); bookkeeping only. |
| DATA-05 (versioned manifest, stratified CI subset) | 04-04, 04-05, 04-07, 04-08, 04-12 | SATISFIED | Truth 4. |
| DATA-06 (commit when at most about 20 MB, reproducible) | 04-04, 04-07, 04-08, 04-10, 04-11, 04-12 | SATISFIED locally | 19,562,414 bytes, fully committed, `dataset-check` byte identity in three environments. Hosted-CI half is human item 2. |

### Anti-Patterns and Review Findings

No TBD, FIXME or XXX debt markers in the phase's source changes. 04-REVIEW.md: 0 critical, 4 warning, 8 info, all open per 04-REVIEW-DISPOSITION.md. None breaks a roadmap success criterion; the new ones from this round:

| Finding | Severity | Effect |
|---------|----------|--------|
| IN-07: 1.8 validated by one reader on one invoice, and mild was read just as correctly | Info | Feeds the weakness noted under truth 5; confirms the human re-read is the right gate. |
| IN-08: `_pinned_source_date_epoch` mutates process-global state, not thread-safe | Info | Generator is single-threaded; tests prove restoration. Does not affect any must-have. |
| WR-02: text-equals-XML test covers fewer fields than D-40's wording | Warning | Carried over from the first report. Ground-truth validity rests on the schema and validator gates, not on this test. Recommend narrowing the D-40 wording or extending the test before the first eval. |
| WR-01, WR-03, WR-04 | Warning | Unchanged from the first report; developer-safety, documentation and latent-tag issues. |

### Human Verification Required

1. **UAT re-test 1: committed blur severe twin.** Read the item-table code, NCM and quantity of `data/dataset/nfe-0095.pdf` at native 220 DPI, ideally also the access-key digits and nfe-0127. Expected: hard but readable. Why human: backstop truth; the sole prior evidence is one transcription.
2. **UAT re-test 3: CI on a pushed branch.** Needs the user's approval to push. Expected: python job green, `just dataset-check` at most about 180 s. Why human: hosted CI, never run on the gap-closure commits.

UAT test 2 (renderer fidelity) already passed in 04-UAT.md and needs no further check.

### Housekeeping Before `/gsd-ship` (not gaps)

- README.md Status row for Phase 4 still reads `Not started` (line 15) and has no `- Phase 4:` verification bullet; AGENTS.md says these are written after verification on the phase branch, and `test_readme_drift.py` is currently green only because the ROADMAP does not yet mark Phase 4 Complete.
- REQUIREMENTS.md DATA-04 checkbox and traceability row still read Pending.
- ROADMAP.md Phase 4 checkbox is still `[ ]`; mark complete only after the two human items pass.

### Gaps Summary

No gaps. G-04-1 and G-04-2 are closed in the committed files and in every offline gate I could run, including the CI-like environment that failed before. The dataset is 154 cases, 19,562,414 bytes, image-only where required, validator-gated, and byte-reproducible without depending on `SOURCE_DATE_EPOCH`. Status stays `human_needed` because the severe-blur legibility call and the first hosted CI run on these commits cannot be settled offline.

---

_Verified: 2026-10-29_
_Verifier: Claude (gsd-verifier)_
