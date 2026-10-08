---
phase: 02-validated-extraction
reviewed: 2026-10-08T00:00:00Z
depth: standard
files_reviewed: 58
files_reviewed_list:
  - dotnet/Carimbo.slnx
  - dotnet/src/Carimbo.Api/EvalEndpoint.cs
  - dotnet/src/Carimbo.Api/Program.cs
  - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Domain/Wire.cs
  - dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/src/Carimbo.Extraction/RepairFeedback.cs
  - dotnet/src/Carimbo.GroundTruth/Carimbo.GroundTruth.csproj
  - dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs
  - dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs
  - dotnet/src/Carimbo.Llm/LlmContracts.cs
  - dotnet/src/Carimbo.Validation/ArithmeticRules.cs
  - dotnet/src/Carimbo.Validation/Carimbo.Validation.csproj
  - dotnet/src/Carimbo.Validation/Findings.cs
  - dotnet/src/Carimbo.Validation/Identity.cs
  - dotnet/src/Carimbo.Validation/InvoiceValidator.cs
  - dotnet/src/Carimbo.Validation/KeyAndDateRules.cs
  - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
  - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
  - dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs
  - dotnet/tests/Carimbo.ScriptedHost/Program.cs
  - dotnet/tests/Carimbo.Validation.Tests/ArithmeticRuleTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/Carimbo.Validation.Tests.csproj
  - dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/KeyAndDateRuleTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/MappingDocTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/NeverThrowsTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/NfeXmlMapperTests.cs
  - dotnet/tests/Carimbo.Validation.Tests/VectorTests.cs
  - dotnet/tools/LlmSpike/Program.cs
  - dotnet/tools/LlmSpike/SchemaProbe.cs
  - .gitignore
  - justfile
  - python/src/carimbo_datagen/cli.py
  - python/src/carimbo_datagen/ids.py
  - python/src/carimbo_datagen/nfe_xml.py
  - python/src/carimbo_datagen/spec.py
  - python/src/carimbo_evals/cli.py
  - python/src/carimbo_evals/grader.py
  - python/src/carimbo_evals/ground_truth.py
  - python/src/carimbo_evals/money.py
  - python/src/carimbo_evals/runner.py
  - python/src/carimbo_evals/summary.py
  - python/tests/test_datagen.py
  - python/tests/test_e2e_fake.py
  - python/tests/test_grader.py
  - python/tests/test_ground_truth.py
  - python/tests/test_models.py
  - python/tests/test_repo_layout.py
  - python/tests/test_runner.py
  - python/tests/test_synthetic_only.py
  - python/tests/test_vectors.py
findings:
  critical: 1
  warning: 6
  info: 4
  total: 11
status: issues_found
---

# Phase 02: Code Review Report

**Reviewed:** 2026-10-08
**Depth:** standard
**Files Reviewed:** 58
**Status:** issues_found

## Summary

The phase adds the validator catalogue, the repair loop, the NF-e ground-truth mappers (.NET and Python), the v2 eval endpoint and the Python runner and grader. Non-test source was read in full. Test files were skimmed for reliability only, and no flaky patterns were found.

The arithmetic, identity and date rules, the repair-loop state machine and the cost-cap accounting all hold up under tracing. The overflow handling in `SafeMath` is sound, and the `IsFullMatch` guard against the .NET `$`-before-newline quirk is applied consistently on the schema-pattern path.

Two of the findings below were reproduced with a scratch console project (in the session scratchpad, not in the repo) that referenced `Carimbo.Domain`, `Carimbo.Validation` and `Carimbo.GroundTruth`. Both are gaps between what `Wire.Options` accepts and what the committed schema says is valid. They undermine the stated invariants "success means schema-valid" and "the validator never throws".

Other concerns are contract edge cases (`UF=EX`), a client timeout that cannot hold the configured repair chain, and cost accounting that conflates "no call made" with "unpriced".

## Critical Issues

### CR-01: A `null` element in `items` or `installments` passes parsing and crashes the validator, so the endpoint returns 500

**File:** `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:325-337` (parse), `dotnet/src/Carimbo.Validation/ArithmeticRules.cs:119-121` and `:189` (dereference), `dotnet/src/Carimbo.Validation/KeyAndDateRules.cs:91` (dereference)
**Issue:** `JsonSerializer.Deserialize<Invoice>` with `Wire.Options` accepts `"items": [null]` and `"installments": [null]`. `RespectNullableAnnotations` does not reject null collection elements here. `PatternViolations()` skips null elements, so `Parse` returns `Success`. `InvoiceValidator.Validate` then dereferences the element (`item.Quantity`, `items.Select(item => item.Total...)`, `invoice.Installments[i].DueDate`) and throws `NullReferenceException`.

Reproduced: both `items null element` and `installments null element` parsed successfully with 0 pattern violations, then `Validate` threw `NullReferenceException`.

Consequences:
- `Parse` catches only `JsonException`, `FormatException` and `OverflowException`, so the exception escapes `ExtractAsync`.
- The Eval endpoint answers 500, and for a model that returns such output the already-paid attempts are lost from the record.
- This contradicts the documented contract in `InvoiceValidator` ("no catch-all, a rule bug surfaces as a failing test") and `ExtractionOutcome.Success` ("schema-valid"). The schema declares item objects, never null.

**Fix:** Reject null elements in `Parse`, next to the pattern check. Alternatively add a null-element walk to `Invoice.PatternViolations`, which already walks the lists by reflection.
```csharp
// InvoiceExtractor.Parse, after the null check on `invoice`
if (invoice.Items.Any(i => i is null) || invoice.Installments.Any(i => i is null))
{
    return new ExtractionOutcome.SchemaInvalid("The model output has a null element in items or installments.");
}
```
Add a case to `NeverThrowsTests` and `ExtractorTests` for `[null]` in both lists.

## Warnings

### WR-01: `Wire.Options` accepts integer, numeric-string and any-case enum values that the schema forbids

**File:** `dotnet/src/Carimbo.Domain/Wire.cs:251`
**Issue:** `new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)` defaults to `allowIntegerValues: true`, and its string reading is case-insensitive. Reproduced: `recipient.tax_id_kind` set to the integer `0`, the string `"0"` and the string `"CNPJ"` all deserialize and yield `success` with 0 findings. `7` deserializes to an undefined enum value.

The exported schema allows only `"cnpj"` and `"cpf"`. The `Wire.Options` doc comment claims "the schema describes exactly what is accepted".

The .NET side therefore reports `success` while the Python grader, which validates `raw_output` against the schema with `jsonschema` and strict Pydantic, reports `schema_valid_* = false` for the same case. The eval would show a success whose schema validity is false, which is a measurement inconsistency in the product's core claim.

**Fix:** Disable integers, and make case strictness explicit.
```csharp
options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
```
The converter stays case-insensitive. For exactness, either add a post-parse check that `Recipient.TaxIdKind` round-trips to the same string as the raw JSON, or use a small custom enum converter. Add a regression test for `0`, `"0"` and `"CNPJ"`.

### WR-02: Foreign recipients (`UF = "EX"`) are reported as an error, which drives a pointless repair

**File:** `dotnet/src/Carimbo.Validation/Identity.cs:23-52` (table) and `:271-277` (`CheckUf`)
**Issue:** `UfCodes` lists the 27 federative units only. NF-e uses `EX` as the recipient UF for exports (`dest/enderDest/UF`), and the schema pattern `^[A-Z]{2}$` admits it. `CheckUf("recipient.uf", ...)` emits `UF_UNKNOWN` at error severity. A correctly extracted export DANFE therefore ends as `validation_failed`, and the repair loop is spent re-reading a value that is printed that way. Such an invoice also usually has no CNPJ or CPF in the recipient (`idEstrangeiro`), which `NfeXmlMapper` and `ground_truth.py` reject as a missing element, so the dataset cannot represent it either.

**Fix:** Accept `EX` for `recipient.uf` only (not for the issuer, whose key cross-check needs a code), or downgrade it to a warning. Document that export recipients are out of scope for the skeleton if that is the intent.

### WR-03: The runner's 900 s read timeout cannot cover the configured repair chain

**File:** `python/src/carimbo_evals/runner.py:366`, related `dotnet/src/Carimbo.Api/Program.cs:16-19`
**Issue:** The client timeout is the literal `httpx2.Timeout(900.0, connect=5.0)`. The server comment says "up to MaxRepairs + 1 sequential provider calls, each with a 300 s timeout". With the default `MaxRepairs = 2` that is exactly 3 x 300 = 900 s, with no margin for validation or network time. `MaxRepairs` may be configured up to 5, which is 1800 s.

When the client times out first, the server keeps calling the provider (the aborted request cancels it only at the next await) and the record becomes `harness_error`. `may_have_reached_provider` then charges a reserve for it, and the real cost is lost from the record.

**Fix:** Derive the client timeout from the same settings, or send the budget in the request. For example, pass `--request-timeout-s` to the CLI with a default of `(max_repairs + 1) * per_attempt_timeout + slack`. At minimum, fail fast in the `skeleton` recipe when `max_repairs > 2`, or add a margin: `Timeout(1000.0, ...)`.

### WR-04: Infrastructure failures with no answered attempt are counted as "unpriced" and consume cost-cap reserve

**File:** `python/src/carimbo_evals/runner.py:309-318`, `python/src/carimbo_evals/summary.py:111-117`, `dotnet/src/Carimbo.Api/EvalEndpoint.cs:248-263`
**Issue:** The endpoint deliberately returns `cost_usd = null` with no `cost_warning` when no attempt was answered ("nothing to price"). The runner's `account()` and the summary's `_totals` treat any null `cost_usd` on a completed record as unpriced:
- Each provider auth, network or rate-limit failure adds `reserve_usd` (default 0.25) to `assumed` and is counted in `unpriced_cases`.
- With a systemic outage (bad key, provider down) the cap trips after a few cases and the run stops with exit code 3, "cost cap", instead of surfacing the failures.
- The summary reports phantom "unpriced" cases, which blurs the F8 distinction between "cost unknown" (a priced-model gap) and "no call completed".

**Fix:** Have the endpoint emit a distinguishable signal, or have the Python side key off it. For example, treat a completed record whose `outcome.status == "infrastructure_failure"` and whose attempts have no response (`usage` all zero and no `cost_warning`) as cost `0`. Keep the conservative reserve only when `cost_warning` is set or when the failure kind is `timeout`. Add runner and summary tests for both branches.

### WR-05: The cost-accounting decorator never disposes the inner gateway

**File:** `dotnet/src/Carimbo.Api/Program.cs:151-157` and `:160-173`, `dotnet/src/Carimbo.Llm/LlmPricing.cs:107`
**Issue:** `DecorateGatewayWithCostAccounting` builds the original gateway by calling its factory directly (`CreateOriginal`). The container therefore never tracks that instance. `CostAccountingLlmGateway` is not `IDisposable` and holds it privately. `AnthropicLlmGateway` is `IDisposable` (it owns an `HttpClient` and the SDK client), so on host shutdown, or when a `WebApplicationFactory` host is disposed in tests, the inner gateway is never disposed. Before decoration it would have been disposed by the container.

**Fix:** Make `CostAccountingLlmGateway` implement `IDisposable` and forward to `(inner as IDisposable)?.Dispose()`. Only do so for instances the wrapper created, not for a pre-built `ImplementationInstance` owned by a test.

### WR-06: `schema-check` cannot detect new or staged artifacts

**File:** `justfile:34-36`
**Issue:** The gate runs `just schema`, then `git diff --exit-code -- schema python/src/carimbo_models`. `git diff` compares the worktree to the index only. A regenerated file that is new and untracked (a new `schema/*.json`), or a stale change that was `git add`ed, makes the gate pass although HEAD differs from a fresh regeneration. This weakens DOM-08, the contract chain's drift gate.

**Fix:**
```make
schema-check:
    "{{ just_executable() }}" --justfile "{{ justfile() }}" schema
    test -z "$(git status --porcelain -- schema python/src/carimbo_models)" || { git status --short -- schema python/src/carimbo_models >&2; exit 1; }
```
Alternatively, use `git diff --exit-code HEAD -- ...` plus `git ls-files --others --exclude-standard -- ...`.

## Info

### IN-01: `RepairFeedback.SafeValue` accepts a trailing newline

**File:** `dotnet/src/Carimbo.Extraction/RepairFeedback.cs:65`
**Issue:** `^[A-Za-z0-9 .:\-]+$` is used with `IsMatch`, and the .NET `$` also matches before a final `\n`. A value like `"12.30\n"` is judged safe and echoed. Today only validator-built strings reach this guard, so it is not exploitable, but the guard exists for defence in depth and the repo already has `Patterns.IsFullMatch` for exactly this quirk.
**Fix:** Use `\z`, i.e. `@"^[A-Za-z0-9 .:\-]+\z"`, or call `Patterns.IsFullMatch`.

### IN-02: Python ground-truth reader does not mirror the .NET XML hardening

**File:** `python/src/carimbo_evals/ground_truth.py:131`
**Issue:** `NfeXmlMapper.Load` explicitly sets `DtdProcessing.Prohibit` and a null resolver (threat T-02-21). The Python reader uses `xml.etree.ElementTree.parse` with no DOCTYPE rejection. ElementTree does not fetch external entities, and current expat limits entity expansion, so the exposure is low, and the inputs are committed synthetic files. The two implementations are documented as following the same rules, though, and the parity is not there. Python's `int()` is also more permissive than `NumberStyles.None` for `nNF` and `serie` (for example `"+5"` and `"5_0"`).
**Fix:** Reject a DOCTYPE (for example `defusedxml.ElementTree.parse`, or a pre-scan for `<!DOCTYPE`). Validate the integers with `str.isascii() and str.isdigit()`.

### IN-03: Recipe arguments are interpolated unquoted into shell text

**File:** `justfile:147`, `:176`
**Issue:** `Extraction__MaxRepairs={{ max_repairs }}` and `--max-cost-usd {{ max_cost }}` are substituted by `just` into the bash script verbatim. `just skeleton "1.00; cmd"` executes `cmd`. The caller is the local user, so this is not a privilege boundary. Values are also not validated: `max_repairs` above 5 makes the Api refuse to start ("Api exited early"), and the message does not say why.
**Fix:** Pass them as positional parameters (`"$1"`), or validate in the script: `[[ "{{ max_repairs }}" =~ ^[0-5]$ ]]`.

### IN-04: Case ids from `cases.jsonl` are joined into a path without validation

**File:** `python/src/carimbo_evals/grader.py:338`
**Issue:** `cases_dir / f"{case_id}.xml"` takes `case_id` from the stored record. The runner writes only file stems, so normal runs are safe. A hand-edited or foreign `cases.jsonl` containing `../../x` would make `grade` read an arbitrary `.xml`. `Extraction:Model` and `Extraction:MaxTokens` upper bounds are likewise unchecked at startup (`Program.cs:47-57`); an empty model id fails only at the first provider call.
**Fix:** Validate `case_id` against `^[A-Za-z0-9._-]+$` in `_read_records`, and require `Extraction:Model` to be non-blank in `CreateApp`.

---

_Reviewed: 2026-10-08_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
