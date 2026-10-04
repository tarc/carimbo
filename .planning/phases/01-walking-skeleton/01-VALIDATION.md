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
| **Full suite command** | `just dotnet-check && just py-check && just schema && git diff --exit-code -- schema python/src/carimbo_models && just docs-check` |
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

Seeded per requirement. The planner and executor refine it to task IDs.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | TBD | 0 | REPO-01 | — | N/A | smoke | `just dotnet-check` | ❌ W0 | ⬜ pending |
| TBD | TBD | 0 | REPO-02 | — | N/A | smoke | `just py-check` | ❌ W0 | ⬜ pending |
| TBD | TBD | 0 | REPO-03 | — | N/A | CI job | CI workflow green on clean ubuntu (no Nix) | ❌ W0 | ⬜ pending |
| TBD | TBD | 0 | REPO-04 | — | N/A | unit | `uv run pytest tests/test_repo_layout.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 0 | CI-01 | — | No secrets, no `pull_request_target` | static | `uv run pytest tests/test_repo_layout.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DOM-01 | — | N/A | unit | `dotnet test` (DomainPurityTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DOM-05 | — | N/A | unit | `dotnet test` (SchemaSnapshotTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DOM-06 | — | N/A | unit | `dotnet test` (ModelSchemaProjectorTests, SchemaBudgetTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DOM-07 | — | N/A | unit | `uv run pytest tests/test_models.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DOM-08 | — | N/A | CI job | `just schema && git diff --exit-code -- schema python/src/carimbo_models` | ❌ W0 | ⬜ pending |
| TBD | TBD | 2 | LLM-02 | — | N/A | unit | `dotnet test` (PricingTests, GatewayWireTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 2 | LLM-06 | — | No key value in docs/spikes | manual + static | `just docs-check` | ❌ W0 | ⬜ pending |
| TBD | TBD | 2 | EXT-01 | — | N/A | unit | `dotnet test` (ExtractorTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 2 | EXT-02 | — | N/A | unit | `dotnet test` (OutcomeMappingTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 2 | API-03 | — | 404 when disabled, 401 without the key | integration | `dotnet test` (EvalEndpointTests, TraceIdTests) | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DATA-01 | — | N/A | unit | `uv run pytest tests/test_datagen.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | DATA-07 | — | Synthetic parties only | unit | `uv run pytest tests/test_synthetic_only.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 3 | EVAL-01 | — | No key in JSONL output | unit | `uv run pytest tests/test_runner.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 3 | EVAL-02 | — | Grading makes no network calls | unit | `uv run pytest tests/test_grader.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | 1 | RES-03 | — | N/A | static | `just docs-check` | ❌ W0 | ⬜ pending |
| TBD | TBD | 3 | (e2e) | — | N/A | integration | `uv run pytest tests/test_e2e_fake.py -q` | ❌ W0 | ⬜ pending |

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
