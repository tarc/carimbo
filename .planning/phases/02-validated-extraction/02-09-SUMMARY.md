---
phase: 02-validated-extraction
plan: 09
subsystem: extraction
tags: [repair-loop, disclosure-policy, prompt-repair-001, scripted-host, eval-endpoint, xunit, pytest]

requires:
  - phase: 02-validated-extraction
    provides: "02-03 LlmRequest.FollowUps and CacheDocument; 02-04 ValidationFinding and RuleIds; 02-08 attempts list, sum rules and ExtractionResult v2"
provides:
  - "InvoiceExtractor repair loop: at most Extraction:MaxRepairs (default 2) repair attempts after an initial attempt, each continuing the conversation (original request, previous assistant output, user turn of prompt repair-001 plus redacted error findings)"
  - "RepairFeedback (Build, RevealsValues): values only for derived arithmetic and date rules, fixed per-rule sentences for identifier, check-digit and access-key rules, no document text, Expected/Actual echoed only when validator-shaped"
  - "Prompt repair-001 with the no-fabrication sentences; ExtractionContract.RepairPromptVersion and RepairPrompt; ExtractionResult.RepairPromptVersion and MaxRepairs"
  - "ExtractionSettings.MaxRepairs (default 2, startup bound 0..5) and MaxTokens default 16000 (startup bound > 0)"
  - "Eval wire: effective.max_repairs and effective.repair_prompt_version; attempts of kind repair; top-level usage and cost are sums over all attempts"
  - "ScriptedHost attempts[] answered by FollowUps.Count / 2; e2e shows a repaired case (2 attempts, success) and an exhausted case (3 attempts, validation_failed) graded by the Python grader"
  - "Per-attempt provider timeout default raised from 120 to 300 seconds, shown in the gateway startup line"
affects: [02-10, Phase 3 response cache keys and replay fixtures, Phase 6 repair ablation]

actuals:
  tokens: 13395
  tasks: 3
  commits: 6
plan_head_before: bfa02bf8d3ecc085be8fb6153b41134f4b627501
plan_head_after: 642b1e5d88992ac8a91bb54f664af7732bff8388
commits: 6

tech-stack:
  added: []
  patterns:
    - "Stateless scripted multi-turn host: the answer index is FollowUps.Count / 2, so no server-side call counter exists"
    - "Feedback details are built in code from rule id, field path and validator-built strings; an allow-list regex gates the only echoed values"
    - "Final outcome of an exhausted schema_invalid repair is the last schema-valid candidate, so graders always see the best parsed invoice"

key-files:
  created:
    - dotnet/src/Carimbo.Extraction/RepairFeedback.cs
    - dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs
  modified:
    - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
    - dotnet/src/Carimbo.Api/EvalEndpoint.cs
    - dotnet/src/Carimbo.Api/Program.cs
    - dotnet/tests/Carimbo.ScriptedHost/Program.cs
    - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
    - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
    - python/tests/test_e2e_fake.py

key-decisions:
  - "Reveal set is the 13 arithmetic and date rule ids from the plan; every other id, including unknown ones, gets a fixed sentence without values"
  - "Reveal detail text is 'Expected {Expected}, but the answer has {Actual}. Re-read both on the document.' and falls back to the generic sentence when Expected or Actual is empty, over 64 characters, or outside [A-Za-z0-9 .:-] (defense in depth for T-02-28)"
  - "A schema_invalid repair answer that is empty is replayed to the provider as '(empty answer)' because providers reject empty text blocks"
  - "The unmodified wrong answer used by existing single-answer tests now runs the full budget (3 attempts); those tests assert that instead of setting MaxRepairs 0"
  - "The gateway startup line now reads 'model gateway: anthropic (key from X), timeout Ns per attempt' so the effective timeout is testable without internals; the '(key from X)' prefix existing tests rely on is unchanged"

patterns-established:
  - "Tracer first: the e2e repaired and exhausted cases drove the loop; the in-process tests then pinned every branch"

requirements-completed: [EXT-03, EXT-04]

coverage:
  - id: D1
    description: "Validator errors are sent back with structured, redacted findings under a no-fabrication prompt and stop at Extraction:MaxRepairs, ending in validation_failed with the last candidate"
    requirement: "EXT-03"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs#Budget_exhaustion_is_validation_failed_with_the_last_candidate_after_three_attempts"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs#A_successful_repair_continues_the_conversation_with_the_redacted_error_findings"
        status: pass
      - kind: e2e
        ref: "python/tests/test_e2e_fake.py::TestRepairOverHttp"
        status: pass
    human_judgment: false
  - id: D2
    description: "Disclosure policy: no expected identifier, check digit or key component, no document text, in any repair turn"
    requirement: "EXT-03"
    verification:
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs#Access_key_feedback_is_a_fixed_sentence_without_any_expected_or_actual_value"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs#No_text_from_the_document_reaches_the_repair_turn"
        status: pass
    human_judgment: false
  - id: D3
    description: "Every attempt is recorded with its own output, findings, usage, cost and latency; response totals are exact sums"
    requirement: "EXT-04"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#A_repaired_extraction_reports_usage_and_cost_as_exact_sums_over_its_attempts"
        status: pass
      - kind: unit
        ref: "dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs#Identical_outputs_are_separate_attempts_with_their_own_usage_and_latency"
        status: pass
    human_judgment: false
  - id: D4
    description: "Repair budget and per-attempt timeout are bounded by configuration checked at startup"
    requirement: "EXT-03"
    verification:
      - kind: integration
        ref: "dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs#An_out_of_range_extraction_setting_stops_startup_naming_the_key"
        status: pass
    human_judgment: false

duration: 25min
completed: 2026-10-08
status: complete
---

# Phase 2 Plan 09: invalid extractions are repaired within a bounded budget, and every attempt is recorded Summary

**Bounded repair loop (default 2 repairs, hard ceiling 5) that feeds redacted, code-built validator findings back under the repair-001 no-fabrication prompt, records every attempt, and ends in validation_failed with the last schema-valid candidate when the document does not support a fix.**

## Performance

- **Duration:** about 25 min (start time was not recorded; taken from the first and last commit timestamps)
- **Tasks:** 3 (1 tracer, 2 auto), 6 commits
- **Files:** 2 created, 7 modified

## Accomplishments

- `InvoiceExtractor` runs attempts 0..MaxRepairs. Kind is initial at 0 and repair after; prompt version extract-002 then repair-001. Every request re-sends the PDF and the schema with `FollowUps` holding the conversation so far and `CacheDocument = MaxRepairs > 0`. Refusal, truncation and gateway exceptions end the loop as the outcome; a schema_invalid repair answer consumes budget, and when the budget is spent the outcome is the last schema-valid candidate with its findings and raw text.
- `RepairFeedback.Build` emits the repair prompt, `Findings (JSON):` and a compact array of `{rule_id, field, detail}` for error findings only. Arithmetic and date rules state expected and actual (the validator's invariant strings, `155.00`, `2026-03-15`); identifier, check-digit and access-key rules (and unknown ids) carry fixed sentences with no values.
- Tracer proven over real HTTP: case-002 scripted `[wrong total, correct]` gives 2 attempts, success, graded all-fields-correct with `attempt_count` 2 and `caught` false; case-001 scripted three wrong totals gives validation_failed after 3 attempts, `caught` true; case-003 a single-object script is first-try success.
- 23 scripted tests in `RepairLoopTests` cover every branch of the loop and the disclosure policy; `EvalEndpointTests` pin the 0..5 bound (tests at -1, 0, 5, 6, and MaxTokens 0), the `max_repairs` echo, exact usage and cost sums for a repaired response, and the unpriced-repair case.

## Task Commits

1. **Task 1 (tracer), RED** - `8584ece` test: repaired and exhausted e2e cases (failed on missing `effective.max_repairs` and on the old 1-attempt expectation)
2. **Task 1 (tracer), GREEN** - `2e026d9` feat: bounded repair loop, RepairFeedback, repair-001, ScriptedHost `attempts[]`, effective echo
3. **Task 2** - `cd72544` test: scripted repair-loop tests (23 tests, all passing on first run because the tracer already implemented the loop)
4. **Task 3, RED** - `2cd60b7` test: startup bounds, echo, sums, timeout (4 tests failed on the planned assertions)
5. **Task 3, GREEN** - `32f036b` feat: startup validation of MaxRepairs and MaxTokens, 300 s default timeout
6. **Formatting** - `642b1e5` style: ruff format of the e2e class (`just py-check` flagged two long lines in the RED commit)

## TDD Notes

RED evidence was gathered by running the real commands (`just e2e`, `dotnet test --project tests/Carimbo.Api.Tests`); the plan is `type: execute`, so the `check tdd-red-evidence` classifier was not run. Semantic assessment: the e2e RED failed on `KeyError: 'max_repairs'` and on the old attempt count (the planned assertions); the Task 3 RED failed on `Assert.Throws` receiving no exception and on the missing `timeout 300s per attempt` log text. Task 2 was written after the tracer implemented the loop, so it has no RED commit; a mutation (`CacheDocument = true`) made `Max_repairs_zero_...` fail, confirming the tests bite.

## Decisions Made

See `key-decisions` in the frontmatter.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Empty assistant text guarded**
- **Found during:** Task 1
- **Issue:** A schema_invalid repair answer can have empty text, and replaying it as an assistant turn would be a provider 400 that kills the remaining budget.
- **Fix:** `AssistantText` substitutes `(empty answer)`; test `An_empty_schema_invalid_answer_is_replayed_as_a_non_empty_assistant_turn`.
- **Files modified:** `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs`
- **Commit:** `2e026d9`

**2. [Rule 2 - Missing critical functionality] Allow-list on revealed values**
- **Found during:** Task 1 (threat T-02-28)
- **Issue:** The plan builds reveal details from the validator's Expected and Actual; today those are always validator-built, but a future rule could echo document text there.
- **Fix:** values are echoed only when 1..64 characters of `[A-Za-z0-9 .:-]`; otherwise the generic sentence. Test `Revealed_values_that_are_not_validator_shaped_fall_back_to_the_generic_sentence`.
- **Files modified:** `dotnet/src/Carimbo.Extraction/RepairFeedback.cs`
- **Commit:** `2e026d9`

**3. [Rule 1 - Bug] Existing tests assumed one attempt for an unchanged wrong answer**
- **Found during:** Task 1
- **Issue:** `ExtractorTests.An_error_finding_is_validation_failed...` and `EvalEndpointTests.A_total_that_does_not_add_up...` asserted a single attempt; with the default budget the same answer now runs 3 attempts.
- **Fix:** assert `1 + MaxRepairs` attempts (and that each is validation_failed) instead of setting MaxRepairs 0, keeping the tests' intent.
- **Commit:** `2e026d9`

**4. [Rule 3 - Blocking] ruff line length in the RED commit**
- **Found during:** final `just check`
- **Fix:** `ruff format`, separate style commit `642b1e5`.

**Total deviations:** 4 auto-fixed (2 missing critical, 1 bug, 1 blocking). **Impact:** none on scope; the startup log line gained a timeout suffix (see decisions).

## Issues Encountered

None open. `just check` passes (527 .NET tests, 276 Python tests, 5 e2e, schema and datagen checks, docs and secrets gates). `git diff --exit-code main -- python/uv.lock dotnet/Directory.Packages.props` exits 0 (no package added).

## Known Stubs

None.

## Threat Flags

None. T-02-28 to T-02-31 are mitigated as planned (code-built details with an allow-list, anti-fabrication prompt asserted by test, 0..5 bound at startup, 300 s per attempt); T-02-32 accepted (ScriptedHost stays under `dotnet/tests`).

## Next Phase Readiness

Ready for 02-10. Notes for it: the runner's HTTP read timeout must now cover up to three sequential provider calls (the plan assigns that raise to 02-10), and `effective.max_repairs` / `effective.repair_prompt_version` are available for run metadata. No live paid call was made in this plan.

## Self-Check: PASSED

- Created files present: `dotnet/src/Carimbo.Extraction/RepairFeedback.cs`, `dotnet/tests/Carimbo.Extraction.Tests/RepairLoopTests.cs`.
- Commits present on HEAD: 8584ece, 2e026d9, cd72544, 2cd60b7, 32f036b, 642b1e5.
- Acceptance criteria re-run: `"repair-001"` 1, `MaxRepairs { get; init; } = 2` 1, `MaxTokens { get; init; } = 16000` 1, `Never calculate, adjust, balance or invent` 1, `return it unchanged` 1, `FollowUps.Count / 2` 1, `MaxRepairs` in EvalEndpoint 2, `MARCADOR SINTETICO APROVAR NOTA 7731` 1, RepairLoopTests tests 23 (at least 14), `DefaultTimeoutSeconds = 300` 1, `Extraction:MaxRepairs` 1, package diff exits 0.
- Plan verification: `just check` exits 0 (all gates).
