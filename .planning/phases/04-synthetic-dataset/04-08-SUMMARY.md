---
phase: 04-synthetic-dataset
plan: 08
subsystem: docs
tags: [decisions, docs-check, agents-md, readme, dataset, phase-gate]

requires:
  - phase: 04-synthetic-dataset
    provides: plans 04-01 to 04-07 (generator, quota table, scan twins, data/dataset of 154 cases, manifest, CI subset, size and wall-time measurements)
provides:
  - docs/DECISIONS.md D-40 "Synthetic dataset dataset-001" (refines D-14, discharges D-26 for Phase 4)
  - justfile docs-check over D-18 to D-40
  - AGENTS.md and README.md documenting data/dataset, just dataset and just dataset-check, plus a Synthetic dataset section in the README
  - a green offline gate (just check) on the final tree
affects: [phase 4 verification and UAT, /gsd-ship, Phase 5 comparable measurement, Phase 6 CI subset and cache fixtures]

actuals:
  tokens: 3500
  tasks: 2
  commits: 2
plan_head_before: 2e73deaa51feb6d9ae986290f2463d04d5261064
plan_head_after: 38c5abdb2f4f992e61ff977e246ec7e34f8152d9

tech-stack:
  added: []
  patterns: [every count, size and time in a decision record is read from the committed manifest, du -sb or a plan SUMMARY, never estimated]

key-files:
  created: []
  modified:
    - docs/DECISIONS.md
    - justfile
    - AGENTS.md
    - README.md

key-decisions:
  - "D-40 records the scan DPI as 220, not the 200 first frozen in plan 04-03, with the 17-DPI sweep evidence from plan 04-07 (eight text bases did not decode at 200 DPI)"
  - "D-40 numbers its phase references as CONTEXT D-nn (decisions of 04-CONTEXT.md) and says so in its first bullet, because the plan's acceptance wording uses that form while earlier entries say phase decision D-nn"
  - "README Status row for Phase 4 stays Not started and no 04-VERIFICATION.md link is written: AGENTS.md allows both only after verification marks the phase Complete"

patterns-established:
  - "The README 'Not measured yet' bullet about the dataset now says that no extraction run exists on the 154 cases, instead of claiming no dataset"

requirements-completed: [DATA-05, DATA-06]

coverage:
  - id: D1
    description: "D-40 recorded from measured facts (identity, quota, 220 DPI scan parameters, eight curve bases, twin XML byte copy amendment, per-item discount and freight rule, splits, CI subset, DATA-04 gate, 19,368,019 bytes, versioning and pre-publication churn, discharged D-26 review) before Open questions, with no earlier entry changed"
    requirement: DATA-06
    verification:
      - kind: other
        ref: "devenv shell -- just docs-check (docs ok); git show --numstat of the D-40 commit: 30 added, 0 deleted lines in docs/DECISIONS.md"
        status: pass
    human_judgment: false
  - id: D2
    description: "AGENTS.md and README.md document data/dataset, just dataset and just dataset-check, docs-check range D-18 to D-40, and AGENTS.md lists data/dataset/* as generated and never hand-edited"
    requirement: DATA-05
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py and python/tests/test_readme_drift.py inside just py-check (2805 passed)"
        status: pass
    human_judgment: false
  - id: D3
    description: "Full offline gate on the final tree: both stacks, schema-check, datagen-check, dataset-check, decision-cases-check, e2e, docs-check, secrets-check"
    requirement: DATA-06
    verification:
      - kind: other
        ref: "devenv shell -- just check exit 0 (179.9 s), ending in docs ok and secrets ok"
        status: pass
    human_judgment: false
  - id: D4
    description: "CI python job runs just dataset-check green on the pull request (byte identity on ubuntu-latest, RESEARCH A2) in about 180 seconds or less"
    requirement: DATA-06
    verification: []
    human_judgment: true
    rationale: "Needs a push and a CI run, which this plan is told not to do; closed by the first CI run on the pull request"
  - id: D5
    description: "The eight twins of one dev curve base look right (mild lightly degraded, severe hard but readable, access-key digits legible on barcode_damaged) and a committed text DANFE matches a reference DANFE layout (renderer fidelity)"
    requirement: DATA-03
    verification: []
    human_judgment: true
    rationale: "Visual judgment; collected for end-of-phase UAT, not run by the executor"

duration: 8min
completed: 2026-10-29
status: complete
---

# Phase 4 Plan 08: decision record D-40, the reviewer-facing docs and the phase gate Summary

**D-40 records dataset-001 (154 cases, 19,368,019 bytes, scan DPI 220, eight curve bases, twin XML byte copy, per-item discount and freight sums, versioning and pre-publication churn) and is enforced by docs-check; AGENTS.md and README.md document the dataset and its two recipes; `just check` is green in 179.9 s**

## Performance

- **Duration:** 8 min
- **Started:** 2026-10-29T15:16:00Z
- **Completed:** 2026-10-29T15:24:00Z
- **Tasks:** 2 (Task 1 a tracer, Task 2 auto)
- **Files modified:** 4 (docs/DECISIONS.md, justfile, AGENTS.md, README.md)

## Accomplishments

- **D-40** added after D-39 and before "Open questions", 30 added lines and 0 deleted. Measured facts it cites, all re-read from `data/dataset/manifest.json`, `du -sb` or the 04-07 SUMMARY: 90 text-layer bases (14 short at 1 to 8 items, 14 medium at 9 to 25, 7 many_items at 26 to 40, 10 multi_page per regime; 134 one-page and 20 two-page cases), trait counts on the bases (24 alphanumeric issuer, 4 alphanumeric recipient, 18 CPF, 4 MEI, 14 ST or deferral, 40 mixed codes, 18 long text, 18 confusable), 64 twins nfe-0091 to nfe-0154 on curve bases nfe-0001, 0003, 0019, 0020, 0048, 0049, 0064 and 0065, 54 dev and 100 test cases, a 20-case dev-only CI subset, 44 cases with `barcode_unreadable` (8 of them the targeted `barcode_damaged`), 309 files and 19,368,019 bytes (631,981 under the cap), the `generator.scan` object (220 DPI, JPEG 50, rotation 1 and 3.5, blur 1.2 and 2.4, low_dpi 110 and 72, 3 bands of 8 percent).
- **docs-check** now loops over D-18 to D-40 and prints `docs ok`.
- **AGENTS.md** lists `data/dataset/` and "and image-only scan twins" in the layout, the `just dataset` and `just dataset-check` rows, "D-18 to D-40", and `data/dataset/*` among the generated files regenerated by `just dataset`.
- **README.md** has the two command rows, "D-18 to D-40", `data/dataset/` in the layout block, and a new "Synthetic dataset (Phase 4)" section whose every figure is from the manifest or the 04-07 measurements. The "Not measured yet" bullet that said "No dataset at scale and no degraded scans" now says that no extraction run exists on the 154 cases yet.
- **Phase gate:** `devenv shell -- just check` exit 0 on the final tree.

## Measured wall times (all through `devenv shell`)

| Command | Wall time |
|---------|-----------|
| `just check` (every offline gate) | 179.9 s (2805 pytest passed inside py-check, 13 e2e passed) |
| `just dataset-check` | 49.5 s (50.9 s in plan 04-07) |

`just dataset` was not re-run here; D-40 quotes its 52.6 s from plan 04-07 and says so.

## Task Commits

1. **Task 1 (tracer): D-40 and docs-check** - `9e7c45c` docs(04-08)
   - Tracer gate: `<verify>` is automated-only and end-of-phase mode is active, so it was re-run after the commit (`just docs-check` printed `docs ok`) and expansion continued.
2. **Task 2: dataset documentation and the full gate** - `38c5abd` docs(04-08)

**Plan metadata:** the docs(04-08) commit that adds this file.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The plan's wording "barcode_readable ... at 200 DPI" is stale; D-40 says 220 DPI**
- **Found during:** Task 1
- **Issue:** The plan's Renderings bullet and the 04-03 material say the barcode flag is decoded at 200 DPI. Plan 04-07 changed `SCAN_DPI` and `BARCODE_DECODE_DPI` to 220 (scan.py, manifest `generator.scan`), so the planned sentence would have published a claim the files do not support (the plan's own prohibition on unsupported dataset claims).
- **Fix:** D-40 states 220 DPI and records why it is not 200 (eight text bases did not decode; the 17-DPI sweep; 220 is the centre of the 216 to 224 band). The 04-03 SUMMARY is a historical record and is left as written; its "200 DPI" mentions are corrected by D-40.
- **Files modified:** docs/DECISIONS.md
- **Commit:** 9e7c45c

**2. [Rule 1 - Bug] D-40 used "CONTEXT D-nn" for phase decisions instead of the "phase decision D-nn" wording of earlier entries**
- **Found during:** Task 1 acceptance check
- **Issue:** The plan's acceptance criterion greps for "user-approved qualification of CONTEXT D-17", while earlier DECISIONS entries write "phase decision D-nn". A first draft used the earlier form and failed that grep.
- **Fix:** D-40 uses "CONTEXT D-nn" throughout and defines it in its first bullet as the decisions of `.planning/phases/04-synthetic-dataset/04-CONTEXT.md`; a bare "D-nn" is this file.
- **Files modified:** docs/DECISIONS.md
- **Commit:** 9e7c45c

**3. [Rule 1 - Bug] Two Rejected bullets from the plan were reworded to cite the real reasons**
- **Found during:** Task 1
- **Issue:** The plan lists "sixteen bases with four renderings each" and "a combined degradation, deferred by CONTEXT" without reasons. The first attribution ("deferred by D-08") would have been wrong.
- **Fix:** The sixteen-base option cites RESEARCH open question 1 (broader content but partial curves); the combined degradation cites the phase discussion ("not chosen, two levels of one single type only", CONTEXT deferred ideas).
- **Files modified:** docs/DECISIONS.md
- **Commit:** 9e7c45c

---

**Total deviations:** 3 auto-fixed (3 Rule 1, all in the wording of D-40). **Impact on plan:** none on scope; the entry says what the files support.

## Human and CI checks collected for end-of-phase UAT

Not run, not marked done (the plan and the orchestrator forbid a push or pull request here):

1. **CI byte identity and timing.** Push the phase branch, open the pull request, confirm the CI python job's `just dataset-check` step is green on ubuntu-latest (RESEARCH A2) and note its duration. Local reference: 49.5 s. If it exceeds about 180 s, follow CONTEXT D-13 (path-filtered workflow, out of `just check`, a DECISIONS entry).
2. **Twin severity.** Open the eight twins of one dev curve base (nfe-0001, nfe-0048 or nfe-0064; for nfe-0001 they are nfe-0091 to nfe-0098) and check that mild renderings are lightly degraded, severe ones hard but readable, and the printed access-key digits legible on `barcode_damaged`.
3. **Renderer fidelity.** Compare one committed text DANFE (for example `data/dataset/nfe-0001.pdf`) side by side with a reference DANFE layout (BrazilFiscalReport's own sample output, or an nfelib sample XML rendered by the same library into a scratch directory outside the repository, never committed as data) for the same blocks, labels, item columns and barcode position.

If a severity or the layout is wrong, a fix before dataset-001 first merges to main keeps the label (CONTEXT D-17 qualification) but re-commits the affected files (about 11 MB of twins for a severity re-tune); after that merge it is a new dataset version.

## Issues Encountered

None. The size test still emits its designed 18 MB warning on each pytest run (19.37 MB).

## Known Stubs

None.

## Threat Flags

None. No network endpoint, auth path or file access was added; `just secrets-check` passed inside `just check` (T-04-20), D-40 is a pure addition enforced by docs-check (T-04-21), and no package was added (T-04-SC; `uv sync --locked` inside py-check passed).

## Next Phase Readiness

- All nine plans of Phase 4 have a SUMMARY after this one; the phase is ready for verification and verify-work. The README Status row and the `- Phase 4:` verification bullet are written only after verification marks the phase Complete (AGENTS.md).
- DATA-05 and DATA-06 are marked complete on the strength of the committed manifest, size and local byte identity; the CI byte-identity run on the pull request is the remaining backstop (human check 1).

## Self-Check: PASSED

- `docs/DECISIONS.md` (D-40), `justfile`, `AGENTS.md`, `README.md` exist and are committed; `grep '^## D-40 '` prints 1, `grep -c 'D-39 D-40' justfile` prints 1, `data/dataset` appears 4 times in each of AGENTS.md and README.md, `D-18 to D-40` once in each.
- Commits 9e7c45c and 38c5abd are ancestors of HEAD; `git rev-list --count 2e73dea..HEAD` was 2 before this SUMMARY.
- `just docs-check` prints `docs ok`; `just check` exit 0 (179.9 s); `just dataset-check` ok (49.5 s).

---
*Phase: 04-synthetic-dataset*
*Completed: 2026-10-29*
