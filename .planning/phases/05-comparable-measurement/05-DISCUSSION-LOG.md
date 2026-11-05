# Phase 5: Comparable Measurement - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md. This log preserves the alternatives considered.

**Date:** 2026-11-05
**Phase:** 05-comparable-measurement
**Areas discussed:** Correct invoice and silent errors; Line items and failure taxonomy; Twins and intervals; Compare, live spend and cuts

---

## Correct invoice and silent errors

**What must be right for an invoice to count as correct?**

| Option | Description | Selected |
|--------|-------------|----------|
| Critical values (recommended) | Key, number, series, date, ids, invoice total, item count, item totals, installment due dates and amounts; an all-values rate beside it | |
| Every graded value | All 27 header fields, all 14 item columns, installments, no extra rows | ✓ |
| Critical header only | The critical list without per-item totals and installments | |

**Denominator of the correct-invoice rate?**

| Option | Description | Selected |
|--------|-------------|----------|
| All but infrastructure (recommended) | Refused, truncated and schema_invalid are not correct; infrastructure and harness errors leave the denominator | ✓ |
| Answered cases only | Only success, validation_failed and schema_invalid count | |

**Default numeric tolerance?**

| Option | Description | Selected |
|--------|-------------|----------|
| Exact by default (recommended) | 0.00 for every amount, quantity and rate, configurable; values off by R$0.01 or less also counted | ✓ |
| Keep ±R$0.01 | Same as today and the validators' arithmetic checks | |

**How strict is the text comparison?**

| Option | Description | Selected |
|--------|-------------|----------|
| Normalized exact (recommended) | The existing name rule; near misses get a tag | ✓ |
| Whitespace only | Case and punctuation count | |
| Fuzzy pass at 0.9 | Similarity of 0.9 or more counts as correct | |

**How is the silent-error rate defined?**

| Option | Description | Selected |
|--------|-------------|----------|
| Invoices, split by payment risk (recommended) | Success cases with a wrong value over success cases, split by whether a payment value is touched; per-field counts | ✓ |
| Invoices, no split | The same without the split | |
| Values only | Wrong values over all graded values on success cases | |

**Notes:** The user chose every graded value over the recommended critical set. Text and numeric strictness were then asked in that light.

---

## Line items and failure taxonomy

**How are predicted rows matched to true rows?**

| Option | Description | Selected |
|--------|-------------|----------|
| scipy, pinned (recommended) | `linear_sum_assignment`, new pinned dependency behind the supply-chain review | ✓ |
| Hand-written on numpy | Short Hungarian with its own tests, no new dependency | |
| Order-preserving alignment | Dynamic programming; doesn't meet EVAL-03 as written | |

**How does the taxonomy tag misses?**

| Option | Description | Selected |
|--------|-------------|----------|
| One tag per wrong value (recommended) | Ordered first-match rules; counts add up; an untagged bucket | ✓ |
| Several tags per value | Every matching rule adds a tag | |
| One tag per invoice | The dominant failure for each failed invoice | |

**Where does the tag list come from?**

| Option | Description | Selected |
|--------|-------------|----------|
| Draft now, freeze after dev (recommended) | Start from a draft list, extend from a live dev run, freeze before test | ✓ |
| Fixed list, written now | Set during planning, never changes in this milestone | |
| Only what the data shows | Rules written only after real failures | |

**Notes:** On moving to the next area the user accepted three stated defaults: row order never affects a pass, match scoring and thresholds are a planning detail, and installments use the same matching.

---

## Twins and intervals

**What is the headline for a run?**

| Option | Description | Selected |
|--------|-------------|----------|
| Two rows, text is primary (recommended) | Text-layer cases on test are primary; image-only is a second row; a pooled row isn't the headline | ✓ |
| One pooled number | All 100 test cases with a twin-aware interval | |

**How do intervals handle rows with twins?**

| Option | Description | Selected |
|--------|-------------|----------|
| Wilson on distinct invoices (recommended) | n is the distinct invoices in the row; a seeded bootstrap only for pooled row-level metrics | ✓ |
| Bootstrap over invoices | Seeded percentile bootstrap for every row with twins | |
| Counts only for twin rows | No interval on those rows | |

**Which slices are reported?**

| Option | Description | Selected |
|--------|-------------|----------|
| Declared list (recommended) | About a dozen slices fixed in code, computed for test, dev and full | ✓ |
| Every manifest tag | One row per tag, automatically | |

**Notes:** On moving on the user accepted two stated defaults: latency p50 and p95 come from the original model latency, and the degradation curve is the summary's rendering rows, not a chart.

---

## Compare, live spend and cuts

**Should Phase 5 pay for a live run?**

| Option | Description | Selected |
|--------|-------------|----------|
| One dev split run (recommended) | 54 cases, about US$1.2 to 2.2, one recipe capped above US$1 | ✓ |
| CI subset only | 20 cases, about US$0.5 to 0.9, within the existing cap | |
| Offline only | No spend; the taxonomy first meets real failures in Phase 6 | |
| One full-dataset run | 154 cases, about US$3.5 to 6, runs test before the freeze | |

**How does compare tell a real change from noise?**

| Option | Description | Selected |
|--------|-------------|----------|
| McNemar on primary only (recommended) | Exact test on the correct-invoice flag over text-layer cases; one slice of MEAS-03 moves in | ✓ |
| Deltas only, MEAS-03 stays v2 | Deltas plus each run's Wilson intervals | |
| Paired bootstrap on all deltas | Seeded paired intervals for every field and slice | |

**What makes a case regressed?**

| Option | Description | Selected |
|--------|-------------|----------|
| Pass flips, plus worsened (recommended) | Regressed, worsened and improved lists; infrastructure failures are not comparable | ✓ |
| Pass flips only | Only correct to not correct | |
| Any value flip | Any graded value going from right to wrong | |

**What does compare refuse?**

| Option | Description | Selected |
|--------|-------------|----------|
| Strict, no override (recommended) | Refuse on manifest hash, grader version or tolerance; other differences are printed; intersection for case sets | ✓ |
| Strict, with --force | The same plus an override flag | |
| Refuse different case sets too | Also refuse when case sets differ | |

**Which D-26 cuts stay out of Phase 5?** (multi-select)

| Option | Description | Selected |
|--------|-------------|----------|
| Compare on decision runs | Extraction runs only | ✓ |
| Slice deltas in compare | Per-field deltas and case lists only | ✓ |
| Repair ablation metrics | MEAS-01 stays deferred | ✓ |
| Replicates and noise floor | MEAS-02 stays deferred | ✓ |

**Notes:** The cost figures were labelled as rough estimates scaled from three skeleton cases.

---

## Claude's Discretion

- A correct invoice requires status `success`.
- The scope of the EVAL-04 self-tests, `compare`'s inputs and exit codes.
- Match scoring, item metrics beyond the floor, the exact tag rules.
- The field-by-slice shape, the output format, the CLI shape, and the recipe name.
- How the scripted full-dataset proof is split between `just e2e` and pytest.
- The alias or dated snapshot for the dev run, decision numbering, and version bumps.

## Deferred Ideas

- A payment-values pass rate beside the primary metric.
- Slice deltas in `compare`, `compare` on decision runs.
- MEAS-01, MEAS-02 and the rest of MEAS-03.
- Thresholds, the gate, charts, the model pair and the budget for test and full runs (Phase 6).
- The dc-004 recipient-mismatch question from Phase 3.1.
