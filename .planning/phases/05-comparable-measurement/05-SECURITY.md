---
phase: "05"
slug: "comparable-measurement"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-11-06"
---

# Phase 05 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| PyPI → python/.venv | third-party code enters the build and runs in every grade | see description |
| decision records → published claims | a reader trusts that the metric was fixed before the numbers | see description |
| stored model output → grader | `cases.jsonl` holds what a model emitted; the grader treats it as data | see description |
| stored model output → tag rules | predicted strings reach regexes and SequenceMatcher | see description |
| summary.json → published claims | a reader trusts the n and the interval printed next to a rate | see description |
| run directory → grader | `run.json` may be missing or damaged after a killed run | see description |
| summary.json from disk → compare | a file may come from a download, another machine or an older grader | see description |
| developer shell → paid provider | the recipe is the only route to spend on the dev split | see description |
| CLI → committed evals/results | a regrade must not reach a verbatim copy | see description |
| test process → ScriptedHost | a local host on a free port with an ephemeral eval key; no provider is reachable from it | see description |
| developer shell → Anthropic API | the only paid call of the phase, with a real key | see description |
| evals/runs → public repository | a verbatim copy becomes public | see description |
| dev failures → rule list | rules fitted to data; the holdout must stay unseen | see description |
| README claims → reviewer | a reader takes the README at its word | see description |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-05-01 | Tampering | python/uv.lock between review and install | medium | mitigate | Task 2 re-runs `uv lock --check`, and Task 3 installs with `--locked`, so a lock other than the reviewed one fails instead of installing **Evidence:** `just py-check` installs with `uv sync --project python --locked` (justfile:26); 05-01-SUMMARY.md records the lock check | closed |
| T-05-02 | Repudiation | primary metric chosen after the numbers | medium | mitigate | D-42 lands in Wave 1, before any grader code and before the live dev run, and git history dates it **Evidence:** `git merge-base --is-ancestor e32da51 4b8ab2c` holds: the D-42 commit precedes the first grader commit | closed |
| T-05-03 | Repudiation | D-26 obligation silently skipped | low | mitigate | D-46 names the cuts and pointer lines under D-26 and D-40 make the discharge visible; docs-check enforces the heading **Evidence:** D-46 lists the cuts; `**Refined by:** D-46` sits under D-26 and D-40; `just docs-check` covers the heading | closed |
| T-05-04 | Tampering | model output with non-list rows or non-string values reaching grader.py | medium | mitigate | `diff_rows` drops non-dict rows and treats a non-list as no rows; `column_right` treats a non-string as wrong; `parse_decimal` returns None on bad input, so a hostile value is a wrong value and never an exception **Evidence:** `clean_rows`, `column_right` (matching.py:71) and `parse_decimal` (normalize.py:28), with tests in test_matching.py and test_grader.py | closed |
| T-05-05 | Denial of service | a very long row list making the score matrix quadratic | low | accept | Output is bounded by MaxTokens 16000 at about 122 tokens per item, so a matrix stays near 130 by 80; offline tool, one operator **Evidence:** Accepted, see Accepted Risks Log. No code control required | closed (accepted) |
| T-05-06 | Repudiation | a lenient grader inflating the primary metric | high | mitigate | Exact Decimal comparison at tolerance 0.00, no similarity in `column_right`, and the boundary tests of Task 2 pin both sides of the threshold **Evidence:** `DEFAULT_TOLERANCE = Decimal("0.00")` (grader.py:36); both live runs report tolerance 0.00; boundary tests in test_grader.py and test_matching.py | closed |
| T-05-07 | Denial of service | SequenceMatcher on very long predicted strings | low | accept | Quadratic only in string length; model output is bounded by MaxTokens and grading is an offline, single-operator step **Evidence:** Accepted, see Accepted Risks Log. No code control required | closed (accepted) |
| T-05-08 | Repudiation | tag rules re-fitted on the holdout | high | mitigate | Pinned-order test makes any edit a visible diff; D-43 and plan 05-13 freeze the list with its grader version before any test run; no recipe runs the test split **Evidence:** `test_tag_order_is_pinned` (test_tags.py:528); D-48 freezes failure-tags-1 with grader-003; no recipe passes `--split test` (justfile:300 is `--split dev`) | closed |
| T-05-09 | Repudiation | a rate that hides its denominator or treats twins as independent | high | mitigate | Each row prints cases, invoices and excluded; Wilson n is distinct base_case values; the counts are pinned against the manifest table **Evidence:** `wilson(correct, cases, invoices)` (stats.py:27); every headline row prints cases, invoices and excluded; `test_twins_of_one_base_count_as_two_cases_and_one_invoice` | closed |
| T-05-10 | Repudiation | non-reproducible regrades from float or RNG drift | medium | mitigate | Decimal inside localcontext, one rounding helper, `random.Random.random` as the only stream, byte-stable regrade test in plan 05-07 **Evidence:** `localcontext` and a seeded `random.Random` in stats.py; `test_a_regrade_is_byte_identical_apart_from_graded_at` | closed |
| T-05-11 | Repudiation | a stopped or broken run passing as a clean one | high | mitigate | `incomplete` and `incomplete_detail` derive from statuses and run.json, summary.md prints a banner line, and tests cover each trigger **Evidence:** `incomplete` and `incomplete_detail` (summary.py:369), INCOMPLETE banner (summary.py:809 on); proven live: the cap-stopped run prints `INCOMPLETE ... stopped: cost_cap` | closed |
| T-05-12 | Tampering | a damaged run.json failing the grade | low | mitigate | `load_run_state` returns `{}` on a missing or malformed file; statuses alone still mark infrastructure and harness cases **Evidence:** `load_run_state` (grader.py:490) returns `{}` on a missing or damaged file | closed |
| T-05-13 | Repudiation | rows treated as independent, giving intervals too narrow | medium | mitigate | Resampling unit is base_case per scope and per slice, recorded in `stats.bootstrap`, with tests that a base and its twin move together; each slice entry shows `invoices`, and D-44 states that the interval runs narrow on few invoices **Evidence:** `bootstrap_sums` resamples per invoice (stats.py:86), and each slice entry shows `invoices`; twin tests in test_summary_v4.py | closed |
| T-05-14 | Repudiation | a regrade that differs from the published summary | medium | mitigate | Byte-stable regrade test, seeded stream, recorded seed and resamples **Evidence:** `test_a_regrade_is_byte_identical_apart_from_graded_at` (test_summary_v4.py:677) | closed |
| T-05-15 | Tampering | crafted summary.json with wrong types or missing keys | medium | mitigate | `load_summary` validates shape and types up front and maps any failure to `CompareRefusal` and exit 2; no eval, no pickle **Evidence:** `load_summary` (compare.py:117) raises `CompareRefusal`; no `eval` or `pickle` in compare.py; exit 2 tested in test_compare.py | closed |
| T-05-16 | Denial of service | oversized summary.json | medium | mitigate | `MAX_SUMMARY_BYTES` checked before parsing; work bounded by the intersection of case ids **Evidence:** `MAX_SUMMARY_BYTES` checked before parsing (compare.py:21, :126) | closed |
| T-05-17 | Repudiation | a comparison of mismatched runs read as a real change | high | mitigate | `check_comparable` refuses on manifest hash, grader version and tolerance, with no override option, asserted by a parameter-set test **Evidence:** `check_comparable` (compare.py:134), parameter-set test at test_compare.py:350; the `compare` command has no override option | closed |
| T-05-18 | Denial of wallet | live dev run beyond budget | high | mitigate | The recipe passes `--max-cost-usd {{ max_cost }}` with default 2.50, the runner stops at the cap with exit 3, and a layout test pins the arguments **Evidence:** `--max-cost-usd {{ max_cost }}` (justfile:301), default 2.50; exit 3 at the cap (cli.py:294); proven live: run.json spent_usd 2.30699480, stopped_reason cost_cap, 3 cases not dispatched | closed |
| T-05-19 | Tampering | regrade overwriting a published run | medium | mitigate | `_is_published_summary` refuses with exit 2; `--out` writes elsewhere; tests hash the run directory **Evidence:** `_is_published_summary` (cli.py:300, :343) with tests on a published copy in test_grader.py; `git log --diff-filter=M -- evals/results` prints nothing | closed |
| T-05-20 | Information disclosure | provider key in logs | high | mitigate | Same `_with-provider-key` path as the other live recipes, presence checks only, and `test_live_recipes_resolve_the_key_through_secretspec_and_never_echo_it` extended to this recipe; ephemeral `CARIMBO_EVAL_API_KEY` per run **Evidence:** `dev-split` is in the live-recipe list of `test_live_recipes_resolve_the_key_through_secretspec_and_never_echo_it` (test_repo_layout.py:112); the eval key is generated per run | closed |
| T-05-21 | Spoofing | a live provider reached from an e2e test | high | mitigate | The module starts Carimbo.ScriptedHost only, which registers the scripted gateway; no provider key is set in its environment, and CI has none **Evidence:** Carimbo.ScriptedHost registers `ScriptedLlmGateway` as `ILlmGateway` (Program.cs:16), so the e2e cannot reach a provider even if a key is present in the environment; CI holds no key | closed |
| T-05-22 | Repudiation | a proof that drifts from the self-tests | medium | mitigate | One catalogue in corruptions.py; expected statuses are asserted per case, not in aggregate **Evidence:** test_e2e_full_dataset.py imports `carimbo_evals.corruptions.corrupted` (line 33); `set(CORRUPTIONS) | {"schema_invalid"} == set(TAG_ORDER)` holds | closed |
| T-05-23 | Denial of wallet | an unapproved paid run, a second paid run, or a raised cap | high | mitigate | Two `gate="blocking-human"` checkpoints: a decision whose `approve-spend` reply is recorded before the paid command is shown, then the human-run step; the instructions forbid raising the cap; a rerun in `read_write` answers from the cache at no cost **Evidence:** `approve-spend` recorded at 2026-11-05T23:57:57Z, before live `started_at` 2026-11-06T00:18:00Z; the human ran both steps; exactly one paid run exists (the replay spent 0.00000000); no rerun happened after the cap stop | closed |
| T-05-24 | Information disclosure | provider key in published files | high | mitigate | The runner stores no request headers; `just secrets-check` runs on the published tree in Task 3 **Evidence:** `just secrets-check` printed `secrets ok` on the published tree, rerun 2026-11-06 at this audit | closed |
| T-05-25 | Information disclosure | real data in published runs | medium | mitigate | All cases are synthetic (D-14); `test_synthetic_only.py` runs in Task 3 **Evidence:** `test_synthetic_only.py`: 31 passed, rerun at this audit | closed |
| T-05-26 | Repudiation | rules fitted on the holdout | high | mitigate | Only the dev run is read; no recipe runs the test split; the human decides at a `gate="blocking-human"` checkpoint; plan 05-13 records the freeze **Evidence:** Human reply `freeze-as-is` at 2026-11-06T02:45:52Z (05-12-SUMMARY.md); `no test-split model answer 100` printed in plans 05-12 and 05-13; no recipe runs the test split | closed |
| T-05-27 | Tampering | published runs rewritten by a regrade | medium | mitigate | `grade --out` to new directories, the CLI guard from plan 05-09, and the acceptance check that no file under evals/results is modified **Evidence:** `git log --diff-filter=M --oneline -- evals/results` prints nothing; `freeze-as-is` produced no regrade | closed |
| T-05-28 | Repudiation | dev figures read as holdout results | high | mitigate | Each dev figure is labelled dev, the section says no `test` result exists, D-48 records the freeze before the holdout, and Task 1's check finds no model answer on a `test` case in any run directory or in `.cache/llm` **Evidence:** Every figure in README `### Phase 5 live dev run` is labelled dev; the section states what the numbers cannot show; D-48 records the freeze; `no test-split model answer 100` ran first | closed |
| T-05-29 | Repudiation | a confound hidden behind a low score | medium | mitigate | The README names the wrap hazards with measured shares, from the summary's `render_hazards` block **Evidence:** README `**Wrap hazards (D-47).**` paragraph gives measured shares: 12 of 30 text-layer dev cases, 5 of 12 against 7 of 18 correct, 1 of 18 wrong units on `PAR` | closed |
| T-05-SC | Tampering | pip install (scipy 1.18.1) | high | mitigate | Exact pin, resolve-only before review, blocking human checkpoint (never auto-approved) over the PyPI page, the lock diff (scipy is the only new package) and the wheel hash, then `uv sync --locked`; provenance attestation noted in the plan context **Evidence:** scipy==1.18.1 in python/pyproject.toml; scipy is the only package added to python/uv.lock against origin/main (checked with git plumbing, comparing `name =` lines); human reply `passed` at plan 05-01 Task 2, recorded in 05-01-SUMMARY.md, before any install | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above workflow.security_block_on count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| R-05-01 | T-05-05 | A very long row list makes the score matrix slower, but output is bounded by MaxTokens 16000 and grading is an offline, single-operator tool. Largest case seen in the dev run: 80 true rows against 119 predicted | Plan 05-03 threat model | 2026-11-06 |
| R-05-02 | T-05-07 | SequenceMatcher is quadratic only in string length, runs on bounded model output, and grading is offline | Plan 05-04 threat model | 2026-11-06 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-11-06 | 30 | 30 | 0 | secure-phase (orchestrator, L1 grep-depth; auditor skipped per short-circuit: register authored at plan time, asvs_level 1) |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-11-06

## Security Audit 2026-11-06

| Metric | Count |
|---|---|
| Threats found | 30 |
| Closed | 30 |
| Open | 0 |
