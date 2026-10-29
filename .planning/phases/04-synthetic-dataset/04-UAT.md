---
status: complete
phase: 04-synthetic-dataset
source: [04-VERIFICATION.md]
started: 2026-10-29T15:43:06Z
updated: 2026-10-29T20:42:58.095Z
---

## Current Test

[testing complete]

## Tests

### 1. Twin legibility across the eight renderings of one dev curve base
expected: Mild renderings are lightly degraded; severe ones are hard but still human-readable (header fields and the printed access-key digits legible, including on barcode_damaged). Scan DPI is 220 (raised from 200 in plan 04-07, recorded in D-40).
result: issue
reported: "Actually, in items_detail.png I can't read a thing on the blur severe. [...] All other are fine. Blur sever looks like: D00132 FITA ADESIVA TRANSPARENTE 39198890 041"
severity: major
note: "Reviewed against the text base on comparison sheets (whole page, issuer block, barcode and access key, item table at 150 DPI, item rows at native 220 DPI). All eight twins show the same invoice; image_clean, both rotations, blur mild, both low_dpi and barcode_damaged are fine. Access key on blur severe: the 8 in group 8452 (digit 29) reads as 5 cold; on its own that was accepted as intended severe difficulty (the mod-11 check digit detects it and only 8 satisfies it). The item table on blur severe is not: read cold at native resolution, code D00137 reads D00132 and NCM 39191010 reads 39198890 (CST 041 correct), so the severe blur turns small digits into misreads, not hard reads."

### 2. Renderer fidelity against a reference DANFE layout
expected: Open data/dataset/nfe-0001.pdf next to a reference DANFE layout (BrazilFiscalReport sample output, or an nfelib sample XML rendered into a scratch directory outside the repo, never committed, D-14). Same blocks, labels, item columns and barcode position.
result: pass
note: "Compared fidelity.png: nfe-0001.pdf next to nfelib sample leiauteNFe/42220575277525000178550030000292481295366801-procNFe.xml rendered by BrazilFiscalReport into the scratchpad (not committed). Same blocks in the same order and position (receipt stub, issuer/DANFE/barcode and access key, natureza and protocolo, destinatário, cálculo do imposto, transportador, item columns, dados adicionais); FATURA/DUPLICATAS absent only because nfe-0001 has no installments; homologation watermark and recipient-name notice are the known tpAmb 2 marking (Phase 2 G-02-1)."

### 3. CI byte identity of `just dataset-check` on ubuntu-latest
expected: Push the phase branch, open the PR, and read the CI python job's `just dataset-check` step. Green (byte identity on ubuntu-latest, RESEARCH A2) and duration at most about 180 s (locally about 52 s); otherwise follow CONTEXT D-13 (move dataset-check to a path-filtered workflow, take it out of the default `just check`, record a DECISIONS entry).
result: issue
reported: "User chose option 1 (push the branch, run CI by workflow_dispatch, no PR). CI run 40915848246 at 238032a: python job failed in `just py-check` (2 failed, 2803 passed), so `just dataset-check` was skipped. Failures: test_scan.py::test_twin_bytes_leak_neither_key_nor_case_id_and_use_dct_without_a85 (twin lacks b'D:19800101000000') and test_dataset_manifest.py::test_a_separate_process_building_cases_in_reverse_order_writes_the_committed_bytes (nfe-0154.pdf hash f1db3a70... != committed 512734e5...)."
severity: blocker

### 4. Re-test 1 - blur severe legibility at sigma 1.8 (D-41)
expected: Read cold, at native 220 DPI, the item table of the committed blur-severe twin nfe-0095 (base nfe-0001): code, NCM and quantity are hard but transcribable, with no misread like D00137 -> D00132 or 39191010 -> 39198890. Optionally also nfe-0127 (blur severe, base nfe-0048): the printed access-key digits and the 7-row item table. The other renderings judged fine in test 1 kept their bytes (plan 04-11 changed only the eight blur-severe twins).
result: pass
note: "Read cold at native 220 DPI from cold sheets (twin only, no text base). User transcribed nfe-0095: D00137 39191010 31,6096 / D00290 84818095 26,5420 / D00317 84439933 46,3900; and nfe-0127: access key 3326 055K QEMO BP00 0185 5500 1000 3965 1614 0601 0040, plus D00161 84818099 47,1893 / D00223 85061010 47,8523 / D00337 85061010 28,1880 / D00476 84716052 60,8300 / D00535 39173100 13,7900 / D00648 96081000 26,6700 / D00724 84716053 20,5106. Every code, NCM and quantity and the 44-character key equal the committed XML (cProd, NCM, qCom, infNFe Id). Committed twin hashes equal manifest.json (nfe-0095 2d8ca912..., nfe-0127 863188d5...). Also closes the access-key cross-check the re-verification left open (IN-07)."

### 5. Re-test 3 - CI byte identity on ubuntu-latest after the PDF date pin (D-41)
expected: With the user's approval, push gsd/phase-04-synthetic-dataset and run CI (workflow_dispatch, as in test 3, or a PR). The python job is green on ubuntu-latest without SOURCE_DATE_EPOCH: `just py-check` passes, including the two twin determinism tests that failed in run 40915848246, and `just dataset-check` passes in at most about 180 s (CONTEXT D-13).
result: pass
note: "User approved the push (workflow_dispatch, no PR). Pushed 238032a..43a3511; `git ls-remote` confirmed the remote head equals local HEAD 43a35111. CI run 40950655793 (https://github.com/tarc/carimbo/actions/runs/40950655793) on 43a35111: conclusion success, all three jobs green. Python job: `just py-check` 2810 passed, 13 deselected (e2e, live), 0 failed, in 59.78 s of pytest (both tests that failed in run 40915848246 are unmarked, so among the 2810); `just datagen-check` ok; `just dataset-check` printed 'ok: data/dataset matches a regeneration from seed 20261022' in 63 s (step 20:38:39Z to 20:39:42Z), under the about 180 s line of CONTEXT D-13. SOURCE_DATE_EPOCH appears nowhere in the job log. Only warning: the designed size notice (19,562,414 bytes, above the 18,000,000 warning line, under the 20,000,000 limit)."

## Summary

total: 5
passed: 3
issues: 2
pending: 0
skipped: 0
blocked: 0

## Gaps

- gap_id: G-04-1
  truth: "Severe renderings are hard but still human-readable: on blur severe the item-table digits (code, NCM, quantity) can be transcribed by a careful reader, as they can on every other rendering"
  status: resolved
  resolved_by: 04-11-PLAN.md
  resolved_at: 2026-10-29
  reason: "User reported: Actually, in items_detail.png I can't read a thing on the blur severe. [...] All other are fine. Blur sever looks like: D00132 FITA ADESIVA TRANSPARENTE 39198890 041"
  severity: major
  test: 1
  root_cause: "python/src/carimbo_datagen/scan.py DEGRADATION['blur']['severe'] = 2.4 (Gaussian sigma in pixels at SCAN_DPI 220) is strong enough to merge the strokes of the small item-table digits (code, NCM, quantity), so a careful reader misreads them (D00137 -> D00132, 39191010 -> 39198890) instead of reading them with effort. Blur mild (1.2), both rotations, both low_dpi severities and barcode_damaged were judged fine; the larger bold access-key digits survive blur severe with one 8/5 ambiguity that the mod-11 check digit catches. The sigma was frozen in plan 04-03 from previews at 200 DPI and kept in pixels when plan 04-07 raised SCAN_DPI to 220."
  artifacts:
    - path: "python/src/carimbo_datagen/scan.py"
      issue: "DEGRADATION['blur']['severe'] = 2.4 too strong for item-table digits (line 39)"
    - path: "data/dataset/nfe-0095.pdf, nfe-0103.pdf, nfe-0111.pdf, nfe-0119.pdf, nfe-0127.pdf, nfe-0135.pdf, nfe-0143.pdf, nfe-0151.pdf"
      issue: "the eight blur-severe twins (one per curve base) carry the too-strong blur"
  missing:
    - "Lower DEGRADATION['blur']['severe'] to a sigma between mild (1.2) and 2.4 at which the item-table code, NCM and quantity digits of a dev curve base can be transcribed by a careful reader (re-check with the user on nfe-0095, the nfe-0001 twin, at native 220 DPI)"
    - "Regenerate only the eight blur-severe twins (no other twin or base byte may change) before dataset-001 first merges to main, keeping the label (CONTEXT D-17 qualification), and refresh the manifest generator.scan parameters and hashes"
    - "Update the blur severe value and the dataset byte count wherever they are cited (docs/DECISIONS.md D-40, README.md Phase 4 section, plan-level tests that pin scan parameters)"

- gap_id: G-04-2
  truth: "`just dataset-check` regenerates data/dataset byte for byte on ubuntu-latest (CI), not only inside the author's devenv shell"
  status: resolved
  resolved_by: 04-10-PLAN.md
  resolved_at: 2026-10-29
  reason: "CI run 40915848246 (user-approved push + workflow_dispatch): the twin determinism tests fail on ubuntu-latest; reproduced locally with SOURCE_DATE_EPOCH unset, where dataset-check reports exactly the 64 twin PDFs nfe-0091..nfe-0154 as different (all 90 text PDFs and all XML match)"
  severity: blocker
  test: 3
  root_cause: "reportlab's TimeStamp (python/.venv/.../reportlab/lib/utils.py:1292-1298) takes the PDF date from the SOURCE_DATE_EPOCH environment variable when it is set and falls back to 946684800 (2000-01-01) in invariant mode. scan.py builds every twin with canvas.Canvas(..., invariant=1) (line 100) and never pins the date itself. The devenv shell exports SOURCE_DATE_EPOCH=315532800 (1980-01-01), so the committed twins embed D:19800101000000 and a document /ID derived from it; ubuntu-latest CI does not set the variable and writes D:20000101000000 and a different /ID. Reproduced locally: `env -u SOURCE_DATE_EPOCH carimbo-datagen dataset-check` reports exactly the 64 twin PDFs (nfe-0091..nfe-0154) and nothing else. Text PDFs are unaffected because danfe.py pins fpdf2's creation date, and the XML has no clock value."
  artifacts:
    - path: "python/src/carimbo_datagen/scan.py"
      issue: "canvas.Canvas(invariant=1) leaves the timestamp to SOURCE_DATE_EPOCH, an environment variable devenv sets and CI does not (line 100)"
    - path: "python/tests/test_scan.py, python/tests/test_dataset_manifest.py"
      issue: "the determinism tests run only in the environment the dataset was built in, so they passed locally and could not see the dependency"
  missing:
    - "Make twin bytes independent of the environment: pin the reportlab timestamp in scan.py to the committed value (D:19800101000000, epoch 315532800) regardless of SOURCE_DATE_EPOCH, so the 56 non-blur-severe twins keep their committed bytes"
    - "Add a regression test that renders a twin in a subprocess with SOURCE_DATE_EPOCH unset and with a different value and asserts the bytes equal the committed or in-process bytes"
    - "Prove it the way CI runs it: `env -u SOURCE_DATE_EPOCH just dataset-check` (or equivalent) passes locally, then CI's python job is green with `just dataset-check` at most about 180 s (re-run UAT test 3)"
