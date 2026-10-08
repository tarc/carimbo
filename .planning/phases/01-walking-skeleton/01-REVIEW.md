---
phase: 01-walking-skeleton
reviewed: 2026-10-08T12:00:00Z
depth: standard
files_reviewed: 12
files_reviewed_list:
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Domain/Wire.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
  - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - python/src/carimbo_evals/cli.py
  - python/src/carimbo_evals/grader.py
  - python/src/carimbo_evals/runner.py
  - python/tests/test_e2e_fake.py
  - python/tests/test_grader.py
  - python/tests/test_runner.py
findings:
  critical: 0
  warning: 2
  info: 2
  total: 4
status: issues_found
---

# Phase 1: Code Review Report (incremental re-review after plans 01-14, 01-15, 01-16)

**Reviewed:** 2026-10-08
**Depth:** standard
**Files Reviewed:** 12
**Status:** issues_found

## Summary

This is an incremental re-review of the files changed since c459e72. The claim that CR-01, WR-01, WR-02, WR-03 and WR-05 are fixed was checked against the code and the tests. All five fixes hold for the failure each one named. None of them is wrong or incomplete in a way that reopens the original finding.

- CR-01 (fixed). `Money.Parse` now uses `decimal.TryParse`, which returns false on overflow, so it throws `FormatException`. `MoneyJsonConverter.Read` also maps `OverflowException` to `JsonException`. The extractor catches `FormatException` and `OverflowException` narrowly, so an oversized `total_amount` ends as `schema_invalid` with HTTP 200 and the cost kept. The Domain, Extraction, Api and Python e2e tests cover it, including the boundary values at and just past `decimal.MaxValue`.
- WR-03 (fixed). `Invoice.PatternViolations()` is a method, so neither the serializer nor the schema exporter sees it. `Patterns.IsFullMatch` correctly rejects a trailing `\n`, which the .NET `$` anchor would accept. `ExtractionOutcome.Success` is documented as schema-valid. The "every schema pattern is enforced" test walks the real model-facing schema, so a new pattern fails the test until it is enforced.
- WR-01 (fixed). The grader splits on `"\n"` only, matching the writer. A torn final line is still tolerated, and a corrupt middle line still raises.
- WR-05 (fixed for the runner). `_load_existing` validates every terminated line before it truncates a torn tail. It raises `CorruptRunError` naming the line, and the CLI exits 2. The grader side of the same defect is still open; see WR-07.
- WR-02 (fixed). Harness errors that may have reached the provider are charged `reserve_usd` against the cap. Every prior record of a selected case is charged on resume, not only the last one. Rejections that cannot have cost money are not charged (400, 401, 404, 413, 415, and connect-phase transport errors). I checked `EvalEndpoint.HandleAsync`: it returns those statuses only before the extractor and gateway run, so excluding them is sound.

New findings are numbered after the ledger IDs. None of them reopens a fixed finding.

The money-pattern length bound is the known Phase 2 (DOM-04) deferral and is not restated here. The code does not contradict that deferral.

Still open from the prior review and untouched by plans 01-14..16: WR-04 and IN-01 to IN-07 (see the ledger). None of them is in this re-review's file scope, and none was re-verified or changed.

## Warnings

### WR-06: The grader's jsonschema verdict is more lenient than .NET for trailing newlines and dates, which contradicts the comment in the e2e test

**File:** `python/src/carimbo_evals/grader.py:120-139` (comment at `python/tests/test_e2e_fake.py:418-422`)
**Issue:** `load_schema_validator` builds a bare `Draft202012Validator`, which has two gaps relative to the .NET extractor that WR-03 just made strict.
1. Patterns. `jsonschema` evaluates `pattern` with Python `re.search`. Python's `$` also matches before a final newline, so `access_key = "<44 valid chars>\n"` is schema-valid for jsonschema. Verified: an instance with trailing newlines on `access_key` and `recipient.cnpj` gives `is_valid == True`. The same value is `schema_invalid` in .NET (`Patterns.IsFullMatch`, the exact quirk the new .NET test pins down) and invalid for Pydantic (its Rust regex has no such quirk). So `schema_valid_jsonschema` and `schema_valid_pydantic` can disagree with each other and with the endpoint's `status`.
2. Formats. No `FormatChecker` is passed, so `"format": "date"` is only an annotation. `issue_date = "2026-02-30"` is valid for jsonschema but rejected by .NET `DateOnly` and by Pydantic.

The e2e comment says "the grader's own schema verdict agrees with .NET for the pattern violation", which is true only for the space-grouped key it tests, not in general. This is distinct from the deferred missing length bound.
**Fix:**
```python
from jsonschema import Draft202012Validator, FormatChecker, validators
import re

def _full_match_pattern(validator, patrn, instance, schema):
    if validator.is_type(instance, "string") and re.fullmatch(patrn, instance) is None:
        yield ValidationError(f"{instance!r} does not match {patrn!r}")

Validator = validators.extend(Draft202012Validator, {"pattern": _full_match_pattern})
return Validator(schema, format_checker=FormatChecker())
```
`re.fullmatch` rejects `"...\n"` because the trailing newline is not consumed. Add grader tests for a trailing-newline key and for a calendar-invalid date.

### WR-07: The grader still tracebacks on a structurally wrong record (the grader half of WR-05)

**File:** `python/src/carimbo_evals/grader.py:248-256`
**Issue:** `_read_records` calls `records[record["case_id"]] = record` on whatever `json.loads` returned. A complete, newline-terminated line such as `[1, 2]` or `{"record_version": 1}` raises `TypeError` or `KeyError`. The `grade` command only catches `(OSError, ValueError)`, so this surfaces as a Python traceback with exit code 1 instead of the documented usage error (exit 2). Plan 01-15 fixed this exact input class for `--resume` (`CorruptRunError`, with tests for `{"record_version": 1}` and `[1, 2]`), but the grader reads the same file and was not given the check. A tampered or hand-edited `cases.jsonl` is therefore handled in one reader and not the other. Later, `grade_run` also builds a path from the untrusted `case_id` (`cases_dir / f"{case_id}.xml"`).
**Fix:** Share the validation. For example, move the per-line checks into one helper used by `_load_existing` and `_read_records`, and raise `ValueError` naming the line:
```python
if not isinstance(record, dict) or not isinstance(record.get("case_id"), str):
    raise ValueError(f"{cases_path}: line {index + 1} is not a run record")
```
Optionally reject a `case_id` that is not a plain file stem (no path separators). Add a grader test next to `test_a_corrupt_middle_line_fails_grading_but_a_torn_final_line_is_ignored` for the two structural cases.

## Info

### IN-08: Assumed spend for harness errors is a flat `reserve_usd`, accumulates across resumes, and is not scaled to the largest observed cost

**File:** `python/src/carimbo_evals/runner.py:289-298,309-311`
**Issue:** Two small inconsistencies in the new accounting.
1. The dispatch reserve is `max(reserve_usd, largest)`, but each possibly-paid harness error is charged only `reserve_usd`. Once real costs exceed the default US$0.05 reserve (a Sonnet call over a multi-page PDF can), a 5xx or read timeout is under-counted relative to what the next dispatch assumes.
2. Every resume re-charges all prior harness-error records. A case that keeps failing with a 5xx adds US$0.05 per attempt for good, so after about 20 attempts the run is permanently stuck at `cost_cap` (exit 3) with no real spend recorded and no way to clear it except editing `cases.jsonl`. This is conservative by design, but nothing tells the operator why the cap was reached. The CLI prints `assumed=` but not that it is made of harness errors.
**Fix:** Use `max(reserve_usd, largest)` for the harness-error charge (accounting for completed records first), and print how many cases make up `assumed` (for example `assumed_cases=`). Document in the `--resume` help text how to clear stale charges, or cap the charge per case at one reserve.

### IN-09: The `FormatException` message copies the model-controlled amount into `failure.message`

**File:** `dotnet/src/Carimbo.Domain/Wire.cs:36,48` and `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:173-183`
**Issue:** `Money.Parse` interpolates the whole input into its exception text (`'{text}' is not a monetary amount ...`). The extractor passes `ex.Message` straight into `SchemaInvalid`, so an arbitrarily long model-controlled string is echoed into the response's `failure.message` as well as `raw_output`. The `JsonException` path already does the same, so this is not new, but the tests now pin the message fragment (`"fits in a decimal"`), which makes the behaviour harder to change.
**Fix:** Drop the value from the exception message, or truncate it (for example to 64 characters), and keep the diagnostic in `raw_output` only.

---

_Reviewed: 2026-10-08T12:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
