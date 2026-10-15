---
phase: 03-replayable-runs
reviewed: 2026-10-15T00:00:00Z
depth: standard
files_reviewed: 8
files_reviewed_list:
  - AGENTS.md
  - README.md
  - docs/DECISIONS.md
  - dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs
  - dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs
  - dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs
  - dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs
  - justfile
findings:
  critical: 0
  warning: 0
  info: 2
  total: 2
status: issues_found
---

# Phase 3: Code Review Report (incremental, plan 03-11)

**Reviewed:** 2026-10-15
**Depth:** standard
**Files Reviewed:** 8
**Status:** issues_found (informational only)

## Summary

This report covers only the 03-11 delta since `16b0195`: the new allowlisted span attribute `carimbo.llm.repair_prompt_version`, decision D-33, the README cache-key list correction, and `docs-check` extended to D-33. The prior full-phase review is in git history at `16b0195`.

The change is correct and small.

- **Production code.** `TracingLlmGateway` sets the tag only when `request.RepairPromptVersion` is non-null. That field is null on the initial attempt and `repair-001` on every repair attempt (`InvoiceExtractor.cs:261-277`). The tag is set before the `try`, so error spans carry it too. The constant is added to `AllowedTags`, so the allowlist invariant (no content in spans) holds. The value is a catalogue label, not prompt text.
- **D-33 rule.** The rule "repair label if present, else prompt label" matches the extractor's `attempts[].prompt_version` logic at `InvoiceExtractor.cs:261`. `AssertCallLabelsMatchAttempts` in the Api tests pins this against real responses, including the `extract-002` override.
- **Verification.** `dotnet test` passes for Carimbo.Llm.Tests (141/141) and Carimbo.Api.Tests (199/199). `just docs-check` prints "docs ok" with D-33 in its list.
- **README cache-key list.** The list in `README.md:31` matches `dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs` field for field:

  | README wording | `LlmCacheKey` field |
  |---|---|
  | model | `model` |
  | max tokens | `max_tokens` |
  | the document cache flag | `cache_document` |
  | prompt and repair prompt versions | `prompt_version`, `repair_prompt_version` |
  | hashes of the prompt text and schema | `prompt_sha256`, `schema_sha256` |
  | the document's hash and media type | `document_sha256`, `document_media_type` |
  | the repair conversation | `follow_ups` |
  | the replicate salt | `replicate_salt` |

  The only key member the README omits is the constant `key_version`, which is not a request property. The old "effort" entry is gone, and `LlmRequest` has no such keyed property.
- **Doc counts.** The D-33 range is consistent across `AGENTS.md:51`, `README.md:22`, `README.md:175` and `justfile:54-58`.

No bugs, security issues or blocking defects were found.

## Info

### IN-01: Sleep used to order spans in a unit test

**File:** `dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs:229`
**Issue:** `await Task.Delay(2, Ct)` separates the two calls so that `SpanScope.Spans` (ordered by `StartTimeUtc`) returns them initial-first. The delay is unnecessary. The calls are sequential, and `OrderBy` is stable over a `ConcurrentQueue` filled in stop order, which is also call order, so ties on `StartTimeUtc` still resolve correctly. A timing-based wait in a test is a small flake and readability smell.
**Fix:** Remove the delay, or look the spans up by tag (`spans.Single(s => s.GetTagItem(LlmTelemetry.RepairPromptVersion) is null)`) rather than by index.

### IN-02: D-31 attribute list was not annotated as refined

**File:** `docs/DECISIONS.md:679` (D-31) and `docs/DECISIONS.md:714` (D-33)
**Issue:** D-31 still enumerates the attribute allowlist ("... prompt version and replicate salt ...") without the new name. D-33 is titled "refines D-31", so the information is correct, but a reader of D-31 alone gets a stale allowlist. This is consistent with the project's superseding-entries policy, so it is only a discoverability nit.
**Fix:** Optionally add "(see D-33 for `carimbo.llm.repair_prompt_version`)" to the D-31 attribute bullet. Do not rewrite D-31 otherwise.

---

_Reviewed: 2026-10-15_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
