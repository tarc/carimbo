# Phase 4: Synthetic Dataset - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-22
**Phase:** 04-synthetic-dataset
**Areas discussed:** Size and mix (D-26 cut), Degraded and unreadable variants, Commit vs regenerate, Splits and CI subset

---

## Size and mix (D-26 cut)

| Option | Description | Selected |
|--------|-------------|----------|
| Keep ≥150 | Generation is free; live spend is a Phase 5/6 question; make the D-26 cut elsewhere | ✓ |
| Cut to ~60–80 | Amend DATA-02 so a full live run is ~US$2–3 on Haiku | |
| 150 generated, smaller 'core' set | ≥150 generated, a ~60-case core for published live runs | |

| Option | Description | Selected |
|--------|-------------|----------|
| Fixed quota table | Committed composition table, exact counts per stratum | ✓ |
| Seeded random draws | Attributes sampled per case from weighted distributions | |
| Quota for key axes, random for the rest | Quotas only on graded slice axes | |

Hard traits (multi-select): ST / deferral codes ✓, Long names and descriptions ✓, Confusable values ✓, Mixed per-item tax codes ✓.

| Option | Description | Selected |
|--------|-------------|----------|
| Cap at what prints in full | Wrap, never truncate; test that the text layer contains every graded string | ✓ |
| Allow truncation, grade printed text | Needs a printed form in the mapping and grader | |

| Option | Description | Selected |
|--------|-------------|----------|
| No cut in Phase 4 | Offline, no spend; cuts land in Phases 5/6 | ✓ |
| Skip renderer-fidelity check | Drop the reference-sample comparison | |
| One degradation level only | One severity per type | |

**User's choice:** all recommended options; every hard-trait kind selected.
**Notes:** Per-item discounts kept conditional on validator/DANFE agreement (Claude's note, not asked).

---

## Degraded and unreadable variants

| Option | Description | Selected |
|--------|-------------|----------|
| Paired twins | Re-render a clean base invoice; `base_case` in manifest | ✓ |
| Independent invoices | Each degraded case is its own invoice | |
| Both | Some twins plus some independent | |

| Option | Description | Selected |
|--------|-------------|----------|
| Targeted damage + computed tag | Damage only the barcode; zxing-cpp `barcode_readable` on every case | ✓ |
| Emergent only | Unreadable when zxing-cpp fails after degradation | |
| Targeted damage only | Only purpose-built variants tagged | |

| Option | Description | Selected |
|--------|-------------|----------|
| Two levels per type | Mild and severe for rotation, blur, low DPI | ✓ |
| One level per type | One fixed setting | |
| Two levels + combined | Plus a realistic-scan combo | |

| Option | Description | Selected |
|--------|-------------|----------|
| Clean image-only twin: yes | Separates text-layer loss from image damage | ✓ |
| No | Compare degraded directly with text-layer original | |

| Option | Description | Selected |
|--------|-------------|----------|
| About 40% image-only | ~90 distinct invoices + ~60 image twins | ✓ |
| About 25% | ~110 + ~40 | |
| About 60% | ~60 + many twins each | |

**User's choice:** all recommended options.

---

## Commit vs regenerate

| Option | Description | Selected |
|--------|-------------|----------|
| Everything, tuned to fit | Commit all XML, PDFs, manifest; tune DPI/JPEG to ≤20 MB | ✓ |
| Text-layer + CI subset images | Rest regenerated and hash-checked | |
| Decide by measured size | Planner measures, falls back if needed | |

| Option | Description | Selected |
|--------|-------------|----------|
| Byte-identical on CI Linux x64 | Full regeneration, identical bytes; other platforms use committed files | ✓ |
| Bytes for XML/text, pixels for images | Pixel tolerance for images | |
| Research decides | Pick from evidence | |

| Option | Description | Selected |
|--------|-------------|----------|
| Image realism; DATA-06 fallback | Keep ≥150 DPI base; overflow → manifest + CI-subset images + DECISIONS entry | ✓ |
| Stay committed; trim image count | Fewer twins, more text cases | |
| Stay committed; raise the cap | Accept ~30 MB | |

| Option | Description | Selected |
|--------|-------------|----------|
| Every PR in `just check` | Move to path-filtered job if > ~2–3 min | ✓ |
| Path-filtered from the start | Only on generator/lockfile/data changes | |

**User's choice:** all recommended options.

---

## Splits and CI subset

| Option | Description | Selected |
|--------|-------------|----------|
| dev / test holdout | ~1/3 dev, ~2/3 test; twins share base split | ✓ |
| Single 'eval' split | No holdout yet | |
| dev / test 50/50 | Bigger dev set | |

| Option | Description | Selected |
|--------|-------------|----------|
| dev | Gate iterations never touch test | ✓ |
| test | Gate measures holdout | |
| Both, stratified | Half from each | |

| Option | Description | Selected |
|--------|-------------|----------|
| ~20, one per key stratum | Deterministic rule; ~US$0.70 to record on Haiku | ✓ |
| ~12, minimal coverage | Cheaper, folded slices | |
| ~30, two per stratum | Steadier, over cap on Sonnet | |

**User's choice:** all recommended options.

---

## Claude's Discretion

- Dataset directory, id scheme, master seed, version label, as-of date
- Exact quota axes and counts; degradation parameters, base DPI, JPEG quality, barcode damage method, tag names
- Generator structure, CLI and recipe names; how the ground-truth tests iterate a manifest
- Deterministic split-assignment and CI-subset selection rules

## Deferred Ideas

- Live-run budget for full-dataset runs (Phase 5/6, where D-26 cuts likely land)
- Combined realistic-scan degradation
- Grading against truncated printed text
- Barcode-first access key at intake
