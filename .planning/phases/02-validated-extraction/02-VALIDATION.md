---
phase: "02"
slug: "validated-extraction"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-10-08"
---

# Phase 02 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 4.0.1 on Microsoft.Testing.Platform; pytest 9.1.1 (markers `e2e`, `live`, deselected by default) |
| **Config file** | `global.json`, `dotnet/Directory.Packages.props`, `python/pyproject.toml` |
| **Quick run command** | `devenv shell -- sh -c 'cd dotnet && dotnet test --project tests/<Project>'` or `devenv shell -- uv run --project python pytest python/tests -q -k <expr>` |
| **Full suite command** | `devenv shell -- just check` |
| **Estimated runtime** | ~30 seconds per project; a few minutes for `just check` |

---

## Sampling Rate

- **After every task commit:** Run the affected project's `dotnet test --project …` or `pytest -k …`
- **After every plan wave:** Run `devenv shell -- just dotnet-check` and `just py-check`; after any record, datagen or schema change also `just schema-check` and `just datagen-check`
- **Before `/gsd-verify-work`:** `devenv shell -- just check` must be green, then the paid skeleton runs (human-verify)
- **Max feedback latency:** 30 seconds per task-level command

---

## Per-Task Verification Map

| Req ID | Behavior | Test Type | Automated Command | File Exists | Status |
|--------|----------|-----------|-------------------|-------------|--------|
| DOM-02 | Skeleton XML maps to `Invoice` with zero error findings; mapping doc lists every `Invoice` property; schema within budget | unit + gate | `dotnet test --project tests/Carimbo.Validation.Tests` (GroundTruthGate); `--project tests/Carimbo.Extraction.Tests` | ❌ W0 | ⬜ pending |
| DOM-03 | Numeric and alphanumeric CNPJ/key accepted; bad pattern is `schema_invalid` with a path | unit | `--project tests/Carimbo.Domain.Tests`; `--project tests/Carimbo.Extraction.Tests` | ✅ extend | ⬜ pending |
| DOM-04 | Money/Quantity/Rate patterns; half-up vectors in C# and Python; no negative zero | unit | `--filter-method "*Rounding*"`; `pytest -k rounding` | ❌ W0 | ⬜ pending |
| VAL-01 | Finding shape; never throws on hostile and extreme inputs | unit / fuzz-loop | `--filter-class "*NeverThrows*"` | ❌ W0 | ⬜ pending |
| VAL-02 | CNPJ vectors (numeric, alphanumeric, repeated, lowercase) | unit | `--filter-class "*Vectors*"`; `pytest -k vectors` | ❌ W0 | ⬜ pending |
| VAL-03 | Key DV + five cross-checks incl. month boundary | unit | `--filter-class "*AccessKey*"` | ❌ W0 | ⬜ pending |
| VAL-04 | Item/total/tax rules per regime family | unit | `--filter-class "*TaxRules*"`, `"*Totals*"` | ❌ W0 | ⬜ pending |
| VAL-05 | Date rules with an injected reference date | unit | `--filter-class "*Dates*"` | ❌ W0 | ⬜ pending |
| VAL-06 | One vector file consumed by both stacks; shared tolerance | unit | both vector commands | ❌ W0 | ⬜ pending |
| EXT-03 | First-try success, repair, budget exhaustion; no-fabrication prompt; feedback hides `expected` for transcribed rules | unit (scripted gateway) | `--project tests/Carimbo.Extraction.Tests` | ✅ extend | ⬜ pending |
| EXT-04 | Attempts list with output, findings, usage, cost, latency; totals are sums; null cost never zero | unit + integration | `--project tests/Carimbo.Extraction.Tests`; `--project tests/Carimbo.Api.Tests` | ✅ extend | ⬜ pending |
| API-01 | Endpoint returns outcome, findings, attempts, trace id; `validation_failed` is HTTP 200 | integration | `--project tests/Carimbo.Api.Tests` | ✅ extend | ⬜ pending |
| (cross-stack) | Scripted host + runner + grader over the reworked cases incl. a repair scenario | e2e | `devenv shell -- just e2e` | ✅ extend | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `dotnet/tests/Carimbo.Validation.Tests/` (in `Carimbo.slnx`) with repo-root helper, vector loader, never-throw loop, ground-truth gate
- [ ] `data/vectors/validator-vectors.json` (+ shared valid-invoice fixture) and `python/tests/test_vectors.py`
- [ ] Shared valid-invoice builders replacing 7-field fixtures in existing .NET and Python tests
- [ ] Scripted host `"attempts"` support and a recording stub gateway
- [ ] Doc-drift test: every `schema/invoice.schema.json` property appears in `docs/DANFE-MAPPING.md`
- [ ] `docs-check` extended to new DECISIONS ids; `just skeleton` gains a `max_repairs` parameter
- [ ] Live schema probe mode in `tools/LlmSpike` (text-only, capped)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Live skeleton run with repairs enabled, then disabled | EXT-03, EXT-04 | Paid call, needs provider key (cap US$1.00/run) | `devenv shell -- just skeleton` with `max_repairs` 2, then 0 |
| Provider accepts the v2 schema grammar | DOM-02 | Paid call; grammar complexity not testable offline | Run the LlmSpike schema probe (text-only, < US$0.01) |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
