---
phase: "3"
slug: "replayable-runs"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-10-15"
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 4.0.1 on Microsoft.Testing.Platform (.NET); pytest 9.1.1 + pytest-asyncio 1.4.0 (Python) |
| **Config file** | `global.json` (MTP runner opt-in); `python/pyproject.toml` (`-m "not e2e and not live"`) |
| **Quick run command** | `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/Carimbo.Llm.Tests'` (or the affected test project); `devenv shell -- uv run --project python pytest python/tests -q` |
| **Full suite command** | `devenv shell -- just check` |
| **Estimated runtime** | ~4–10 seconds per test project; full `just check` a few minutes |

---

## Sampling Rate

- **After every task commit:** Run the affected test project (`dotnet test --project tests/<Project>`) plus the affected pytest file
- **After every plan wave:** Run `devenv shell -- just dotnet-check` and `devenv shell -- just py-check`
- **Before `/gsd-verify-work`:** `devenv shell -- just check` must be green, then the capped live proof (manual, below)
- **Max feedback latency:** ~10 seconds per task

---

## Per-Task Verification Map

Filled by the planner/executor per task. Requirement → test mapping from RESEARCH.md:

| Requirement | Behavior | Test Type | Automated Command | File Exists | Status |
|-------------|----------|-----------|-------------------|-------------|--------|
| LLM-04 | Cache key stable (pinned vector), every `LlmRequest` property covered, model/prompt version/salt/PDF changes miss | unit | `cd dotnet && dotnet test --project tests/Carimbo.Llm.Tests` | ❌ W0 `CacheKeyTests.cs` | ⬜ pending |
| LLM-04 | Cache on by default in Development/Eval at `.cache/llm` under the content root, off with `Llm:Cache:Mode off`; startup error when configured outside Development/Eval (D-27) | integration | `cd dotnet && dotnet test --project tests/Carimbo.Api.Tests` | ❌ W0 `CacheConfigurationTests.cs` | ⬜ pending |
| LLM-05 | read_write / read_only / refresh semantics; truncated/refused never cached; original latency+cost on hit | unit | `cd dotnet && dotnet test --project tests/Carimbo.Llm.Tests` | ❌ W0 `CachingGatewayTests.cs` | ⬜ pending |
| LLM-01 | Persistent 5xx → `1 + MaxRetries` attempts; 400 → 1; no Polly/Http.Resilience reference | unit + static | `cd dotnet && dotnet test --project tests/Carimbo.Llm.Tests` | extends `AnthropicGatewayTests.cs` | ⬜ pending |
| LLM-03 | One span per model call, allowlisted tags only, no content; shares response trace id | unit + integration | `dotnet test --project tests/Carimbo.Llm.Tests` / `tests/Carimbo.Api.Tests` | ❌ W0 `TracingGatewayTests.cs`, `TracingWiringTests.cs` | ⬜ pending |
| EXT-05 | Prompt catalog; version in cache key and result metadata | unit | `cd dotnet && dotnet test --project tests/Carimbo.Extraction.Tests` | extends `ExtractorTests.cs` | ⬜ pending |
| API-02 | Overrides take effect, invalid values → field-specific 400, echo in `effective` | integration | `cd dotnet && dotnet test --project tests/Carimbo.Api.Tests` | extends `EvalEndpointTests.cs` | ⬜ pending |
| API-04 | Golden fixtures equal fresh output; pytest parses them strictly | contract | `dotnet test --project tests/Carimbo.Api.Tests` then `uv run --project python pytest python/tests/test_contract_golden.py -q` | ❌ W0 | ⬜ pending |
| REPO-05 | `compose.yaml` pins Jaeger v2 tag, loopback ports, README one command | static | `uv run --project python pytest python/tests/test_repo_layout.py -q` | extends existing | ⬜ pending |
| LLM-04/05 e2e | read_write then read_only rerun against ScriptedHost: zero attempts, hit, incurred cost 0 | e2e | `devenv shell -- just e2e` | extends `test_e2e_fake.py` | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `dotnet/tests/Carimbo.Llm.Tests/CacheKeyTests.cs`, `CachingGatewayTests.cs`, `TracingGatewayTests.cs`
- [ ] `dotnet/tests/Carimbo.Api.Tests/CacheConfigurationTests.cs`, `TracingWiringTests.cs`, `GoldenResponseTests.cs`, overrides tests
- [ ] `python/tests/test_contract_golden.py`, `python/src/carimbo_evals/contract.py`
- [ ] `data/contract/eval-responses/*.json` (generated) with the `CARIMBO_UPDATE_GOLDEN` rewrite path
- [ ] `OpenTelemetry.Exporter.InMemory` in `Carimbo.Api.Tests.csproj` and `Directory.Packages.props`
- [ ] `ScriptedHost` reports `HttpAttempts: 1` so a replay is distinguishable from a call
- [ ] Shared counting `ILlmGateway` test double

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Replay of the three skeleton cases makes zero provider calls and reports original latency/cost | LLM-05 | Needs a provider key for the seeding run; never in CI | One capped `just skeleton` (read_write), then the replay recipe (read_only); record evidence in the plan SUMMARY |
| Trace id from an eval response resolves in Jaeger with one `chat …` span per attempt and no content | LLM-03, REPO-05 | Needs Docker and a running Jaeger | `docker compose up -d`, run a case, `curl http://127.0.0.1:16686/api/traces/<id>` and inspect tags |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s per task
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
