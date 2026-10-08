---
phase: 02-validated-extraction
reviewed: 2026-10-08T00:00:00Z
depth: standard
files_reviewed: 14
files_reviewed_list:
  - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Domain/Wire.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/src/Carimbo.Extraction/RepairFeedback.cs
  - dotnet/src/Carimbo.Validation/Findings.cs
  - dotnet/src/Carimbo.Validation/InvoiceValidator.cs
  - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
  - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs
  - justfile
findings:
  critical: 0
  warning: 5
  info: 8
  total: 13
status: issues_found
---

# Phase 02: Code Review Report (after gap closure 02-11 and 02-12)

**Reviewed:** 2026-10-08
**Depth:** standard
**Files Reviewed:** 14
**Status:** issues_found

## Summary

Scope: the diff `040fd0f..HEAD` of the 14 listed files, which holds plans 02-11 (null list elements, `NULL_VALUE`) and 02-12 (`StrictEnumJsonConverter`, `Wire.EnumNames`, D-25). I read the changed source in full and the changed tests for reliability. I also ran a scratch console project (in the session scratchpad, not in the repo) that referenced `Carimbo.Domain` to check behaviour instead of trusting the test suite.

### Status of the earlier blocking findings

- **CR-01 (null `items` / `installments` element crashes the validator, HTTP 500): RESOLVED.** Reproduced against HEAD: `"items":[null]` and `"installments":[null]` deserialize, then `Invoice.NullViolations()` returns `items[0]` / `installments[0]`. `InvoiceExtractor.Parse` (lines 332-339) turns that into `SchemaInvalid` before the validator runs, and `InvoiceValidator.Validate` is total on nulls as defence in depth. Null list element, null list, null nested record, null required string and `ie: null` all behave as the plan states. A null `code` inside an item is rejected by the serializer itself.
- **WR-01 (`Wire.Options` accepts integer, numeric-string and any-case enum values): RESOLVED.** Reproduced against HEAD: `"CNPJ"`, `0` and `"0"` for `tax_id_kind` now raise `JsonException` with path `$.recipient.tax_id_kind` and without the value in the message; the JSON-escaped spelling of `cnpj` still parses (correct, it is the same string). `CanonicalSchema.ExportJson()` is byte-identical to the committed `schema/invoice.schema.json`.

I found no Critical issues and no new Warning-level defect in the gap-closure code. The new findings are Info items (latent hazards and test quality). The five Warnings below are carried forward unchanged: they sit in files that `git diff 040fd0f HEAD` shows as untouched (`Identity.cs`, `runner.py`, `summary.py`, `EvalEndpoint.cs`, `Program.cs`, `LlmPricing.cs`) or in the one line of `justfile` the gap closure did not change (`schema-check`). Their IDs are kept so the disposition record stays aligned.

## Warnings

### WR-02: Foreign recipients (`UF = "EX"`) are reported as an error, which drives a pointless repair (carried forward, open)

**File:** `dotnet/src/Carimbo.Validation/Identity.cs:23-52` (table) and `:271-277` (`CheckUf`)
**Issue:** `UfCodes` lists the 27 federative units only. NF-e uses `EX` as the recipient UF for exports, and the schema pattern `^[A-Z]{2}$` admits it. `CheckUf("recipient.uf", ...)` emits `UF_UNKNOWN` at error severity, so a correctly extracted export DANFE ends as `validation_failed` and burns repair budget on a value that is printed that way. Such a recipient usually has no CNPJ or CPF either (`idEstrangeiro`), which the ground-truth mappers reject, so the dataset cannot represent it.
**Fix:** Accept `EX` for `recipient.uf` only (not for the issuer, whose key cross-check needs a real code), or downgrade it to a warning. Document that export recipients are out of scope for the skeleton if that is the intent.

### WR-03: The runner's 900 s read timeout cannot cover the configured repair chain (carried forward, open)

**File:** `python/src/carimbo_evals/runner.py:366`, related `dotnet/src/Carimbo.Api/Program.cs:16-19`
**Issue:** The client timeout is the literal `httpx2.Timeout(900.0, connect=5.0)`. With the default `MaxRepairs = 2` the server needs up to 3 x 300 s = 900 s, so there is no margin, and `MaxRepairs` may be configured up to 5 (1800 s). When the client times out first the record becomes `harness_error`, the reserve is charged, and the real cost is lost.
**Fix:** Derive the client timeout from the same settings, for example `(max_repairs + 1) * per_attempt_timeout + slack`, or pass it as a CLI option that the `skeleton` recipe computes. At minimum use `Timeout(1000.0, ...)` and fail fast in the recipe when `max_repairs > 2`.

### WR-04: Infrastructure failures with no answered attempt are counted as "unpriced" and consume cost-cap reserve (carried forward, open)

**File:** `python/src/carimbo_evals/runner.py:309-318`, `python/src/carimbo_evals/summary.py:111-117`, `dotnet/src/Carimbo.Api/EvalEndpoint.cs:248-263`
**Issue:** The endpoint returns `cost_usd = null` with no `cost_warning` when no attempt was answered. The runner's `account()` and the summary's `_totals` treat any null `cost_usd` on a completed record as unpriced. Each auth, network or rate-limit failure then adds `reserve_usd` (default 0.25) and counts as an unpriced case; a systemic outage trips the cap after a few cases and the run stops with exit code 3 ("cost cap") instead of surfacing the failures.
**Fix:** Treat a completed record whose outcome is `infrastructure_failure` and whose attempts hold no response (no usage, no `cost_warning`) as cost 0. Keep the conservative reserve only when `cost_warning` is set or the failure kind is `timeout`. Add runner and summary tests for both branches.

### WR-05: The cost-accounting decorator never disposes the inner gateway (carried forward, open)

**File:** `dotnet/src/Carimbo.Api/Program.cs:151-157` and `:160-173`, `dotnet/src/Carimbo.Llm/LlmPricing.cs:107`
**Issue:** `DecorateGatewayWithCostAccounting` builds the original gateway by calling its factory directly, so the container never tracks that instance, and `CostAccountingLlmGateway` is not `IDisposable`. `AnthropicLlmGateway` owns an `HttpClient` and the SDK client, so on host shutdown or when a `WebApplicationFactory` host is disposed the inner gateway is never disposed.
**Fix:** Make `CostAccountingLlmGateway` implement `IDisposable` and forward to `(inner as IDisposable)?.Dispose()`, only for instances the wrapper created (not a pre-built `ImplementationInstance` owned by a test).

### WR-06: `schema-check` cannot detect new or staged artifacts (carried forward, open)

**File:** `justfile:34-36` (line 36 is the `git diff` call)
**Issue:** The gate regenerates, then runs `git diff --exit-code -- schema python/src/carimbo_models`. `git diff` compares the worktree to the index only, so a regenerated file that is new and untracked, or a stale change that was `git add`ed, passes although HEAD differs from a fresh regeneration. That weakens DOM-08, and plan 02-12 leans on this gate as evidence that the schema is byte-identical (its D4 coverage row cites `just schema-check`).
**Fix:**
```make
schema-check:
    "{{ just_executable() }}" --justfile "{{ justfile() }}" schema
    test -z "$(git status --porcelain -- schema python/src/carimbo_models)" || { git status --short -- schema python/src/carimbo_models >&2; exit 1; }
```
Alternatively `git diff --exit-code HEAD -- ...` plus `git ls-files --others --exclude-standard -- ...`.

## Info

### IN-01: `RepairFeedback.SafeValue` accepts a trailing newline (carried forward, open)

**File:** `dotnet/src/Carimbo.Extraction/RepairFeedback.cs:66` (the regex; `IsSafe` is at `:115-116`)
**Issue:** `^[A-Za-z0-9 .:\-]+$` is used with `IsMatch`, and the .NET `$` also matches before a final `\n`, so `"12.30\n"` is judged safe and echoed. Only validator-built strings reach the guard today, so it is not exploitable, but the repo already has `Patterns.IsFullMatch` for exactly this quirk.
**Fix:** Use `\z`: `@"^[A-Za-z0-9 .:\-]+\z"`.

### IN-02: Python ground-truth reader does not mirror the .NET XML hardening (carried forward, open)

**File:** `python/src/carimbo_evals/ground_truth.py:131`
**Issue:** `NfeXmlMapper.Load` sets `DtdProcessing.Prohibit` and a null resolver (T-02-21); the Python reader uses `xml.etree.ElementTree.parse` with no DOCTYPE rejection, and `int()` is more permissive than `NumberStyles.None` (`"+5"`, `"5_0"`). Low exposure (committed synthetic input), but the two implementations are documented as following the same rules.
**Fix:** Reject a DOCTYPE (`defusedxml` or a pre-scan) and validate integers with `str.isascii() and str.isdigit()`.

### IN-03: Recipe arguments are interpolated unquoted into shell text (carried forward, open)

**File:** `justfile:147`, `:176`
**Issue:** `Extraction__MaxRepairs={{ max_repairs }}` and `--max-cost-usd {{ max_cost }}` are substituted verbatim into the bash script; `just skeleton "1.00; cmd"` runs `cmd`. Local-user only, so not a privilege boundary, but values are also unvalidated: `max_repairs` above 5 makes the Api refuse to start with the unhelpful "Api exited early".
**Fix:** Pass them as positional parameters, or validate first: `[[ "{{ max_repairs }}" =~ ^[0-5]$ ]]`.

### IN-04: Case ids from `cases.jsonl` are joined into a path without validation (carried forward, open)

**File:** `python/src/carimbo_evals/grader.py:338`
**Issue:** `cases_dir / f"{case_id}.xml"` takes `case_id` from the stored record. A hand-edited or foreign `cases.jsonl` containing `../../x` makes `grade` read an arbitrary `.xml`. `Extraction:Model` and `Extraction:MaxTokens` bounds are likewise unchecked at startup (`Program.cs:47-57`).
**Fix:** Validate `case_id` against `^[A-Za-z0-9._-]+$` in `_read_records`; require a non-blank `Extraction:Model` in `CreateApp`.

### IN-05: The enum name table has latent hazards for aliased and flags enums (new)

**File:** `dotnet/src/Carimbo.Domain/Wire.cs:311-323` (`EnumNames`), `:178-193` (converter constructor), `dotnet/src/Carimbo.Domain/CanonicalSchema.cs:104-117`
**Issue:** `EnumNames` maps every value from `Enum.GetValues` through `Enum.GetName(enumType, value)`. For an enum with two members sharing one value, `GetValues` returns the value twice and `GetName` returns the same (first) name twice. The exported `enum` array would then hold a duplicated name, and the alias member could never be read or written by its own name. For a `[Flags]` combination, `Write` throws `JsonException` because the combined value is not in `_byValue`, where the old built-in converter wrote a comma list. Nothing in the Domain triggers either today (`TaxIdKind` and `DecisionOutcome` are plain), and `Every_public_domain_enum_is_read_and_written_by_exact_wire_name_only` only covers the Domain assembly's current enums, so it would not catch a future aliased one.
**Fix:** Iterate `Enum.GetNames(enumType)` with `Enum.Parse` for the value instead, or have `EnumNames` and the converter constructor throw `InvalidOperationException` on a duplicate value or a `[Flags]` enum, so the unsupported shape fails at startup and in `SchemaExport` rather than silently.

### IN-06: The `NULL_VALUE` repair sentence is unreachable through the extractor and untested (new)

**File:** `dotnet/src/Carimbo.Extraction/RepairFeedback.cs:61`
**Issue:** `InvoiceExtractor` validates only a `Success` outcome, and `Parse` rejects every null before it, so a `NULL_VALUE` finding never reaches `RepairFeedback.Build` in the pipeline. A repair answer with a null element goes through the `SchemaInvalid` branch, which sends only the generic schema-mismatch lead plus the previous candidate's errors, and tells the model nothing about the null path. The new dictionary entry has no test (the grep for `NULL_VALUE` finds none in a RepairFeedback test), so it can rot unnoticed. Also note the asymmetry: a null in the initial answer ends the run without using the repair budget, while the same null in a repair answer consumes budget and retries.
**Fix:** Either add a `RepairFeedbackTests` case asserting the fixed sentence and that no value is echoed, or drop the entry and the rule-id coupling. If the asymmetry is intended (D-12, an initial schema_invalid is final), say so in D-25.

### IN-07: `Wire.Options` still accepts duplicate JSON properties, so "accepts only what the schema allows" is overstated (new)

**File:** `dotnet/src/Carimbo.Domain/Wire.cs:291-304` (doc comment) and `:325-338` (`Create`), `docs/DECISIONS.md` D-25
**Issue:** Verified with a scratch run: `{"tax_id_kind":"cpf","tax_id_kind":"cnpj"}` deserializes to `Cnpj` (the last value wins), while `new JsonSerializerOptions(Wire.Options) { AllowDuplicateProperties = false }` rejects it with `Duplicate property 'tax_id_kind'`. Python's `json.loads` also keeps the last value, so the eval does not diverge today, but the doc comment ("the schema describes exactly what is accepted") and D-25 ("accepts only what the committed schema allows") are stronger than the code. A duplicate key whose first value is invalid and whose second is valid is accepted as a success.
**Fix:** Set `AllowDuplicateProperties = false` in `Wire.Create()` (available on the pinned .NET 10 SDK) and add a `Strict_parsing_rejects_a_duplicate_property` test, or soften the doc comment and D-25 to list the known remaining gap.

### IN-08: The exhaustive null-leaf test has a loose sanity bound (new)

**File:** `dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs:648`
**Issue:** `Assert.True(checkedLeaves >= 25, ...)` guards the walk, but the plan summary records the real count as 28. The bound can lose three leaves, for example a refactor of `Leaves()` that skips a nested record, and the test still passes with weaker coverage. `Assert.DoesNotContain("CNPJ", ex.Message)` in `The_enum_parse_error_names_the_json_path_and_never_echoes_the_value` and in the extractor test likewise guards only that one spelling of the value.
**Fix:** Assert the exact count (or compute the expected number from the `Invoice` type graph) so a dropped leaf fails the test, and loop the not-echoed assertion over the whole spelling matrix.

---

_Reviewed: 2026-10-08_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
