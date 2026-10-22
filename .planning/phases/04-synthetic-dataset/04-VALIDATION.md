---
phase: "04"
slug: "synthetic-dataset"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-10-22"
---

# Phase 04 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | pytest 9.1.1 + pytest-asyncio 1.4.0 (Python); xUnit v3 4.0.1 on Microsoft.Testing.Platform (.NET) |
| **Config file** | `python/pyproject.toml` (markers `e2e`, `live`, deselected by default), `global.json` (MTP runner) |
| **Quick run command** | `devenv shell -- uv run --project python pytest python/tests/test_dataset_*.py -q` |
| **Full suite command** | `devenv shell -- just check` |
| **Estimated runtime** | ~300 seconds (full, includes `dataset-check` regeneration), ~30 seconds (quick) |

---

## Sampling Rate

- **After every task commit:** Run the quick command for the module touched, plus `just datagen-check` whenever `spec.py`, `nfe_xml.py` or `danfe.py` change
- **After every plan wave:** Run `just py-check` and the .NET dataset gate class
- **Before `/gsd-verify-work`:** Full suite must be green, and one green CI run on the PR before any image PDF is committed as final
- **Max feedback latency:** 300 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 04-01-01 | 01 | 1 | DATA-02 | — | N/A | unit | `{filled by planner}` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `python/tests/test_dataset_manifest.py` — counts, strata, split, CI subset, hashes, size budget (DATA-02, DATA-05, DATA-06)
- [ ] `python/tests/test_dataset_spec.py` — tax-code arithmetic and trait coverage from specs (DATA-02)
- [ ] `python/tests/test_dataset_scans.py` — no text layer, barcode flags, leak check (DATA-03)
- [ ] `python/tests/test_dataset_text_equals_xml.py` — D-05 graded-string presence (DATA-03)
- [ ] `python/tests/test_dataset_ground_truth.py` — jsonschema + Pydantic over the manifest (DATA-04)
- [ ] `dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs` (or a generalized `GroundTruthGateTests`) (DATA-04)
- [ ] `justfile` recipes `dataset`, `dataset-check`, `check` wiring; `.github/workflows/ci.yml` step (DATA-06)

*Framework install: none (pytest and xUnit exist).*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Byte-identical image PDFs on the CI runner | DATA-06 | Determinism proven only on this machine (research assumption A2) | Push the PR, confirm the `dataset-check` CI step is green before treating the committed image PDFs as final |
| Supply-chain review of the new pins (numpy, reportlab, runtime pypdfium2/zxing-cpp) | DATA-03 | Lock diff needs a human look (Phase 1 precedent) | Review the `uv.lock` diff at the checkpoint before `uv lock` is committed |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 300s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
