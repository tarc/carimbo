---
phase: 01-walking-skeleton
plan: 13
subsystem: repo-tooling
tags: [just, devenv, github-actions, agents-md, secretspec, live-skeleton, repo-01, repo-02, repo-03, repo-04, ci-01, dom-08]
status: complete

requires:
  - phase: 01-walking-skeleton
    provides: "Schema export and generated models (01-04, 01-09), datagen and datagen check (01-05..07), eval endpoint and runner (01-08), CostAccountingLlmGateway (01-11), AnthropicLlmGateway registered on a resolved provider key (01-12)"
provides:
  - "justfile: dotnet-check, py-check, schema, schema-check, datagen, datagen-check, e2e, docs-check, secrets-check, check, spike-live, skeleton, plus private _with-provider-key and _skeleton-run"
  - "README with the Nix-free reviewer path, the devenv path, the secrets and live-calls procedure and the commands table"
  - "devenv.nix, devenv.yaml and devenv.lock: toolchain only, no services, no nixpkgs ruff or pyright"
  - "AGENTS.md as the canonical instruction file and a root CLAUDE.md that only imports it"
  - ".github/workflows/ci.yml: jobs dotnet, python, contract on every PR, every step a just recipe, read-only token, no secrets"
  - "python/tests/test_repo_layout.py: 10 static checks of agent files, recipes, secretspec.toml and the CI shape"
  - "One recorded live skeleton run: 3 of 3 cases success, US$0.019492"
affects: [phase-1-verification, 02-dataset]

actuals:
  tokens: 8882
  tasks: 3
  commits: 2
plan_head_before: 7ddb885cb24c62e4d971a3f9ff20094d9b4258ac
plan_head_after: 0af93f4cb45970345344d98c4a676b476e465da3

tech-stack:
  added: []
  patterns:
    - "Every CI step after setup is a just recipe, so local and CI commands are identical"
    - "Live recipes go through one private wrapper (_with-provider-key): exported key, else secretspec run with SECRETSPEC_REASON, else exit 2; it tests presence only and never prints a value"
    - "One CI job (contract) owns regeneration of schema/ and python/src/carimbo_models/, and regeneration always precedes git diff --exit-code"

key-files:
  created:
    - justfile
    - README.md
    - devenv.nix
    - devenv.yaml
    - devenv.lock
    - AGENTS.md
    - CLAUDE.md
    - .github/workflows/ci.yml
    - python/tests/test_repo_layout.py
  modified:
    - .gitignore

key-decisions:
  - "secrets-check matches sk-ant-<kind><NN>-<8+ key characters> (real key shapes such as sk-ant-api03-...) instead of any 8 characters after sk-ant-, because the broader pattern flagged the word 'synthetic' in an existing 01-12 test string."
  - "devenv.lock is committed: it pins the devenv nixpkgs input, in line with the exact-pin rule."
  - "The eval key for the skeleton run is minted with `uv run --project python python -c` rather than bare python3, because uv is a required tool and python3 is not on every PATH."

patterns-established:
  - "Live paid steps run only through just skeleton and just spike-live; the run cap (US$1.00) is passed down as the recipe argument"

requirements-completed: [REPO-01, REPO-02, REPO-03, REPO-04, CI-01, DOM-08]

coverage:
  - id: D1
    description: "One command per stack: just dotnet-check builds with warnings as errors, format-checks and tests the .NET solution; just py-check syncs, lints, type-checks and tests Python; just check runs every offline gate"
    requirement: "REPO-01, REPO-02"
    verification:
      - kind: command
        ref: "devenv shell -- just check exits 0 (170 .NET tests, 121 Python tests, e2e 3 passed, schema-check, datagen-check, docs ok, secrets ok)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Reviewer path without Nix is documented in README (SDK per global.json, uv, just) and CI runs the same recipes on a clean Ubuntu runner on every PR"
    requirement: "REPO-03, CI-01"
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_ci_workflow_is_fork_safe_and_unfiltered, #test_ci_steps_after_setup_are_just_recipes"
        status: pass
    human_judgment: true
    rationale: "A hosted GitHub Actions run on a pushed PR is the only proof that the Nix-free path works on ubuntu-latest; nothing was pushed in this plan"
  - id: D3
    description: "The developer gets the same toolchain from devenv shell on NixOS-WSL, with no services and no nixpkgs ruff or pyright"
    requirement: "REPO-03"
    verification:
      - kind: command
        ref: "devenv shell -- just dotnet-check py-check exits 0 and devenv shell -- just check exits 0 on the author's NixOS-WSL machine"
        status: pass
    human_judgment: false
  - id: D4
    description: "AGENTS.md is the single canonical instruction file; CLAUDE.md contains only @AGENTS.md"
    requirement: "REPO-04"
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_claude_md_only_imports_agents_md, #test_agents_md_is_canonical_and_self_sufficient, #test_every_documented_just_recipe_exists, #test_secretspec_procedure_is_documented"
        status: pass
    human_judgment: true
    rationale: "Instruction discovery by Claude Code and by OpenCode needs the interactive runtimes"
  - id: D5
    description: "Regeneration of generated artifacts and its diff check run in exactly one CI job, regeneration before git diff --exit-code, plain pull_request trigger, read-only permissions, no secrets, no path filters"
    requirement: "DOM-08, CI-01"
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_schema_check_regenerates_before_it_diffs, #test_ci_workflow_is_fork_safe_and_unfiltered"
        status: pass
      - kind: command
        ref: "just schema-check inside just check exits 0; ! grep -nE 'pull_request_target|secrets\\.' .github/workflows/ci.yml prints nothing"
        status: pass
    human_judgment: false
  - id: D6
    description: "The live recipes use the exported key when present, otherwise resolve it through secretspec run with SECRETSPEC_REASON, otherwise exit 2 naming both options; no recipe prints the key"
    requirement: "REPO-01"
    verification:
      - kind: command
        ref: "env -u CARIMBO_ANTHROPIC_API_KEY -u ANTHROPIC_API_KEY just _with-provider-key sh -c 'test -n ...' prints resolved-via-secretspec; the PATH-stripped no-key run prints the message naming CARIMBO_ANTHROPIC_API_KEY and `secretspec set` and exit=2; the key-echo grep prints nothing"
        status: pass
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_live_recipes_resolve_the_key_through_secretspec_and_never_echo_it, #test_secretspec_toml_declares_an_optional_key_and_the_development_profile"
        status: pass
    human_judgment: false
  - id: D7
    description: "just skeleton runs the three cases live through the endpoint within the US$1.00 cap and writes a graded summary with field grades, tokens, cost, latency and trace IDs"
    requirement: "REPO-01"
    verification:
      - kind: command
        ref: "plan verify one-liner over evals/runs/skeleton-20261008T001300Z prints: skeleton ok skeleton-20261008T001300Z {success: 3, total: 3} 0.01949200; ! grep -rlE 'sk-ant-|x-api-key' evals/runs prints nothing; git status --porcelain evals prints nothing"
        status: pass
    human_judgment: false

duration: 13min
completed: 2026-10-08
---

# Phase 1 Plan 13: One command per stack, CI on every PR, and the live skeleton run Summary

**A root justfile gives one recipe per stack plus `just check` for every offline gate, AGENTS.md (imported by a one-line CLAUDE.md) serves both agent runtimes, a three-job CI workflow runs the same recipes on every PR with no secrets, and `just skeleton` ran the three cases live through secretspec for US$0.019492 (3 of 3 success).**

## Performance

- **Duration:** 13 min (2026-10-08 00:00:45Z to about 00:13:30Z)
- **Tasks:** 3 of 3 (Task 3 changed no file, so it has no commit)
- **Files:** 9 created, 1 modified (about 8.9k tokens on the chars/4 scale over the 35.5 KB diff, which includes the 65-line devenv.lock)

## Accomplishments

- **justfile (D-13).** Recipes `dotnet-check`, `py-check`, `schema`, `schema-check`, `datagen`, `datagen-check`, `e2e`, `docs-check`, `secrets-check`, `check`, `spike-live`, `skeleton` and the private `_with-provider-key` and `_skeleton-run`. `schema-check` runs `just schema` before `git diff --exit-code -- schema python/src/carimbo_models`, so a stale artifact fails deterministically (DOM-08).
- **Provider key handling (D-10).** `_with-provider-key` execs the command unchanged when `CARIMBO_ANTHROPIC_API_KEY` or `ANTHROPIC_API_KEY` is set, otherwise runs it under `secretspec run --` with `SECRETSPEC_REASON` defaulted, otherwise exits 2 with a message naming both options. It tests presence with `-n` and never prints a value.
- **`_skeleton-run`.** Mints an ephemeral eval key, runs `datagen-check`, builds the Api into a temp dir, starts it from the DLL on `127.0.0.1:5080` (refusing to run if the port already answers), polls `/healthz` for up to 120 s, runs `carimbo-evals run --max-cost-usd`, stops the Api by trap, and grades whatever was recorded before passing on the runner's exit code.
- **README** with the Nix-free reviewer path, the devenv path, the commands table, the layout, and the full secrets section (export with `read -rs`, or the optional secretspec procedure).
- **devenv.nix / devenv.yaml.** Toolchain only: dotnet-sdk_10, uv, just, Python 3.12, git, curl, Node (for pyright), the secretspec CLI. No services; ruff and pyright come from the uv lock; devenv's secretspec integration is not enabled, so `devenv shell` never loads the key.
- **AGENTS.md** (project, layout, commands, conventions, secrets, what not to use, planning) as the single instruction file; **CLAUDE.md** is exactly `@AGENTS.md`; `.claude/CLAUDE.md` is untouched.
- **CI** (`ci.yml`): plain `pull_request`, `push` on main and `workflow_dispatch`; `contents: read`; no `secrets.`; no path filters; jobs `dotnet`, `python`, `contract`; `just schema-check` appears exactly once. Action majors re-verified with `git ls-remote` on 2026-10-08: checkout v7, setup-dotnet v6, setup-uv v7, setup-just v4.
- **test_repo_layout.py:** 10 static checks, all passing.

## Live skeleton run (counts toward the US$5 cap, D-09)

Command: `nix shell nixpkgs#dotnet-sdk_10 nixpkgs#uv nixpkgs#just -c just skeleton`, with both provider-key variables unset in the calling environment. The key was resolved by the recipe's own `secretspec run` fallback (startup line: `model gateway: anthropic (key from CARIMBO_ANTHROPIC_API_KEY)`). Exit 0, one run, no retry.

| Field | Value |
|-------|-------|
| Run directory | `evals/runs/skeleton-20261008T001300Z` (git-ignored; `cases.jsonl`, `run.json`, `summary.json`, `summary.md`) |
| Models | requested `claude-haiku-4-5`, returned `claude-haiku-4-5-20251001` |
| Pricing / prompt version | `anthropic-2026-10-04` / `extract-001` |
| Outcomes | 3 of 3 `success` (0 refused, 0 schema_invalid, 0 truncated, 0 harness errors) |
| Tokens | input 17,712, output 356, cache read 0, cache write 0 |
| Cost | US$0.01949200 (case-001 0.004154, case-002 0.004266, case-003 0.011072), 0 unpriced |
| Latency | min 2,344 ms (case-003), median 3,748 ms (case-001), max 3,869 ms (case-002) |
| Trace IDs | `d0b2e0f116b074f0169bed320c3f7cef`, `dc03db551118d77c5373ec3d10c773b8`, `9c3ebfc96ac500ca5df073b1251e3f66` |
| total_amount delta | 0.00 on all three |

Field grades (9 graded fields per case): case-001 8/9, case-002 8/9, case-003 9/9. Every miss is `access_key`. The model transcribed the 44-digit key from the printed page imperfectly: case-001 returned `33260143247393000145550008000346154184354539` against the truth `33260143247393000145550080003461541843545391` (a digit slipped out of place and the last digit lost); case-002 returned `35260008NJE7F4NI0001195500800009951691708001` for an alphanumeric-CNPJ key. This is the measurement the skeleton exists to produce: reading a long digit string from a rendered page is where the model is weakest, and it is exactly what the Code 128 barcode decode (D-05) is meant to take over. It is a finding for the extraction phase, not a defect in this plan.

**Spend:** spike US$0.2551 + 01-12 smoke US$0.0042 + this run US$0.0195 = **US$0.2788 cumulative for Phase 1**, under the US$5.00 cap (US$4.7212 left). No key or auth header appears in the run directory (`! grep -rlE 'sk-ant-|x-api-key' evals/runs` prints nothing).

## devenv human check, run here

`devenv shell -- just dotnet-check py-check` and then `devenv shell -- just check` both exit 0 on the author's NixOS-WSL machine (devenv 2.4.1, dotnet 10.0.400 from devenv-nixpkgs, uv 0.12.11, just 1.58.0): 170 .NET tests and 121 Python tests pass, ruff and pyright (from the uv lock) are clean, the e2e test passes (3), `schema-check`, `datagen-check`, `docs-check` and `secrets-check` pass. The first attempt exposed two NixOS problems, fixed below as deviations 2 and 3.

**Nix GC root created:** `/home/tarci/projects/carimbo/.devenv/` (about 1.4 MB of links; `.devenv/gc/shell`, `.devenv/gc/task-config-devenv-config-task-config` and `.devenv/bash-bash` pin the devenv shell closure). `nix-store --gc --print-roots` confirms the three roots. Also created: `python/.venv` (235 MB, rebuilt on the Nix interpreter by the devenv run; git-ignored) and the existing .NET `bin/` and `obj/` directories. `.devenv*` is now git-ignored.

## Task Commits

1. **Task 1: justfile, README, devenv toolchain** - `9b7d4db` (feat)
2. **Task 2: AGENTS.md, CLAUDE.md, CI workflow, repo-layout test** - `0af93f4` (feat)
3. **Task 3: live skeleton run** - no commit; the plan allows only a recipe fix in this task and none was needed (`evals/runs/` is git-ignored)

**Plan metadata:** committed separately after this file (docs: complete plan).

## Decisions Made

See `key-decisions`: the narrower `secrets-check` pattern, committing `devenv.lock`, and minting the eval key through `uv run` instead of bare `python3`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] secrets-check flagged a synthetic test string**
- **Found during:** Task 1, first `just check`
- **Issue:** The plan's pattern (`sk-ant-` plus 8 or more key characters) matched `sk-ant-synthetic` in `dotnet/tests/Carimbo.Llm.Tests/AnthropicGatewayTests.cs:268`, a deliberately fake string used to prove an echoed body does not leak.
- **Fix:** The pattern now requires the real key shape, `sk-ant-<kind><NN>-` followed by at least 8 key characters (for example `sk-ant-api03-...`). Checked it against a sample real-shaped string (matches) and against the repo (no hit). The recipe line itself still cannot match.
- **Files modified:** `justfile`
- **Commit:** `9b7d4db`

**2. [Rule 3 - Blocking] pyright cannot fetch Node on this machine**
- **Found during:** Task 1, first `just check`
- **Issue:** The PyPI pyright wrapper downloads Node when none is on PATH; here the download failed (`CERTIFICATE_VERIFY_FAILED` from the uv-managed Python), and a downloaded Node binary would not run on NixOS anyway.
- **Fix:** `pkgs.nodejs` added to `devenv.nix` with a comment; README keeps the note that a fresh non-Nix machine fetches Node itself. Outside devenv, `nix shell ... nixpkgs#nodejs` works.
- **Files modified:** `devenv.nix`
- **Commit:** `9b7d4db`

**3. [Rule 3 - Blocking] Prebuilt wheels cannot find libstdc++ under the Nix interpreter**
- **Found during:** Task 1, first `devenv shell -- just py-check`
- **Issue:** With `UV_PYTHON` pointing at the Nix Python, importing `zxingcpp` failed with `libstdc++.so.6: cannot open shared object file`, so `test_datagen.py` could not be collected.
- **Fix:** `env.LD_LIBRARY_PATH` set to `stdenv.cc.cc.lib` and `zlib` in `devenv.nix`. Second run: all 121 tests pass.
- **Files modified:** `devenv.nix`
- **Commit:** `9b7d4db`

**4. [Rule 2 - Missing critical] `.devenv*` was not fully ignored and devenv.lock was untracked**
- **Found during:** Task 1 (devenv run)
- **Issue:** `.gitignore` listed only `.devenv/`; devenv also writes `.devenv*` files, and `devenv.lock` (the nixpkgs pin) is not in the plan's file list.
- **Fix:** `.devenv*` added to `.gitignore`; `devenv.lock` committed so the toolchain is pinned.
- **Files modified:** `.gitignore`, `devenv.lock`
- **Commit:** `9b7d4db`

**5. Environment adaptations (not plan deviations):** the eval key is minted with `uv run --project python python -c ...` (python3 is not on PATH here; uv is required anyway); the Api is built into a temp directory and run from its DLL for a clean process id to stop; `_skeleton-run` additionally refuses to start if something already answers on port 5080, so a stale server can never be graded as the new run.

**Total deviations:** 4 auto-fixed (1 bug, 2 blocking, 1 missing-critical). **Impact:** none on scope; all are needed for the recipes to work on NixOS-WSL or to avoid a false positive.

## Authentication Gates

None. The provider key resolved through the recipe's secretspec fallback (`CARIMBO_ANTHROPIC_API_KEY` stored, not exported), so the Task 3 precondition was met. The key value was never read, printed or logged.

## Issues Encountered

- `secrets-check`, pyright and the NixOS libstdc++ problem above were found by running the real commands and fixed in place.
- `devenv.nix` replaces the host `LD_LIBRARY_PATH` (which on WSL lists `/usr/lib/wsl/lib`) inside the devenv shell. That is harmless for this toolchain but would matter if GPU libraries were needed there.

## Outstanding human checks (not verifiable in this run)

1. **CI on GitHub (CI-01, REPO-03 reviewer path):** push the branch, open a PR, and confirm the `dotnet`, `python` and `contract` jobs pass on `ubuntu-latest` without Nix. Nothing was pushed, as instructed. CI-01 should stay open until this has been seen.
2. **Agent-runtime discovery (REPO-04):** ask Claude Code and OpenCode "what is the one command that checks everything, and where does the API key come from?"; both should answer `just check` and `CARIMBO_ANTHROPIC_API_KEY` (fallback `ANTHROPIC_API_KEY`), optionally via `secretspec run`, citing AGENTS.md. The file shape is asserted by `test_repo_layout.py`; the runtime behaviour is not.
3. **Fresh-clone check:** the plan's wording says "in a fresh clone"; devenv was run in this working copy, which exercises the same `devenv.nix`/`devenv.lock` but not a clone from scratch.

## Known Stubs

None.

## Threat Flags

None beyond the plan's register. T-01-SC mitigated (only first-party or tool-vendor actions at verified majors; `uv sync --locked`); T-01-32 (plain `pull_request`, `contents: read`, no `secrets.`; asserted by `test_ci_workflow_is_fork_safe_and_unfiltered` and the negative grep); T-01-33 (`just secrets-check` in the contract job, `.env*` ignored); T-01-34 (run cap 1.00 USD passed to the runner, one run, cumulative spend tracked); T-01-35 (Api bound to 127.0.0.1, ephemeral eval key, Development only, killed by trap); T-01-37 (`_with-provider-key` tests presence only, key-echo grep and test empty, `devenv shell` loads no secret).

## Next Phase Readiness

Phase 1 plans are all executed. Remaining before the phase can be called verified: the two human checks above (first PR run, runtime instruction discovery). The skeleton result gives the first measured baseline for Phase 2/3: field accuracy 25 of 27, all misses on `access_key`, mean cost US$0.0065 per case on `claude-haiku-4-5`. Remaining Phase 1 live budget: US$4.7212.

## Self-Check: PASSED

- FOUND: justfile, README.md, devenv.nix, devenv.yaml, devenv.lock, AGENTS.md, CLAUDE.md, .github/workflows/ci.yml, python/tests/test_repo_layout.py
- FOUND commits (ancestors of HEAD): 9b7d4db, 0af93f4
- Acceptance criteria re-run for Tasks 1, 2 and 3: all pass (counts, negative greps, `cat CLAUDE.md` prints `@AGENTS.md`, `.claude/CLAUDE.md` unchanged, `git status --porcelain evals` empty)
- Plan verification: `devenv shell -- just check` exit 0; `just skeleton` exit 0 with the verify one-liner printing `skeleton ok`
