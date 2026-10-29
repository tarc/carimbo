---
status: testing
phase: 04-synthetic-dataset
source: [04-VERIFICATION.md]
started: 2026-10-29T15:43:06Z
updated: 2026-10-29T16:49:56Z
---

## Current Test

number: 3
name: CI byte identity of `just dataset-check` on ubuntu-latest
expected: |
  Push the phase branch, open the PR, and read the CI python job's `just dataset-check` step.
  Green (byte identity on ubuntu-latest) and duration at most about 180 s.
awaiting: user response

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
result: [pending]

## Summary

total: 3
passed: 1
issues: 1
pending: 1
skipped: 0
blocked: 0

## Gaps

- gap_id: G-04-1
  truth: "Severe renderings are hard but still human-readable: on blur severe the item-table digits (code, NCM, quantity) can be transcribed by a careful reader, as they can on every other rendering"
  status: failed
  reason: "User reported: Actually, in items_detail.png I can't read a thing on the blur severe. [...] All other are fine. Blur sever looks like: D00132 FITA ADESIVA TRANSPARENTE 39198890 041"
  severity: major
  test: 1
  artifacts: []  # Filled by diagnosis
  missing: []    # Filled by diagnosis
