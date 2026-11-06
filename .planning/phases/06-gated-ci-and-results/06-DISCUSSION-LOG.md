# Phase 6: Gated CI and Results - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-11-06
**Phase:** 06-gated-ci-and-results
**Areas discussed:** Model pair and paid budget, What the PR gate enforces, Live tier trigger and spend, Published artifacts and replay, Image-only row validity, Barcode key and dc-004

The last two areas were added by the user, in free text on the area selection.

---

## Image-only row validity

| Option | Selected |
|--------|----------|
| Keep dataset-001, add a side row excluding recipient.name | ✓ |
| Keep dataset-001, explain in the analysis | |
| Fix the generator, dataset-002 | |

**Notes:** The claim checked out on dev (21 of 21 answered image cases), but every one of those cases has at least 16 wrong values, so the notice does not explain the 0% row. Twins come from 3 invoices on dev and 5 on test.

## Barcode key and dc-004

| Question | Options | Selected |
|----------|---------|----------|
| When to build the barcode key | Baseline first; insert a phase before 6; both in Phase 6 | Baseline first |
| dc-004 recipient check | Defer with barcode; separate quick task now | Defer with barcode |

**Notes (final review):** One more test run is pre-declared for that phase, designed from dev evidence only.

## Model pair and paid budget

| Question | Options | Selected |
|----------|---------|----------|
| Published pair | Haiku 5.5 vs Sonnet 5.5; Haiku 5.5 vs Haiku 4.5; Haiku 4.5 vs Sonnet 5.5; one model, two prompts | Haiku 5.5 vs Sonnet 5.5 |
| Spend limit | One-time US$12; one-time US$15; keep US$5 | One-time US$12, with changes (below) |
| Haiku 5.5 before test | Probe then full dev; probe then 10 dev cases; probe only | Probe then full dev |
| Run settings | extract-003 repair 2; extract-003 repair 0; new prompt first | extract-003 repair 2 |

**User's choice on the cap (free text):** One-time US$12 cap as in option 1, recorded in DECISIONS, including a 10-case Sonnet 5.5 dev check (about US$0.90) before its test run, so its per-case cost is measured and the test run's cap is set from it. If the measured costs don't fit, ask before the test runs.

**Notes:** The user asked for questions one at a time after a batched call.

## What the PR gate enforces

| Question | Options | Selected |
|----------|---------|----------|
| Threshold kind | Exact baseline; floors with a margin; floors plus case-level compare | Exact baseline |
| Gated set | Per-grader and case-level; headline only; whole summary | Per-grader and case-level |
| Cache miss | Fail and name the fix; skip with a warning; fail unless a label waives it | Fail and name the fix |
| CI fixtures | Haiku 5.5 dev, keep 20 cases; Haiku 4.5 dev plus top-up; replay all 54 | Haiku 5.5 dev, keep 20 cases |

**Notes:** The third fixtures option carried an unfounded "about 60 MB" figure, corrected in the session (the 148 cached entries take 3.1 MB). It was not selected.

## Live tier trigger and spend

| Question | Options | Selected |
|----------|---------|----------|
| Trigger | Manual dispatch only; plus same-repo prompt PRs; plus nightly | Manual dispatch only |
| Scope | CI subset, Haiku 5.5, US$0.25; CI subset, US$1.00; whole dev, US$2.50 | CI subset, Haiku 5.5, US$0.25 |
| Result | Report only; fail below a floor | Report only |

## Published artifacts and replay

| Question | Options | Selected |
|----------|---------|----------|
| Reports layout | New runs in reports only; move everything; keep using results | New runs in reports only |
| Test replay | Read-only exception; reviewer types the flag; script outside just | Read-only exception |
| Same scores | Record refused and truncated outcomes; reconcile in replay; treat as failed publication | Record those outcomes |
| Fixtures | evals/fixtures per run; one flat directory; release asset | evals/fixtures per run |
| Results doc | docs/RESULTS.md plus README excerpt; README only; inside evals/reports | docs/RESULTS.md plus README excerpt |

## Final review (user's additions, all recorded in CONTEXT.md)

- One more test run is pre-declared for the barcode and dc-004 phase, designed from dev evidence only.
- A 10-case Sonnet 5.5 dev check before its test run, with each test cap set from measured cost and a stop to ask if it doesn't fit US$12.
- The Haiku 5.5 probe checks thinking and effort behavior, and the published table states each model's setting.
- The live tier key sits in a GitHub environment with the user as required reviewer, and is a dedicated carimbo-workspace key with a Console spend limit.
- Replay never passes the key, and a cache miss fails instead of calling the provider.
- Stored refused and truncated outcomes replay in read_only but aren't served to new read_write runs.
- The headline shows silent errors split into payment and other fields, and names the image-row confound and the wrap hazard.
- Declared CI exclusions are counted and shown in the CI output.
- The docs check covers numbers in prose, not only in tables.

## Claude's Discretion

Thresholds file format, recipe names, CI job layout, versioning of the side row, the prose-number mechanism, the cache entry change, pricing and default model changes, DECISIONS numbering.

## Deferred Ideas

Barcode key and dc-004 phase, prompt iteration, automatic live triggers, a live score floor, dataset-002, repair ablation.
