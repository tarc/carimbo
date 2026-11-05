---
phase: "05"
slug: "comparable-measurement"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-11-05"
---

# Phase 05 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | pytest 9.1.1 + pytest-asyncio 1.4.0 (Python), markers `e2e` and `live` deselected by default |
| **Config file** | `python/pyproject.toml` `[tool.pytest.ini_options]` |
| **Quick run command** | `devenv shell -- uv run --project python pytest python/tests/test_<module>.py -q` |
| **Full suite command** | `devenv shell -- just check` |
| **Estimated runtime** | ~300 seconds (full), ~60 seconds (`just py-check`, plan for ~90), ~15 seconds (quick) |

---

## Sampling Rate

- **After every task commit:** Run the new test module for the task, plus `ruff check python` and `pyright -p python`
- **After every plan wave:** Run `just py-check`
- **Before `/gsd-verify-work`:** `just check` green; after the approved live dev run, `just py-check` again with the committed result files and the README Status row
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

Rows are filled from the plans once they exist. The requirement-to-test map below comes from RESEARCH.md and is the sampling contract the plans must satisfy.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | TBD | TBD | EVAL-03 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_grader.py -q` | extend | ⬜ pending |
| TBD | TBD | TBD | EVAL-03 | — | N/A | unit, all 154 cases | `devenv shell -- uv run --project python pytest python/tests/test_matching.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-04 | — | N/A | unit, all 154 cases | `devenv shell -- uv run --project python pytest python/tests/test_selftests.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-04 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_grader.py -q -k "infrastructure or harness or incomplete"` | extend | ⬜ pending |
| TBD | TBD | TBD | EVAL-05 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_stats.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-05 | — | N/A | unit + tmp run dir | `devenv shell -- uv run --project python pytest python/tests/test_summary_v4.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-05 | — | N/A | e2e | `devenv shell -- uv run --project python pytest python/tests/test_e2e_full_dataset.py -q -m e2e` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-06 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_silent_errors.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-07 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_tags.py -q` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | EVAL-08 | — | N/A | unit + CLI runner | `devenv shell -- uv run --project python pytest python/tests/test_compare.py -q` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `python/tests/test_matching.py`, `test_selftests.py`, `test_stats.py`, `test_tags.py`, `test_silent_errors.py`, `test_summary_v4.py`, `test_compare.py` — new modules for EVAL-03 to EVAL-08
- [ ] `python/tests/test_e2e_full_dataset.py` — new, `e2e` marker, the offline full-dataset proof (D-20)
- [ ] `python/src/carimbo_evals/corruptions.py` — shared corruption catalogue, so the pytest self-tests and the e2e proof cannot drift (importlib mode means test files cannot import each other)
- [ ] Remove or rewrite `test_grader_default_tolerance_matches_the_vector_file` in `python/tests/test_vectors.py` (D-03 breaks it; the vector file stays)
- [ ] Update the pinned versions and tolerance string in `test_grader.py` and `test_e2e_fake.py`
- [ ] scipy added to the lock and installed only after the blocking review checkpoint (D-08)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| scipy pin approved | EVAL-03 | Blocking supply-chain review (D-08), same as plans 01-01 and 04-02 | Reviewer checks the PyPI page and the lock diff before any install |
| Live `dev` run | EVAL-05, EVAL-07 | Paid step, runs only after explicit spend approval (D-19) | Run the capped recipe, then the free `read_only` replay; copy the run verbatim to `evals/results/` |
| Tag list frozen before `test` | EVAL-07 | Judgment call on the live failures (D-12) | Review the `untagged` bucket from the dev run, extend the rules, freeze with the grader version |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
