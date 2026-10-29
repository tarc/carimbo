---
phase: 04-synthetic-dataset
plan: 03
subsystem: testing
tags: [pdfium, pillow, reportlab, zxing-cpp, image-only-pdf, barcode, determinism]

requires:
  - phase: 04-synthetic-dataset
    provides: plan 04-02 pinned and installed pillow, numpy, reportlab, pypdfium2 and zxing-cpp
provides:
  - scan.py: render_twin turns a text DANFE into eight deterministic image-only renderings
  - barcode_readable, a flag measured from the final PDF bytes (page 1 at 200 DPI, Code 128 equal to the key)
  - is_image_only, the text-layer-absence definition (zero pdfium text objects, empty text range)
  - scan_parameters(), the frozen degradation parameters for the manifest generator.scan object (plan 04-04)
affects: [04-04 manifest generator block, 04-07 committed image-only twins, 04-08 CI regeneration check]

actuals:
  tokens: 4100
  tasks: 2
  commits: 4
plan_head_before: a74af233ed08a6519d41337039ab88224c437e16
plan_head_after: 0dac2f58ba08f28c73a1b36b2701ef20cbbafb5a

tech-stack:
  added: []
  patterns: [per-twin numpy RNG from sha256(seed_text), reportlab invariant=1 with useA85=0 and no page compression, barcode flag decided from final bytes, targeted damage verified by construction]

key-files:
  created: [python/src/carimbo_datagen/scan.py, python/tests/test_scan.py]
  modified: []

key-decisions:
  - "Degradation parameters frozen unchanged from the RESEARCH strawman after inspecting previews of every rendering: rotation 1.0/3.5 degrees, blur sigma 1.2/2.4, low_dpi 110/72, JPEG q50, scan and barcode-decode DPI 200"
  - "barcode_damaged locates the key's Code 128 on the 200 DPI raster itself (no separate 300 DPI locate pass), draws 3 pale bands of 8% box width, and raises ValueError if the twin still decodes"
  - "render_twin rejects any (rendering, severity) pair not listed in RENDERINGS, so the catalogue is the single validity source"

patterns-established:
  - "Twin bytes depend only on (base bytes, rendering, severity, seed_text): no module-level RNG, no wall clock, no Pillow PDF writer"
  - "A rendering whose purpose is a measurable property (unreadable barcode) asserts that property on its own output"

requirements-completed: []  # DATA-03 is completed by the committed twins in plan 04-07

coverage:
  - id: D1
    description: "render_twin yields image-only (one image object, zero text objects, zero extractable characters) PDFs for all eight renderings with the base page count and size"
    requirement: DATA-03
    verification:
      - kind: unit
        ref: "python/tests/test_scan.py#test_every_rendering_is_image_only_with_the_base_pages_and_deterministic"
        status: pass
    human_judgment: false
  - id: D2
    description: "Twins are byte-deterministic in-process and across fresh processes, and leak neither the access key nor the case id"
    requirement: DATA-03
    verification:
      - kind: unit
        ref: "python/tests/test_scan.py#test_fresh_process_twin_matches_this_process"
        status: pass
      - kind: unit
        ref: "python/tests/test_scan.py#test_twin_bytes_leak_neither_key_nor_case_id_and_use_dct_without_a85"
        status: pass
    human_judgment: false
  - id: D3
    description: "barcode_readable is true for the text PDF and image_clean, false for barcode_damaged, measured from the final bytes"
    requirement: DATA-03
    verification:
      - kind: unit
        ref: "python/tests/test_scan.py#test_barcode_flag_is_true_for_clean_scans_and_false_for_damaged_ones"
        status: pass
    human_judgment: false
  - id: D4
    description: "Severe renderings stay human-readable (header fields, printed key digits) while mild ones stay easy to read"
    verification: []
    human_judgment: true
    rationale: "Legibility is an eye judgement; the executor's preview notes are below and the end-of-phase human check in plan 04-08 confirms it"

duration: 25min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 03: image-only twins of a DANFE, rendered, degraded, wrapped and flagged Summary

**scan.py rasterizes a text DANFE with pdfium, applies one of eight degradations, JPEG-encodes it and wraps it in a byte-deterministic reportlab image-only PDF, with the barcode flag measured from the final bytes at 200 DPI and the parameters frozen after a preview inspection.**

## Performance

- **Duration:** about 25 min
- **Tasks:** 2 (1 tracer, 1 auto), both TDD
- **Files:** 2 created (scan.py, test_scan.py)

## Accomplishments

- `render_twin(base_pdf, rendering, severity, *, seed_text, access_key)` produces all eight renderings in `RENDERINGS` order (image_clean; rotation, blur, low_dpi at mild and severe; barcode_damaged). Each twin has one grayscale JPEG per page under `/DCTDecode` (no ASCII85), reportlab `invariant=1` metadata (1980 date, anonymous, untitled) and no page compression, so no interpreter-zlib output enters the bytes.
- `is_image_only` defines text-layer absence as zero pdfium text objects and an empty `get_text_range` on every page; the skeleton text PDFs fail it, every twin passes it.
- `barcode_readable` decodes page 1 of the final PDF bytes at `BARCODE_DECODE_DPI` 200 with zxing-cpp and requires a Code 128 whose text equals the 44-character key exactly (case-003 has an alphanumeric key and passes).
- `barcode_damaged` damages only the located barcode region, raises `ValueError` ("no Code 128 equal to the access key on page 1") on a base with no matching barcode, and raises if the finished twin still decodes.
- `scan_parameters()` returns the JSON-serializable constants plan 04-04 writes as `generator.scan`.

## Frozen parameters

| Constant | Value |
|----------|-------|
| `SCAN_DPI`, `BARCODE_DECODE_DPI` | 200 |
| `JPEG_QUALITY` | 50 (grayscale, `optimize=True`) |
| `DEGRADATION["rotation"]` | mild 1.0, severe 3.5 degrees (sign drawn from the twin RNG, bicubic, white fill) |
| `DEGRADATION["blur"]` | mild sigma 1.2, severe sigma 2.4 pixels at 200 DPI |
| `DEGRADATION["low_dpi"]` | mild 110, severe 72 DPI raster at the full page size |
| `BARCODE_DAMAGE` | 3 bands, band width 0.08 of the box width, gray 215 to 245 |

## Preview inspection (CONTEXT D-08)

Page 1 of every rendering of skeleton case-001 (1 page) and case-003 (2 pages) was written to a scratch directory outside the repository and opened. Sizes are PDF bytes; the flag is `barcode_readable` at 200 DPI. No constant needed a change from the strawman.

| Rendering | case-001 | case-003 | Barcode | Observation |
|-----------|----------|----------|---------|-------------|
| image_clean | 244,888 | 702,927 | yes | faithful to the text DANFE, all fields and the key digits crisp |
| rotation mild | 254,798 | 716,237 | yes | 1 degree skew, indistinguishable at normal reading |
| rotation severe | 268,073 | 754,634 | yes | clearly crooked, page corner clipped by the fill, header, key digits and item rows still easy |
| blur mild | 161,844 | 440,905 | no | slightly soft, every field and the key readable |
| blur severe | 108,952 | 279,464 | no | soft, header and key digits legible, 5 pt labels and item rows degrade but values stay readable |
| low_dpi mild | 100,735 | 278,344 | no | JPEG grain, everything readable |
| low_dpi severe | 50,808 | 135,485 | no | pixelated; header and key digits legible, the dense 40-row item table of case-003 is hardest here |
| barcode_damaged | 244,368 | 702,131 | no | three pale bands across the bars, printed key digits intact |

Natural unreadable-barcode slices therefore come from blur (both severities), low DPI (both) and barcode_damaged; rotation alone keeps the barcode readable, as RESEARCH measured.

## Task Commits

TDD, one RED and one GREEN per task:

1. Task 1 (tracer) RED: `3694da6` test(04-03): failing tests for the rotation-mild twin
2. Task 1 GREEN: `6df4415` feat(04-03): rotation-mild image-only twin with a measured barcode flag
3. Task 2 RED: `93e9aef` test(04-03): failing tests for all eight renderings and the barcode damage
4. Task 2 GREEN: `0dac2f5` feat(04-03): all eight twins, targeted barcode damage, frozen parameters

Tracer gate: end-of-phase mode with an automated-only verify, so the tracer's verify was re-run (8 passed) before expansion.

## TDD Gate Compliance

RED evidence: scan.py held signature stubs raising `NotImplementedError`, so every test collected and executed and failed on behavior (8 failures in task 1; 21 failures with 12 passing in task 2, the remaining failures being the missing renderings, `RENDERINGS`, `BARCODE_DAMAGE` and `scan_parameters`). The pytest output is not in a format the `tdd-red-evidence` classifier supports, so the semantic assessment was done by hand: the target tests ran and failed on the planned behavior, not on import or fixtures. RED precedes GREEN for both tasks; no REFACTOR commit was needed.

## Deviations from Plan

None - plan executed exactly as written. Two small implementation notes, neither a deviation: pyright needed targeted `# pyright: ignore[reportAttributeAccessIssue]` on pdfium's dynamically set `PdfObject.type` (the plan anticipated targeted ignores only where reported), and the barcode is located on the 200 DPI raster directly because it decodes there for both skeleton cases and the final-bytes check guards the outcome anyway.

**Total deviations:** 0.

## Known Stubs

None.

## Threat Flags

None. T-04-05 (leak) is mitigated by `invariant=1`, `is_image_only` and the byte grep for key and case id in every parametrized twin test; T-04-06 (reproducibility) by the per-twin RNG, uncompressed content stream and the fresh-process digest test.

## Issues Encountered

None. Verification: `just py-check` passes (707 passed, 13 deselected), plus ruff and pyright clean. No PNG or PDF was added under the repository; previews live in the session scratchpad.

## Next Phase Readiness

Ready for 04-04: the manifest generator block can embed `scan_parameters()`. Backstops still open: the end-of-phase human look at committed twins (plan 04-08) and the CI byte-identity check on ubuntu-latest (RESEARCH A2, plan 04-08).

## Self-Check: PASSED

- python/src/carimbo_datagen/scan.py and python/tests/test_scan.py exist; commits 3694da6, 6df4415, 93e9aef and 0dac2f5 are ancestors of HEAD.
