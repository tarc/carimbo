---
phase: 01
review: 01-REVIEW.md
titles: json
findings:
  - id: CR-01
    severity: critical
    disposition: fixed
    title: "Oversized `total_amount` throws `OverflowException`, which becomes an HTTP 500 after a paid call and bypasses the typed-failure contract"
  - id: WR-01
    severity: warning
    disposition: fixed
    title: "`_read_records` splits JSONL on Unicode line separators and crashes on valid runs"
  - id: WR-02
    severity: warning
    disposition: fixed
    title: "Runner cost cap ignores spend on requests that ended as harness errors"
  - id: WR-03
    severity: warning
    disposition: fixed
    title: "A model answer that violates the schema patterns is reported as `success`"
  - id: WR-04
    severity: warning
    disposition: open
    title: "`carimbo-datagen build --case X` silently overwrites the full manifest with a partial one"
  - id: WR-05
    severity: warning
    disposition: fixed
    title: "Resuming a run with a corrupt (non-final) JSONL line produces an uncaught traceback"
  - id: IN-01
    severity: info
    disposition: open
    title: "`LlmFailureKind.NotConfigured` is never produced or consumed"
  - id: IN-02
    severity: info
    disposition: open
    title: "HTTP 408 and 409 are classified as `BadRequest`"
  - id: IN-03
    severity: info
    disposition: open
    title: "The inner `AnthropicLlmGateway` is never disposed"
  - id: IN-04
    severity: info
    disposition: open
    title: "Contract version `\"1\"` is duplicated as a literal"
  - id: IN-05
    severity: info
    disposition: open
    title: "Negative invoice totals are accepted"
  - id: IN-06
    severity: info
    disposition: open
    title: "CI action references are mutable tags, and `.gitignore` ignores `.env.example`"
  - id: IN-07
    severity: info
    disposition: open
    title: "Cwd-relative default paths in the grader and CLI"
open: 8
total: 13
recorded: 2026-10-08T00:25:47.516Z
---

# Phase 01: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| CR-01 | critical | fixed | 01-14 cc0f7a2 bc6a4fb |
| WR-01 | warning | fixed | 01-15 4021173 |
| WR-02 | warning | fixed | 01-15 5d7990d |
| WR-03 | warning | fixed | 01-14 7acaac7 |
| WR-04 | warning | open | - |
| WR-05 | warning | fixed | 01-15 09d2cf8 |
| IN-01 | info | open | - |
| IN-02 | info | open | - |
| IN-03 | info | open | - |
| IN-04 | info | open | - |
| IN-05 | info | open | - |
| IN-06 | info | open | - |
| IN-07 | info | open | - |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
