---
phase: 05-comparable-measurement
plan: 12
subsystem: evals
tags: [failure-tags, untagged-bucket, dev-run-review, d-12, d-47, blocking-human-decision]

requires:
  - phase: 05-comparable-measurement
    provides: the live dev run dev-split-20261106T001800Z and its replay (05-11), the tag rules and their guards (05-04), grader-003 summaries (05-05 to 05-08)
provides:
  - the Task 1 failure report below (tag counts, the untagged bucket, wrap-hazard concentration, candidate rules, recommendation)
affects: [05-13, phase-06]

key-decisions: []

status: blocked
blocked_reason: "Task 2 is a gate=blocking-human decision (freeze-as-is or extend). Task 3 has not run. This file stays blocked until Task 3 completes it."
---

# Phase 5 Plan 12: the tag list reviewed against the real failures of the dev run (Task 1 report, plan not complete)

**Status: blocked at Task 2.** Task 1 (the read-only report) is done. The human decision (Task 2) and its application (Task 3) are not. Nothing under `python/` or `evals/results` changed.

## Inputs and checks

- **LIVE_RUN** `dev-split-20261106T001800Z` (the measure; `evals/results/dev-split-20261106T001800Z/summary.json`, `grader-003`, tolerance 0.00, `failure-tags-1`), **REPLAY_RUN** `dev-split-20261106T021526Z` (not used for counts; it replays the same 51 answers).
- Verify 1: `report inputs ok ['dev-split-20261106T001800Z', 'dev-split-20261106T021526Z']`.
- Verify 2: `no test-split model answer 100`. No run directory under `evals/results` or `evals/runs`, and no `.cache/llm` entry, holds a model answer for a test case. The review was made without the holdout seen.
- Reproduction: regrading the 51 stored records of the live run with the current grader reproduces the published `wrong` lists exactly (827 wrong values, identical tags). All counts below come from the published `summary.json` (per case `wrong`, `failure_tags`, `render_hazards`, `case_tags`, `rendering`, `base_case`); the candidate simulations (sections 3 to 5) are scratch regrades with extra predicates patched in memory, no repository file touched.

## 0. Read this first: the run is incomplete, and what that limits

The cost cap (US$2.50) stopped the live run after **51 of the 54 dev cases** (`stopped_reason` `cost_cap`, `incomplete` true, `cases_not_run` 3, spent US$2.30699480). The three unrun cases are **nfe-0144 (low_dpi/mild), nfe-0145 (low_dpi/severe) and nfe-0146 (barcode_damaged)**, all image-only twins of invoice nfe-0064. The replay records them as `cache_miss` infrastructure failures and excludes them.

What this limits:
- **Complete:** the text-layer dev row (30 of 30 cases, 30 distinct invoices) and the invoice-level figures (all 30 dev invoices are covered).
- **Short by 3 cases:** everything about image-only cases: the image row (21 of 24 cases, 3 invoices), the image share of tag counts, the three renderings low_dpi/mild, low_dpi/severe and barcode_damaged (2 cases each instead of 3), and the count of wrong values on nfe-0064 (338 here; its 8 twins are 5 here). The tag list review is not affected in kind (every tag that appears on the image rows appears on all three image invoices that did run), but absolute image counts would be higher with the 3 missing cases.
- The dataset has **30 distinct invoices in the 51 run cases**: 30 text cases plus 21 image cases that are renderings of three of those same invoices (nfe-0001: 8 twins, nfe-0048: 8, nfe-0064: 5). Counts over image rows are not independent.

## 1. Status counts, `incomplete`, headline.dev

| | |
|---|---|
| Cases run | 51 (success 16, validation_failed 35; refused 0, truncated 0, schema_invalid 0, infrastructure_failure 0, harness_error 0) |
| `incomplete` | true (3 selected cases not run, stopped: cost_cap) |
| Model | claude-haiku-4-5-20251001 only; 125 answered attempts, 0 cache hits (live run) |
| Wrong values | 827 over 51 cases; 4 in success cases (silent errors), 823 in validation_failed cases |
| Silent errors (dev) | 4 of 16 success cases, 0.2500 [0.1018, 0.4950]; payment 0, other 4. The 4 wrong values are recipient.name x3 and item.unit x1, and all four carry a tag (text_wrong 2, text_near_miss 2) |

| headline.dev | cases | invoices | excluded | correct | rate | 95% interval |
|---|---|---|---|---|---|---|
| text | 30 | 30 | 0 | 12 | 0.4000 | [0.2459, 0.5768] |
| image | 21 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| pooled | 51 | 30 | 0 | 12 | 0.2353 | [0.1193, 0.4114] |

Wrong values by rendering: **text 67 values in 18 cases; image 760 values in 21 cases** (nfe-0064 twins 338, nfe-0048 twins 254, nfe-0001 twins 168). The image rows are 92% of all wrong values and come from three invoices.

## 2. `failure_tags.counts` and `cases`, sorted by count

`failure_tags`: `version` failure-tags-1, `wrong_values` 827, `untagged` 265 (32.0%). Tag counts add up to 827.

| tag | rule | values | cases | text values | text cases | image values | invoices (base_case) |
|---|---|---|---|---|---|---|---|
| untagged | 18 | 265 | 35 | 22 | 14 | 243 | 15 |
| invented_row | 6 | 172 | 21 | 40 | 1 | 132 | 4 |
| dropped_row | 5 | 94 | 13 | 0 | 0 | 94 | 3 |
| digit_substitution | 12 | 79 | 16 | 0 | 0 | 79 | 3 |
| trailing_rows_lost | 4 | 70 | 13 | 0 | 0 | 70 | 3 |
| text_wrong | 15 | 62 | 23 | 2 | 2 | 60 | 5 |
| tax_code_wrong | 16 | 21 | 12 | 0 | 0 | 21 | 3 |
| text_near_miss | 14 | 20 | 14 | 3 | 3 | 17 | 6 |
| date_wrong | 17 | 19 | 15 | 0 | 0 | 19 | 3 |
| zero_for_blank | 10 | 10 | 4 | 0 | 0 | 10 | 1 |
| value_from_other_row | 7 | 5 | 2 | 0 | 0 | 5 | 1 |
| digit_count | 13 | 5 | 4 | 0 | 0 | 5 | 3 |
| digit_transposition | 11 | 4 | 3 | 0 | 0 | 4 | 2 |
| value_from_other_box | 8 | 1 | 1 | 0 | 0 | 1 | 1 |
| schema_invalid, party_swap, merged_rows, split_rows, thousands_misparse | 0, 1, 2, 3, 9 | 0 | 0 | 0 | 0 | 0 | 0 |

Observations:
- **The 67 text-layer wrong values are untagged 22, invented_row 40 (all nfe-0038), text_near_miss 3 (nfe-0007, 0084, 0088) and text_wrong 2 (nfe-0002, 0042).** No other tag has a text-layer value. The 18 failed text cases are 14 with an untagged value (nfe-0007 also has a near-miss name; nfe-0038 also has the invented rows) plus 4 that fail only on a tagged text value (nfe-0002, 0042, 0084, 0088).
- `invented_row` (172): 40 of them are the single text case nfe-0038 (an 80-row, multi-page invoice answered with 119 rows); 132 are image cases.
- `trailing_rows_lost`, `dropped_row`, `digit_substitution`, `tax_code_wrong`, `date_wrong`, `zero_for_blank`, `digit_count`, `digit_transposition`, `value_from_other_row` and `value_from_other_box` occur on **image cases only**, on 1 to 3 invoices.
- **Tags with count 0 in this run (proven only on seeded corruptions):** `schema_invalid`, `party_swap`, `merged_rows`, `split_rows`, `thousands_misparse`. Barely observed (1 to 5 values, 1 to 4 cases, image only): `value_from_other_box` 1, `digit_transposition` 4, `digit_count` 5, `value_from_other_row` 5.

## 3. The untagged bucket: all 265 values, then 40 of them

**Full breakdown of the 265** (the bucket is 35 cases: 14 text, 21 image):

- **By case status:** validation_failed 265, success 0. Every validation_failed case (35 of 35) holds at least one untagged value; the 16 success cases hold none.
- **By rendering:** text 22 (14 cases), image 243 (21 cases; nfe-0001 twins 8 cases, nfe-0048 twins 8, nfe-0064 twins 5).
- **By status x rendering:** validation_failed/text 22, validation_failed/image 243, success/text 0, success/image 0 (there is no success image case).
- **By kind of event:** all 265 are wrong values of a matched cell or a header field (`event` null). No missing or extra row is untagged.
- **By column** (every untagged column is a header id or an amount; none is text, date or tax code):

| column | total | text | img nfe-0001 | img nfe-0048 | img nfe-0064 | distinct image cases |
|---|---|---|---|---|---|---|
| item.total | 44 | 0 | 6 | 8 | 30 | 12 |
| access_key | 30 | 9 | 8 | 8 | 5 | 21 |
| issuer.cnpj | 29 | 8 | 8 | 8 | 5 | 21 |
| totals.products_total | 22 | 1 | 8 | 8 | 5 | 21 |
| totals.invoice_total | 21 | 1 | 7 | 8 | 5 | 20 |
| item.quantity | 21 | 0 | 2 | 3 | 16 | 7 |
| issuer.ie | 19 | 0 | 7 | 7 | 5 | 19 |
| item.unit_price | 19 | 0 | 2 | 4 | 13 | 8 |
| recipient.tax_id | 13 | 1 | 6 | 5 | 1 | 12 |
| recipient.ie | 10 | 0 | 8 | 1 | 1 | 10 |
| number | 10 | 0 | 3 | 5 | 2 | 10 |
| totals.ipi_amount | 6 | 0 | 6 | 0 | 0 | 6 |
| installment.amount | 6 | 0 | 0 | 6 | 0 | 2 |
| item.ipi_amount | 4 | 0 | 4 | 0 | 0 | 4 |
| item.ipi_rate | 3 | 0 | 3 | 0 | 0 | 3 |
| totals.icms_base | 2 | 1 | 0 | 1 | 0 | 1 |
| totals.icms_amount | 2 | 1 | 0 | 1 | 0 | 1 |
| totals.other_expenses | 2 | 0 | 2 | 0 | 0 | 2 |
| totals.discount | 2 | 0 | 2 | 0 | 0 | 2 |

- Header ids (access_key, issuer.cnpj, recipient.tax_id, issuer.ie, recipient.ie, number): **111** untagged values. Amounts (everything else): **154**.
- **Why the digit rules miss them:** digit_substitution needs exactly one differing character, digit_transposition an adjacent swap, digit_count a length difference of one. The untagged ids differ in two or more positions (text-layer examples: an access key shifted by two characters, a CNPJ whose letters were replaced by digits), are another identity entirely, or are `null` (17 values: issuer.ie 10, recipient.ie 7, all image). The untagged amounts differ in several digits, so no digit rule fits, and none equals another box or row.
- **Text layer only (22 values, 14 cases):** access_key 9, issuer.cnpj 8, recipient.tax_id 1 (18 header ids; 8 of the 14 cases carry the `alphanumeric_cnpj` or `alphanumeric_recipient` case tag), and 4 totals boxes on one case, nfe-0038.

**40 of the 265** (all 22 text-layer values, plus the first image value of each of the 18 most frequent columns; grouped by column; for the image rows pick the invoice with the lowest case id, so the sample over-represents nfe-0001's twins, nfe-0091 and others):

**access_key**
- nfe-0027 (text): expected `352602YR2HKY57000101550010008842741570241181`, predicted `35260211YR2HKY570000015500100088427415702411`
- nfe-0029 (text): expected `312601HAH0FJVP000177550080006336791932078630`, predicted `31260104HA0FJV000177550080006336791932078630`
- nfe-0033 (text): expected `312605ZUHYSNLS000106550060005449921558309759`, predicted `31260525UHYSNSLS0001065500600054499215583097`
- nfe-0037 (text): expected `332606HQ67LX26000156550050008048791746397765`, predicted `33260621HQ67LX260001565500050008048797917463`
- nfe-0057 (text): expected `35260103444151000127550050005850051008982073`, predicted `35260103444151000127550005000585005100898207`
- nfe-0060 (text): expected `312607KM3LBBA6000126550030008502551086821184`, predicted `31260700KM3LBBA60001265500300085025510868211`
- nfe-0061 (text): expected `35260708454670000190550080003484741044189004`, predicted `35260708454670000190550080003484741041890009`
- nfe-0071 (text): expected `41260714039326000137550010000588221752843016`, predicted `41260714039326000137550010000588221757284316`
- nfe-0077 (text): expected `312606OXYNBCFX000120550080002523821436312298`, predicted `31260601XYNBCFX00012055500800025238214363122`
- nfe-0091 (image): expected `43260325425810000110550060008452981340452454`, predicted `43260325429845298160006008452981304502454001`

**installment.amount**
- nfe-0126 (image, row 0): expected `17601.20`, predicted `52803.59` (the invoice total, in all three installments)

**issuer.cnpj**
- nfe-0001 (text): expected `25425810000110`, predicted `11055006000191`
- nfe-0011 (text): expected `27294124000183`, predicted `19382135000135`
- nfe-0027 (text): expected `YR2HKY57000101`, predicted `YR2HKY57000015`
- nfe-0029 (text): expected `HAH0FJVP000177`, predicted `00017755008000`
- nfe-0033 (text): expected `ZUHYSNLS000106`, predicted `25589659000105`
- nfe-0048 (text): expected `5KQEMOBP000185`, predicted `00018555001000`
- nfe-0060 (text): expected `KM3LBBA6000126`, predicted `00KM3LBBA60012`
- nfe-0077 (text): expected `OXYNBCFX000120`, predicted `OX7YNB7CFX0001`
- nfe-0091 (image): expected `25425810000110`, predicted `10773317927217`

**issuer.ie**
- nfe-0091 (image): expected `107382179217`, predicted `165.066.150.089`

**item.ipi_amount**
- nfe-0091 (image, row 0): expected `1203.73`, predicted `144.33`

**item.ipi_rate**
- nfe-0091 (image, row 0): expected `10.00`, predicted `1.20`

**item.quantity**
- nfe-0093 (image, row 0): expected `31.6096`, predicted `51.6000`

**item.total**
- nfe-0093 (image, row 0): expected `12037.26`, predicted `103.20`

**item.unit_price**
- nfe-0093 (image, row 0): expected `380.8104`, predicted `2.0000`

**number**
- nfe-0095 (image): expected `845298`, predicted `645`

**recipient.ie**
- nfe-0091 (image): expected `150898155636`, predicted `None`

**recipient.tax_id**
- nfe-0007 (text): expected `7VW4V4SF000124`, predicted `07V4V4SF000124`
- nfe-0092 (image): expected `93041999000106`, predicted `29837789000180`

**totals.discount**
- nfe-0095 (image): expected `0.00`, predicted `4181.98`

**totals.icms_amount**
- nfe-0038 (text): expected `107296.72`, predicted `158507.23`

**totals.icms_base**
- nfe-0038 (text): expected `617775.53`, predicted `913536.82`
- nfe-0129 (image): expected `0.00`, predicted `752.40`

**totals.invoice_total**
- nfe-0038 (text): expected `621539.92`, predicted `917301.21`
- nfe-0091 (image): expected `31656.60`, predicted `31440.33`

**totals.ipi_amount**
- nfe-0091 (image): expected `2181.96`, predicted `1099.19`

**totals.other_expenses**
- nfe-0091 (image): expected `0.00`, predicted `181.96`

**totals.products_total**
- nfe-0038 (text): expected `617775.53`, predicted `913536.82`
- nfe-0091 (image): expected `27933.83`, predicted `19998.77`

## 4. Concentration on the wrap hazards (D-47, Pitfall 1)

**The failures do not concentrate on the hazards. The hazards explain one wrong value in the whole run.**

- **`unit` on `PAR` rows:** 18 wrong `unit` values in the run; **1** has expected `PAR` (nfe-0042, row 0, predicted `PA`): **5.6%** of the wrong `unit` values. 15 `PAR` rows exist in the run, all in 10 text cases; 14 were read correctly. The other 17 wrong `unit` values are all on image cases, with expected `CX` (8), `UN` (4), `PCT` (3), `LT` (2): scan misreads (`UNI`, `UN.`, `PT`, `L.T`), not a wrap. None of the 21 image cases carries a `PAR` row or a number of 100000 or more (their `render_hazards` are empty).
- **`confusable` (number_wrap) against the others (text layer):** 4 confusable dev cases (nfe-0017, 0067, 0079, 0077): **3 correct**, 2 wrong values in total, both untagged (access_key and issuer.cnpj of nfe-0077). The 26 other text cases: 9 correct, 67 - 2 = 65 wrong values (untagged 20, invented_row 40, text_near_miss 3, text_wrong 2). No confusable case has a wrong `unit_price` or `total`. `thousands_misparse` and `digit_count` have zero text-layer values; `value_from_other_box` has none either.
- **Text-layer cases not correct: 18 of 30.** 7 carry a hazard (nfe-0027, 0033, 0038, 0042, 0060, 0071, 0077), 11 do not. Of the 7, **only nfe-0042 has a wrong value on a hazard cell** (the `PAR` row above). The other 6 fail on access keys, CNPJs or nfe-0038's totals and rows, none of which wraps.
- **`render_hazards` block (dev, text layer):** `any` 12 cases / 5 correct (41.7%), `none` 18 / 7 correct (38.9%), `unit_wrap` 10 / 3 correct, `number_wrap` 4 / 3 correct. Correctness is about the same with and without a hazard.
- **Conclusion:** the wrap hazards are not a rendering confound for this run's text-layer failures. The confound that is present is a different one: **the image rows** (0 of 21 correct, 760 of 827 wrong values, 3 invoices), where a wrong value can come from the scan and not from a kind of mistake a tag names. By D-47's reading, concentration on a rendering property is a reason to read failures as confounded, not a reason to add a tag.

## 5. Candidates

Threshold from the plan: at least 3 wrong values across at least 2 cases. Because image twins of one invoice are not independent, each row also states the figures with twins counted once: **invoices** = distinct `base_case`, **deduplicated values** = distinct (base_case, column, row). A candidate "still clears" if it has at least 3 deduplicated values over at least 2 invoices. All simulations are scratch regrades with the candidate placed at the stated position; "taken from" is the current tag of the values it would take.

| # | proposed tag | one-sentence rule | position in `TAG_ORDER` | values, taken from | text values / cases | image values / cases / invoices | dedup values / invoices | still clears twins as one? |
|---|---|---|---|---|---|---|---|---|
| C1 | `id_shifted` | A header id value of the same length as the expected one whose characters equal the expected's after one character is deleted at one place and another inserted at another, which no earlier digit rule took. | after `digit_count` (index 14; `text_near_miss` and later move up by one) | 5, all from `untagged` | 4 / 4 (nfe-0007 recipient.tax_id, nfe-0027 issuer.cnpj, nfe-0057 and nfe-0071 access_key) | 1 / 1 / 1 (nfe-0130 recipient.tax_id) | 5 / 5 | yes |
| C2 | `id_garbled` | A header id value (access key, CNPJ or tax id, state registration, number, series) that is not null and that no earlier rule tagged. | after `id_shifted` (or after `digit_count` if C1 is not taken) | 94 alone (89 after C1), all from `untagged` | 18 / 13 | 76 / 21 / 3 | 34 / 14 | yes (a per-kind catch-all, like `tax_code_wrong`) |
| C3 | `id_missing` | A header id value is null in the answer while the expected has one. | after `zero_for_blank` (index 11) | 17, all from `untagged` (issuer.ie 10, recipient.ie 7) | 0 / 0 | 17 / 16 / 3 | 4 / 3 | nominally yes, **image only** |
| C4 | `nonzero_for_zero` (research candidate "mirror of zero_for_blank") | A numeric wrong value that is non-zero where the expected value is exactly zero. | after `zero_for_blank` (index 11) | 6 from `untagged`; the 7th occurrence (nfe-0092) stays `value_from_other_box`, which has the lower rule number | 0 / 0 | 6 / 5 / 2 | 4 / 2 | nominally yes, **image only** |
| C5 | `name_tail_dropped` | A party name whose words are a proper prefix of the expected name's words (the last word is missing). | after `digit_count`, before `text_near_miss` | 3: 1 from `text_wrong` (nfe-0002), 2 from `text_near_miss` (nfe-0084, nfe-0088), all recipient.name, all dropping `SINTETICA` | 3 / 3 | 0 | 3 / 3 | yes, **text layer only** |
| C6 | `form_label_read` | A party name whose normalized text contains HOMOLOGACAO or RAZAO SOCIAL, or equals DANFE (a printed form caption read as the value). | after `name_tail_dropped`, before `text_near_miss` | 13, all from `text_wrong` (recipient.name) | 0 / 0 | 13 / 13 / 3 | 3 / 3 | nominally yes, **image only**, and the string list is fitted to this generator's own watermark |
| C7 | `date_day_month_swap` (research candidate) | A wrong date whose day and month are the expected's month and day. | after `text_wrong` | 3, from `date_wrong` (nfe-0140, 0141, 0142: expected 2026-01-07, predicted 2026-07-01) | 0 / 0 | 3 / 3 / 1 | 1 / 1 | **no** (one invoice) |
| C8 | `amount_garbled` | A numeric wrong value no earlier rule took. | after `digit_count` | 154, all from `untagged` | 4 / 1 (nfe-0038) | 150 / 21 / 3 | 78 / 4 | not proposed: a pure catch-all that empties the bucket by renaming it, and its text-layer evidence is one case |

Seeded corruptions for C1 to C6 (each applies on invoices where the field is present; applicable counts to be measured in Task 3): C1 delete one character of `access_key` and insert a different one at a non-adjacent position; C2 replace three non-adjacent characters of `access_key` with others; C3 set `recipient.ie` or `issuer.ie` to `null` where it is non-empty; C4 set a zero `totals.discount`, `freight`, `insurance` or `other_expenses` to an amount equal to no other box; C5 drop the last word of `recipient.name` where it has two or more; C6 set `recipient.name` to `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL`.

**The two research candidates against the data:**
- **Day and month swap in dates (Pitfall 6):** occurs, **3 wrong values in 3 cases, all in one invoice** (nfe-0064's twins nfe-0140, 0141, 0142, issue_date 2026-01-07 read as 2026-07-01). Zero in the text layer (nfe-0064's text case is correct). The other 16 `date_wrong` values are not swaps (a different year, month or day). It does **not** clear the threshold when the twins count once.
- **Expected zero with a predicted value (mirror of `zero_for_blank`):** occurs, **7 wrong values in 6 cases, 2 invoices** (`totals.discount` 2, `totals.other_expenses` 2, `totals.icms_base` 1, `totals.icms_amount` 1, and one value that `value_from_other_box` already takes), **all image**. It clears the threshold with twins counted once (4 deduplicated values, 2 invoices) on image evidence only.

**Merging or splitting the draft tags (CONTEXT leaves it to this review):** none is indicated by the data. The tags that exist split cleanly (no value needed to sit between two), and the tags with the highest counts are row events, not value tags.

## 6. Guards from plan 05-04 and the zero-count tags

- **Did a guard exclude any dev value?** **No.** Re-running the three predicates without their guards (merged_rows and split_rows without the non-zero test on the absorbed or extra row's value, party_swap without the requirement that the two expected party values differ) over all 51 cases adds no match: zero extra cells, zero extra rows, zero extra crossover cases. The guarded predicates do not fire either, so `merged_rows`, `split_rows` and `party_swap` stay at 0 for a real reason (no merge, split or party crossover occurred), not because a guard hid one. The guards cost nothing on this run's real failures, and the seeded corruptions remain their only proof.
- **Tags with count 0:** `schema_invalid`, `party_swap`, `merged_rows`, `split_rows`, `thousands_misparse` (see section 2). With one model, one run and 30 independent invoices this says only that Haiku 4.5 on this dataset did not produce them.

## Recommendation (advice for the human; Claude does not decide the list)

**Recommended: `freeze-as-is` (failure-tags-1, grader-003).**

Why, weighing the plan's tradeoff (rules fitted on 54 dev cases against a smaller `untagged` bucket):
1. **The bucket is mostly three invoices.** 243 of the 265 untagged values (91.7%) are on image rows, which are 21 renderings of 3 invoices and 0 correct. Rules fitted there describe the scan degradation of three documents, not a kind of mistake, and the share is the same on the text layer (32.8%) as on the images (32.0%), so the image rows add noise, not a different failure mode.
2. **What is left is already visible by column.** All 265 untagged values sit in 19 columns that are header ids or amounts, and the summary already reports field accuracy per column and per slice. The big candidates (C2, C3, C8) are per-column catch-alls: they would shrink the bucket from 265 to 148 by renaming it, and a measurement-first project should not buy a smaller number that way.
3. **The text-layer evidence is small.** The only candidates with text-layer support and independent invoices are C1 (4 text values), C5 (3) and C2 (18, a catch-all). Together C1 and C5 move 8 of 827 values (1.0%) and the bucket from 265 to 260.
4. **Wrap hazards are not a reason to add anything** (section 4): they explain one wrong value, and the guards cost nothing (section 6).
5. **The change is one-way** after the test split exists (D-12), costs a version bump, regraded copies of both dev runs and rules fitted on 30 independent invoices, and Phase 6 reads `untagged` again anyway. This report, kept in the plan record, is the analysis of that bucket.

**If you choose `extend`, accept changes by name from section 5** (each has a rule, a position, the values it takes, and a seeded corruption). Smallest set with text-layer evidence: **C1 `id_shifted` + C5 `name_tail_dropped`** (8 values; untagged 265 to 260). Bucket-shrinking set: **C1, C2, C3, C4, C5** (untagged 265 to 148, 17.9% of 827; tag count 19 to 24; `untagged` moves from rule 18 to rule 23). C6 would be fitted to the generator's own watermark text and is not recommended even then; C7 and C8 are not recommended. The version bump if any is accepted: `TAGS_VERSION = "failure-tags-2"`, `GRADER_VERSION = "grader-004"`.

## Task status

| Task | State |
|---|---|
| 1. report (this file) | done; both verify commands printed their ok lines; `git status --porcelain` lists no change under `python/` or `evals/results` |
| 2. decide the final list (`gate="blocking-human"`) | **waiting for the human**: reply `freeze-as-is`, or `extend` plus the changes accepted by name from section 5. The reply goes in this file verbatim. |
| 3. apply the decision | not started |

## Deviations from Plan

None so far. The section-3 sample of 40 is a deterministic selection rule (all text-layer values, then the first image value of each of the 18 most frequent columns), stated above, because the plan asks for "up to 40" without a rule.
