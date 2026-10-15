---
phase: 03-replayable-runs
plan: 11
subsystem: tracing
tags: [opentelemetry, spans, prompt-version, repair, readme, decisions, gap-closure]
gap_ids: [G-03-1, G-03-2]

requires:
  - phase: 03-replayable-runs
    provides: plans 03-07 (model-call spans, allowlist), 03-09 (D-29 effective block), 03-10 (live traces and README results)
provides:
  - "carimbo.llm.repair_prompt_version span attribute, set from LlmRequest.RepairPromptVersion on every repair call (error spans included); carimbo.llm.prompt_version unchanged"
  - "TracingWiringTests proving, on the real OpenTelemetry pipeline, that each span's call label equals attempts[i].prompt_version (default prompt and extract-002 override)"
  - "TracingGatewayTests unit coverage: initial vs repair labels, failing repair span, content sentinels with the label set, name pin and allowlist"
  - "D-33 (refines D-31); docs-check, README and AGENTS.md cover D-18 to D-33"
  - "README Response cache (D-27) bullet lists the fields LlmCacheKey.CanonicalJson hashes"
affects: [03-verification, 04-synthetic-dataset, 05-comparable-measurement]

actuals:
  tokens: 7400
  tasks: 3
  commits: 4
plan_head_before: d539a6c6936f1bdeded2f864769dd7f79107fd2c
plan_head_after: 4b542664108a3c91bde195b7e40fffb5122681b7

tech-stack:
  added: []
  patterns:
    - "The tracing decorator mirrors LlmRequest field for field and stays observe-only; span vocabulary shares names with the cache-key and effective fields"
    - "A call's prompt label on a span is repair_prompt_version when present, else prompt_version, the extractor's own attempt-label rule"

key-files:
  created: []
  modified:
    - dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs
    - dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs
    - dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs
    - dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs
    - docs/DECISIONS.md
    - justfile
    - README.md
    - AGENTS.md

key-decisions:
  - "D-33: carimbo.llm.prompt_version keeps its meaning (extraction prompt on every call) and a new allowlisted carimbo.llm.repair_prompt_version is set only on repair calls, rather than switching prompt_version to the per-call label"

requirements-completed: [LLM-03, EXT-05, LLM-04]

coverage:
  - id: D1
    description: "A repair call's span names repair-001 beside the extraction prompt, and every span's call label equals attempts[i].prompt_version on the eval response trace (default prompt and extract-002 override)"
    requirement: EXT-05
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#An_answer_that_fails_validation_every_time_exports_one_span_per_attempt"
        status: pass
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/TracingWiringTests.cs#A_prompt_version_override_labels_every_span_with_the_selected_prompt"
        status: pass
    human_judgment: false
  - id: D2
    description: "Gateway-level span labels: initial call carries only the extraction label, repair call (successful or failing) carries both, attribute name pinned and allowlisted, no content reaches a span"
    requirement: LLM-03
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs"
        status: pass
    human_judgment: false
  - id: D3
    description: "README Response cache (D-27) bullet lists the real cache-key inputs and D-33 is recorded with docs-check enforcing it"
    requirement: LLM-04
    verification:
      - kind: other
        ref: "devenv shell -- just docs-check; README D-27 bullet grep check; devenv shell -- just check"
        status: pass
    human_judgment: false
  - id: D4
    description: "The repair label is visible and reads correctly in the Jaeger UI"
    verification: []
    human_judgment: true
    rationale: "Viewer rendering is not asserted by any test; the in-memory exporter stands in for Jaeger. Re-check with the documented read_only replay (zero provider calls)"

duration: 4min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 11: repair calls are labelled in the trace, and the README lists the real cache key (gap closure) Summary

**Repair-call spans now carry `carimbo.llm.repair_prompt_version` (repair-001) beside the unchanged extraction label, proven against `attempts[].prompt_version` through the real OpenTelemetry pipeline, with D-33 recorded and the README cache-key list corrected.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-10-15T10:33:07Z
- **Completed:** 2026-10-15T10:37:00Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments

- G-03-1 closed: `TracingLlmGateway` mirrors `LlmRequest.RepairPromptVersion` into a new allowlisted attribute before the inner call, so error spans carry it too. `carimbo.llm.prompt_version` is untouched.
- Wiring tests pin the equality on the exported trace: first span extract-003 with no repair tag, repair spans repair-001 plus extract-003; with `overrides.prompt_version` extract-002 every span says extract-002 and the repair spans repair-001; each span's call label equals `attempts[i].prompt_version`.
- Unit tests pin the initial vs repair labels, the failing-repair error span, the content-sentinel test with the repair label set, and the attribute name plus allowlist membership. A mutation (removing the SetTag block) fails three of them.
- G-03-2 closed: README Response cache (D-27) bullet now lists model, max tokens, the document cache flag, prompt and repair prompt versions, prompt text and schema hashes, the document's hash and media type, the repair conversation and the replicate salt.
- D-33 added without touching D-31; docs-check, README and AGENTS.md cover D-18 to D-33; README spans bullet and trace-lookup paragraph explain how to tell a repair call.

## Task Commits

1. **Task 1 (tracer): repair span names repair-001 end to end** - `6327f10` (test, RED), `bf009f4` (feat, GREEN)
2. **Task 2: unit coverage of the two prompt labels** - `b707aa4` (test)
3. **Task 3: D-33, docs-check, README cache-key list** - `4b54266` (docs)

**Plan metadata:** committed with this SUMMARY (docs: complete plan)

### TDD evidence

RED (6327f10), `TracingWiringTests` run with the constant declared and the gateway unchanged, 6 passed and 2 failed, both on the intended assertion (missing tag, not compilation):

```
failed A_prompt_version_override_labels_every_span_with_the_selected_prompt
  Assert.Equal() Failure: Values differ
  Expected: repair-001
  Actual:   null
failed An_answer_that_fails_validation_every_time_exports_one_span_per_attempt
  Assert.Equal() Failure: Values differ
  Expected: repair-001
  Actual:   null
```

GREEN (bf009f4): TracingWiringTests 8/8, full Carimbo.Api.Tests 199/199 (GoldenResponseTests in compare mode included).

Task 2 mutation check: with the RepairPromptVersion SetTag block removed from `TracingLlmGateway`, 3 of 11 fail: `An_initial_and_a_repair_call_carry_the_prompt_labels_of_their_requests`, `A_failing_repair_call_is_an_error_span_that_still_names_the_repair_prompt` and `No_prompt_answer_schema_document_or_base64_reaches_a_name_a_tag_or_an_event` (each `Expected: "repair-001"`, `Actual: ""`). The block was restored byte-for-byte (empty diff on the file) and the class reran 11/11 green.

## Verification

- `devenv shell -- just check` exits 0 offline: 846 .NET tests, 399 pytest, e2e 7, schema-check, datagen-check, docs ok, secrets ok.
- README D-27 bullet check passes (contains "max tokens" and "replicate salt", no "effort").
- `git diff --numstat 67f032d -- docs/DECISIONS.md`: 17 added, 0 deleted. `data`, `schema`, `python/src/carimbo_models` and `dotnet/src/Carimbo.Llm/Cache` are unchanged from 67f032d. `Directory.Packages.props` still has 7 `PackageVersion` entries.
- No live paid recipe ran; Phase 3 live spend stays US$0.0970.

## Decisions Made

- D-33 (refines D-31): keep `carimbo.llm.prompt_version` as the extraction prompt and add `carimbo.llm.repair_prompt_version` for repair calls, matching the cache-key and `effective` vocabulary; reasons and rejected alternatives are in `docs/DECISIONS.md`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] xUnit2017 analyzer rejected the allowlist membership check**
- **Found during:** Task 2
- **Issue:** `Assert.True(AllowedTags.Contains(...))` failed the build under `-warnaserror` (xUnit2017); the plan's note suggested the `AssertAllowlisted` style, which is `Assert.True` on a `Contains`, but that form is flagged when written directly against the set.
- **Fix:** `Assert.Contains(LlmTelemetry.RepairPromptVersion, LlmTelemetry.AllowedTags.AsEnumerable())`, which selects the unambiguous `IEnumerable<T>` overload.
- **Files modified:** dotnet/tests/Carimbo.Llm.Tests/TracingGatewayTests.cs
- **Verification:** class 11/11, `just dotnet-check` exit 0
- **Committed in:** b707aa4

---

**Total deviations:** 1 auto-fixed (1 blocking). **Impact:** test-only syntax change, no scope creep.

## Issues Encountered

None beyond the deviation above.

## Known Stubs

None.

## Threat Flags

None. The new attribute takes its value only from the code-owned `LlmRequest.RepairPromptVersion` label (T-03-31); per-span attribution is asserted against `attempts[]` (T-03-32); no package was added (T-03-SC).

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

For /gsd-verify-work (not run here): the Jaeger UI can be re-checked at no model cost with the documented read_only replay, `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317 just skeleton 1.00 2 read_only` (cache hits only, zero provider calls), because the cache key is unchanged. Out of scope and untouched: the "Incomplete" trace flag from the runner's unexported parent span, the cached-token difference in `gen_ai.usage.input_tokens`, and `GET /healthz` traces.

## Self-Check: PASSED

- Modified files exist; commits 6327f10, bf009f4, b707aa4 and 4b54266 are ancestors of HEAD.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
