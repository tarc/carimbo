---
phase: 03-replayable-runs
verified: 2026-10-15T10:15:00Z
status: human_needed
score: 5/5 must-haves verified
covered_files:
  - ".planning/phases/03-replayable-runs/03-01-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-01-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-02-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-02-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-03-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-03-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-04-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-04-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-05-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-05-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-06-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-06-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-07-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-07-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-08-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-08-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-09-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-09-SUMMARY.md"
  - ".planning/phases/03-replayable-runs/03-10-PLAN.md"
  - ".planning/phases/03-replayable-runs/03-10-SUMMARY.md"
  - "README.md"
  - "compose.yaml"
  - "dotnet/src/Carimbo.Api/EvalEndpoint.cs"
  - "dotnet/src/Carimbo.Api/Program.cs"
  - "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
  - "dotnet/src/Carimbo.Extraction/PromptCatalog.cs"
  - "dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs"
  - "dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs"
  - "dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs"
  - "dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs"
  - "dotnet/src/Carimbo.Llm/Telemetry/LlmTelemetry.cs"
  - "dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs"
  - "justfile"
  - "python/src/carimbo_evals/contract.py"
  - "python/src/carimbo_evals/runner.py"
  - "python/src/carimbo_evals/summary.py"
covered_digest: "v3:sha256:422377e28500b3b89ddd41e993c4a407791aa371a308de1d722b00fd5c364895"
behavior_unverified: 0
overrides_applied: 0
re_verification: false
gaps: []
human_verification:
  - test: "Open http://localhost:16686 (Jaeger is still running with the three seeding traces in memory), choose service carimbo-api, and open the trace for case-001 (f720b1d530a4adac18a1ad9ce440ff95) and case-002 (d65bb5551a0bd401f1d5eb7958d8b0ab)"
    expected: "Each trace shows a POST /eval/extractions server span with three `chat claude-haiku-4-5` child spans (case-003, e1408a0541e6c473885d513c65c2d63e, shows one); each span carries model, token counts and carimbo.llm.cost_usd and no prompt, answer or PDF text"
    why_human: "The API-level lookup passed (checked again in this run), but the visual UI check that plan 03-10 left pending (D-31 and the 03-07 'a reviewer can find the spans' deliverable) needs a person at a browser."
  - test: "Read the README section 'Phase 3 live skeleton runs' and the trace-viewer section, then follow `docker compose up -d` and `just skeleton 1.00 2 read_only` as a reviewer would"
    expected: "The prose is clear, the replay is labelled as a replay, and the seeding cost (US$0.0970) is presented as the cost of extraction, not the replay's zero"
    why_human: "03-02 D2 and 03-06 D-items are wording and clarity judgments; tests only check that the command and URLs appear."
  - test: "Confirm the LLM-04 environment policy in Program.cs ResolveCache: outside Development and Eval, a non-blank Llm:Cache:Directory fails startup even when Llm:Cache:Mode is off, and a mode other than off fails startup"
    expected: "You accept this strictness (03-03 D6). It satisfies the roadmap wording 'production configuration runs with the cache off' and is pinned by CacheConfigurationTests."
    why_human: "A policy preference that no test can decide. Not a gap: the criterion is met either way."
  - test: "Confirm D-30: extract-002 is restored byte-exact as a selectable prompt, with its known label defects G-02-1 and G-02-2, as the second catalogued prompt (03-08 D5)"
    expected: "You prefer a real earlier prompt over a test-only second prompt, or you record a superseding decision."
    why_human: "A design preference. The restore is byte-exact and pinned by SHA-256 (e1b74d10...), and the override demonstrably changes the cache key."
  - test: "Review the judgment-tier prohibitions below (all recorded `resolved` in the plans) and accept or reject my non-authoritative verdicts: unverified-prohibition, human review recommended"
    expected: "See the Prohibitions table; each has code or test evidence and a PASS verdict from me."
    why_human: "ADR-550 D3/D4: judgment-tier prohibitions get a non-authoritative LLM verdict and a human-review flag, never a silent pass."
---

# Phase 3: Replayable Runs Verification Report

**Phase Goal:** As a developer, I want to replay eval reruns from a request-hash cache and see every model call as a costed span in a local trace viewer, so that I can iterate on prompts and graders without paying twice and can trace any case back to its model calls.
**Verified:** 2026-10-15
**Status:** human_needed
**Re-verification:** No, initial verification

All five roadmap success criteria are achieved in the code and in the committed live evidence. No gaps. The status is `human_needed` only because of one pending visual check (Jaeger UI), two policy confirmations, and the judgment-tier prohibition flags.

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | read_only rerun makes zero provider calls and reports original latency and cost; refresh calls the model; different model, prompt version or salt misses; truncated and refused answers never cached; production runs with the cache off | VERIFIED | `CachingLlmGateway.cs`: `TryRead` before any inner call, `ReadOnly` miss throws `LlmFailureKind.CacheMiss`, `Refresh` skips the read, `TryStore` stores only `StopReason == EndTurn`. A hit returns `HttpAttempts = 0` with the entry's stored latency, usage and cost. `LlmCacheKey.cs` hashes model, max_tokens, cache_document, prompt_version, repair_prompt_version, prompt, schema, document, follow-ups and replicate_salt; `CacheMode` is deliberately excluded and covered by a property-coverage test. `Program.cs ResolveCache` returns no cache outside Development/Eval and throws if a directory or non-off mode is configured there. Live: replay run `skeleton-20261015T091111Z` has 7/7 attempts `status: hit`, `http_attempts: 0`, `latency_ms` and `cost_usd` identical to the seeding run per attempt (e.g. 14917 ms, 0.01200925), `spent_usd 0.00000000`, `notional_usd 0.09700510` equal to the seeding run's `spent_usd`. 842 .NET tests pass (re-run in this verification). |
| 2 | One `docker compose` command starts a local trace viewer; looking up an eval response's trace ID shows one span per model call with model, tokens, cost and no prompt, response or PDF content | VERIFIED (UI visual pending, see human item 1) | `compose.yaml`: `cr.jaegertracing.io/jaegertracing/jaeger:2.22.0`, pinned, ports 16686/4317/4318 bound to 127.0.0.1. `TracingLlmGateway` is the outermost decorator, one `chat <model>` span per `CompleteAsync` (hits included); tag keys limited to `LlmTelemetry.AllowedTags`; no content-bearing key exists. `AlwaysOnSampler` set in code. OTLP exporter registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. I queried the running Jaeger (`/api/traces/<id>`) for the case-001, -002, -003 seeding traces and the case-001 replay trace: 3, 3, 1 and 3 `chat claude-haiku-4-5` spans, each with `gen_ai.request.model`, `gen_ai.usage.*`, `carimbo.llm.cost_usd`, `cache.status`, `http_attempts` (1 on the seeding spans, 0 on the replay spans). A leak scan of 25-character chunks of every attempt's `raw_output` against the Jaeger JSON found 0 matches, and no PDF base64 marker. |
| 3 | Transient provider errors are retried with backoff by exactly one policy; tests with a failing fake transport show it | VERIFIED | `AnthropicLlmGatewayOptions.MaxRetries` is passed to the SDK (default 2, bounded 0 to 5 at startup in `Program.cs`). The only `DelegatingHandler` is the attempt counter (visibility only). No Polly or Resilience references in any project file. `RetryOwnershipTests` run the composed chain Caching over CostAccounting over the gateway with a recording transport: a persistent 529 gives exactly 3 attempts and caches nothing; 529, 529, 200 gives one cached response with `HttpAttempts` 3 whose replay makes no HTTP attempt; a 400 is attempted once. `InvoiceExtractor` maps `LlmGatewayException` to a typed infrastructure failure and does not retry it. |
| 4 | Per-request overrides (model, prompt version, max repairs, cache mode, replicate salt) take effect and the response echoes the effective configuration; the prompt version is in the cache key and the result metadata | VERIFIED | `EvalEndpoint.cs` validates each override against its own `overrides.<field>` name (cache mode enum, salt 1 to 64 characters of `[A-Za-z0-9._-]`, catalogued prompt version only, model must be priceable, max_repairs a JSON integer 0 to 5) and builds `ExtractionOverrides`. `InvoiceExtractor` applies `Overrides.Model`, `MaxRepairs`, `PromptVersion` (via `WithPromptVersion`), `CacheMode` and `ReplicateSalt` to each `LlmRequest`. Live records echo `effective` (`cache_mode`, `max_repairs`, `model`, `prompt_version extract-003`, `repair_prompt_version`, `replicate_salt`, `pricing_version`, `schema_sha256`) and each attempt carries `prompt_version` (extract-003, repair-001) and a cache block with its key. The key includes `prompt_version` and `repair_prompt_version` (`LlmCacheKey.CanonicalJson`). `PromptCatalog` holds exactly extract-002 and extract-003, SHA-pinned. |
| 5 | pytest parses golden eval-response fixtures emitted by the .NET tests, so a breaking change to the response contract fails CI | VERIFIED | `data/contract/eval-responses/` holds 11 fixtures written by `GoldenResponseTests` (`just golden`); `python/tests/test_contract_golden.py` parses each with strict Pydantic models (`ConfigDict(extra="forbid", strict=True)` in `carimbo_evals/contract.py`). Mutation check in this run: the baseline `success.json` validates, and the same document with one added member is rejected with a `ValidationError`. A request fixture round trip (`build_payload` reproduces `eval-request.json`) covers the other direction. |

**Score:** 5/5 truths verified (0 present, behavior-unverified)

### Plan-level must-haves (spot-checked beyond the roadmap)

| Plan | Key truth | Status | Evidence |
|------|-----------|--------|----------|
| 03-01 | Pinned key vector, atomic temp-and-move writes, malformed entry is a miss | VERIFIED (with WR-01 exception) | `LlmCacheStore.Write` writes `<key>.<rand>.tmp` then `File.Move(overwrite)`; `PathFor` accepts only 64 lowercase hex; `TryRead` catches parse and I/O errors. Exception: JSON `null` in a required member is not treated as a miss (WR-01). |
| 03-03 | Cache default-on in Development/Eval at `.cache/llm`, refused elsewhere; `.cache/` untracked | VERIFIED | `Program.cs ResolveCache`; `.gitignore` line 12 `.cache/`; `git ls-files .cache` is empty. |
| 03-04 | Contract 3 only; cache override refused on a cache-off host | VERIFIED | `EvalEndpoint.cs` lines 126 (contract_version) and 159-170 (`overrides.cache_mode` ... "needs a response cache"). The route is mapped only in Development/Eval (`TryMap`). |
| 03-06 | incurred versus notional cost; runner caps incurred spend | VERIFIED | Live: replay `incurred_cost_usd 0.00000000`, run.json `spent_usd 0.00000000` and `notional_usd 0.09700510`; summary.json totals carry `cost_usd`, `incurred_cost_usd` and a cache block; `model_latency_ms` keeps the original latency. |
| 03-07 | Tracing adds no retry; hit and miss are separate spans; allowlisted attributes only | VERIFIED | `TracingLlmGateway` only observes; replay spans carry `cache.status hit`, `http_attempts 0`. |
| 03-10 | Live seeding at or under US$1.00, free replay, README evidence | VERIFIED | Seeding US$0.0970 (cap 1.00, phase total about US$0.58 across phases, under the US$5 cap); README lines 81 to 114 label the replay as a replay and cite the seeding cost. |

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `dotnet/src/Carimbo.Llm/Cache/LlmCacheKey.cs` | canonical key | VERIFIED | Fixed member order, versioned (`llm-cache-key-1`), wired from `CachingLlmGateway` |
| `dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs` | atomic file store | VERIFIED | Substantive, wired from `Program.cs` via `LlmCacheStore(cache.Directory)` |
| `dotnet/src/Carimbo.Llm/Cache/CachingLlmGateway.cs` | mode matrix decorator | VERIFIED | Wired in `DecorateGateway` |
| `dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs` and `LlmTelemetry.cs` | one costed span per call | VERIFIED | Outermost decorator; `AddSource(LlmTelemetry.SourceName)` registered |
| `dotnet/src/Carimbo.Extraction/PromptCatalog.cs` | versioned prompts | VERIFIED | Two SHA-pinned versions, default extract-003 |
| `compose.yaml` | pinned loopback Jaeger | VERIFIED | Pinned tag, loopback ports; Jaeger answering on 127.0.0.1:16686 |
| `python/src/carimbo_evals/contract.py` | strict response models | VERIFIED | `extra="forbid"`, mutation rejected |
| `data/contract/eval-responses/*.json` (11) | golden fixtures | VERIFIED | All outcome statuses plus hit, miss, unpriced, overrides |
| `evals/results/skeleton-20261015T09*` | committed live evidence | VERIFIED | Both run directories contain `cases.jsonl`, `run.json`, `summary.json`, `summary.md` |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Program.DecorateGateway` | Tracing over Caching over CostAccounting over adapter | decorator chain | WIRED | Order confirmed in the factory: cost accounting innermost, cache outside it (replay carries original cost), tracing outermost (hits are spans) |
| `InvoiceExtractor` | `LlmRequest` | overrides copied per request | WIRED | `PromptVersion`, `RepairPromptVersion`, `CacheMode`, `ReplicateSalt`, `Model` at lines 236-279 |
| `EvalEndpoint` | `ExtractionOverrides` | validated request overrides | WIRED | Lines 155-277 |
| `Program.RegisterTracing` | OTLP exporter | env var gate | WIRED | Exporter only when `OTEL_EXPORTER_OTLP_ENDPOINT` set (see WR-02) |
| `.NET GoldenResponseTests` | `python/tests/test_contract_golden.py` | `data/contract/eval-responses` | WIRED | Fixtures generated by `just golden`, parsed strictly |
| `justfile skeleton` | runner `--cache-mode` | third recipe argument | WIRED | Used for the live replay |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| Replay `attempts[].latency_ms`, `cost_usd` | cache entry `response` | files written by the live seeding run | Yes, identical to the seeding attempt values | FLOWING |
| Span tags | `LlmResponse.Usage`, `Cost` | provider response through cost accounting | Yes, match `cases.jsonl` usage | FLOWING |
| `summary.json` cache block, `model_latency_ms` | per-attempt records | runner records | Yes, 7/7 hits, original latency kept | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full .NET suite | `cd dotnet && dotnet test` | 842 passed, 0 failed, 0 skipped | PASS |
| Contract strictness | validate `success.json` with and without an added member via `carimbo_evals.contract.EvalResponse` | baseline ok, mutated rejected | PASS |
| Live replay evidence | analysis of `cases.jsonl` in both run directories | 7/7 hit, http 0, latency and cost equal to seeding | PASS |
| Trace lookup | `curl http://127.0.0.1:16686/api/traces/<id>` for four trace ids | 3, 3, 1 and 3 chat spans, allowlisted attributes, no content | PASS |
| Debt markers | grep TBD/FIXME/XXX/TODO/HACK in 41 files changed since 4864c12 | none | PASS |

The orchestrator's `just check` (exit 0) was consulted, not repeated. No paid recipe was run.

### Probe Execution

No probes: the repository has no `scripts/` directory and no plan declares one. Step 7c skipped.

### Requirements Coverage

Every ID in the phase header appears in at least one plan's `requirements:` frontmatter and in REQUIREMENTS.md (marked `[x]` and "Complete" in the traceability table). No orphaned Phase 3 requirement.

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|--------------|-------------|--------|----------|
| LLM-01 | 03-05 | Single non-stacked retry policy with backoff | SATISFIED | SDK `MaxRetries` is the only owner; no Polly; `RetryOwnershipTests` 3 attempts for a persistent 529 |
| LLM-03 | 03-07, 03-10 | One OTel span per call with model, tokens, cost, no content | SATISFIED | `TracingLlmGateway`, allowlist, live Jaeger lookup |
| LLM-04 | 03-01, 03-03, 03-04, 03-06, 03-10 | SHA-256 request-hash cache incl. salt, on in dev/eval, off in production | SATISFIED | `LlmCacheKey`, `ResolveCache` (see WR-01 for a robustness gap) |
| LLM-05 | 03-01, 03-03, 03-04, 03-06, 03-10 | read-write, read-only, refresh; hits report original latency and cost; truncated or refused never cached | SATISFIED | `CachingLlmGateway`; live replay |
| EXT-05 | 03-01, 03-03, 03-08 | Prompt version in cache key and result metadata | SATISFIED | Key field, `effective.prompt_version`, `attempts[].prompt_version` |
| REPO-05 | 03-02, 03-07, 03-10 | Trace viewer with one docker compose command | SATISFIED | `compose.yaml`, README, running Jaeger |
| API-02 | 03-04, 03-08 | Per-request overrides, echo effective configuration | SATISFIED | `EvalEndpoint` validation, `effective` block |
| API-04 | 03-09 | Golden fixtures emitted by .NET parsed by pytest | SATISFIED | 11 fixtures, strict models, mutation rejected |

### Prohibitions (judgment tier, non-authoritative verdicts)

Flag for all rows: unverified-prohibition, human review recommended. None is a test-tier prohibition.

| Plan | Prohibition | My verdict | Evidence |
|------|-------------|------------|----------|
| 03-01 LLM-05 | Must not present a replay as a fresh call | PASS | Hit always sets `HttpAttempts = 0` and `Cache.Status Hit`; live replay shows both |
| 03-02 REPO-05 | Privacy (viewer not exposed beyond loopback) | PASS, but guard test is weak (WR-03) | `compose.yaml` ports are `127.0.0.1:` only |
| 03-03 LLM-04 | Cache must not serve outside Development/Eval | PASS | `ResolveCache` returns null cache and throws on configuration; `.cache/` ignored and untracked |
| 03-04 API-02 | An override must not switch a cache on in a host that has none | PASS | `EvalEndpoint` 400s `overrides.cache_mode` on a cache-off host; the route exists only in Development/Eval, where the default host cache is on |
| 03-04 LLM-05 | No mixed cache configurations in one run directory | PASS (not re-derived; covered by runner resume refusal tests and the passing suite) | summary and 03-04 tests |
| 03-05 LLM-01 | Do not retry refused, truncated or invalid answers | PASS | A 200 refusal returns normally from the SDK; only HTTP statuses are retried; the extractor's repair loop is bounded and separate |
| 03-06 LLM-05 | Do not report notional cost as spend, nor hide it | PASS with caveat (WR-05) | summary.md prints incurred and notional side by side; the unknown-incurred case under-reports |
| 03-06 LLM-05 | Do not count a call free unless provably no provider call | PASS | Incurred is 0 only for hit or cache_miss; unknown failure stays null |
| 03-07 LLM-03 | No span export unless the endpoint is explicitly set | PASS with caveat (WR-02) | Exporter only added when the variable is set |
| 03-08 EXT-05 | No prompt text through the request | PASS | Only catalogued versions accepted, uncatalogued value is a 400 |
| 03-08 API-02 | No unpriceable model, no repair budget above ceiling | PASS | `pricing.CanPrice`, `MaxRepairsLimit` 5 |
| 03-09 API-04 | No hand-edited golden fixtures | PASS | Golden test compares byte for byte and names `just golden` |
| 03-10 LLM-05 | Do not publish the replay's zero as the cost of extraction | PASS | README line 93 |
| 03-10 LLM-03 | No live spend without human approval | PASS | 03-10 summary records the approved spend; US$0.0970 spent |

### Anti-Patterns Found

No debt markers, no stubs, no hardcoded empty data in the changed sources. The open code-review findings (0 critical) were checked against the code and are reported here as warnings, not blockers.

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `dotnet/src/Carimbo.Llm/Cache/LlmCacheStore.cs` | 221-257 | `GetProperty(...).GetString()!` accepts a JSON null as a hit (WR-01); `Parse` result then reaches the extractor and throws, giving a 500 instead of a miss | Warning | Violates the store's "invalid entry is a miss" claim; needs a hand-damaged cache file, so not on the goal path |
| `dotnet/src/Carimbo.Api/Program.cs` | 124, 135-143 | Exporter switch and log read only `OTEL_EXPORTER_OTLP_ENDPOINT` (WR-02) | Warning | Fails closed; log can name the wrong destination if the traces-specific variable is also set |
| `python/tests/test_repo_layout.py` | 183-188 | Loopback regex covers only quoted short syntax (WR-03) | Warning | Guard weaker than its claim; current `compose.yaml` is correct |
| `dotnet/src/Carimbo.Api/EvalEndpoint.cs` | 288 | `effective.pricing_version` is the host's current version even for a replay priced earlier (WR-04) | Warning | Provenance label can be wrong after a pricing bump; true today |
| `python/src/carimbo_evals/summary.py` | 129-135, 265 | `summary.md` omits the unknown-incurred count (WR-05) | Warning | Headline "incurred" can read as exact after an answered-then-failed case; `summary.json` has `unpriced_incurred_cases` |
| `README.md` | 31 | Lists "effort" among the cache key fields; the key has no effort field (coverage matrix records effort as opt-out) | Info | Doc inaccuracy |
| `dotnet/src/Carimbo.Llm/Telemetry/TracingLlmGateway.cs`, `CachingLlmGateway.cs`, `justfile` | IN-01 to IN-03 | Info items from 03-REVIEW | Info | Advisory |

Other observations (not findings): `summary.md`'s `latency ms` column on a replay shows the real wall time (48, 48, 8), while `model_latency_ms` and every attempt's `latency_ms` carry the original latency, so SC1's "reports the original latency" is met at attempt level and in `summary.json`. A read_only replay still needs the provider key to be present for the gateway to register (README says so); keyless reproducibility is Phase 6.

### Human Verification Required

1. **Jaeger UI visual check.** Open http://localhost:16686, service `carimbo-api`, trace `f720b1d530a4adac18a1ad9ce440ff95` (case-001, 3 model-call spans) and `e1408a0541e6c473885d513c65c2d63e` (case-003, 1 span). Expected: server span plus one `chat claude-haiku-4-5` span per attempt, with model, tokens and cost and no content. Why human: the UI check plan 03-10 left pending; the API-level equivalent passed here.
2. **README clarity and honesty of wording** (03-02 D2, 03-06). Follow the documented commands as a reviewer would.
3. **LLM-04 environment strictness policy** (03-03 D6). Production with a directory set fails startup even with mode off. Accept or relax.
4. **D-30 restored extract-002 with known label defects** (03-08 D5). Accept the decision or record a superseding one.
5. **Judgment-tier prohibition flags** above (unverified-prohibition, human review recommended).

Items assessed and not carried as human: 03-04 safety prohibition (verified from `EvalEndpoint`/`TryMap`/`ResolveCache`), 03-05 quality failures not retried (verified from the SDK-only retry design and the extractor's typed failure mapping), 03-07 real OTLP/Jaeger export (verified by live API lookup), 03-01 EXT-05 half-delivery (completed in 03-03/03-08 and verified live).

### Gaps Summary

None. The phase goal is achieved: a developer can replay the Phase 1 cases from the request-hash cache with zero provider calls and see the original latency and cost (live: US$0.0970 seeding, US$0 replay, 7/7 hits), and every model call is a costed, content-free span in a local Jaeger, retrievable by the response's trace id. Five non-blocking warnings (WR-01 to WR-05) remain open in 03-REVIEW-DISPOSITION.md; WR-01 and WR-04 are worth fixing before Phase 6 commits replay fixtures, because replay provenance is the product.

---

_Verified: 2026-10-15T10:15:00Z_
_Verifier: Claude (gsd-verifier)_
