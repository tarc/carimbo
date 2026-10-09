# Phase 2: Validated Extraction - Pattern Map

**Mapped:** 2026-10-08
**Files analyzed:** 22 (new and modified)
**Analogs found:** 22 / 22 (all in-repo, git-tracked; no mirror paths)

Almost every file extends an existing Phase 1 file. For modified files the "analog" is the file itself plus its sibling. Line numbers refer to the current tree.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match |
|---|---|---|---|---|
| `dotnet/src/Carimbo.Domain/Invoice.cs` (mod: LineItem, Totals, Installment, Party ie/uf/tax_id_kind, reflection `PatternViolations`) | model | transform | itself (lines 12-88) | exact |
| `dotnet/src/Carimbo.Domain/Wire.cs` (mod: `Quantity`, `Rate` converters, half-up rounding helper) | utility | transform | `Money` + `MoneyJsonConverter` (Wire.cs 14-86) | exact |
| `dotnet/src/Carimbo.Domain/CanonicalSchema.cs` (mod: pattern branch per new struct) | utility | transform | `typeof(Money)` branch (lines 85-93) | exact |
| `dotnet/src/Carimbo.Validation/*` (new project or in Domain: `Finding`, `InvoiceValidator`, check digits, tax rules) | service | transform (pure, never throws) | `Patterns.IsFullMatch` / `PatternViolations` (Invoice.cs) + `ids.py` check-digit math | role-match |
| `dotnet/src/Carimbo.Validation/Carimbo.Validation.csproj` | config | n/a | `dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj` | exact |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` (mod: repair loop, `ValidationFailed`, attempts, prompt v2, repair prompt) | service | request-response (multi-turn) | itself (lines 10-188) | exact |
| `dotnet/src/Carimbo.Extraction/ModelSchemaProjector.cs`, `SchemaBudget.cs` (verify only) | utility | transform | themselves | exact |
| `dotnet/src/Carimbo.Llm/LlmContracts.cs` (mod: follow-up turns, cache_control on document) | model | request-response | itself (lines 12-25); `dotnet/tools/LlmSpike/Program.cs:96` for `CacheControlEphemeral` | exact |
| `dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs` (mod: multi-turn messages) | adapter | request-response | itself | exact |
| `dotnet/src/Carimbo.Api/EvalEndpoint.cs` (mod: contract v2, attempts, findings, `validation_failed`, `reference_date`) | controller | request-response | itself (lines 22-150) | exact |
| `dotnet/src/Carimbo.Api/Program.cs` (mod: bind `Extraction:MaxRepairs`, TimeProvider, timeout 300) | config | n/a | itself | exact |
| `dotnet/tests/Carimbo.ScriptedHost/Program.cs` (mod: `attempts[]` array) | test host | request-response | itself (lines 22-86) | exact |
| `dotnet/tests/Carimbo.Validation.Tests/*` (new; vector-file theory) | test | transform | `dotnet/tests/Carimbo.Domain.Tests/DomainTests.cs` | role-match |
| `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs` (mod: repair cases) | test | request-response | itself (Respond/ExtractAsync helpers) | exact |
| `dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs`, `Domain.Tests/SchemaSnapshotTests.cs`, `SchemaProjectionTests.cs` (mod) | test | request-response | themselves | exact |
| Ground-truth gate test (XML to `Invoice` mapper + zero-error assertion) | test | file-I/O | `SchemaSnapshotTests.cs` (reads committed files) | partial |
| `python/src/carimbo_datagen/ids.py` (mod: `make_cpf`, `is_valid_cpf`, `is_valid_access_key`) | utility | transform | itself (lines 22-94) | exact |
| `python/src/carimbo_datagen/spec.py`, `nfe_xml.py`, `danfe.py` (mod: Normal regime, IPI, cobr/dup, CPF, natOp, IE/UF) | model/generator | transform | themselves | exact |
| `python/src/carimbo_evals/money.py` (mod: `round_half_up`) | utility | transform | itself (lines 1-19) | exact |
| `python/src/carimbo_evals/grader.py`, `summary.py`, `runner.py` (mod) | service | batch / transform | themselves (grader.py 1-60) | exact |
| `python/tests/test_vectors.py` (new), `test_grader.py`, `test_e2e_fake.py`, `test_synthetic_only.py` (mod) | test | transform | `python/tests/test_synthetic_only.py` (access-key oracle) | role-match |
| `data/vectors/validator-vectors.json`, shared valid-invoice fixture, `docs/` field-mapping doc | data/doc | n/a | none (see below) | none |

## Pattern Assignments

### `Invoice.cs` additions (model, transform)
Copy the record style: `[property: Description]` + `[property: RegularExpression(Patterns.X)]` on positional params, constants in `Patterns` with explicit `[0-9]`.
```csharp
// Invoice.cs 12-21, 40-49, 83-88
public const string Cnpj = "^[A-Z0-9]{12}[0-9]{2}$";
public sealed record Party(
    [property: Description("CNPJ without punctuation: ...")]
    [property: RegularExpression(Patterns.Cnpj)]
    string Cnpj,
    [property: Description("Company name exactly as printed")]
    string Name);
```
- Add `Patterns.Cpf`, `Uf`, `Ncm`, `Cfop`, `Quantity`, `Rate`. Never `\d`.
- New nested records need unique titles (CanonicalSchema hoists titled objects into `$defs`).
- `PatternViolations()` (lines 60-79) is a hand-written list that must become a reflection walker (RESEARCH Pattern 4), emitting paths such as `items[2].ncm`. Keep it a method, not a property, so the serializer and exporter ignore it. Keep `IsFullMatch` (lines 29-36), the `$` newline fix.
- Required nullable `ie` uses `string?`; `Wire.Options` has `RespectNullableAnnotations`, and it consumes union budget (see SchemaBudget).

### `Wire.cs`: `Quantity` and `Rate` (utility, transform)
**Analog:** `Money` / `MoneyJsonConverter` (Wire.cs 14-86). Copy exactly: static compiled Regex from a `Patterns` constant, `Parse` that checks the pattern first, then `decimal.TryParse(AllowLeadingSign|AllowDecimalPoint, InvariantCulture)`, throws `FormatException`; converter requires `JsonTokenType.String` and wraps `FormatException` and `OverflowException` in `JsonException`.
Add `[JsonConverter]` on each readonly record struct. Half-up helper lives next to `Wire`: `Math.Round(x, 2, MidpointRounding.AwayFromZero)` (mirror of the Python `round_half_up`).

### `CanonicalSchema.cs` (utility, transform)
```csharp
// lines 85-93: custom converters export as literal `true`, so emit the pattern explicitly. No title.
if (context.TypeInfo.Type == typeof(Money))
{
    schema = new JsonObject { ["type"] = "string", ["pattern"] = Patterns.Money };
}
```
Add one branch per new struct (`Quantity`, `Rate`). Check string-enum export (`tax_id_kind`) gets `type: string` (RESEARCH Pitfall 3). Run `just schema` after.

### Validators (service, transform; pure, never throw)
**Analogs:** `Patterns.IsFullMatch` (Invoice.cs 29-36) for pure static style; `ids.py` for the algorithm port.
Port (ids.py 22-46, 65-72):
```python
def char_value(ch): return ord(ch) - 48
def _mod11_digit(s): r = s % 11; return 0 if r < 2 else 11 - r
# cnpj weights (5,4,3,2,9,8,7,6,5,4,3,2) / (6,5,4,3,2,9,8,7,6,5,4,3,2)
# access key: weights 2..9 right to left over key43
```
In C# use explicit `ch - '0'` style ranges, guard with `IsFullMatch` first so no exceptions, and inject `TimeProvider` or a reference `DateOnly` (D-11). Finding record: `{Field, RuleId, Expected, Actual, Severity}` with UPPER_SNAKE ids. Repair feedback is a redacted projection of findings (RESEARCH Pattern 2). The new csproj copies `Carimbo.Extraction.csproj` (a ProjectReference to Domain only; Validation must not reference Llm).

### `InvoiceExtractor.cs` repair loop (service, request-response)
**Analog:** itself. Keep these pieces:
- `ExtractionContract.Build()` (lines 19-41): prompt const raw string, `ModelSchemaProjector.Project(CanonicalSchema.Export())`, `SchemaBudget.EnsureWithin`, SHA256 of schema. Bump to `extract-002`; add a repair prompt with its own version string.
- Outcome union (lines 53-94): private ctor, abstract `Status`, nested sealed records. Add `ValidationFailed(Invoice Candidate, IReadOnlyList<Finding>)` with status `"validation_failed"`.
- Failure ordering (lines 140-148): stop reason decides before parsing; refusal/truncation are typed, never repaired.
```csharp
catch (LlmGatewayException ex)   // only catch typed gateway exceptions, no catch-all
{ return Result(new ExtractionOutcome.InfrastructureFailure(ex.Kind, ex.HttpStatus, ex.RequestId, ex.Message), null, null); }
```
- Parse (151-184): narrow catches of `JsonException`/`FormatException`/`OverflowException`; keep narrow.
- `ExtractionSettings` (45-50): add `MaxRepairs = 2`; raise `MaxTokens` (Pitfall 1). `ExtractionResult` gains `Attempts` (kind, raw, parsed, findings, usage, cost, latency, prompt version); totals sum over attempts. `IInvoiceExtractor.ExtractAsync` stays free of case ids.

### `LlmContracts.cs` / gateway (model, request-response)
`LlmRequest(Model, MaxTokens, Prompt, Document, OutputSchemaJson)` (lines 20-25) is single-turn. Extend additively with an optional follow-up list (previous assistant text, user findings turn); the scripted host selects `attempts[request.FollowUps.Count / 2]`. Document cache_control is a flag on `LlmDocument`; reference `dotnet/tools/LlmSpike/Program.cs:96` for `CacheControlEphemeral`. Provider types must not cross the seam. `LlmResponse.Cost` is set by the accounting decorator; each attempt goes through the same path.

### `EvalEndpoint.cs` (controller, request-response)
Keep the order in `HandleAsync` (lines 75-143): auth hash compare first, 415, 413, strict `ReadFromJsonAsync` with `JsonException` mapped to `Invalid(...)`, then field checks.
```csharp
private const string ContractVersion = "1";   // line 29; bump to "2" (RESEARCH open question 6)
if (request.ContractVersion != ContractVersion) return Invalid("contract_version", $"must be \"{ContractVersion}\".");
```
Add optional `reference_date` request field validated like the other fields; echo in `EvalEffective`. Typed failures stay HTTP 200 with `outcome.status`.

### `ScriptedHost/Program.cs` (test host)
Lines 26-34 key by SHA256 of the document; lines 36-43 handle `failure`; 45-61 build `LlmResponse` via the `OptionalString`/`Count` helpers. Add an optional `"attempts": [...]` array; if present, take `attempts[followUps.Count / 2]` as `root`, otherwise use the file's root, so existing files keep working.

### `ids.py` (utility, transform)
Add `make_cpf(rng)` / `is_valid_cpf` (mod-11, weights 10..2 and 11..2, reject repeated digits) with the same shape as `cnpj_check_digits` / `is_valid_cnpj` (lines 36-46). Add `is_valid_access_key` using `access_key_check_digit` (65-72). `make_cnpj(rng, alphanumeric=...)` (49-62) already supports the alphanumeric form. Tests come from the vector file, not from code under test.

### `money.py` (utility)
Decimal-only, no I/O, `Decimal` + `InvalidOperation`, strings only (lines 8-19). Add `round_half_up(value: Decimal, places=2)` with `quantize(..., rounding=ROUND_HALF_UP)`.

### `grader.py` (service, batch)
Frozen dataclass `GroundTruth` (lines 33-42) plus `_text(parent, "a/b")` namespaced lookup (45-49) and `load_ground_truth`. Extend with the new scalars (totals as `Decimal`, `item_count`, `installment_count`, party `ie/uf/tax_id_kind`, `operation_nature`), `DEFAULT_TOLERANCE = Decimal("0.01")`. Rename `total_amount` to `totals.invoice_total` (also in `summary.FIELDS` and `test_e2e_fake.py`). Imports no HTTP client. Generated `carimbo_models` is never edited.

### Tests
- xUnit: `Assert` only (no FluentAssertions). Constants plus `Respond(text)`/`ExtractAsync` helpers in `ExtractorTests.cs`; theory data via `TheoryData<T>` (lines 16-24). Vector-file theory reads `data/vectors/validator-vectors.json` from the repo root (follow how `SchemaSnapshotTests` locates committed files).
- pytest: `test_synthetic_only.py` already checks `access_key_check_digit` against nfelib sample keys; model `test_vectors.py` on it.
- Pitfall 9: budget one task for a shared hand-checked valid-invoice JSON fixture used by both stacks.

## Shared Patterns

### Never throw, typed results
**Source:** `InvoiceExtractor.cs` 132-138 and 169-183. Validators return findings, extractor catches only gateway/parse exceptions. **Apply to:** Validation, extractor, endpoint.

### Wire decimal discipline
**Source:** `Wire.cs` 28-52. Pattern check, then restricted `NumberStyles`, invariant culture. **Apply to:** `Quantity`, `Rate`, any new decimal.

### Explicit digit classes
**Source:** `Invoice.cs` 8-10. `[0-9]` only, never `\d` or `char.IsDigit`. **Apply to:** all patterns and check-digit code.

### Generated artifacts
`just schema` after record changes, `just datagen` after spec changes; `schema-check` and `datagen-check` gate CI. Never hand-edit `schema/*.json`, `generated.py`, `data/skeleton/*`.

### Schema budget
`SchemaBudget.EnsureWithin` (called at `ExtractionContract.Build`) fails at more than 24 optional or 16 union properties. Prefer required fields (`"0.00"` for blank tax columns).

## No Analog Found

| File | Role | Reason |
|---|---|---|
| `data/vectors/validator-vectors.json` | data | no hand-curated shared JSON exists; shape proposed in RESEARCH "Vector file shape" (lines ~464-490) |
| XML to `Invoice` mapper + field-mapping doc | service/doc | no .NET XML reader exists; follow the mapping table in RESEARCH (lines ~319-343) and the Python `load_ground_truth` XPaths in grader.py |
| Repair prompt text | config | none; RESEARCH Pattern 2 and Pitfall 7 |
| Normal-regime/IPI/dup generation | generator | datagen is Simples-only today; extend `spec.py`/`nfe_xml.py`, validate against nfelib XSD |

## Metadata

**Analog search scope:** `dotnet/src`, `dotnet/tests`, `dotnet/tools`, `python/src`, `python/tests`
**Files read:** Invoice.cs, Wire.cs, InvoiceExtractor.cs, LlmContracts.cs, EvalEndpoint.cs (1-150), ScriptedHost/Program.cs, ids.py, money.py, grader.py (1-60), CanonicalSchema.cs (80-100), ExtractorTests.cs (1-40)
**Not read in detail (planner should open):** AnthropicLlmGateway.cs, SchemaBudget.cs, ModelSchemaProjector.cs, spec.py, nfe_xml.py, danfe.py, runner.py, summary.py
**Pattern extraction date:** 2026-10-08
