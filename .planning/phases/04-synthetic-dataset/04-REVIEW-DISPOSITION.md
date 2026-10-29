---
phase: 04
review: 04-REVIEW.md
titles: json
findings:
  - id: IN-07
    severity: info
    disposition: open
    title: "(new) The blur severe sigma of 1.8 is validated by one reader on one invoice, and the reader transcribed mild (1.2) just as correctly"
  - id: IN-08
    severity: info
    disposition: open
    title: "(new) `_pinned_source_date_epoch` mutates process-global state; it is not thread-safe and its restore can raise"
  - id: WR-01
    severity: warning
    disposition: open
    title: "`dataset-build --out DIR` deletes every unlisted `.xml` and `.pdf` in DIR"
  - id: WR-02
    severity: warning
    disposition: open
    title: "`test_dataset_text_equals_xml` does not prove \"every graded string is printed\" (D-05)"
  - id: WR-03
    severity: warning
    disposition: open
    title: "Module docstring claims lazy import, but `cli.py` imports the whole scan stack at import time"
  - id: WR-04
    severity: warning
    disposition: open
    title: "Twin tags inherit `barcode_unreadable` from the base, so the tag can contradict the twin's own flag"
  - id: IN-01
    severity: info
    disposition: open
    title: "`dataset_check` does not handle generation `ValueError`, unlike `dataset_build`"
  - id: IN-02
    severity: info
    disposition: open
    title: "`dataset.py` reaches into `cli.py` privates and duplicates constants"
  - id: IN-03
    severity: info
    disposition: open
    title: "Garbled comment above the `QUOTA` table"
  - id: IN-04
    severity: info
    disposition: open
    title: "A fully discounted line item exists (nfe-0087, `vProd == vDesc == 6.16`)"
  - id: IN-05
    severity: info
    disposition: open
    title: "`rl_config.useA85 = 0` mutates reportlab global state on every call"
  - id: IN-06
    severity: info
    disposition: open
    title: "The `barcode_readable` flag is constant within four of the eight rendering slices"
open: 12
total: 12
recorded: 2026-10-29T19:51:33.994Z
---

# Phase 04: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| IN-07 | info | open | - |
| IN-08 | info | open | - |
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

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
