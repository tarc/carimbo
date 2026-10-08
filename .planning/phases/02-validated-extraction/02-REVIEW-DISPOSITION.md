---
phase: 02
review: 02-REVIEW.md
titles: json
findings:
  - id: WR-02
    severity: warning
    disposition: open
    title: "Foreign recipients (`UF = \"EX\"`) are reported as an error, which drives a pointless repair (carried forward, open)"
  - id: WR-03
    severity: warning
    disposition: open
    title: "The runner's 900 s read timeout cannot cover the configured repair chain (carried forward, open)"
  - id: WR-04
    severity: warning
    disposition: open
    title: "Infrastructure failures with no answered attempt are counted as \"unpriced\" and consume cost-cap reserve (carried forward, open)"
  - id: WR-05
    severity: warning
    disposition: open
    title: "The cost-accounting decorator never disposes the inner gateway (carried forward, open)"
  - id: WR-06
    severity: warning
    disposition: open
    title: "`schema-check` cannot detect new or staged artifacts (carried forward, open)"
  - id: IN-01
    severity: info
    disposition: open
    title: "`RepairFeedback.SafeValue` accepts a trailing newline (carried forward, open)"
  - id: IN-02
    severity: info
    disposition: open
    title: "Python ground-truth reader does not mirror the .NET XML hardening (carried forward, open)"
  - id: IN-03
    severity: info
    disposition: open
    title: "Recipe arguments are interpolated unquoted into shell text (carried forward, open)"
  - id: IN-04
    severity: info
    disposition: open
    title: "Case ids from `cases.jsonl` are joined into a path without validation (carried forward, open)"
  - id: IN-05
    severity: info
    disposition: open
    title: "The enum name table has latent hazards for aliased and flags enums (new)"
  - id: IN-06
    severity: info
    disposition: open
    title: "The `NULL_VALUE` repair sentence is unreachable through the extractor and untested (new)"
  - id: IN-07
    severity: info
    disposition: open
    title: "`Wire.Options` still accepts duplicate JSON properties, so \"accepts only what the schema allows\" is overstated (new)"
  - id: IN-08
    severity: info
    disposition: open
    title: "The exhaustive null-leaf test has a loose sanity bound (new)"
  - id: CR-01
    severity: critical
    disposition: open
    title: "A `null` element in `items` or `installments` passes parsing and crashes the validator, so the endpoint returns 500"
  - id: WR-01
    severity: warning
    disposition: open
    title: "`Wire.Options` accepts integer, numeric-string and any-case enum values that the schema forbids"
open: 15
total: 15
recorded: 2026-10-08T20:35:33.593Z
---

# Phase 02: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| WR-02 | warning | open | - |
| WR-03 | warning | open | - |
| WR-04 | warning | open | - |
| WR-05 | warning | open | - |
| WR-06 | warning | open | - |
| IN-01 | info | open | - |
| IN-02 | info | open | - |
| IN-03 | info | open | - |
| IN-04 | info | open | - |
| IN-05 | info | open | - |
| IN-06 | info | open | - |
| IN-07 | info | open | - |
| IN-08 | info | open | - |
| CR-01 | critical | open | - (not in the current review) |
| WR-01 | warning | open | - (not in the current review) |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
