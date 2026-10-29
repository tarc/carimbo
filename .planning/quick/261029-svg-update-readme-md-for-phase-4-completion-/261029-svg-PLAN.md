---
phase: quick-261029-svg
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - README.md
autonomous: true
requirements:
  - QUICK-261029-svg
estimate:
  tokens: 30000
  raw_tokens: 30000
  tasks: 2
  confidence: low
must_haves:
  truths:
    - "The README Status row `4. Synthetic Dataset` has state `Complete (2026-10-29)`, matching the ROADMAP progress table row `4. Synthetic Dataset | 12/12 | Complete | 2026-10-29`; every other Status row is byte-identical to 74f54fd"
    - "The Verification and UAT section opens with one `- Phase 4:` bullet, above the unchanged Phase 3.1, 3, 2 and 1 bullets, that links 04-VERIFICATION.md, 04-UAT.md, 04-REVIEW.md and 04-REVIEW-DISPOSITION.md and states only facts recorded there: verification passed with 4/5 must-have truths verified automatically after gap-closure plans 04-10 to 04-12, the fifth (severe renderings readable) settled by UAT; UAT first round 3 tests, 1 passed, 2 issues (G-04-1 blur misreads at sigma 2.4, G-04-2 twin bytes depending on SOURCE_DATE_EPOCH, CI run 40915848246); plans 04-11 and 04-10 closed both (D-41); re-test round 2 of 2 (nfe-0095 and nfe-0127 read cold at 220 DPI matched the XML, CI run 40950655793 green without SOURCE_DATE_EPOCH, 2810 tests, dataset-check 63 s); review 0 critical, 4 warnings, 8 info, all open, IN-07 named"
    - "The README intro (line 5) says Phase 4 is complete (2026-10-29) next to the earlier phases, and says the 154-case dataset is generated and checked offline with no model run on it yet; the Spend paragraph records Phase 4 at US$0 because no model was called"
    - "`devenv shell -- uv run --project python pytest python/tests/test_readme_drift.py -q` passes and `devenv shell -- just check` exits 0 from the repo root"
    - "README.md is the only file changed since 74f54fd outside .planning/quick and .planning/STATE.md; the work stays on branch gsd/phase-04-synthetic-dataset, nothing is pushed, no live paid recipe runs"
  artifacts:
    - path: "README.md"
      provides: "Reviewer-facing record that Phase 4 is complete, with its verification, UAT and review outcome, the intro and the spend"
      contains: "| Complete (2026-10-29) |"
  key_links:
    - from: "README.md"
      to: ".planning/phases/04-synthetic-dataset/04-VERIFICATION.md"
      via: "relative markdown link in the `- Phase 4:` bullet of Verification and UAT (the link test_readme_drift.py requires)"
      pattern: "](.planning/phases/04-synthetic-dataset/04-VERIFICATION.md)"
    - from: "README.md"
      to: ".planning/phases/04-synthetic-dataset/04-UAT.md"
      via: "relative markdown link in the same bullet"
      pattern: "](.planning/phases/04-synthetic-dataset/04-UAT.md)"
    - from: "README.md Status table"
      to: ".planning/ROADMAP.md progress table"
      via: "State cell equals `Complete (<Completed date>)` (status_drift in test_readme_drift.py)"
      pattern: "| Complete (2026-10-29) |"
---

<objective>
Bring README.md in line with the completed Phase 4 on the phase branch, before `/gsd-ship`: the Status row says `Complete (2026-10-29)`, the Verification and UAT section gains a `- Phase 4:` bullet linking the phase's verification, UAT and review reports, the intro names Phase 4 as complete, and the Spend paragraph records Phase 4's US$0.

Purpose: AGENTS.md ("Branching and pull requests") requires the README to reflect a completed phase before it ships, and `python/tests/test_readme_drift.py` (inside `just py-check`, so `just check` and CI) fails until the Status row and the verification bullet exist. Commit 74f54fd marked Phase 4 Complete in the ROADMAP, which turned those two checks red.

Output: edits to README.md only. Docs only: no code, schema, data, golden fixture, recipe, `.planning/phases` or ROADMAP change. No live paid recipe runs, no push, no branch creation or switch.
</objective>

<execution_context>
@~/.claude/gsd-core/workflows/execute-plan.md
@~/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@AGENTS.md
@README.md
@python/tests/test_readme_drift.py
@.planning/STATE.md
@.planning/ROADMAP.md (only the Progress table near line 311; grep for it, do not read the file)
@.planning/phases/04-synthetic-dataset/04-VERIFICATION.md (frontmatter lines 1 to 59 and lines 61 to 82)
@.planning/phases/04-synthetic-dataset/04-UAT.md
@.planning/phases/04-synthetic-dataset/04-REVIEW-DISPOSITION.md (frontmatter)

Checkout: the main checkout at the repo root, on branch `gsd/phase-04-synthetic-dataset`. This quick task does NOT get its own `gsd/quick-*` branch: commit on the current branch and do not push. Every command below runs from the checkout root, because devenv evaluates `devenv.nix` from the current directory. The repo-root `.devenv` is already recorded in the footprint ledger, so no ledger edit is needed. `devenv shell` prints a notice that devenv 2.4.1 is newer than the devenv input in devenv.lock; ignore it and do not run `devenv update` (it would change devenv.lock, which is out of scope).

Planning-time baseline (observed 2026-10-29 at HEAD 74f54fd, clean tree):
- `devenv shell -- uv run --project python pytest python/tests/test_readme_drift.py -q`: 2 failed, 25 passed. test_status_table_matches_the_roadmap ("Phase 4 is Complete on 2026-10-29 in .planning/ROADMAP.md, but README.md's Status row says ...") and test_every_complete_phase_links_its_verification ("no '- Phase 4:' bullet: add one that links .planning/phases/04-synthetic-dataset/04-VERIFICATION.md").
- `devenv shell -- just check`: dotnet-check passed; py-check failed with exactly those 2 tests (2808 passed, 13 deselected), so the later recipes did not run. `git diff --stat 0cd063c HEAD -- . ':(exclude).planning'` is empty, and 04-VERIFICATION.md records a full green `just check` at 0cd063c, so README.md is the only file that has to change.
- The status-ok, verification-ok, intro-ok, spend-ok and scope-ok gates below all fail at 74f54fd. They were dry-run on a scratch copy of README.md with exactly the text this plan prescribes: all four content gates printed their marker, and the drift checks (status, verification, docs-check range, evals/results links) plus test_repo_layout's recipe-mention check reported no problem.

README rules to keep intact (python/tests/test_repo_layout.py and test_readme_drift.py): every `just <name>` in an inline code span must name a real justfile recipe (`just py-check` and `just dataset-check` do); no line may begin with the word just; the docs-check row keeps `D-18 to D-41`; every `evals/results/<run>` link stays as it is. Style: plain English, no em dashes (README.md has none today).

Fact sheet (the only facts the new text may state; each re-read at planning time):
- `.planning/ROADMAP.md` Progress table: `| 4. Synthetic Dataset | 12/12 | Complete    | 2026-10-29 |`. Phases 5 and 6 are not started; their README rows stay as they are.
- `04-VERIFICATION.md` frontmatter: `status: passed`, `score: 4/5 must-haves verified`; body: re-verification after gap closure (plans 04-10 to 04-12 for UAT gaps G-04-1 and G-04-2); truth 5 (severe renderings hard but still human-readable) was routed to a human because legibility is a visual judgement. 74f54fd changed the status from human_needed to passed after the UAT re-tests.
- `04-UAT.md`: `status: complete`; tests 1 to 3 are the first round, tests 4 and 5 are re-tests; summary total 5, passed 3, issues 2. Test 2 (renderer fidelity against a reference DANFE) passed. Test 1 issue G-04-1: at sigma 2.4 the severe blur turned item-table digits into misreads (D00137 read as D00132, NCM 39191010 read as 39198890). Test 3 issue G-04-2: CI run 40915848246 failed two twin determinism tests because twin bytes depended on SOURCE_DATE_EPOCH. Both gaps resolved, by 04-11 (blur) and 04-10 (date pin). Test 4 passed: nfe-0095 and nfe-0127 read cold at native 220 DPI, every code, NCM and quantity and nfe-0127's access key equal the committed XML. Test 5 passed: CI run 40950655793 on 43a3511 green, `just py-check` 2810 passed, `just dataset-check` in 63 s, SOURCE_DATE_EPOCH absent from the job log.
- `docs/DECISIONS.md` D-41 records both fixes (sigma 1.8, PDF date pinned in code); the README Phase 4 section already cites D-41.
- `04-REVIEW.md` frontmatter: critical 0, warning 4, info 8. `04-REVIEW-DISPOSITION.md`: 12 findings, all `disposition: open`; IN-07 says the 1.8 sigma rests on one reader, who transcribed mild (1.2) just as correctly; 04-VERIFICATION.md calls the severe/mild contrast thin.
- Spend: `04-CONTEXT.md` line 11 says "Phase 4 makes no paid model call."; `04-12-SUMMARY.md` says no model was called (US$0); `evals/results/` has no run newer than 2026-10-22. `data/dataset` holds 154 XML and 154 PDF files.
</context>

<tasks>

<task type="tracer">
  <name>Task 1: Status row and Phase 4 verification bullet turn the drift test green</name>
  <files>README.md</files>
  <precondition>`git branch --show-current` prints gsd/phase-04-synthetic-dataset and `git status --porcelain -- README.md` prints nothing; otherwise stop and report, do not switch or create a branch.</precondition>
  <read_first>
    - README.md (lines 7 to 17 and lines 176 to 183)
    - python/tests/test_readme_drift.py (status_drift and verification_drift, lines 86 to 254)
  </read_first>
  <action>
Wire the one path the drift test reads end to end: ROADMAP progress row, README Status row, README verification bullet, verification file.

1. Status table, the `4. Synthetic Dataset` row (line 15). Replace only its third cell, the state, with `Complete (2026-10-29)`, in the same form as rows 1 to 3.1. Keep the first two cells and the pipe-and-single-space layout, so the row reads exactly as in the status-ok gate. Every other Status row stays byte-identical.

2. Verification and UAT section (heading `### Verification and UAT`, line 176). Add one bullet as the first item, directly above the existing `- Phase 3.1:` bullet (the section lists phases newest first). Write it verbatim, as one line:

- Phase 4: verification passed with 4/5 must-have truths verified automatically, re-verified after the gap-closure plans 04-10 to 04-12 ([`04-VERIFICATION.md`](.planning/phases/04-synthetic-dataset/04-VERIFICATION.md)). The fifth (severe renderings are hard but still human-readable) is a visual judgement and was settled by UAT. UAT is complete: the first round ran 3 tests, 1 passed (renderer fidelity against a reference DANFE) and 2 issues were found. G-04-1: at sigma 2.4 the severe blur turned small item-table digits into misreads (code D00137 read as D00132, NCM 39191010 as 39198890). G-04-2: the twin PDF bytes depended on `SOURCE_DATE_EPOCH`, so CI run 40915848246 failed two twin determinism tests on ubuntu-latest. Plan 04-11 lowered the severe blur to sigma 1.8 and plan 04-10 pinned the twin PDF date in code (D-41), and the re-test round passed 2 of 2: read cold at native 220 DPI, every item code, NCM and quantity of the blur-severe twins nfe-0095 and nfe-0127, and the access key of nfe-0127, matched the XML, and CI run 40950655793 was green on ubuntu-latest without `SOURCE_DATE_EPOCH` (`just py-check` 2810 tests passed, `just dataset-check` in 63 s) ([`04-UAT.md`](.planning/phases/04-synthetic-dataset/04-UAT.md)). The code review ([`04-REVIEW.md`](.planning/phases/04-synthetic-dataset/04-REVIEW.md)) found 0 critical issues, 4 warnings and 8 info findings, all open in [`04-REVIEW-DISPOSITION.md`](.planning/phases/04-synthetic-dataset/04-REVIEW-DISPOSITION.md). One of them, IN-07, notes that the severe blur level rests on one reader, who transcribed the mild blur just as correctly, so the contrast between mild and severe blur is thin.

The form copies the Phase 1 bullet (criteria verified automatically, the remainder confirmed by UAT), the Phase 3 bullet (UAT rounds, gap ids, closing plans, re-test round) and the Phase 3.1 bullet (review counts and links). Every number comes from the fact sheet. Leave the Phase 3.1, 3, 2 and 1 bullets and the review-warnings bullet unchanged. Touch nothing else in this task.
  </action>
  <verify>
    <automated>devenv shell -- uv run --project python pytest python/tests/test_readme_drift.py -q</automated>
    <fails_when>The command exits non-zero or pytest reports a failed test, above all test_status_table_matches_the_roadmap or test_every_complete_phase_links_its_verification.</fails_when>
    <automated>grep -qxF '| 4. Synthetic Dataset | 150+ seeded, varied and degraded cases with validated ground truth, manifest and fixed CI subset | Complete (2026-10-29) |' README.md && awk 'index($0, "| 4. Synthetic Dataset |") == 1 {n++} END{exit n!=1}' README.md && base="$(git show 74f54fd:README.md)" && test "$(printf '%s\n' "$base" | grep -E '^\| [0-9.]+\. ' | grep -vF '| 4. Synthetic Dataset |')" = "$(grep -E '^\| [0-9.]+\. ' README.md | grep -vF '| 4. Synthetic Dataset |')" && grep -qE '^\| 4\. Synthetic Dataset \| 12/12 \| Complete +\| 2026-10-29 \|' .planning/ROADMAP.md && echo status-ok</automated>
    <fails_when>No `status-ok` line is printed and the exit status is non-zero: the Phase 4 row does not read exactly as required or appears more than once, another Status row differs from 74f54fd, or the ROADMAP progress row no longer says Complete on 2026-10-29.</fails_when>
    <automated>awk '/^- Phase 4:/{n++} END{exit n!=1}' README.md && awk '/^### Verification and UAT/{f=1;next} /^#/{f=0} f && /^- Phase/{print; exit}' README.md | grep '^- Phase 4: verification passed with 4/5 must-have truths verified automatically, re-verified after the gap-closure plans 04-10 to 04-12' | grep -F '](.planning/phases/04-synthetic-dataset/04-VERIFICATION.md)' | grep -F '](.planning/phases/04-synthetic-dataset/04-UAT.md)' | grep -F '](.planning/phases/04-synthetic-dataset/04-REVIEW.md)' | grep -F '](.planning/phases/04-synthetic-dataset/04-REVIEW-DISPOSITION.md)' | grep -F 'G-04-1' | grep -F 'G-04-2' | grep -F '(D-41)' | grep -F 're-test round passed 2 of 2' | grep -F 'CI run 40915848246' | grep -F 'CI run 40950655793' | grep -F '4 warnings and 8 info findings' | grep -qF 'IN-07' && base="$(git show 74f54fd:README.md)" && test "$(printf '%s\n' "$base" | grep -E '^- Phase (3\.1|3|2|1):')" = "$(grep -E '^- Phase (3\.1|3|2|1):' README.md)" && P=.planning/phases/04-synthetic-dataset && grep -qxF 'status: passed' "${P}/04-VERIFICATION.md" && grep -qxF 'score: 4/5 must-haves verified' "${P}/04-VERIFICATION.md" && grep -qF 'plans 04-10 to 04-12' "${P}/04-VERIFICATION.md" && grep -qxF 'status: complete' "${P}/04-UAT.md" && grep -qxF 'total: 5' "${P}/04-UAT.md" && grep -qxF 'passed: 3' "${P}/04-UAT.md" && grep -qxF 'issues: 2' "${P}/04-UAT.md" && grep -qE '^### 4\. Re-test' "${P}/04-UAT.md" && grep -qE '^### 5\. Re-test' "${P}/04-UAT.md" && grep -qF 'CI run 40915848246' "${P}/04-UAT.md" && grep -qF 'CI run 40950655793' "${P}/04-UAT.md" && grep -qF '2810 passed' "${P}/04-UAT.md" && grep -qF 'in 63 s' "${P}/04-UAT.md" && grep -qE '^ +critical: 0$' "${P}/04-REVIEW.md" && grep -qE '^ +warning: 4$' "${P}/04-REVIEW.md" && grep -qE '^ +info: 8$' "${P}/04-REVIEW.md" && awk '/^ +disposition: open$/{n++} END{exit n!=12}' "${P}/04-REVIEW-DISPOSITION.md" && grep -qF 'id: IN-07' "${P}/04-REVIEW-DISPOSITION.md" && echo verification-ok</automated>
    <fails_when>No `verification-ok` line is printed and the exit status is non-zero: there is not exactly one `- Phase 4:` bullet, it is not the first `- Phase` bullet of Verification and UAT, it lacks one of the four links or a required fact, an older bullet changed since 74f54fd, or a source report no longer records the stated status, score, UAT counts, CI runs, review counts or the 12 open dispositions.</fails_when>
    <automated>devenv shell -- uv run --project python pytest python/tests/test_repo_layout.py -q</automated>
    <fails_when>The command exits non-zero or pytest reports a failed test, for example test_every_documented_just_recipe_exists on README.md.</fails_when>
  </verify>
  <acceptance_criteria>
    - test_readme_drift.py passes (27 tests; at 74f54fd 2 failed)
    - The Phase 4 Status row reads exactly as in the status-ok gate; every other Status row equals 74f54fd (status-ok gate)
    - The first bullet of `### Verification and UAT` is the single Phase 4 bullet with the four links and the fact-sheet facts; the Phase 3.1 to Phase 1 bullets equal 74f54fd (verification-ok gate)
    - test_repo_layout.py passes
  </acceptance_criteria>
  <done>The README Status row and verification record show Phase 4 complete on 2026-10-29, consistent with ROADMAP.md and the committed verification, UAT and review reports, and the README drift test is green.</done>
</task>

<task type="auto">
  <name>Task 2: Intro and Spend name Phase 4, then the whole-suite and scope gates</name>
  <files>README.md</files>
  <read_first>
    - README.md (line 5 and line 174 only)
  </read_first>
  <action>
1. Intro, line 5. Replace the whole line with this paragraph, verbatim, as one line:

Milestone 1, "Extraction, measured", has seven phases, including Phase 3.1 (Thin Agent Slice), inserted after Phase 3 by D-26. Phases 1 and 2 are complete (2026-10-08), Phase 3 is complete (2026-10-15), Phase 3.1 is complete (2026-10-22) and Phase 4 is complete (2026-10-29). The evidence so far is small: three synthetic extraction cases (one model, one run per configuration) and eight synthetic decision cases (one model, one live run). The 154-case dataset of Phase 4 is generated and checked offline, but no model has been run on it yet. The sections below separate what is measured from what is not.

The first, third and last sentences are unchanged; the second gains the Phase 4 clause, and the fourth is new. It matches the existing "Not measured yet" bullet about the 154-case dataset, which stays as it is.

2. Spend paragraph (`### Spend`, line 174). Insert one sentence between the Phase 3.1 sentence, which ends "the replay spent nothing).", and the sentence "Each is under the US$5 phase cap.". The new sentence, verbatim: Phase 4: US$0 (the dataset is generated offline and no model was called). Nothing else in the paragraph changes.

3. Touch nothing else in README.md. If, while reading, the executor finds another README sentence that contradicts Phase 4 being complete, fix only that fact from the fact sheet and record it in the SUMMARY. Then run every gate below from the checkout root (`devenv shell -- just check` takes several minutes; give it a timeout of at least 600000 ms). Commit README.md on the current branch as `docs(04): mark phase 4 complete in README`; do not push.
  </action>
  <verify>
    <automated>sed -n 5p README.md | grep -F 'has seven phases, including Phase 3.1 (Thin Agent Slice), inserted after Phase 3 by D-26.' | grep -F 'Phases 1 and 2 are complete (2026-10-08), Phase 3 is complete (2026-10-15), Phase 3.1 is complete (2026-10-22) and Phase 4 is complete (2026-10-29).' | grep -F 'three synthetic extraction cases (one model, one run per configuration) and eight synthetic decision cases (one model, one live run).' | grep -F 'The 154-case dataset of Phase 4 is generated and checked offline, but no model has been run on it yet.' | grep -qF 'The sections below separate what is measured from what is not.' && test "$(ls data/dataset/*.xml | wc -l)" = 154 && echo intro-ok</automated>
    <fails_when>No `intro-ok` line is printed and the exit status is non-zero: line 5 lacks one of the five required sentences, or data/dataset no longer holds 154 XML ground-truth files.</fails_when>
    <automated>grep -qF 'Phase 3.1: US$0.0908 (the agent probe US$0.0287 plus the one live decision run US$0.0621; the replay spent nothing). Phase 4: US$0 (the dataset is generated offline and no model was called). Each is under the US$5 phase cap.' README.md && grep -qF 'Phase 4 makes no paid model call.' .planning/phases/04-synthetic-dataset/04-CONTEXT.md && git diff --quiet 74f54fd -- evals/results && echo spend-ok</automated>
    <fails_when>No `spend-ok` line is printed and the exit status is non-zero: the Spend paragraph lacks the Phase 4 sentence in its place, 04-CONTEXT.md no longer records that Phase 4 makes no paid model call, or evals/results changed since 74f54fd.</fails_when>
    <automated>devenv shell -- just check</automated>
    <fails_when>The command exits non-zero; any recipe (dotnet-check, py-check, schema-check, datagen-check, dataset-check, decision-cases-check, e2e, docs-check, secrets-check) reports an error.</fails_when>
    <automated>names="$(git diff --name-only 74f54fd -- . ':(exclude).planning/quick' ':(exclude).planning/STATE.md')" && test "$names" = "README.md" && test "$(git branch --show-current)" = gsd/phase-04-synthetic-dataset && ! grep -q '—' README.md && echo scope-ok</automated>
    <fails_when>No `scope-ok` line is printed and the exit status is non-zero: a file other than README.md changed since 74f54fd outside .planning/quick and .planning/STATE.md, README.md did not change, the branch is not gsd/phase-04-synthetic-dataset, or an em dash appeared in README.md.</fails_when>
  </verify>
  <acceptance_criteria>
    - Line 5 names Phase 4 complete (2026-10-29) after the earlier phases and says the 154-case dataset has no model run yet (intro-ok gate)
    - The Spend paragraph records Phase 4 at US$0 between the Phase 3.1 sentence and the phase-cap sentence (spend-ok gate)
    - `devenv shell -- just check` exits 0
    - README.md is the only changed file outside .planning/quick and .planning/STATE.md, on branch gsd/phase-04-synthetic-dataset, with no em dash (scope-ok gate)
  </acceptance_criteria>
  <done>README.md records Phase 4 as complete everywhere it lists phase state or spend, every offline gate is green, and the change is committed on the phase branch without a push.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| repo to public reader | README.md is published on GitHub and read by reviewers; every claim it makes must be backed by a committed file |

## STRIDE Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-261029svg-01 | Repudiation | README.md Phase 4 status, verification, UAT, review and spend claims | medium | mitigate | Every new sentence is given verbatim in the tasks and built only from the fact sheet; the verification-ok, intro-ok and spend-ok gates re-read the sources (04-VERIFICATION.md status and score, 04-UAT.md counts, re-test headings and CI run ids, 04-REVIEW.md counts, 12 open dispositions, 04-CONTEXT.md no-paid-call line, 154 XML files, unchanged evals/results) in the same command that checks the README text |
| T-261029svg-02 | Information disclosure | README.md | low | mitigate | Prose edits only; the provider key is never read or printed; `just check` runs `just secrets-check`; no live recipe runs |
| T-261029svg-03 | Tampering | ROADMAP.md, .planning/phases, code, data, devenv.lock | low | mitigate | The scope-ok gate fails unless README.md is the only file changed since 74f54fd outside .planning/quick and .planning/STATE.md; the action forbids `devenv update` |
| T-261029svg-SC | Tampering | npm/pip/cargo installs | low | accept | No package is installed or upgraded; `uv run` and `just py-check` use the committed lock |
</threat_model>

<verification>
- `devenv shell -- uv run --project python pytest python/tests/test_readme_drift.py -q` passes (2 failed at 74f54fd)
- `devenv shell -- uv run --project python pytest python/tests/test_repo_layout.py -q` passes
- `devenv shell -- just check` exits 0
- The status-ok, verification-ok, intro-ok, spend-ok and scope-ok gates print their markers
- `git diff --name-only 74f54fd` (excluding .planning/quick and .planning/STATE.md) lists exactly README.md
</verification>

<success_criteria>
- A reviewer sees Phase 4 complete (2026-10-29) in the intro and the Status table, consistent with ROADMAP.md, and is told the 154-case dataset has not been run through a model yet
- Phase 4's passed verification (4/5 automatic, the fifth by UAT), the UAT rounds with G-04-1 and G-04-2 and their closing plans, the re-test evidence, and the review outcome (4 warnings, 8 info, all open, IN-07) are recorded with links to the four committed reports
- The Spend paragraph records Phase 4 at US$0
- test_readme_drift.py and `just check` are green again
- One commit on gsd/phase-04-synthetic-dataset touching README.md, plus the quick-task SUMMARY and STATE.md; nothing pushed
</success_criteria>

<output>
Create `.planning/quick/261029-svg-update-readme-md-for-phase-4-completion-/261029-svg-SUMMARY.md` when done
</output>
