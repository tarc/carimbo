---
phase: "05"
slug: "comparable-measurement"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: validated
nyquist_compliant: true
wave_0_complete: true
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

Filled at the end of the phase. The Task ID column names the plan that delivered the row; later plans extended some modules. Counts are from each row's command, run on 2026-11-06 at commit 9909584.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 05-03 | 05-03 | 2 | EVAL-03 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_grader.py -q` | ✅ | ✅ green (115 passed) |
| 05-03 | 05-03 | 2 | EVAL-03 | — | N/A | unit, all 154 cases | `devenv shell -- uv run --project python pytest python/tests/test_matching.py -q` | ✅ | ✅ green (23 passed) |
| 05-04 | 05-04 | 3 | EVAL-04 | — | N/A | unit, all 154 cases | `devenv shell -- uv run --project python pytest python/tests/test_selftests.py -q` | ✅ | ✅ green (184 passed) |
| 05-06 | 05-06 | 4 | EVAL-04 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_grader.py -q -k "infrastructure or harness or incomplete"` | ✅ | ✅ green (13 passed) |
| 05-05 | 05-05 | 3 | EVAL-05 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_stats.py -q` | ✅ | ✅ green (24 passed) |
| 05-07 | 05-07 | 5 | EVAL-05 | — | N/A | unit + tmp run dir | `devenv shell -- uv run --project python pytest python/tests/test_summary_v4.py -q` | ✅ | ✅ green (34 passed) |
| 05-10 | 05-10 | 7 | EVAL-05 | — | N/A | e2e | `devenv shell -- uv run --project python pytest python/tests/test_e2e_full_dataset.py -q -m e2e` | ✅ | ✅ green (3 passed) |
| 05-06 | 05-06 | 4 | EVAL-06 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_silent_errors.py -q` | ✅ | ✅ green (33 passed) |
| 05-04 | 05-04 | 3 | EVAL-07 | — | N/A | unit | `devenv shell -- uv run --project python pytest python/tests/test_tags.py -q` | ✅ | ✅ green (52 passed) |
| 05-08 | 05-08 | 5 | EVAL-08 | — | N/A | unit + CLI runner | `devenv shell -- uv run --project python pytest python/tests/test_compare.py -q` | ✅ | ✅ green (30 passed) |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `python/tests/test_matching.py`, `test_selftests.py`, `test_stats.py`, `test_tags.py`, `test_silent_errors.py`, `test_summary_v4.py`, `test_compare.py` — new modules for EVAL-03 to EVAL-08
- [x] `python/tests/test_e2e_full_dataset.py` — new, `e2e` marker, the offline full-dataset proof (D-20)
- [x] `python/src/carimbo_evals/corruptions.py` — shared corruption catalogue, so the pytest self-tests and the e2e proof cannot drift (importlib mode means test files cannot import each other)
- [x] Remove or rewrite `test_grader_default_tolerance_matches_the_vector_file` in `python/tests/test_vectors.py` (D-03 breaks it; the vector file stays)
- [x] Update the pinned versions and tolerance string in `test_grader.py` and `test_e2e_fake.py`
- [x] scipy added to the lock and installed only after the blocking review checkpoint (D-08)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| scipy pin approved | EVAL-03 | Blocking supply-chain review (D-08), same as plans 01-01 and 04-02 | Reviewer checks the PyPI page and the lock diff before any install. **Done:** approved at plan 05-01 (reply `passed`), and scipy 1.18.1 installed only afterwards |
| Live `dev` run | EVAL-05, EVAL-07 | Paid step, runs only after explicit spend approval (D-19) | Run the capped recipe, then the free `read_only` replay; copy the run verbatim to `evals/results/`. **Done:** plan 05-11, after a recorded `approve-spend`; the human ran both. The cap stopped the live run at 51 of 54 dev cases (US$2.30699480), and the partial run was published as incomplete |
| Tag list frozen before `test` | EVAL-07 | Judgment call on the live failures (D-12) | Review the `untagged` bucket from the dev run, extend the rules, freeze with the grader version. **Done:** plan 05-12, human reply `freeze-as-is`; plan 05-13 recorded the freeze as D-48 with `failure-tags-1` and `grader-003` |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 90s (per-task module runs: 20 s at most, `test_selftests.py`. The full pytest step of `just py-check` took 88.89 s, so the wave-level check sits at this limit)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** validate-phase audit, 2026-11-06: 0 gaps across EVAL-03 to EVAL-08

## Validation Audit 2026-11-06

| Metric | Count |
|---|---|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
