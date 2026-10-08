---
phase: "1"
slug: "walking-skeleton"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: validated
nyquist_compliant: true
wave_0_complete: true
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
| 01-01-T1/T3 | 01-01 | 1 | REPO-02 | T-01-SC | Nothing installed before human supply-chain approval | smoke | `uv lock --project python --check`; `uv sync --project python --locked`; `UV ruff check python && UV pyright -p python` | ✅ | ✅ green |
| 01-02-T1 | 01-02 | 1 | REPO-01 | T-01-SC | Exact NuGet pins | smoke | `N dotnet build dotnet/Carimbo.slnx -warnaserror` | ✅ | ✅ green |
| 01-03-T1 (tracer) | 01-03 | 2 | EXT-01, EXT-02, API-03, EVAL-01, EVAL-02 | T-01-05, T-01-08 | 401 without key; key absent from JSONL/summary | e2e | `N UV pytest python/tests/test_e2e_fake.py -m e2e -q` | ✅ | ✅ green |
| 01-04-T1 | 01-04 | 3 | DOM-01 | T-01-03 | pt-BR money rejected | unit | `N sh -c 'cd dotnet && dotnet test'` (DomainTests) | ✅ | ✅ green |
| 01-04-T2 | 01-04 | 3 | DOM-05, DOM-08 | T-01-10 | N/A | unit | `N dotnet run --project dotnet/tools/SchemaExport -- --check`; `N sh -c 'cd dotnet && dotnet test'` (SchemaSnapshotTests) | ✅ | ✅ green |
| 01-04-T3 | 01-04 | 3 | RES-03 | T-01-11 | Original entries untouched | static | `grep -c '^## D-18 \|^## D-19 \|^## D-20 ' docs/DECISIONS.md` | ✅ | ✅ green |
| 01-05-T1/T2 | 01-05 | 4 | DOM-06, DOM-08 | T-01-12 | Sent schema == committed | unit | `N sh -c 'cd dotnet && dotnet test'` (SchemaProjectionTests); `N dotnet run --project dotnet/tools/SchemaExport -- --check` | ✅ | ✅ green |
| 01-06-T1 | 01-06 | 3 | DATA-07 | T-01-13 | Synthetic parties only | unit | `UV pytest python/tests/test_synthetic_only.py -q` | ✅ | ✅ green |
| 01-06-T2 | 01-06 | 3 | DATA-01 | — | N/A | unit | `UV pytest python/tests/test_datagen.py -q` | ✅ | ✅ green |
| 01-07-T2 | 01-07 | 4 | DATA-01 | T-01-15 | Committed bytes == regeneration | unit | `UV carimbo-datagen check`; `UV pytest python/tests/test_datagen.py -q` | ✅ | ✅ green |
| 01-08-T1 | 01-08 | 5 | EXT-01, EXT-02 | T-01-20 | No eval metadata reaches the provider | unit | `N sh -c 'cd dotnet && dotnet test'` (ExtractorTests) | ✅ | ✅ green |
| 01-08-T2 | 01-08 | 5 | API-03 | T-01-17..19 | 404 when disabled, 401 without key, 400/413 on bad input | integration | `N sh -c 'cd dotnet && dotnet test'` (EvalEndpointTests) | ✅ | ✅ green |
| 01-09-T1 | 01-09 | 5 | DOM-07, DOM-08 | — | N/A | unit | `UV pytest python/tests/test_models.py -q` | ✅ | ✅ green |
| 01-09-T2 | 01-09 | 5 | EVAL-01 | T-01-21, T-01-22 | No key in JSONL; cost cap boundary | unit | `UV pytest python/tests/test_runner.py -q` | ✅ | ✅ green |
| 01-09-T3 | 01-09 | 5 | EVAL-02 | T-01-23 | Grading makes no network calls | unit | `UV pytest python/tests/test_grader.py -q`; `N UV pytest python/tests/test_e2e_fake.py -m e2e -q` | ✅ | ✅ green |
| 01-10-T1 | 01-10 | 5 | LLM-06 | T-01-24 | Offline, no key | unit (self-test) | `N dotnet run --project dotnet/tools/LlmSpike -- --offline-selftest` | ✅ | ✅ green |
| 01-10-T2 | 01-10 | 5 | LLM-06 | T-01-24, T-01-25 | No key, headers or PDF bytes in docs/fixtures | manual-with-key + static | live run, then `! grep -rlE 'sk-ant-|x-api-key|JVBERi0' docs/spikes dotnet/tests/Carimbo.Llm.Tests/Fixtures` | ✅ | ✅ green |
| 01-11-T1/T2 | 01-11 | 6 | LLM-02 | T-01-27 | Unknown model → null cost, never 0 | unit | `N sh -c 'cd dotnet && dotnet test'` (PricingTests, EvalEndpointTests) | ✅ | ✅ green |
| 01-12-T1 | 01-12 | 7 | LLM-02, LLM-06, EXT-01, EXT-02 | T-01-28, T-01-31 | Key never in messages; wire schema == committed | unit | `N sh -c 'cd dotnet && dotnet test'` (AnthropicGatewayTests) | ✅ | ✅ green |
| 01-12-T2 | 01-12 | 7 | EXT-01 (live), D-10 | T-01-29 | Route absent without provider key | integration + live smoke | `N sh -c 'cd dotnet && dotnet test'`; JSONL check of `evals/runs/smoke-01-12` | ✅ | ✅ green |
| 01-13-T1 | 01-13 | 8 | REPO-01, REPO-02, REPO-03, DOM-08 | T-01-33 | secrets-check in the gate | smoke | `NJ just check` | ✅ | ✅ green |
| 01-13-T2 | 01-13 | 8 | REPO-04, CI-01 | T-01-32 | No secrets, no privileged PR trigger | static | `UV pytest python/tests/test_repo_layout.py -q` | ✅ | ✅ green |
| 01-13-T3 | 01-13 | 8 | (phase goal) | T-01-34, T-01-35 | Cap US$1 per run; localhost; ephemeral eval key | live | `NJ just skeleton`, then the summary.json check | ✅ | ✅ green |
| 01-14-T1 (tracer) | 01-14 | 9 | EXT-01, EXT-02 | T-01-38 | Oversized amount is a typed schema_invalid with HTTP 200 and a visible cost, never a 500 | integration | `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/Carimbo.Api.Tests --filter-method "*An_amount_too_large_for_a_decimal*"'` | ✅ | ✅ green |
| 01-14-T2 | 01-14 | 9 | EXT-01, EXT-02 | T-01-38, T-01-40 | Only value-conversion exceptions map to schema_invalid; no catch-all | unit | `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/Carimbo.Domain.Tests --filter-method "*Money*" && dotnet test --project tests/Carimbo.Extraction.Tests'` | ✅ | ✅ green |
| 01-14-T3 | 01-14 | 9 | EXT-01, EXT-02 | T-01-39, T-01-42 | Extraction success means schema-valid: every schema pattern enforced | unit + gate | `devenv shell -- sh -c 'cd dotnet && dotnet test'`; `devenv shell -- just dotnet-check`; `devenv shell -- just schema-check` | ✅ | ✅ green |
| 01-15-T1 (tracer) | 01-15 | 9 | EVAL-01, EVAL-02 | T-01-43 | Possibly-paid harness errors are charged against the cost cap; CLI exits 3 at the cap | unit + e2e | `devenv shell -- uv run --project python pytest python/tests/test_runner.py -q -k "charged or cap_when_server_errors or every_prior_harness_error"` | ✅ | ✅ green |
| 01-15-T2 | 01-15 | 9 | EVAL-02 | T-01-44 | Grader splits JSONL on the newline only; Unicode line separators do not break a run | unit | `devenv shell -- uv run --project python pytest python/tests/test_grader.py -q` | ✅ | ✅ green |
| 01-15-T3 | 01-15 | 9 | EVAL-01 | T-01-45 | Corrupt cases.jsonl on resume: exit 2 naming the line, file untouched | unit + e2e | `devenv shell -- uv run --project python pytest python/tests/test_runner.py -q -k "corrupt or torn"`; `devenv shell -- just py-check` | ✅ | ✅ green |
| 01-16-T1 (tracer) | 01-16 | 10 | EXT-01, EXT-02, EVAL-01, EVAL-02 | T-01-47 | Both gap truths hold over HTTP through the documented commands; eval key absent from the run directory | e2e | `devenv shell -- uv run --project python pytest python/tests/test_e2e_fake.py -m e2e -q -k TestTypedEdgeOutcomesOverHttp` | ✅ | ✅ green |
| 01-16-T2 | 01-16 | 10 | EXT-01, EXT-02, EVAL-01, EVAL-02 | T-01-48 | Closed findings cite a plan and a commit that resolves | gate + static | `devenv shell -- just check`; `grep -cE '^\| (CR-01\|WR-01\|WR-02\|WR-03\|WR-05) \| (critical\|warning) \| fixed \| 01-1[45] [0-9a-f]{7,}' .planning/phases/01-walking-skeleton/01-REVIEW-DISPOSITION.md` | ✅ | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] Repo scaffold: justfile, `dotnet/global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.slnx`, `python/pyproject.toml` + `uv.lock`, `.editorconfig`, `.gitattributes`, `.gitignore`, CI workflow, AGENTS.md/CLAUDE.md
- [x] `dotnet/tests/*` projects with xunit.v3 and `Microsoft.AspNetCore.Mvc.Testing`
- [x] `python/tests/`: `test_repo_layout.py`, `test_models.py`, `test_datagen.py`, `test_synthetic_only.py`, `test_runner.py`, `test_grader.py`, `test_e2e_fake.py`
- [x] Fake gateway and counting `HttpMessageHandler` test support
- [x] Sanitised recorded-usage fixtures (from spike step 8; until then, hand-built and labelled synthetic)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions | Result |
|----------|-------------|------------|-------------------|--------|
| devenv path on NixOS-WSL | REPO-03 | devenv is not installable in CI or this container | On the author's machine: `devenv shell -- just dotnet-check py-check` | ✅ 2026-10-08: `devenv shell -- just check` exit 0 in the working copy (devenv 2.4.1); a fresh clone not yet tried |
| Claude Code and OpenCode both discover the instruction file | REPO-04 | Needs both agent runtimes interactively | Open the repo in each and ask "what is the check command?" | ⬜ pending (file shape asserted by `test_repo_layout.py`) |
| First PR CI run green on ubuntu-latest | CI-01 | Needs a push and a GitHub runner | Open the PR; `dotnet`, `python`, `contract` jobs pass | ⬜ pending (nothing pushed yet) |
| Live spike recorded | LLM-06 | Paid API call with a real key | `just spike-live`, then review `docs/spikes/` | ✅ 01-10: US$0.2551, `docs/spikes/01-llm-gateway.md` |
| Live skeleton: 3 cases, real model, graded | (phase goal) | Paid API call | `just skeleton` within the US$5 cap | ✅ 01-13: 3/3 success, field grades 8/9, 8/9, 9/9 (access_key misses), US$0.0195; phase total US$0.2788 |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 120s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** validated 2026-10-08 (validate-phase audit after execution; `devenv shell -- just check` exit 0)

## Validation Audit 2026-10-08

| Metric | Count |
|---|---|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
