---
phase: 05
review: 05-REVIEW.md
titles: json
findings:
  - id: CR-01
    severity: critical
    disposition: open
    title: "A run killed before its end, or resumed on a narrower selection, grades as `incomplete: false`"
  - id: WR-01
    severity: warning
    disposition: open
    title: "`compare` lists a case that degraded to refused or truncated as \"improved\" (and the reverse as \"worsened\")"
  - id: WR-02
    severity: warning
    disposition: open
    title: "`compare` raises raw tracebacks on structurally odd summaries, contradicting its stated contract"
  - id: WR-03
    severity: warning
    disposition: open
    title: "`summary.cache.hit_rate` rounds with a different rule than every other rate"
  - id: WR-04
    severity: warning
    disposition: open
    title: "`run_cases --resume` truncates the torn tail before it checks the overrides, so \"nothing has been written when this raises\" is false"
  - id: IN-01
    severity: info
    disposition: open
    title: "Stale comment says per-item grading is future work"
  - id: IN-02
    severity: info
    disposition: open
    title: "`primary_metric.headline_row` points at a row a dev-only summary does not have"
  - id: IN-03
    severity: info
    disposition: open
    title: "Protection of the `test` split is by recipe absence only"
  - id: IN-04
    severity: info
    disposition: open
    title: "Replay summaries print \"unpriced\" and fold cache misses into infrastructure failures"
  - id: IN-05
    severity: info
    disposition: open
    title: "`grade` leaks tracebacks for malformed run files and builds a path from the record's `case_id`"
  - id: IN-06
    severity: info
    disposition: open
    title: "`compare` does not surface that either run is incomplete"
  - id: IN-07
    severity: info
    disposition: open
    title: "One third of the wrong values in the published dev run are `untagged` (observation only, tags frozen under D-48)"
  - id: IN-08
    severity: info
    disposition: open
    title: "Impossible-state guard: re-draw loop for an all-zero trace and span id"
open: 13
total: 13
recorded: 2026-11-06T03:18:03.827Z
---

# Phase 05: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| CR-01 | critical | open | - |
| WR-01 | warning | open | - |
| WR-02 | warning | open | - |
| WR-03 | warning | open | - |
| WR-04 | warning | open | - |
| IN-01 | info | open | - |
| IN-02 | info | open | - |
| IN-03 | info | open | - |
| IN-04 | info | open | - |
| IN-05 | info | open | - |
| IN-06 | info | open | - |
| IN-07 | info | open | - |
| IN-08 | info | open | - |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
