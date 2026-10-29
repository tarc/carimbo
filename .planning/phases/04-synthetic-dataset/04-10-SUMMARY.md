---
phase: 04-synthetic-dataset
plan: 10
subsystem: testing
tags: [reportlab, determinism, source-date-epoch, ci, twin-pdf, gap-closure]
gap_closure: true
gap_ids: [G-04-2]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-08 committed data/dataset (64 image-only twins carrying D:19800101000000), scan.write_image_only_pdf, dataset.build_case_files
provides:
  - scan.PDF_EPOCH (315532800) and the scoped SOURCE_DATE_EPOCH pin around the reportlab canvas, so twin bytes no longer depend on the caller's environment
  - test_twins_built_without_or_with_another_source_date_epoch_have_the_committed_bytes, a permanent subprocess proof inside just py-check
  - test_twin_bytes_do_not_depend_on_source_date_epoch and test_writing_a_pdf_restores_source_date_epoch, the in-process proofs
affects: [04-11, 04-12, UAT re-test 3 (CI half of G-04-2)]

actuals:
  tokens: 2020
  tasks: 2
  commits: 3
plan_head_before: f6674858f7c291f5cd24133ed47ed0e404295d4a
plan_head_after: ffd12070025226b7c381763aa7b403c22d3a78ff

tech-stack:
  added: []
  patterns: [library-read environment variable pinned in code inside a try/finally scope instead of exported by the caller]

key-files:
  created: []
  modified:
    - python/src/carimbo_datagen/scan.py
    - python/tests/test_dataset_manifest.py
    - python/tests/test_scan.py

key-decisions:
  - "PDF_EPOCH is a scan.py constant with a comment, not a scan_parameters() entry, so manifest.json stays byte-identical and the plan can prove that no dataset byte moved (D-41 records it in plan 04-12)"
  - "The pin is scoped (set, then restored in a finally block) rather than exported once, so the text DANFE writer and subprocesses never see the pinned value"

patterns-established:
  - "Environment-variant subprocess tests: copy os.environ, pop or set the variable, run the same builder script, compare with committed hashes"

requirements-completed: [DATA-06]

coverage:
  - id: D1
    description: "write_image_only_pdf gives every twin the PDF date D:19800101000000 and the /ID derived from epoch 315532800 whatever SOURCE_DATE_EPOCH holds, and restores the previous value or absence, also when writing raises"
    requirement: DATA-06
    verification:
      - kind: unit
        ref: "python/tests/test_scan.py#test_twin_bytes_do_not_depend_on_source_date_epoch"
        status: pass
      - kind: unit
        ref: "python/tests/test_scan.py#test_writing_a_pdf_restores_source_date_epoch"
        status: pass
    human_judgment: false
  - id: D2
    description: "Separate processes with SOURCE_DATE_EPOCH removed or set to 1700000000 build nfe-0001, nfe-0091 and nfe-0095 with exactly the committed sha256 values"
    requirement: DATA-06
    verification:
      - kind: integration
        ref: "python/tests/test_dataset_manifest.py#test_twins_built_without_or_with_another_source_date_epoch_have_the_committed_bytes"
        status: pass
    human_judgment: false
  - id: D3
    description: "just dataset-check, just datagen-check and just py-check pass with SOURCE_DATE_EPOCH unset, dataset-check also with 1700000000, and no byte under data/ changed"
    requirement: DATA-06
    verification:
      - kind: other
        ref: "devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check"
        status: pass
      - kind: other
        ref: "devenv shell -- env -u SOURCE_DATE_EPOCH just py-check"
        status: pass
    human_judgment: true
    rationale: "The CI half of G-04-2 (the GitHub runner itself) is only proven by UAT re-test 3 after the human approves a push; this plan reproduces the runner environment locally"

duration: 22min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 10: twin bytes no longer depend on SOURCE_DATE_EPOCH (gap closure G-04-2) Summary

**scan.py pins the twin PDF date and /ID seed to PDF_EPOCH=315532800 in a scoped, restored SOURCE_DATE_EPOCH, so `just dataset-check` regenerates all 154 cases byte-identically with the variable unset, at 315532800 or at 1700000000, and no byte under data/ moved.**

## Performance

- **Duration:** about 22 min
- **Completed:** 2026-10-29T18:06Z
- **Tasks:** 2 (a tracer and an auto task, both TDD)
- **Files modified:** 3 (one source file, two test files)

## Accomplishments

- Root cause of CI run 40915848246 closed in code: reportlab 5.0.1 takes the document date and the /ID seed from `SOURCE_DATE_EPOCH`; the devenv shell exports 315532800 and the runner sets nothing. The pin reproduces the committed bytes in both.
- A permanent subprocess test in `just py-check` starts a Python process without the variable and another with 1700000000 and compares nfe-0001 (text), nfe-0091 (image_clean twin) and nfe-0095 (blur twin) with the committed sha256 values, so the CI failure is reproduced and closed locally.
- Full end-to-end proof with the CI environment (details below); `git diff --stat f667485 HEAD -- data` is empty.

## Task Commits

1. **Task 1 (tracer), RED: failing subprocess test** - `36a5d03` (test)
2. **Task 1 (tracer), GREEN: PDF_EPOCH and the scoped pin** - `8519dbe` (fix)
3. **Task 2: in-process byte equality and environment restoration tests** - `ffd1207` (test)

**Plan metadata:** the docs(04-10) commit that carries this SUMMARY.

## TDD evidence

**RED (`36a5d03`).** `devenv shell -- uv run --project python pytest python/tests/test_dataset_manifest.py -q -k source_date_epoch` gave `2 failed, 236 deselected`. Both parameters (`[unset]`, `[1700000000]`) failed on exactly the two twin PDFs, with "Omitting 4 identical items" (the nfe-0001 text PDF and the three XML hashes matched), so no second environment dependency exists:

```
{'nfe-0091.pdf': '24ed8915ec90...'} != {'nfe-0091.pdf': 'c81ad761fc30...'}   (unset)
{'nfe-0095.pdf': '2927e7c9b0ed...'} != {'nfe-0095.pdf': '91b454bb3fb3...'}   (unset)
{'nfe-0091.pdf': 'ebd7bec6d603...'} != {'nfe-0091.pdf': 'c81ad761fc30...'}   (1700000000)
{'nfe-0095.pdf': 'd870758fafdd...'} != {'nfe-0095.pdf': '91b454bb3fb3...'}   (1700000000)
```

The target tests executed and failed on the planned assertion (`assert built == expected`), so the semantic assessment is: valid RED, intended reason.

**GREEN (`8519dbe`).** Both parameters pass (`2 passed, 236 deselected`), and `env -u SOURCE_DATE_EPOCH pytest test_scan.py test_dataset_manifest.py test_dataset_scans.py` reports `647 passed, 1 warning` (the warning is the pre-existing dataset size line, 19,368,019 bytes against the 18,000,000 warning line).

**Non-vacuity of the Task 2 tests.** With the body of `_pinned_source_date_epoch` replaced by a bare `yield`, `test_twin_bytes_do_not_depend_on_source_date_epoch` fails (`AssertionError: assert b'%PDF-1.3...' == b'%PDF-1.3...'`, `At index 291200 diff: b'0' != b'2'`, the date digits) and the restoration test passes (a bare yield mutates nothing). With the try/finally restore removed, `test_writing_a_pdf_restores_source_date_epoch` fails (`assert 'SOURCE_DATE_EPOCH' not in environ(...)`). scan.py was then restored byte for byte (`git diff` against the fix commit empty) and the two tests pass.

## CI-environment proofs

`devenv shell -- env -u SOURCE_DATE_EPOCH env` shows the variable absent (count 0); `devenv shell -- env` shows `SOURCE_DATE_EPOCH=315532800`.

| Command | Result | Wall time |
|---------|--------|-----------|
| `devenv shell -- env -u SOURCE_DATE_EPOCH just dataset-check` | exit 0, `ok: data/dataset matches a regeneration from seed 20261022` | 47 s |
| `devenv shell -- env SOURCE_DATE_EPOCH=1700000000 just dataset-check` | exit 0, same ok line | 48 s |
| `devenv shell -- env -u SOURCE_DATE_EPOCH just datagen-check` | exit 0, `ok: data/skeleton matches a regeneration from seed 20261004` | 2 s |
| `devenv shell -- env -u SOURCE_DATE_EPOCH just py-check` | exit 0, ruff clean, pyright 0 errors, `2809 passed, 13 deselected, 1 warning` | 52 s |
| `devenv shell -- just py-check` | exit 0, `2809 passed, 13 deselected, 1 warning` | 48 s |

`git status --porcelain -- data` prints nothing, and `git diff --stat f667485 HEAD -- data` (baseline read from the plan ledger) prints nothing: the 64 twins, 90 text PDFs, XML and manifest.json are the committed bytes.

## Files Created/Modified

- `python/src/carimbo_datagen/scan.py` - `PDF_EPOCH = 315532800` with its explanatory comment, `_pinned_source_date_epoch()` context manager (set, yield, restore or delete in `finally`), canvas construction through `pdf.save()` inside it, docstring updated; `scan_parameters()` unchanged.
- `python/tests/test_dataset_manifest.py` - environment-variant subprocess test (ids `unset`, `1700000000`) reusing `_SUBPROCESS_BUILD` and `_sha256`; the existing reverse-order test is untouched.
- `python/tests/test_scan.py` - byte-equality test over three environments and the restoration test (normal path, raising call, variable absent or set).

## Decisions Made

- PDF_EPOCH lives only in scan.py (a documented constant), not in `scan_parameters()`, so manifest.json is unchanged; plan 04-12 records it in D-41.
- The pin is scoped and restored instead of exported once, so nothing outside the canvas sees the pinned value, and no justfile, ci.yml or devenv.nix change stands in for the in-code pin.

## Deviations from Plan

None - plan executed exactly as written. (The planned `devenv shell -- uv run ... -k source_date_epoch` Task 2 verify also selects only the two new tests, as the plan expects.)

## Issues Encountered

- First mutation attempt used a zsh variable holding the command string, which zsh does not word-split; rerun with a shell function. No effect on repository state.

## Known Stubs

None.

## Threat Flags

None. T-04-25 and T-04-26 are mitigated as planned (pin, restoration test); no new network, auth or file surface.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for 04-11 (regenerating the severe-blur twins): twin bytes are now independent of `SOURCE_DATE_EPOCH`, and the 56 twins outside blur severe still carry their committed bytes.
- The CI half of G-04-2 stays for UAT re-test 3 after a human-approved push; the local CI-like environment passes every gate.

## Self-Check: PASSED

- `python/src/carimbo_datagen/scan.py`, `python/tests/test_dataset_manifest.py`, `python/tests/test_scan.py` exist.
- Commits `36a5d03`, `8519dbe`, `ffd1207` are ancestors of HEAD; `git rev-list --count f667485..HEAD` was 3 before this SUMMARY.
- `git status --porcelain -- data` and `git diff --stat f667485 HEAD -- data` are empty.

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*
