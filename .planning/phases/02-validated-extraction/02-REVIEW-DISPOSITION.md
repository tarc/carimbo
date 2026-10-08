---
phase: 02
review: 02-REVIEW.md
titles: json
findings:
  - id: CR-01
    severity: critical
    disposition: open
    title: "A `null` element in `items` or `installments` passes parsing and crashes the validator, so the endpoint returns 500"
  - id: WR-01
    severity: warning
    disposition: open
    title: "`Wire.Options` accepts integer, numeric-string and any-case enum values that the schema forbids"
  - id: WR-02
    severity: warning
    disposition: open
    title: "Foreign recipients (`UF = \"EX\"`) are reported as an error, which drives a pointless repair"
  - id: WR-03
    severity: warning
    disposition: open
    title: "The runner's 900 s read timeout cannot cover the configured repair chain"
  - id: WR-04
    severity: warning
    disposition: open
    title: "Infrastructure failures with no answered attempt are counted as \"unpriced\" and consume cost-cap reserve"
  - id: WR-05
    severity: warning
    disposition: open
    title: "The cost-accounting decorator never disposes the inner gateway"
  - id: WR-06
    severity: warning
    disposition: open
    title: "`schema-check` cannot detect new or staged artifacts"
  - id: IN-01
    severity: info
    disposition: open
    title: "`RepairFeedback.SafeValue` accepts a trailing newline"
  - id: IN-02
    severity: info
    disposition: open
    title: "Python ground-truth reader does not mirror the .NET XML hardening"
  - id: IN-03
    severity: info
    disposition: open
    title: "Recipe arguments are interpolated unquoted into shell text"
  - id: IN-04
    severity: info
    disposition: open
    title: "Case ids from `cases.jsonl` are joined into a path without validation"
open: 11
total: 11
recorded: 2026-10-08T18:48:37.726Z
---

# Phase 02: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| CR-01 | critical | open | - |
| WR-01 | warning | open | - |
| WR-02 | warning | open | - |
| WR-03 | warning | open | - |
| WR-04 | warning | open | - |
| WR-05 | warning | open | - |
| WR-06 | warning | open | - |
| IN-01 | info | open | - |
| IN-02 | info | open | - |
| IN-03 | info | open | - |
| IN-04 | info | open | - |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
