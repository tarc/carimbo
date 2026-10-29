---
phase: 04-synthetic-dataset
reviewed: 2026-10-29T00:00:00Z
depth: standard
files_reviewed: 16
files_reviewed_list:
  - AGENTS.md
  - README.md
  - data/dataset/manifest.json
  - data/dataset/nfe-0095.pdf
  - data/dataset/nfe-0103.pdf
  - data/dataset/nfe-0111.pdf
  - data/dataset/nfe-0119.pdf
  - data/dataset/nfe-0127.pdf
  - data/dataset/nfe-0135.pdf
  - data/dataset/nfe-0143.pdf
  - data/dataset/nfe-0151.pdf
  - docs/DECISIONS.md
  - justfile
  - python/src/carimbo_datagen/scan.py
  - python/tests/test_dataset_manifest.py
  - python/tests/test_scan.py
findings:
  critical: 0
  warning: 4
  info: 8
  total: 12
status: issues_found
---

# Phase 4: Code Review Report

**Reviewed:** 2026-10-29
**Depth:** standard
**Files Reviewed:** 16
**Status:** issues_found

**New in this run (incremental re-review of gap closure plans 04-10, 04-11, 04-12, diff base a249ac5):** IN-07 and IN-08. WR-01 to WR-04 and IN-01 to IN-06 are carried forward verbatim from the review at a249ac5 and are not re-assessed here, except that IN-06 was re-checked against the regenerated manifest (see below).

## Summary

This run reviewed only what changed since a249ac5: the `SOURCE_DATE_EPOCH` pin in `scan.py` (plan 04-10), the blur severe sigma change 2.4 to 1.8 with the eight regenerated twins and manifest (plan 04-11), D-41, the README paragraph and the `D-18 to D-41` range in AGENTS.md, README.md and the justfile (plan 04-12), and the new tests.

I found no correctness bug, no security issue, and no inconsistency between the committed data and its manifest. Verified by running things:

- **Manifest vs bytes.** `sha256sum` equivalent (Python `hashlib`) over all 154 `pdf_sha256` entries in `data/dataset/manifest.json` matches the committed PDFs. 0 mismatches. 309 files in `data/dataset`.
- **Manifest diff is minimal.** `git diff a249ac5 HEAD -- data/dataset/manifest.json` touches exactly eight `pdf_sha256` lines (nfe-0095, 0103, 0111, 0119, 0127, 0135, 0143, 0151) and `generator.scan.degradation.blur.severe` (2.4 to 1.8). `git diff --stat -- data` shows only those eight PDFs and the manifest. Commit e84a9a9 touched the same nine data files plus `scan.py`.
- **Numbers in D-41 and the README.** The eight twins sum to 1,208,192 bytes before and 1,402,587 after (delta 194,395); `du -sb data/dataset` is 19,562,414 (was 19,368,019, delta 194,395); headroom to the cap is 437,586. All match D-41 and README. 1.8 px at 220 DPI is 0.208 mm, as stated. The eight blur-severe twins keep `barcode_readable: false` with the `barcode_unreadable` tag, and 44 of 154 cases are unreadable, as D-41 claims.
- **Pin mechanism.** In reportlab 5.0.1, `TimeStamp.__init__` (`reportlab/lib/utils.py:1291`) reads `SOURCE_DATE_EPOCH` at `Canvas` construction (`pdfdoc.py:139`) and again at `PDFInfo` (`pdfdoc.py:1758`), both inside the `with _pinned_source_date_epoch()` block in `write_image_only_pdf`, which also contains `pdf.save()`. The pin therefore covers every read. `PDF_EPOCH = 315532800` is 1980-01-01T00:00:00Z, which matches `D:19800101000000` asserted in `test_scan.py`.
- **Tests.** `env -u SOURCE_DATE_EPOCH pytest python/tests/test_scan.py python/tests/test_dataset_manifest.py` passes: 274 passed, 1 warning (the pre-existing size-budget warning, now quoting 19,562,414 bytes). The new subprocess test covers the unset case and a foreign value; the in-process tests cover unset, foreign and the devenv value, and restoration on both the normal path and the exception path.
- **Docs gates.** `D-41` has the `## D-41 ` heading that `just docs-check` greps for; the range `D-18 to D-41` is consistent across AGENTS.md, README.md and the justfile. No non-planning file outside D-40 still cites 2.4, 19,368,019 or 631,981 (D-40 keeps them, with a `Superseded in part by: D-41` pointer and an explicit list in D-41 of which D-40 statements are superseded, per the supersede-not-rewrite convention).
- **Re-check of IN-06.** The regenerated manifest still has all eight blur-severe twins unreadable, so IN-06 stands unchanged for blur severe.

The two new items are minor. The substantive residual risk is not in the code: the 1.8 choice rests on one reader and one invoice (IN-07), and the byte-identity claim on the GitHub runner is local evidence only until the next CI run, as D-41 itself says.

## Warnings

No new warnings in this run. WR-01 to WR-04 are carried forward below.

## Info

### IN-07: (new) The blur severe sigma of 1.8 is validated by one reader on one invoice, and the reader transcribed mild (1.2) just as correctly

**File:** `python/src/carimbo_datagen/scan.py:44-52` and `docs/DECISIONS.md` D-41, first bullet (entry at lines 913-932)
**Issue:** CONTEXT D-08 asks for severe renderings that are "hard but still readable". D-41 records that at 1.8 the user transcribed all three items of nfe-0095 correctly, exactly as at 1.6 and at the mild 1.2; the only discriminating observation is that 2.0 produced one wrong digit and 2.4 was unreadable. The chosen point is therefore the sigma just below the first observed failure, 1.5 times mild, on a single invoice and a single reader. D-41 states the limits honestly (the nfe-0127 and access-key cross-check was not performed; UAT re-test 1 re-reads nfe-0095 at phase end), so this is not a documentation error. The risk is downstream: Phase 5 may find blur mild and blur severe nearly indistinguishable in model accuracy (n = 8 each), or find nfe-0127, 0103 and others (denser item tables, longer text) still unreadable at 1.8, since the transcription covered only the first three rows of one low-density case. The one-line comment in `scan.py` ("the user chose 1.8 on native-resolution previews") also gives no pointer to D-41 for the evidence.
**Fix:** No code change required. Before Phase 5 cites blur severe as a hard-but-readable point, run the deferred cross-check on a second blur-severe twin (a long-item case such as nfe-0143, which is the largest at 226,541 bytes), and add the pointer to the `scan.py` comment: `# ... (CONTEXT D-08; evidence and rejected values in docs/DECISIONS.md D-41)`.

### IN-08: (new) `_pinned_source_date_epoch` mutates process-global state; it is not thread-safe and its restore can raise

**File:** `python/src/carimbo_datagen/scan.py:104-114`
**Issue:** The context manager rewrites `os.environ["SOURCE_DATE_EPOCH"]` for the duration of the PDF write. Today the generator is single-threaded (no `Thread`/`Pool` use in `carimbo_datagen`), so the pin is correct and the tests prove the restore on both normal and exception paths. If the dataset build is ever parallelised with threads, two twins writing concurrently can interleave: thread B reads `previous == "315532800"` (set by A) and A's exit then deletes or restores the wrong value, or a concurrent unrelated reader (for example a text-PDF writer in another thread) sees the pin. Also, if the block ever clears the variable itself, `del os.environ[...]` in `finally` raises `KeyError` and masks the original exception. The decision in D-41 to rely on the env var rather than overwrite reportlab's timestamp is sound (a reportlab bump cannot silently change a private recipe), so the alternative is not to change approach but to bound it.
**Fix:** Document the single-thread assumption in the docstring, and make the restore tolerant: `os.environ.pop("SOURCE_DATE_EPOCH", None)` in the `previous is None` branch. If threading is ever introduced, guard the whole `write_image_only_pdf` body with a module-level `threading.Lock`.

## Carried forward from the review at a249ac5 (plans 04-01 to 04-09)

Copied verbatim, ids unchanged.

## Warnings

### WR-01: `dataset-build --out DIR` deletes every unlisted `.xml` and `.pdf` in DIR

**File:** `python/src/carimbo_datagen/dataset.py:221-224` (exposed by `python/src/carimbo_datagen/cli.py:215`)
**Issue:** After writing the manifest, `build_dataset_files` unlinks every `*.xml` / `*.pdf` in `out_dir` that is not in the manifest. `--out` is a free option on `dataset-build`. Running `carimbo-datagen dataset-build --out data/skeleton` (or any directory holding other XML/PDF files) silently deletes the published skeleton cases. `data/skeleton` is the directory whose bytes the published runs and `data/decision` are keyed to. The same hazard applies to a typo'd or relative `--out`. The cleanup is also not atomic with the build. If `select_ci_subset` raises `ValueError` for a non-default `--seed`, the XML and PDF files are already written and no manifest exists, so the directory is left half-built.
**Fix:** Restrict deletion to the dataset's own file pattern and refuse a non-empty foreign directory:
```python
_CASE_FILE = re.compile(r"^nfe-\d{4}\.(xml|pdf)$")
...
for path in sorted(out_dir.iterdir()):
    if _CASE_FILE.fullmatch(path.name) and path.name not in listed:
        path.unlink()
```
Also guard at the start: raise `ValueError` if `out_dir` contains any `.xml`/`.pdf` that does not match `_CASE_FILE`. Compute the manifest, including `select_ci_subset`, before the files are written, or build into a temp directory and move it into place.

### WR-02: `test_dataset_text_equals_xml` does not prove "every graded string is printed" (D-05)

**File:** `python/tests/test_dataset_text_equals_xml.py:82-117` (claim in `docs/DECISIONS.md` D-40, "Printed text and renderer fidelity")
**Issue:** Two weaknesses leave the D-05 guarantee ("ground truth always equals what is printed") unproven for much of the graded surface.

1. **Coverage.** The checked fields are names, natOp, item code/description/NCM/unit/quantity/price/total, vNF and the access key. The schema also grades `number`, `series`, `issue_date`, issuer CNPJ/IE/UF, recipient tax id/IE/UF, item `cst_csosn`, `cfop`, `icms_*`, `ipi_*`, and the totals `icms_*`, `icms_st_*`, `freight`, `discount`, `ipi_amount` and `products_total`. They are not checked, and neither are the installments. D-40 states that `totals.discount` and `totals.freight` are graded because the DANFE prints them, yet nothing verifies that the printed text contains them. A renderer that dropped or truncated one of these would pass.
2. **Weak oracle.** Each assertion is `strip(value) in printed`, a substring test over the whole document with all whitespace removed. Short values trivially match elsewhere on the page: units such as `M`, `UN`, `PAR`, quantities such as `1,0000`, and NCM fragments. Item N's unit, quantity or price can be missing or truncated and the test still passes because another row or label contains the same characters.

**Fix:** Extend `_graded_strings` with the missing fields, formatted with Decimal, and the CNPJ/CPF and date masks as printed. For per-item fields, assert on the item's own printed row, for example `f"{ncm} 0{cst} {cfop}"` as `test_tax_groups` already does. Alternatively, narrow the D-40 and test docstring claims to "the strings listed".

### WR-03: Module docstring claims lazy import, but `cli.py` imports the whole scan stack at import time

**File:** `python/src/carimbo_datagen/cli.py:3-4, 30`
**Issue:** The docstring says `carimbo_datagen.dataset` is "imported lazily so the skeleton commands never load the scan stack". But line 30, `from carimbo_datagen.quota import DATASET_SEED`, imports `quota`, which at module level does `from carimbo_datagen.scan import RENDERINGS`. That pulls in numpy, pypdfium2, zxingcpp, PIL and reportlab. So every `carimbo-datagen build`/`check` invocation (including `just datagen-check`) loads the full stack, and the stated isolation does not exist. The comment misleads anyone reasoning about import cost or optional dependencies.
**Fix:** Either import lazily inside `dataset_build`/`dataset_check` (read `DATASET_SEED` there, or move the constant to a light module such as `ids.py`), or delete the claim from the docstring.

### WR-04: Twin tags inherit `barcode_unreadable` from the base, so the tag can contradict the twin's own flag

**File:** `python/src/carimbo_datagen/dataset.py:187-201, 228-257`
**Issue:** A twin's tag set starts from `base_entry["tags"]`, which already includes `barcode_unreadable` if the base's text PDF failed to decode. `_entry` only ever adds that tag, never removes it. If the base were unreadable but its `image_clean` twin readable, the twin would carry `barcode_unreadable` with `barcode_readable: true`. This is latent today: all 90 text bases decode. D-09 makes the flag per case ("measured by zxing-cpp on the rendered page"). A future DPI or renderer change would hit it. `test_barcode_unreadable_is_tagged_exactly_when_the_flag_is_false` would catch it only after the fact.
**Fix:** Strip the inherited tag before recomputing:
```python
tags = {t for t in base_entry["tags"] if t not in ("text_layer", "barcode_unreadable")} | {...}
```
and have `_entry` set or clear `barcode_unreadable` from `readable`.

## Info

### IN-01: `dataset_check` does not handle generation `ValueError`, unlike `dataset_build`

**File:** `python/src/carimbo_datagen/cli.py:233-248` vs `220-224`
**Issue:** `dataset_build` turns a `ValueError` (a page-count mismatch from `_check_pages`, "no dev case matches CI cell", "barcode_damaged twin still decodes") into `error: ...` plus exit 2. `dataset_check` lets the same error escape as a traceback. In CI the exit code is nonzero either way, but the message is much harder to read.
**Fix:** Wrap `dataset.check_dataset(seed, directory)` in the same `try/except ValueError` and exit 2.

### IN-02: `dataset.py` reaches into `cli.py` privates and duplicates constants

**File:** `python/src/carimbo_datagen/dataset.py:23-31, 49-62`
**Issue:**
- It imports `_PYTHON_MAJOR_MINOR`, `_compare_directories`, `_expected`, `_render_pdf_atomic`, `_sha256` and `_write_atomic` from `cli`. The shared helpers belong in a small non-CLI module.
- `_GENERATOR_PACKAGES` duplicates `cli._GENERATOR_PACKAGES` plus extras.
- `_ST_CODES` duplicates `dataset_spec._ST_CODES`. If one changes, the `st_deferral` tag drifts from the codes actually generated, and the .NET `Only_st_deferral_cases_carry_...` test keys on that tag.
- `_length_tag` hard-codes 8 and 25, which duplicate the quota item bands.

**Fix:** Move the helpers, the package list and the ST code set into one module (for example `common.py`). Derive `_length_tag` from the band constants or the cell.

### IN-03: Garbled comment above the `QUOTA` table

**File:** `python/src/carimbo_datagen/quota.py:79-84`
**Issue:** The comment splices two thoughts mid-sentence ("Plan 04-07 fixes the eight curve slots ...: the table fixes case ids, bytes and slice sizes ..."). This is the most change-sensitive table in the repo (D-03, costly to reverse), and its explanation does not read.
**Fix:** Rewrite as two comments: one saying the slots are fixed by plans 04-05 to 04-07 and asserted literally by `test_dataset_spec.py`, and one listing the curve-slot constraints.

### IN-04: A fully discounted line item exists (nfe-0087, `vProd == vDesc == 6.16`)

**File:** `python/src/carimbo_datagen/dataset_spec.py:470-485, 522-526`
**Issue:** `_shares` caps each slot at the item's gross total, so the discount spread can zero out an item (`vProd - vDesc = 0`). It is valid arithmetic, and the validators and XSD accept it, but a 100%-discounted line is unrealistic. It is also a confusable edge for the extraction graders in Phase 5 that is not tagged as a trait. Separately, `icms_base`/`st_base` use the gross item total and ignore the per-item `vDesc` that `vNF` subtracts. The data is internally consistent and graded as printed, but it is not how SEFAZ tax bases usually work. That is worth one sentence in D-40 so a reviewer does not read it as a bug.
**Fix:** Cap each slot at, say, `gross - 1` cent (or 90% of gross), or tag the case. Document the ICMS-base-on-gross convention.

### IN-05: `rl_config.useA85 = 0` mutates reportlab global state on every call

**File:** `python/src/carimbo_datagen/scan.py:98`
**Issue:** The setting is process-wide and is never restored. Anything else in the same process that uses reportlab (tests, future tooling) silently gets non-ASCII85 output. It is harmless for the CLI today, but it is a hidden side effect inside what looks like a pure function.
**Fix:** Set it once at module import with a comment, or save and restore it around `canvas.Canvas(...)` with `try/finally`.

### IN-06: The `barcode_readable` flag is constant within four of the eight rendering slices

**File:** `python/src/carimbo_datagen/scan.py:37-41` (observed in `data/dataset/manifest.json`)
**Issue:** From the committed manifest, `blur mild`, `blur severe`, `low_dpi mild`, `low_dpi severe` and `barcode_damaged` are 8/8 unreadable, `rotation severe` is 4/8, and `image_clean` and `rotation mild` are 8/8 readable. The 1.2 px Gaussian blur at 220 DPI and the 110 DPI downsample already destroy a Code 128 whose module is about 2.3 px. So "mild" is not mild for the barcode, and `barcode_unreadable` carries no within-slice signal for those slices. That undermines the D-09 intent to separate targeted damage from incidental loss. Phase 5 should know this before it slices on the flag. It is not a defect in the byte-identical gate.
**Fix:** No code change required for Phase 4. Add one sentence to D-40 or the Phase 5 notes saying the flag is degenerate for blur and low_dpi slices, so graders do not treat it as an independent covariate there.

---

_Reviewed: 2026-10-29_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
