---
phase: 04-synthetic-dataset
plan: 11
subsystem: testing
tags: [datagen, scan, degradation, blur, gaussian, pillow, dataset, uat-gap]
gap_closure: true
gap_ids: [G-04-1]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-10 byte-identical regeneration with SOURCE_DATE_EPOCH unset or set (dataset-check ok in CI-like environments)
provides:
  - blur severe sigma 1.8 (was 2.4), chosen by the user on native-resolution previews
  - literal pin of the whole DEGRADATION table in test_scan.py
  - the eight blur-severe twins and manifest.json regenerated, nothing else under data/ changed
affects: [04-12 README and D-41 decision record, UAT re-test 1, phase 5 and later evals that read the blur-severe twins]

actuals:
  tokens: 1100   # chars/4 over scan.py and test_scan.py changes only; the eight binary PDFs and the manifest are not counted
  tasks: 3
  commits: 2

plan_head_before: fd19bec1776d102bb02266dbe3f018db723895d5
plan_head_after: e84a9a91a26872849302099511f5a23099cdd695
commits: 2

tech-stack:
  added: []
  patterns:
    - "Degradation severities are pinned by a literal test, so a change of any severity is a visible test change"
    - "A human-judged rendering parameter is chosen at a blocking-human checkpoint on a preview proven to be the production path (self-check against committed bytes), and the committed artifact is checked against the previewed sha256"

key-files:
  created: []
  modified:
    - python/src/carimbo_datagen/scan.py
    - python/tests/test_scan.py
    - data/dataset/manifest.json
    - data/dataset/nfe-0095.pdf
    - data/dataset/nfe-0103.pdf
    - data/dataset/nfe-0111.pdf
    - data/dataset/nfe-0119.pdf
    - data/dataset/nfe-0127.pdf
    - data/dataset/nfe-0135.pdf
    - data/dataset/nfe-0143.pdf
    - data/dataset/nfe-0151.pdf

key-decisions:
  - "Blur severe sigma is 1.8 (1.5 times mild 1.2, about 0.21 mm at 220 DPI); chosen by the user at the blocking-human checkpoint"
  - "The label stays dataset-001 under the CONTEXT D-17 qualification: dataset-001 has not merged to main and no file under evals/results cites it (checked by git grep)"

requirements-completed: [DATA-03, DATA-06]

coverage:
  - id: D1
    description: "DEGRADATION is pinned literally in test_scan.py with mild below severe for every degradation type, and blur severe is 1.8"
    requirement: DATA-03
    verification:
      - kind: unit
        ref: "python/tests/test_scan.py#test_degradation_parameters_are_the_frozen_values"
        status: pass
    human_judgment: false
  - id: D2
    description: "Only the eight blur-severe twins and manifest.json changed under data/dataset; the manifest differs only in blur severe and the eight pdf_sha256 values; the dataset regenerates byte for byte with SOURCE_DATE_EPOCH unset"
    requirement: DATA-06
    verification:
      - kind: integration
        ref: "git diff --name-only fd19bec HEAD -- data/dataset (nine paths) plus the manifest comparison command of Task 3 verify"
        status: pass
      - kind: integration
        ref: "devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check"
        status: pass
    human_judgment: false
  - id: D3
    description: "Blur severe at 1.8 is hard but still readable on the item-table digits of nfe-0095 at native 220 DPI (CONTEXT D-08)"
    requirement: DATA-03
    verification: []
    human_judgment: true
    rationale: "Legibility is a human reading judgment; the user transcribed the preview at native resolution and chose 1.8. The UAT re-test 1 at the end of the phase re-reads the committed nfe-0095."

duration: 7 min (continuation from Task 3; Task 1 preview work and the Task 2 checkpoint ran in the earlier agent session and are not timed here)
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 11: blur severe readable with effort, chosen by the user on the rendered twin (gap closure G-04-1) Summary

**Blur severe Gaussian sigma lowered from 2.4 to 1.8 by the user's own reading of native-resolution previews, pinned by a literal DEGRADATION test, with exactly the eight blur-severe twins and the manifest regenerated and the dataset at 19,562,414 bytes.**

## Performance

- **Duration:** 7 min for Task 3 (continuation); Tasks 1 and 2 are in the earlier session
- **Completed:** 2026-10-29T19:23Z (feat commit)
- **Tasks:** 3 (Task 1 tracer, Task 2 blocking-human checkpoint, Task 3 TDD)
- **Files modified:** 11 (scan.py, test_scan.py, manifest.json, eight PDFs)

## Accomplishments

- G-04-1 closed in the data: blur severe is 1.8, the level at which the user transcribed every item code, NCM and quantity of nfe-0095 correctly.
- `test_degradation_parameters_are_the_frozen_values` pins rotation (1.0, 3.5), blur (1.2, 1.8) and low_dpi (110, 72) literally, plus the ordering of mild and severe for each type.
- Only nine files under data/dataset changed; all other twins, bases, XML and manifest fields keep their committed bytes.

## The user's choice and reading (T-04-28)

At the Task 2 `gate="blocking-human"` checkpoint the user answered **"1.8 (Recommended)"**.

The user's own transcription of `nfe-0095-items.png` at native resolution, reading bottom-up without the answer key (plan 04-12 records it in D-41):

| sigma | reading |
|-------|---------|
| 2.4 | nothing readable |
| 2.0 | item 1 read D00137 / 39191010 / 31.8096 (quantity wrong, answer 31,6096); item 2 D00290 / 84818095 / 26.5420 correct; item 3 D00317 / 84439933 / 46,3900 correct. One digit wrong, so 2.0 fails |
| 1.8 | all three items correct (D00137 39191010 31.6096; D00290 84818095 26.5420; D00317 84439933 46,3900) |
| 1.6 | all three items correct |
| 1.2 | all three items correct, with the comma decimal separator read as printed (31,6096; 26,5420; 46,3900) |

Notes from the user: at 1.6 to 2.0 the user read the printed decimal comma of items 1 and 2 as a period; no digit was lost. A diagonal stroke printed on the page itself crosses the CFOP/UN/QTD area of row 3 in every band, including 1.2.

**Not performed:** the optional cross-check on `nfe-0127-items.png` and `nfe-0095-key.png` (Task 2 step 4). The user chose 1.8 directly. The choice rests on the nfe-0095 item table alone.

Previews lived outside the repository, in `/tmp/claude-1000/-home-tarci-projects-carimbo/c2e0bcbc-cb82-4648-9b5f-074e50c60abe/scratchpad/g04-1` (`candidates.md`, `answers.md`, the sheets and `blur_severe_preview.py`); none was committed.

## Task Commits

1. **Task 1 (tracer): preview, candidate sigma to production twin bytes** - no commit by design (scratch tooling outside the repository). Self-check passed: at 2.4 all eight twins had the committed `pdf_sha256`; at 1.2 nfe-0095 had the committed `pdf_sha256` of nfe-0094.
2. **Task 2 (checkpoint:human-verify, blocking-human): sigma choice** - no commit; resolved by the user (1.8).
3. **Task 3 (TDD):**
   - RED `ee8e093` - `test(04-11): pin the whole DEGRADATION table literally`
   - GREEN `e84a9a9` - `feat(04-11): blur severe sigma 1.8, chosen by the user on native-resolution previews`

**Plan metadata:** the docs commit that carries this SUMMARY (see git log).

## TDD record

- **RED:** `devenv shell -- uv run --project python pytest python/tests/test_scan.py -q -k frozen_values` failed with `{'blur': {'mild': 1.2, 'severe': 2.4}} != {'blur': {'mild': 1.2, 'severe': 1.8}}` (the other two items, rotation and low_dpi, identical). Semantic assessment: the target test executed and failed on the planned assertion, on the blur severe value only, for the intended reason (scan.py still held 2.4). The pytest report is not one of the formats `gsd_run check tdd-red-evidence` parses, so the classifier was not run; the assessment above is by inspection of the real output.
- **GREEN:** scan.py blur severe set to 1.8 with the UAT reason in the comment; the test passes.
- **REFACTOR:** none.

## Measured results (Task 3 acceptance)

- Baseline ledger and `plan_head_before`: `fd19bec1776d102bb02266dbe3f018db723895d5` (printed by the baseline step).
- `git diff --name-only fd19bec HEAD -- data/dataset`: exactly `manifest.json` and nfe-0095, 0103, 0111, 0119, 0127, 0135, 0143, 0151 (9 paths); no untracked file in data/dataset.
- nfe-0095 committed sha256 `2d8ca9128d360bc41185ac0c4a8a5b8ba19384f4262d9b8c8f28b106f543a072`, equal to the candidates.md row for 1.8 (the approved preview).
- `du -sb data/dataset`: **19,562,414** bytes, headroom **437,586** to 20,000,000; equal to the projection in candidates.md.
- The eight new twin sizes (bytes): nfe-0095 153,476; nfe-0103 157,047; nfe-0111 187,723; nfe-0119 178,061; nfe-0127 173,107; nfe-0135 145,932; nfe-0143 226,541; nfe-0151 180,700; sum 1,402,587 (was 1,208,192).
- `barcode_readable` of all eight blur-severe twins in the committed manifest: false. `barcode_unreadable` cases in the manifest: 44 (unchanged).
- Manifest comparison (only blur severe and the eight blur-severe `pdf_sha256` removed): equal.
- `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check`: `ok: data/dataset matches a regeneration from seed 20261022`.
- `devenv shell -- just py-check`: 2810 passed, 13 deselected; one expected warning that the dataset is above the 18,000,000-byte warning line (hard limit 20,000,000, D-12).
- Targeted run `test_scan.py test_dataset_scans.py test_dataset_manifest.py`: 650 passed.
- `git grep -l dataset-001 -- evals/results`: empty, so the D-17 qualification still allows the label.
- Acceptance greps: `G-04-1` in scan.py and `frozen_values` in test_scan.py both at least 1.

## Decisions Made

- Sigma 1.8 (user's choice), pinned literally; label stays dataset-001 (D-17 qualification). README.md and docs/DECISIONS.md are not edited here; plan 04-12 records the change and the user's transcription in D-41.

## Deviations from Plan

None - plan executed exactly as written. (The optional cross-check in Task 2 step 4 was not performed by the user; this is a user choice at a checkpoint, not an executor deviation.)

## Issues Encountered

None.

## Known Stubs

None.

## Threat Flags

None. No new network, auth or file-access surface; preview files stayed outside the repository (T-04-29).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for plan 04-12 (README task and D-41 decision record, including the user's transcription above). UAT re-test 1 re-reads the committed nfe-0095 at the end of the phase.
- Reversibility: before dataset-001 first merges to main a re-tune keeps the label; after that merge any change is a new dataset version.

## Self-Check: PASSED

- Files modified exist: scan.py, test_scan.py, manifest.json and the eight PDFs (FOUND).
- Commits `ee8e093` and `e84a9a9` are ancestors of HEAD (FOUND).
- All Task 3 acceptance criteria and plan-level verification commands re-run green (above).

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*
