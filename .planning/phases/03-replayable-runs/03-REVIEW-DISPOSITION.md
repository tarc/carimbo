---
phase: 03
review: 03-REVIEW.md
titles: json
findings:
  - id: IN-01
    severity: info
    disposition: open
    title: "Sleep used to order spans in a unit test"
  - id: IN-02
    severity: info
    disposition: open
    title: "D-31 attribute list was not annotated as refined"
  - id: WR-01
    severity: warning
    disposition: open
    title: "A cache entry with a JSON `null` in a required member is read as a hit (confirms T-03-01 residual)"
  - id: WR-02
    severity: warning
    disposition: open
    title: "Exporter switch and startup log read only `OTEL_EXPORTER_OTLP_ENDPOINT`, but the exporter honours the traces-specific variable (confirms T-03-18 residual)"
  - id: WR-03
    severity: warning
    disposition: open
    title: "The compose loopback test checks only quoted short-syntax ports (confirms T-03-06 residual)"
  - id: WR-04
    severity: warning
    disposition: open
    title: "A cache replay is labelled with the current pricing version while its costs come from the pricing version stored in the entry"
  - id: WR-05
    severity: warning
    disposition: open
    title: "`summary.md` under-reports spend when an attempt's incurred cost is unknown"
  - id: IN-03
    severity: info
    disposition: open
    title: "`justfile` interpolates recipe arguments unquoted into bash"
open: 8
total: 8
recorded: 2026-10-15T10:41:52.354Z
---

# Phase 03: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| IN-01 | info | open | - |
| IN-02 | info | open | - |
| WR-01 | warning | open | - (not in the current review) |
| WR-02 | warning | open | - (not in the current review) |
| WR-03 | warning | open | - (not in the current review) |
| WR-04 | warning | open | - (not in the current review) |
| WR-05 | warning | open | - (not in the current review) |
| IN-03 | info | open | - (not in the current review) |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
