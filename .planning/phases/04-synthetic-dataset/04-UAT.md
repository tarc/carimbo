---
status: testing
phase: 04-synthetic-dataset
source: [04-VERIFICATION.md]
started: 2026-10-29T16:45:00Z
updated: 2026-10-29T16:45:00Z
---

## Current Test

number: 1
name: Twin legibility across the eight renderings of one dev curve base
expected: |
  Open the eight committed twins of one dev curve base (for example nfe-0001, whose twins are
  the cases in data/dataset/manifest.json with base_case nfe-0001 and rendering other than text)
  and compare mild with severe. Mild renderings are lightly degraded; severe ones are hard but
  still human-readable (header fields and the printed access-key digits legible, including on
  barcode_damaged).
awaiting: user response

## Tests

### 1. Twin legibility across the eight renderings of one dev curve base
expected: Mild renderings are lightly degraded; severe ones are hard but still human-readable (header fields and the printed access-key digits legible, including on barcode_damaged). Scan DPI is 220 (raised from 200 in plan 04-07, recorded in D-40).
result: [pending]

### 2. Renderer fidelity against a reference DANFE layout
expected: Open data/dataset/nfe-0001.pdf next to a reference DANFE layout (BrazilFiscalReport sample output, or an nfelib sample XML rendered into a scratch directory outside the repo, never committed, D-14). Same blocks, labels, item columns and barcode position.
result: [pending]

### 3. CI byte identity of `just dataset-check` on ubuntu-latest
expected: Push the phase branch, open the PR, and read the CI python job's `just dataset-check` step. Green (byte identity on ubuntu-latest, RESEARCH A2) and duration at most about 180 s (locally about 52 s); otherwise follow CONTEXT D-13 (move dataset-check to a path-filtered workflow, take it out of the default `just check`, record a DECISIONS entry).
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
