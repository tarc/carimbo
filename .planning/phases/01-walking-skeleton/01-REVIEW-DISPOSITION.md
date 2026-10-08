---
phase: 01
review: 01-REVIEW.md
titles: json
findings:
  - id: WR-06
    severity: warning
    disposition: open
    title: "The grader's jsonschema verdict is more lenient than .NET for trailing newlines and dates, which contradicts the comment in the e2e test"
  - id: WR-07
    severity: warning
    disposition: open
    title: "The grader still tracebacks on a structurally wrong record (the grader half of WR-05)"
  - id: IN-08
    severity: info
    disposition: open
    title: "Assumed spend for harness errors is a flat `reserve_usd`, accumulates across resumes, and is not scaled to the largest observed cost"
  - id: IN-09
    severity: info
    disposition: open
    title: "The `FormatException` message copies the model-controlled amount into `failure.message`"
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
open: 12
total: 17
recorded: 2026-10-08T03:35:01.945Z
---

# Phase 01: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| WR-06 | warning | open | - |
| WR-07 | warning | open | - |
| IN-08 | info | open | - |
| IN-09 | info | open | - |
| CR-01 | critical | fixed | 01-14 cc0f7a2 bc6a4fb (not in the current review) |
| WR-01 | warning | fixed | 01-15 4021173 (not in the current review) |
| WR-02 | warning | fixed | 01-15 5d7990d (not in the current review) |
| WR-03 | warning | fixed | 01-14 7acaac7 (not in the current review) |
| WR-04 | warning | open | - (not in the current review) |
| WR-05 | warning | fixed | 01-15 09d2cf8 (not in the current review) |
| IN-01 | info | open | - (not in the current review) |
| IN-02 | info | open | - (not in the current review) |
| IN-03 | info | open | - (not in the current review) |
| IN-04 | info | open | - (not in the current review) |
| IN-05 | info | open | - (not in the current review) |
| IN-06 | info | open | - (not in the current review) |
| IN-07 | info | open | - (not in the current review) |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
