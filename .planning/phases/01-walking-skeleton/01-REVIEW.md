---
phase: 01-walking-skeleton
reviewed: 2026-10-08T00:00:00Z
depth: standard
files_reviewed: 70
files_reviewed_list:
  - AGENTS.md
  - CLAUDE.md
  - devenv.nix
  - devenv.yaml
  - docs/DECISIONS.md
  - dotnet/Carimbo.slnx
  - dotnet/Directory.Build.props
  - dotnet/Directory.Packages.props
  - dotnet/src/Carimbo.Api/Carimbo.Api.csproj
  - dotnet/src/Carimbo.Api/EvalEndpoint.cs
  - dotnet/src/Carimbo.Api/Program.cs
  - dotnet/src/Carimbo.Domain/CanonicalSchema.cs
  - dotnet/src/Carimbo.Domain/Carimbo.Domain.csproj
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Domain/Wire.cs
  - dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/src/Carimbo.Extraction/ModelSchemaProjector.cs
  - dotnet/src/Carimbo.Extraction/SchemaBudget.cs
  - dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs
  - dotnet/src/Carimbo.Llm/Carimbo.Llm.csproj
  - dotnet/src/Carimbo.Llm/LlmContracts.cs
  - dotnet/src/Carimbo.Llm/LlmPricing.cs
  - dotnet/src/Carimbo.Llm/pricing.json
  - dotnet/tests/Carimbo.Api.Tests/Carimbo.Api.Tests.csproj
  - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
  - dotnet/tests/Carimbo.Domain.Tests/Carimbo.Domain.Tests.csproj
  - dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs
  - dotnet/tests/Carimbo.Domain.Tests/SchemaSnapshotTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/Carimbo.Extraction.Tests.csproj
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/SchemaProjectionTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/Carimbo.Llm.Tests.csproj
  - dotnet/tests/Carimbo.Llm.Tests/PricingTests.cs
  - dotnet/tests/Carimbo.ScriptedHost/Carimbo.ScriptedHost.csproj
  - dotnet/tests/Carimbo.ScriptedHost/Program.cs
  - dotnet/tools/LlmSpike/LlmSpike.csproj
  - dotnet/tools/LlmSpike/Program.cs
  - dotnet/tools/SchemaExport/Program.cs
  - dotnet/tools/SchemaExport/SchemaExport.csproj
  - .editorconfig
  - .gitattributes
  - .github/workflows/ci.yml
  - .gitignore
  - global.json
  - justfile
  - python/pyproject.toml
  - python/src/carimbo_datagen/cli.py
  - python/src/carimbo_datagen/danfe.py
  - python/src/carimbo_datagen/ids.py
  - python/src/carimbo_datagen/__init__.py
  - python/src/carimbo_datagen/nfe_xml.py
  - python/src/carimbo_datagen/spec.py
  - python/src/carimbo_evals/cli.py
  - python/src/carimbo_evals/grader.py
  - python/src/carimbo_evals/__init__.py
  - python/src/carimbo_evals/money.py
  - python/src/carimbo_evals/runner.py
  - python/src/carimbo_evals/summary.py
  - python/src/carimbo_models/__init__.py
  - python/tests/test_datagen.py
  - python/tests/test_e2e_fake.py
  - python/tests/test_grader.py
  - python/tests/test_models.py
  - python/tests/test_repo_layout.py
  - python/tests/test_runner.py
  - python/tests/test_synthetic_only.py
  - README.md
  - secretspec.toml
findings:
  critical: 1
  warning: 5
  info: 7
  total: 13
status: issues_found
---

# Phase 1: Code Review Report

**Reviewed:** 2026-10-08
**Depth:** standard
**Files Reviewed:** 70
**Status:** issues_found

## Summary

The walking skeleton is carefully built. Authentication happens before the body is read, the key is compared by hash with `FixedTimeEquals`, provider and eval keys are kept out of logs and responses, failures are typed, and cost is held as `decimal` / `Decimal` throughout. The deliberate choices recorded in D-01..D-21 (SDK `MaxRetries` 0, the direct SDK behind `ILlmGateway`, money as a string) were not treated as defects.

The defects found are in edge paths that the green suite does not exercise:

- One BLOCKER: a model answer with an oversized `total_amount` escapes the typed-failure contract and turns into an HTTP 500, after the paid call has already happened. The runner's cost cap cannot see that spend. I reproduced this against the real `Wire.Options`.
- A reproducible grader crash on JSONL lines that contain U+2028, U+2029 or U+0085.
- A schema-validity gap, an uncaught resume error and a manifest-truncation footgun.

Structural findings (fallow) were not provided for this review.

## Critical Issues

### CR-01: Oversized `total_amount` throws `OverflowException`, which becomes an HTTP 500 after a paid call and bypasses the typed-failure contract

**File:** `dotnet/src/Carimbo.Domain/Wire.cs:58-65` (with `dotnet/src/Carimbo.Domain/Wire.cs:37-40` and `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:147-159`)
**Issue:** `Money.Parse` accepts any digit string matching `^-?[0-9]+\.[0-9]{2}$`, then calls `decimal.Parse`. A value with more than about 28 integer digits (for example a hallucinated `"99999999999999999999999999999999.00"`) throws `OverflowException`, not `FormatException`. `MoneyJsonConverter.Read` only catches `FormatException`, and System.Text.Json does not wrap arbitrary converter exceptions, so the `OverflowException` leaves `JsonSerializer.Deserialize`. `InvoiceExtractor.Parse` only catches `JsonException`, so the exception reaches `EvalEndpoint.HandleAsync`, which has no handler for it. Result: HTTP 500 instead of `outcome.status = "schema_invalid"`.

Verified by running the real `Wire.Options` in a scratch project:

```
10.00                                  -> ok
99999999999999999999999999999999.00    -> OverflowException
```

Consequences:
1. The headline guarantee ("typed failures are never wrong answers", D-06 and the `ExtractionOutcome` doc comment) is broken for a model-controlled input. The structured-output schema only pins the pattern and not the length, so the API will not prevent this.
2. The Anthropic call has already been made and billed. The runner records an HTTP 500 as `harness_error` with no `cost_usd` (`runner.py:116-122`), so the spend is invisible to the cost cap (`runner.py:225-234`). `--resume` re-runs the case, which pays again and can overflow again. This undermines the US$1/US$5 caps the project relies on (D-09).
3. `Money.Parse` is public domain API and will throw the same exception for any caller.

**Fix:**
```csharp
// Wire.cs, Money.Parse: parse without throwing on overflow.
if (!decimal.TryParse(
        text,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture,
        out var amount))
{
    throw new FormatException($"'{text}' is not a monetary amount that fits in a decimal.");
}

return new Money(amount);
```
`TryParse` returns false on overflow, so the converter's existing `FormatException` to `JsonException` mapping then applies. As defence in depth, also catch `OverflowException` in `MoneyJsonConverter.Read` and add a regression test with a 32-digit amount in both `DomainTests` and `ExtractorTests` (expecting `SchemaInvalid`). Separately, see WR-02 for counting cost on harness errors.

## Warnings

### WR-01: `_read_records` splits JSONL on Unicode line separators and crashes on valid runs

**File:** `python/src/carimbo_evals/grader.py:244-254` (writer: `python/src/carimbo_evals/runner.py:254`)
**Issue:** The runner writes records with `json.dumps(..., ensure_ascii=False)`, which leaves U+2028, U+2029 and U+0085 unescaped inside strings. The grader reads with `str.splitlines(keepends=True)`, which also splits on those characters, so one record becomes two fragments that both fail `json.loads`. Only a torn *final* line without a trailing newline is tolerated, so `carimbo-evals grade` raises `JSONDecodeError` (surfaced as exit 2) and the whole run is ungradable. Any model `raw_output` (or error message) containing one of these characters triggers it. Reproduced: `json.dumps({"x": "a\u2028b"}, ensure_ascii=False)` yields two `splitlines()` fragments, both invalid JSON.
**Fix:** Split on `"\n"` only (the writer's own delimiter), mirroring `_load_existing`, which already uses `bytes.splitlines` and is safe:
```python
text = cases_path.read_text(encoding="utf-8")
lines = text.split("\n")          # the writer terminates every record with "\n"
for index, line in enumerate(lines):
    if not line.strip():
        continue
    try:
        record = json.loads(line)
    except ValueError:
        if index == len(lines) - 1:   # unterminated final fragment (torn write)
            break
        raise
    records[record["case_id"]] = record
```
Add a test whose record contains `"\u2028"`.

### WR-02: Runner cost cap ignores spend on requests that ended as harness errors

**File:** `python/src/carimbo_evals/runner.py:255-260` (and `runner.py:225-234`)
**Issue:** `account(record)` is only called for `completed` records. A harness error (HTTP 5xx, timeout at the 180 s client limit, connection reset after the server already called the model) may well have incurred provider cost, but it adds nothing to `spent` or `assumed`. Combined with `--resume`, which re-runs every harness error, a systematically failing case (see CR-01) can be paid for repeatedly without ever touching the cap. The docstring promises the cap bounds spend; it only bounds *recorded* spend.
**Fix:** Charge `reserve_usd` to `assumed` for every harness-error record whose HTTP status is not a client-side rejection (anything other than 400, 401, 413, 415), and count prior harness errors the same way when resuming:
```python
else:
    harness_errors += 1
    if record["http"]["status"] not in (400, 401, 413, 415):
        assumed += reserve_usd
        unpriced += 1
```

### WR-03: A model answer that violates the schema patterns is reported as `success`

**File:** `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:147-159` (root cause `dotnet/src/Carimbo.Domain/Invoice.cs:25-26,37-38`)
**Issue:** The `[RegularExpression]` attributes on `AccessKey` and `Cnpj` only feed the exported schema; `JsonSerializer` ignores them. Verified: `"access_key": "bad"` deserializes without error and the extractor returns `ExtractionOutcome.Success`. The Python grader later records `schema_valid_jsonschema = false` for the same answer, so the .NET `status` (`success`) and the Python schema verdict disagree. Anything downstream that trusts `Status == "success"` (the Phase 3+ validators, the agent) will see an invalid access key or CNPJ as a valid typed `Invoice`. The `Wire.Options` doc says "the schema describes exactly what the deserializer accepts", which is not true for patterns.
**Fix:** After deserializing, validate the pattern-bearing members (a pure function in Domain, for example `Invoice.Validate()` using `Patterns.*` with `RegexOptions.CultureInvariant`) and return `SchemaInvalid` with the offending field names. If this is deliberately deferred to the Phase 3 validators (D-05/D-06), say so in the `ExtractionOutcome.Success` doc comment and in the eval endpoint contract, so that `success` is not read as "schema valid".

### WR-04: `carimbo-datagen build --case X` silently overwrites the full manifest with a partial one

**File:** `python/src/carimbo_datagen/cli.py:95-104,121`
**Issue:** `build_dataset` always writes `manifest.json` listing only the cases built in that call. Running `carimbo-datagen build --case case-001` leaves case-002 and case-003 files on disk but removes them from the manifest. The runner/grader and the spike read `manifest.json` (`grade_run` hashes it into `summary.dataset.manifest_sha256`), and `datagen-check` then reports a confusing byte diff. Nothing warns the user.
**Fix:** Either refuse a partial build into a directory that already holds a manifest, or build a subset into the manifest by merging existing entries:
```python
if case and out.joinpath(MANIFEST_NAME).exists():
    typer.echo("error: --case would overwrite the manifest; build all cases or use another --out", err=True)
    raise typer.Exit(2)
```

### WR-05: Resuming a run with a corrupt (non-final) JSONL line produces an uncaught traceback

**File:** `python/src/carimbo_evals/runner.py:147-156` (caller `python/src/carimbo_evals/cli.py:128-145`)
**Issue:** `_load_existing` calls `json.loads(line)` and `record["case_id"]` with no error handling for a corrupt middle line. The CLI only catches `FileExistsError` around `run_cases`, so `JSONDecodeError`/`KeyError` surfaces as a Python traceback and exit code 1, which the documented exit-code contract (0/2/3/4) does not include. Only a torn final line is repaired.
**Fix:** Catch `(ValueError, KeyError)` in `_load_existing`, raise a `ValueError` naming the line number, and catch `ValueError` in the CLI next to `FileExistsError` to exit with `EXIT_USAGE`.

## Info

### IN-01: `LlmFailureKind.NotConfigured` is never produced or consumed

**File:** `dotnet/src/Carimbo.Llm/LlmContracts.cs:67`
**Issue:** Dead enum member (the only occurrence in the repository). It will also appear in the wire vocabulary of `failure.kind` once serialized, advertising a state the system cannot reach.
**Fix:** Remove it, or add the code path (for example when the gateway is selected but no key resolves).

### IN-02: HTTP 408 and 409 are classified as `BadRequest`

**File:** `dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs:241-249`
**Issue:** The `_ => BadRequest` arm maps 408 (request timeout) and 409 (conflict) to a permanent client error, although the SDK itself treats both as retryable. When Phase 3 (LLM-01) owns the retry policy and keys decisions off `LlmFailureKind`, these transient statuses would be treated as non-retryable.
**Fix:** Map 408 to `Timeout` and 409 to `ServerError` (or add a `Conflict` kind) and add the cases to `AnthropicGatewayTests`.

### IN-03: The inner `AnthropicLlmGateway` is never disposed

**File:** `dotnet/src/Carimbo.Api/Program.cs:117-138`
**Issue:** `DecorateGatewayWithCostAccounting` builds the original through `CreateOriginal` inside a factory for `CostAccountingLlmGateway`, which is not `IDisposable`. The container therefore never disposes the wrapped `AnthropicLlmGateway` (its `HttpClient` and `AnthropicClient`). Harmless for a process-lifetime singleton, but it is a leak if the host is built repeatedly (tests, tools).
**Fix:** Make `CostAccountingLlmGateway` implement `IDisposable` and dispose `inner` when it is disposable, or register the inner gateway under a key and let the container own it.

### IN-04: Contract version `"1"` is duplicated as a literal

**File:** `dotnet/src/Carimbo.Api/EvalEndpoint.cs:29,208`
**Issue:** `ContractVersion` is a private const used for request validation, while `EvalResponse.From` hardcodes `"1"`. The two can drift apart on a version bump, and the Python side has a third copy (`runner.py:31`).
**Fix:** Reference `EvalEndpoint.ContractVersion` (make it `internal`) from `EvalResponse.From`.

### IN-05: Negative invoice totals are accepted

**File:** `dotnet/src/Carimbo.Domain/Invoice.cs:20`
**Issue:** `Patterns.Money` is `^-?[0-9]+\.[0-9]{2}$` and is used for `Invoice.TotalAmount`, so `"-5.00"` is a valid total in both the schema and the parser. An NF-e `vNF` is never negative. Cosmetic today, but it widens the model-facing contract for no benefit.
**Fix:** Keep the signed pattern for future adjustment amounts, but use an unsigned pattern for `TotalAmount`, or add the check to the Phase 3 validators.

### IN-06: CI action references are mutable tags, and `.gitignore` ignores `.env.example`

**File:** `.github/workflows/ci.yml:27-60`, `.gitignore:13-14`
**Issue:** Third-party actions (`extractions/setup-just`, `astral-sh/setup-uv`) are referenced by major-version tag, so the workflow's supply chain can change under the repository's "pin exact versions" convention (`AGENTS.md`). The `.env.*` pattern also ignores a would-be `.env.example`.
**Fix:** Pin actions to full commit SHAs with a version comment, and add `!.env.example` to `.gitignore`.

### IN-07: Cwd-relative default paths in the grader and CLI

**File:** `python/src/carimbo_evals/grader.py:34-35,279`, `python/src/carimbo_evals/cli.py:71-73,161-169`
**Issue:** `DEFAULT_SCHEMA_PATH = Path("schema/invoice.schema.json")` (and `data/skeleton`) resolve against the current directory, while `grade_case` falls back to `_REPO_SCHEMA_PATH` (anchored to the package location). Running `carimbo-evals grade` from any other directory fails with a file-not-found exit 2, and the two defaults can silently diverge. `--base-url` also sends the eval key over plain `http://` to whatever host is given, with no warning for a non-loopback target.
**Fix:** Default the CLI schema option to `_REPO_SCHEMA_PATH`, and warn (or require a flag) when `--base-url` is not loopback and not `https`.

---

_Reviewed: 2026-10-08_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
