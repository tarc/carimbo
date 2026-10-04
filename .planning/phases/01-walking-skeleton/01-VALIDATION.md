---
phase: "1"
slug: "walking-skeleton"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-10-04"
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Source: `01-RESEARCH.md` § Validation Architecture.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | .NET: xunit.v3 4.0.1 on Microsoft.Testing.Platform. Python: pytest 9.1.1 + pytest-asyncio 1.4.0 |
| **Config file** | `dotnet/global.json`, `dotnet/Directory.Build.props`, `python/pyproject.toml` (none yet; Wave 0 installs) |
| **Quick run command** | `cd dotnet && dotnet test` / `cd python && uv run pytest -q` (touched stack only) |
| **Full suite command** | `just check` (= dotnet-check, py-check, schema-check, datagen-check, e2e, docs-check, secrets-check; created in plan 01-13). Before 01-13, run the per-task commands in the map below |
| **Estimated runtime** | ~60 seconds after restore |

In this cloud container, dotnet and just are not on PATH. Run them as `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#just -c <command>`.

---

## Sampling Rate

- **After every task commit:** run the quick command for the touched stack. Also run `just schema-check` when Domain or the projector changed.
- **After every plan wave:** run the full suite command.
- **Before `/gsd-verify-work`:** the full suite must be green in CI on a clean runner, and `just skeleton` must have run once against the real API within the US$5 cap.
- **Max feedback latency:** 120 seconds

---

## Per-Task Verification Map

Refined by the planner to real plan and task IDs (2026-10-04). Prefixes: `N` = `nix shell nixpkgs#dotnet-sdk_10 -c`, `NJ` = `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#just -c`, `UV` = `uv run --project python`. All commands run from the repo root.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 01-01-T1/T3 | 01-01 | 1 | REPO-02 | T-01-SC | Nothing installed before human supply-chain approval | smoke | `uv lock --project python --check`; `uv sync --project python --locked`; `UV ruff check python && UV pyright -p python` | ❌ W0 (01-01 creates) | ⬜ pending |
| 01-02-T1 | 01-02 | 1 | REPO-01 | T-01-SC | Exact NuGet pins | smoke | `N dotnet build dotnet/Carimbo.slnx -warnaserror` | ❌ W0 (01-02 creates) | ⬜ pending |
| 01-03-T1 (tracer) | 01-03 | 2 | EXT-01, EXT-02, API-03, EVAL-01, EVAL-02 | T-01-05, T-01-08 | 401 without key; key absent from JSONL/summary | e2e | `N UV pytest python/tests/test_e2e_fake.py -m e2e -q` | ❌ W0 (01-03 creates) | ⬜ pending |
| 01-04-T1 | 01-04 | 3 | DOM-01 | T-01-03 | pt-BR money rejected | unit | `N sh -c 'cd dotnet && dotnet test'` (DomainTests) | ❌ W0 (01-04 creates) | ⬜ pending |
| 01-04-T2 | 01-04 | 3 | DOM-05, DOM-08 | T-01-10 | N/A | unit | `N dotnet run --project dotnet/tools/SchemaExport -- --check`; `N sh -c 'cd dotnet && dotnet test'` (SchemaSnapshotTests) | ❌ W0 | ⬜ pending |
| 01-04-T3 | 01-04 | 3 | RES-03 | T-01-11 | Original entries untouched | static | `grep -c '^## D-18 \|^## D-19 \|^## D-20 ' docs/DECISIONS.md` | ✅ (docs/DECISIONS.md exists) | ⬜ pending |
| 01-05-T1/T2 | 01-05 | 4 | DOM-06, DOM-08 | T-01-12 | Sent schema == committed | unit | `N sh -c 'cd dotnet && dotnet test'` (SchemaProjectionTests); `N dotnet run --project dotnet/tools/SchemaExport -- --check` | ❌ W0 | ⬜ pending |
| 01-06-T1 | 01-06 | 3 | DATA-07 | T-01-13 | Synthetic parties only | unit | `UV pytest python/tests/test_synthetic_only.py -q` | ❌ W0 | ⬜ pending |
| 01-06-T2 | 01-06 | 3 | DATA-01 | — | N/A | unit | `UV pytest python/tests/test_datagen.py -q` | ❌ W0 | ⬜ pending |
| 01-07-T2 | 01-07 | 4 | DATA-01 | T-01-15 | Committed bytes == regeneration | unit | `UV carimbo-datagen check`; `UV pytest python/tests/test_datagen.py -q` | ❌ W0 | ⬜ pending |
| 01-08-T1 | 01-08 | 5 | EXT-01, EXT-02 | T-01-20 | No eval metadata reaches the provider | unit | `N sh -c 'cd dotnet && dotnet test'` (ExtractorTests) | ❌ W0 | ⬜ pending |
| 01-08-T2 | 01-08 | 5 | API-03 | T-01-17..19 | 404 when disabled, 401 without key, 400/413 on bad input | integration | `N sh -c 'cd dotnet && dotnet test'` (EvalEndpointTests) | ❌ W0 | ⬜ pending |
| 01-09-T1 | 01-09 | 5 | DOM-07, DOM-08 | — | N/A | unit | `UV pytest python/tests/test_models.py -q` | ❌ W0 | ⬜ pending |
| 01-09-T2 | 01-09 | 5 | EVAL-01 | T-01-21, T-01-22 | No key in JSONL; cost cap boundary | unit | `UV pytest python/tests/test_runner.py -q` | ❌ W0 | ⬜ pending |
| 01-09-T3 | 01-09 | 5 | EVAL-02 | T-01-23 | Grading makes no network calls | unit | `UV pytest python/tests/test_grader.py -q`; `N UV pytest python/tests/test_e2e_fake.py -m e2e -q` | ❌ W0 | ⬜ pending |
| 01-10-T1 | 01-10 | 5 | LLM-06 | T-01-24 | Offline, no key | unit (self-test) | `N dotnet run --project dotnet/tools/LlmSpike -- --offline-selftest` | ❌ W0 | ⬜ pending |
| 01-10-T2 | 01-10 | 5 | LLM-06 | T-01-24, T-01-25 | No key, headers or PDF bytes in docs/fixtures | manual-with-key + static | live run, then `! grep -rlE 'sk-ant-|x-api-key|JVBERi0' docs/spikes dotnet/tests/Carimbo.Llm.Tests/Fixtures` | ❌ W0 | ⬜ pending |
| 01-11-T1/T2 | 01-11 | 6 | LLM-02 | T-01-27 | Unknown model → null cost, never 0 | unit | `N sh -c 'cd dotnet && dotnet test'` (PricingTests, EvalEndpointTests) | ❌ W0 | ⬜ pending |
| 01-12-T1 | 01-12 | 7 | LLM-02, LLM-06, EXT-01, EXT-02 | T-01-28, T-01-31 | Key never in messages; wire schema == committed | unit | `N sh -c 'cd dotnet && dotnet test'` (AnthropicGatewayTests) | ❌ W0 | ⬜ pending |
| 01-12-T2 | 01-12 | 7 | EXT-01 (live), D-10 | T-01-29 | Route absent without provider key | integration + live smoke | `N sh -c 'cd dotnet && dotnet test'`; JSONL check of `evals/runs/smoke-01-12` | ❌ W0 | ⬜ pending |
| 01-13-T1 | 01-13 | 8 | REPO-01, REPO-02, REPO-03, DOM-08 | T-01-33 | secrets-check in the gate | smoke | `NJ just check` | ❌ W0 | ⬜ pending |
| 01-13-T2 | 01-13 | 8 | REPO-04, CI-01 | T-01-32 | No secrets, no privileged PR trigger | static | `UV pytest python/tests/test_repo_layout.py -q` | ❌ W0 | ⬜ pending |
| 01-13-T3 | 01-13 | 8 | (phase goal) | T-01-34, T-01-35 | Cap US$1 per run; localhost; ephemeral eval key | live | `NJ just skeleton`, then the summary.json check | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Repo scaffold: justfile, `dotnet/global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.slnx`, `python/pyproject.toml` + `uv.lock`, `.editorconfig`, `.gitattributes`, `.gitignore`, CI workflow, AGENTS.md/CLAUDE.md
- [ ] `dotnet/tests/*` projects with xunit.v3 and `Microsoft.AspNetCore.Mvc.Testing`
- [ ] `python/tests/`: `test_repo_layout.py`, `test_models.py`, `test_datagen.py`, `test_synthetic_only.py`, `test_runner.py`, `test_grader.py`, `test_e2e_fake.py`
- [ ] Fake gateway and counting `HttpMessageHandler` test support
- [ ] Sanitised recorded-usage fixtures (from spike step 8; until then, hand-built and labelled synthetic)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| devenv path on NixOS-WSL | REPO-03 | devenv is not installable in CI or this container | On the author's machine: `devenv shell -- just dotnet-check py-check` |
| Claude Code and OpenCode both discover the instruction file | REPO-04 | Needs both agent runtimes interactively | Open the repo in each and ask "what is the check command?" |
| Live spike recorded | LLM-06 | Paid API call with a real key | `just spike-live`, then review `docs/spikes/` |
| Live skeleton: 3 cases, real model, graded | (phase goal) | Paid API call | `just skeleton` within the US$5 cap |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 120s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
