---
phase: quick-261005-nle
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - secretspec.toml
  - .planning/phases/01-walking-skeleton/01-CONTEXT.md
  - .planning/phases/01-walking-skeleton/01-13-PLAN.md
autonomous: true
requirements:
  - QUICK-261005-nle
estimate:
  tokens: 45000
  raw_tokens: 45000
  tasks: 2
  confidence: low
must_haves:
  truths:
    - "secretspec.toml is tracked in git with exactly the bytes tested before planning (sha256 a02083bde418273d06836f4a776822f9f0b07abc18b7844e74c76cad4ba521d2): CARIMBO_ANTHROPIC_API_KEY declared optional in the default profile, plus an empty development profile, and no secret value"
    - "D-10 in 01-CONTEXT.md keeps its ID, position and Reversibility note and now says: live calls run locally through secretspec run (dev procedure) or in the cloud environment; lookup order CARIMBO_ANTHROPIC_API_KEY then ANTHROPIC_API_KEY; never logged, echoed or committed; secretspec.toml declares the key optional. No decision is renumbered"
    - "Plan 01-13 makes README and AGENTS.md document the dev procedure (secretspec config global init once per machine, secretspec set CARIMBO_ANTHROPIC_API_KEY at the prompt, secretspec run -- claude --continue) and say that secretspec is optional because an exported CARIMBO_ANTHROPIC_API_KEY also works"
    - "Plan 01-13 makes the live recipes (spike-live, skeleton) work whether the key is already exported or must come from secretspec run, which they call with SECRETSPEC_REASON set. They never echo the key, and each of these behaviours has greppable acceptance criteria and automated checks"
    - "Plan 01-13 keeps its structure (wave 8, depends_on 01-09 and 01-12, the same files_modified, three tasks) and still passes plan-structure and frontmatter validation"
  artifacts:
    - path: "secretspec.toml"
      provides: "Optional declaration of CARIMBO_ANTHROPIC_API_KEY plus the development profile"
      contains: "required = false"
    - path: ".planning/phases/01-walking-skeleton/01-CONTEXT.md"
      provides: "D-10 amended in place"
      contains: "secretspec run -- claude --continue"
    - path: ".planning/phases/01-walking-skeleton/01-13-PLAN.md"
      provides: "Executable plan that carries the secretspec dev procedure into README, AGENTS.md, devenv.nix and the live just recipes"
      contains: "_with-provider-key"
  key_links:
    - from: ".planning/phases/01-walking-skeleton/01-CONTEXT.md"
      to: "secretspec.toml"
      via: "D-10 names the committed declaration file and the secretspec run procedure"
      pattern: "secretspec.toml"
    - from: ".planning/phases/01-walking-skeleton/01-13-PLAN.md"
      to: "secretspec.toml"
      via: "interfaces, read_first, key_links and the repo-layout test of 01-13 reference it"
      pattern: "secretspec.toml"
---

<objective>
Record the local secretspec setup for the Anthropic API key and commit it, in two parts:

- commit the already-tested `secretspec.toml` unchanged, and amend D-10 in place so that it covers the local secretspec path next to the cloud environment;
- update plan 01-13 (not yet executed). When it runs, it then documents the dev procedure for reviewers and agents, makes the live `just` recipes use secretspec, and verifies all of this with concrete checks.

Purpose: D-10 currently assumes live calls run only in the cloud environment. The author now runs them locally from NixOS-WSL, with the key held in gnome-keyring through secretspec. If this is not recorded before Phase 1 executes, 01-13 ships README, AGENTS.md and recipes that only know `export`. An agent session that was not launched through `secretspec run` would then have no documented way to reach the key.

Output: one tracked `secretspec.toml`, an amended D-10 (plus its matching Integration Points line), and an amended `01-13-PLAN.md`. This takes two commits.
</objective>

<execution_context>
@~/.claude/gsd-core/workflows/execute-plan.md
@~/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@.claude/CLAUDE.md
@secretspec.toml
@.planning/phases/01-walking-skeleton/01-CONTEXT.md
@.planning/phases/01-walking-skeleton/01-13-PLAN.md

Facts verified before planning. Do not re-test them, and never print the key while working:
- secretspec 0.21.0 is installed in the user profile (`~/.nix-profile/bin/secretspec`). The nixpkgs attribute `secretspec` exists, at 0.21.1 in nixpkgs and 0.20.0 in `github:cachix/devenv-nixpkgs/rolling`. Both versions have `secretspec run --reason <R>`, and both read the same reason from env `SECRETSPEC_REASON`. `run` finds `secretspec.toml` by walking up from the current directory.
- Provider: keyring (gnome-keyring). The global profile is `development`. `secretspec run` fails with "Invalid profile" when the active profile is not declared, and it does not fall back to default. That is why `secretspec.toml` declares an empty `[profiles.development]`.
- `required = false` is deliberate. A required secret that is unset blocks `secretspec run`. With the key optional, a missing key simply means the variable is absent, which matches D-10: live steps are skipped and the fake client covers everything else.
- The require_reason policy defaults to "agents". When an agent such as Claude Code invokes secretspec without `--reason` or `SECRETSPEC_REASON`, secretspec fails with "Accessing secrets requires a reason". A human running it from their own terminal is unaffected.
- `secretspec set NAME` without `--profile` writes to the global profile, which is the one `run` reads. Always use the prompt and never pass the value as an argument, because it would land in shell history.
- A Claude Code (or OpenCode) session sees only the variables present when it was launched, so it must be (re)started as `secretspec run -- claude --continue`.
- `~/.config/secretspec/config.toml` is created per machine by `secretspec config global init`. It is user state, never edited by this repo, and never Nix-managed.
- Out of scope: the Windows secretspec.exe, and the D-10 copy inside `01-RESEARCH.md` (a research snapshot; 01-13 reads other sections of it).
- Plans 01-10 and 01-12 stay unchanged. Their key resolution order (CARIMBO_ANTHROPIC_API_KEY, then ANTHROPIC_API_KEY) and their presence-only preconditions already match the amended D-10, because `secretspec run` exports exactly that variable.
- Planning-time baselines: HEAD is `4488fce`. `secretspec.toml` is untracked, with sha256 `a02083bde418273d06836f4a776822f9f0b07abc18b7844e74c76cad4ba521d2`. `.planning/state.json` is untracked and out of scope (never stage it). In 01-CONTEXT.md, D-10 is line 42 and the Integration Points bullet is line 87. The decision IDs are D-01 to D-15. 01-13 currently passes `verify.plan-structure` (valid, 3 tasks, no warnings) and `frontmatter.validate --schema plan`. Its highest threat ID across Phase 1 is T-01-36.
- A just 1.58 prototype of the wrapper recipe described in Task 2 (edit E8) was run in a scratch directory. Arguments such as `-c`, `--project` and `--` pass through `[positional-arguments]` intact. With no key and no secretspec on PATH the wrapper exits 2. Private `_` recipes are hidden from `just --list`.
- node is not on PATH. Run gsd-tools as `nix shell nixpkgs#nodejs -c node ~/.claude/gsd-core/bin/gsd-tools.cjs ...`.

<d10_replacement>
- **D-10:** Live calls run locally or in the cloud environment; either way the key reaches the process as the environment variable `CARIMBO_ANTHROPIC_API_KEY` (not `ANTHROPIC_API_KEY`, which Claude Code reserves for its own auth and which cloud env settings flag). Locally (dev procedure) the key is stored with secretspec; the committed `secretspec.toml` declares `CARIMBO_ANTHROPIC_API_KEY` as optional (`required = false`, plus an empty `development` profile because `secretspec config global init` selects it), and `secretspec run` injects it, either into an agent session launched as `secretspec run -- claude --continue` (a session sees only variables present at launch) or into the live `just` recipes, which call `secretspec run` with an access reason when the variable is not already exported. secretspec is optional: exporting `CARIMBO_ANTHROPIC_API_KEY` works too. In the cloud environment the key is an environment setting; cloud env variables are visible to anyone using the environment, so it is a dedicated low-limit key. The Api resolves the key as `CARIMBO_ANTHROPIC_API_KEY` first, falling back to `ANTHROPIC_API_KEY` for local dev; it is never logged, echoed, committed or written to spike docs. Without a key, live steps are blocked, everything else proceeds against a fake `IChatClient`/fake transport, and the endpoint is unavailable. — **Reversibility:** reversible — one config lookup. *(Amended 2026-10-05, quick task 261005-nle: local secretspec path added; key name and lookup order unchanged.)*
</d10_replacement>

<integration_points_replacement>
- GitHub Actions (none exist yet) and the `CARIMBO_ANTHROPIC_API_KEY` variable: a cloud environment setting (configured 2026-10-04; visible from a new session), or locally injected by `secretspec run` from the committed `secretspec.toml` (optional; see D-10).
</integration_points_replacement>
</context>

<!-- planner-discipline-allow: secretspec: -->

<tasks>

<task type="tracer">
  <name>Task 1: Commit secretspec.toml unchanged and amend D-10 in place</name>
  <files>secretspec.toml, .planning/phases/01-walking-skeleton/01-CONTEXT.md</files>
  <read_first>
    - secretspec.toml (declarations only, no values)
    - .planning/phases/01-walking-skeleton/01-CONTEXT.md lines 39-43 (D-08 to D-11) and lines 85-88 (Integration Points)
  </read_first>
  <action>
This is the thinnest end-to-end path: the committed declaration file and the locked decision that points to it.

1. Do not edit `secretspec.toml`; the orchestrator already tested it. Run `sha256sum secretspec.toml` and compare with the planning-time hash in `<context>`. If the hash differs, stop and report it, because the file changed after it was tested.
2. In `.planning/phases/01-walking-skeleton/01-CONTEXT.md`, replace the whole D-10 bullet with the single line inside `<d10_replacement>` (the line that starts with `- **D-10:**`, currently line 42). Keep it in the same position and on one line. Per D-10, the ID, the "Live & budget" heading and the Reversibility note stay. Do not renumber or touch any other decision.
3. In the same file, replace the Integration Points bullet that starts with `- GitHub Actions (none exist yet)` (currently line 87) with the single line inside `<integration_points_replacement>`. Leave every other line unchanged.
4. Stage exactly these two paths with `git add secretspec.toml .planning/phases/01-walking-skeleton/01-CONTEXT.md`. Never use `git add -A` or `git add .`, because `.planning/state.json` is untracked and out of scope. Commit with the message `docs(quick-261005-nle): commit secretspec.toml and amend D-10 for local secretspec runs`.
  </action>
  <verify>
    <automated>git ls-files --error-unmatch secretspec.toml && git rev-parse HEAD:secretspec.toml</automated>
    <fails_when>non-zero exit, or the printed blob id is not 5aea0f98b501a35fce1e92c0bfa57b97504614e7 (the `git hash-object` of the tested file at planning time)</fails_when>
    <automated>grep -E '^- \*\*D-10:\*\*' .planning/phases/01-walking-skeleton/01-CONTEXT.md | grep -F 'secretspec run -- claude --continue' | grep -F 'secretspec.toml' | grep -F 'required = false' | grep -F '`CARIMBO_ANTHROPIC_API_KEY` first, falling back to `ANTHROPIC_API_KEY`' | grep -F 'never logged, echoed, committed' | grep -F '**Reversibility:** reversible' | grep -c 'Amended 2026-10-05'</automated>
    <fails_when>output is not exactly 1</fails_when>
    <automated>grep -oE '^- \*\*D-[0-9]{2}' .planning/phases/01-walking-skeleton/01-CONTEXT.md | grep -oE 'D-[0-9]{2}' | paste -sd' '</automated>
    <fails_when>output is not exactly "D-01 D-02 D-03 D-04 D-05 D-06 D-07 D-08 D-09 D-10 D-11 D-12 D-13 D-14 D-15"</fails_when>
    <automated>git diff --numstat 4488fce -- .planning/phases/01-walking-skeleton/01-CONTEXT.md</automated>
    <fails_when>output is not one line starting with "2" TAB "2" (exactly two lines replaced, nothing else touched)</fails_when>
    <automated>! grep -nE 'sk-ant-[A-Za-z0-9_-]{8,}' secretspec.toml .planning/phases/01-walking-skeleton/01-CONTEXT.md</automated>
    <fails_when>non-zero exit or any line printed (a key-shaped string would be committed)</fails_when>
  </verify>
  <acceptance_criteria>
    - `git ls-files secretspec.toml` prints `secretspec.toml`, and the committed blob hashes to the planning-time sha256
    - `grep -F 'GitHub Actions (none exist yet)' .planning/phases/01-walking-skeleton/01-CONTEXT.md | grep -c 'secretspec run'` prints 1
    - `git show --stat --format= HEAD` lists exactly `secretspec.toml` and `.planning/phases/01-walking-skeleton/01-CONTEXT.md`
    - `git status --porcelain .planning/state.json` still shows it as untracked (`??`)
  </acceptance_criteria>
  <done>secretspec.toml is tracked byte-for-byte as tested, D-10 covers the local secretspec path with its ID and Reversibility note intact, and both changes are in one commit that contains nothing else.</done>
</task>

<task type="auto">
  <name>Task 2: Carry the secretspec dev procedure into plan 01-13 without changing its structure</name>
  <files>.planning/phases/01-walking-skeleton/01-13-PLAN.md</files>
  <read_first>
    - .planning/phases/01-walking-skeleton/01-13-PLAN.md (whole file; edits below cite its current line numbers)
    - .planning/phases/01-walking-skeleton/01-CONTEXT.md amended D-10 (Task 1)
    - secretspec.toml
    - .planning/phases/01-walking-skeleton/01-10-PLAN.md line 118 and 01-12-PLAN.md lines 190-197 (read only, to confirm the resolution order the recipes rely on; do not edit)
  </read_first>
  <action>
Apply edits E1 to E16 to `.planning/phases/01-walking-skeleton/01-13-PLAN.md` with scoped Edit calls. Do not rewrite the whole file. Keep the frontmatter keys `wave`, `depends_on`, `files_modified`, `autonomous` and `requirements` byte-identical, and keep exactly three tasks with their names, types and `<files>` lists unchanged. `secretspec.toml` is read by 01-13 and never modified by it, so it does not join `files_modified`. Two rules apply to every text you add. First, no text may contain a key value, and every check on the key tests presence only. Second, the YAML key `secretspec` followed directly by a colon must not appear anywhere inside an `<action>` body, because E10 adds a negative grep for it on devenv.yaml. Citations: D-10 for all key handling, D-13 for recipes, D-14 for devenv and README, D-15 for AGENTS.md.

E1 (frontmatter, `user_setup` env var `source`, line 31): replace the quoted value with: Anthropic Console -> API keys (dedicated low-limit key, D-10). Locally: store it once with `secretspec set CARIMBO_ANTHROPIC_API_KEY` (prompted) and launch through `secretspec run`, or export it. Cloud environment: an environment setting. Keep it a double-quoted YAML string.

E2 (frontmatter `must_haves.truths`, after the `just skeleton` truth on line 47): append two double-quoted truths.
- The live recipes `just spike-live` and `just skeleton` use CARIMBO_ANTHROPIC_API_KEY (or ANTHROPIC_API_KEY) when it is already exported, and otherwise resolve it through `secretspec run` with SECRETSPEC_REASON set. With neither, they exit 2 with a message naming both options. No recipe prints the key (D-10).
- README and AGENTS.md document the optional secretspec dev procedure (`secretspec config global init` once per machine, `secretspec set CARIMBO_ANTHROPIC_API_KEY` at the prompt, `secretspec run -- claude --continue`) and say that exporting the variable works without secretspec.

E3 (frontmatter `must_haves.key_links`, after the CLAUDE.md link): append a link with from "justfile", to "secretspec.toml", via "the private _with-provider-key recipe falls back to secretspec run with SECRETSPEC_REASON set when no provider key is exported; secretspec.toml declares the key optional", and pattern "secretspec run".

E4 (`<objective>`, after the D-14 bullet): add the bullet: the provider-key procedure for live calls, either an exported variable or the optional secretspec setup (D-10).

E5 (`<interfaces>`, new last bullet): Committed before this plan by quick task 261005-nle: `secretspec.toml` declares `CARIMBO_ANTHROPIC_API_KEY` in `[profiles.default]` with `required = false`, plus an empty `[profiles.development]`. That is the profile `secretspec config global init` selects, and `secretspec run` fails on an undeclared profile. secretspec CLI (0.21 in the user profile, 0.20 in devenv-nixpkgs):
- `secretspec run -- CMD` injects the declared secrets into CMD only, finding secretspec.toml by walking up. An agent caller must supply a reason with `--reason R` or env `SECRETSPEC_REASON`.
- An optional secret that is not stored is simply absent from CMD's environment.
- `secretspec set NAME` prompts for the value and writes the global profile that `run` reads.

E6 (the "Execution environment" paragraph after `</interfaces>`, line 118): append the sentence: Live steps need the provider key in the environment: exported, inherited from a session launched with `secretspec run`, or resolved by the live recipes' own secretspec fallback.

E7 (Task 1 `<read_first>`): add the bullet `secretspec.toml` (committed; the optional key declaration the live recipes fall back to).

E8 (Task 1 `<action>`, the recipe list and the README and devenv paragraphs):
(a) Insert a new recipe bullet before the `spike-live` bullet: `_with-provider-key +cmd` is a private recipe, hidden from `just --list`, marked with the `[positional-arguments]` attribute. It is a bash shebang recipe that runs its arguments with the provider key available (D-10). It never prints, logs or echoes a key value and only tests presence with `-n`. It checks three branches in order:
   1. If `CARIMBO_ANTHROPIC_API_KEY` or `ANTHROPIC_API_KEY` is non-empty, exec the arguments unchanged. This covers the cloud environment, a reviewer's export, and a session launched with `secretspec run`.
   2. Otherwise, if `command -v secretspec` succeeds and `secretspec.toml` exists in the recipe's working directory (the repo root), export `SECRETSPEC_REASON`. Keep any value the caller supplied and default to `carimbo live recipe: Anthropic model calls`. Then exec `secretspec run --` followed by the arguments. Agents run these recipes, and secretspec refuses agent access without a reason.
   3. Otherwise, print to stderr: provider key unset: export CARIMBO_ANTHROPIC_API_KEY (fallback ANTHROPIC_API_KEY), or store it with secretspec set CARIMBO_ANTHROPIC_API_KEY (see README). Then exit 2.

   secretspec.toml declares the key optional, so branch 2 succeeds even when nothing is stored. The wrapped command's own presence check then exits 2 (LlmSpike prints "provider key unset" per 01-10, and `_skeleton-run` step 1 does the same), so there is no retry loop. Wherever a recipe calls another recipe, re-invoke just as `{{just_executable()}} --justfile {{justfile()}}`.
(b) Replace the `spike-live` bullet: `spike-live` runs the LlmSpike live command with `--budget-usd 1.00` and the paths from 01-10 through `_with-provider-key`, so it works with an exported key or through secretspec.
(c) Replace the `skeleton` bullet and its seven steps as follows.
   - `skeleton max_cost="1.00"` becomes one line that runs `_with-provider-key` on `_skeleton-run` with the max_cost argument (D-09, D-10).
   - `_skeleton-run max_cost` is a new private bash shebang recipe that holds the existing steps 1 to 7 unchanged, except that the step 1 message also names `secretspec set CARIMBO_ANTHROPIC_API_KEY`.
(d) Replace the README "Live run" bullet with a "Secrets and live calls" section that covers:
   - The key is optional. Without it, `just check` runs everything against the fake model client, and the live recipes exit 2.
   - Without secretspec (any reviewer): export `CARIMBO_ANTHROPIC_API_KEY` in the shell that runs just. Suggest `read -rs CARIMBO_ANTHROPIC_API_KEY` followed by `export CARIMBO_ANTHROPIC_API_KEY`, so the value stays out of shell history. `ANTHROPIC_API_KEY` is the fallback; carimbo prefers its own name because Claude Code uses that one for its own auth.
   - With secretspec, which is optional and the author's dev setup:
     - Run `secretspec config global init` once per machine and pick a provider such as the OS keyring. The repo's `secretspec.toml` declares the `development` profile that init selects.
     - Run `secretspec set CARIMBO_ANTHROPIC_API_KEY` without `--profile`, and paste the value at the prompt; never pass it as an argument.
     - Then either run `just skeleton` or `just spike-live` directly (they call `secretspec run` themselves when the variable is not exported), or launch the agent session as `secretspec run -- claude --continue` (or `secretspec run -- opencode`). A session sees only variables present when it was launched, so restart it after setting the key.
     - An agent that calls secretspec itself must pass `--reason` or set `SECRETSPEC_REASON`; the recipes already set one.
     - If secretspec is installed but not configured for this repo, exporting the variable bypasses it.
   - `just skeleton` costs at most US$1.00 per run. Read `evals/runs/<run>/summary.md`.
   - Never print, log or commit the key. Use a dedicated low-limit key.
(e) In the devenv.nix bullets, add `pkgs.secretspec` to `packages`. Add a comment saying it is the CLI only: neither devenv.nix nor devenv.yaml enables devenv's own secretspec integration, so `devenv shell` never reads the keyring or loads the key. Offline checks therefore never see a live key, and agents running `devenv shell -- just check` need no access reason. The key is resolved only by the live recipes.

E9 (Task 1 `<verify>`, after the `just check` pair): add two `<automated>` commands, each with `<fails_when>`.
- Command 1: env -u CARIMBO_ANTHROPIC_API_KEY -u ANTHROPIC_API_KEY nix shell nixpkgs#just -c just _with-provider-key sh -c 'test -n "${CARIMBO_ANTHROPIC_API_KEY:-}" && echo resolved-via-secretspec'
  It fails when the exit is non-zero or "resolved-via-secretspec" is absent. It runs only where `command -v secretspec` succeeds and the key has been stored with `secretspec set`. Elsewhere, say so in the SUMMARY.
- Command 2: nix shell nixpkgs#just nixpkgs#bash -c sh -c 'env -u CARIMBO_ANTHROPIC_API_KEY -u ANTHROPIC_API_KEY PATH="$(dirname "$(readlink -f "$(command -v just)")"):$(dirname "$(readlink -f "$(command -v bash)")")" just _with-provider-key true; echo "exit=$?"'
  It fails when the output lacks "exit=2", or when the stderr message does not name both CARIMBO_ANTHROPIC_API_KEY and `secretspec set`.

E10 (Task 1 `<acceptance_criteria>`):
- Replace the bullet about `CARIMBO_ANTHROPIC_API_KEY` in the justfile with: `grep -c 'CARIMBO_ANTHROPIC_API_KEY' justfile` is at least 1, and `grep -nE '(echo|printf)[^#]*\$\{?(CARIMBO_)?ANTHROPIC_API_KEY' justfile` prints nothing.
- Extend the README bullet: it also names `secretspec config global init`, `secretspec set CARIMBO_ANTHROPIC_API_KEY` and `secretspec run -- claude --continue`, and states that secretspec is optional.
- Add: `grep -c 'secretspec run' justfile` and `grep -c 'SECRETSPEC_REASON' justfile` are each at least 1, and `grep -c '_with-provider-key' justfile` is at least 3.
- Add: `grep -c 'pkgs.secretspec' devenv.nix` is at least 1, and `grep -cE '^secretspec:' devenv.yaml` prints 0.
Also extend Task 1 `<done>` with: the live recipes take the key from the environment or from secretspec.

E11 (Task 2 `<read_first>`): add the bullet `secretspec.toml` (asserted by the repo-layout test).

E12 (Task 2 `<action>`):
(a) Replace the `## Secrets and live calls` bullet with this content:
   - The provider key is read from `CARIMBO_ANTHROPIC_API_KEY`, then `ANTHROPIC_API_KEY` (D-10). It is optional; without it, everything except the live recipes runs against the fake model client.
   - Dev procedure with secretspec (optional; exporting the variable works too): run `secretspec config global init` once per machine, then `secretspec set CARIMBO_ANTHROPIC_API_KEY` at the prompt (never with the value as an argument), then `secretspec run -- claude --continue` (or `secretspec run -- opencode`), because an agent session sees only the variables present at launch.
   - An agent that invokes secretspec itself must pass `--reason` or set `SECRETSPEC_REASON`; the live recipes already do.
   - Never print, log, echo or commit a key. Agents check presence only, never the value.
   - Live paid steps run only through `just spike-live` and `just skeleton`, with caps of US$1.00 per run and US$5 per phase. CI uses no provider key.
(b) Add these checks to the `python/tests/test_repo_layout.py` bullets:
   - The text of AGENTS.md between `## Secrets and live calls` and the next `## ` heading contains `secretspec config global init`, `secretspec set CARIMBO_ANTHROPIC_API_KEY`, `secretspec run -- claude --continue`, `SECRETSPEC_REASON` and the word `optional`. README.md contains the first three.
   - `secretspec.toml`, parsed with the stdlib `tomllib`, has `profiles.default.CARIMBO_ANTHROPIC_API_KEY.required` equal to False, and `profiles` has a `development` key. A required key would block `secretspec run` for anyone without one, and a missing development profile breaks `secretspec run` after `secretspec config global init`.
   - The justfile contains `secretspec run` and `SECRETSPEC_REASON`, and no justfile line matches the key-echo regex from Task 1's acceptance criteria.
(c) Amend the recipe-name check: the recipe-header regex accepts names that start with `_`, and it skips `[attribute]` lines.

E13 (Task 2 `<verify>` and `<acceptance_criteria>`):
- In the agent-runtime human check, change the expected answer to: Both answer `just check` and CARIMBO_ANTHROPIC_API_KEY (fallback ANTHROPIC_API_KEY), optionally provided through `secretspec run`, citing AGENTS.md.
- Add the acceptance criteria: `grep -c 'secretspec set CARIMBO_ANTHROPIC_API_KEY' AGENTS.md` and `grep -c 'secretspec run -- claude --continue' AGENTS.md` are each at least 1.

E14 (Task 3): replace the `<precondition>` line with: the provider key is resolvable, either exported as CARIMBO_ANTHROPIC_API_KEY or ANTHROPIC_API_KEY, or stored with secretspec. Check with: nix shell nixpkgs#just -c just _with-provider-key sh -c '[ -n "${CARIMBO_ANTHROPIC_API_KEY:-${ANTHROPIC_API_KEY:-}}" ] && echo set || echo unset'. It prints set when met. When unmet, it prints unset or exits 2 with "provider key unset". In the action, after the sentence that runs `just skeleton` once, add: if the key is not exported, the recipe resolves it through its secretspec fallback; never export, print or paste the value yourself.

E15 (`<threat_model>`, after T-01-35): add the row T-01-37, Information disclosure, live recipes and secretspec fallback, severity high, disposition mitigate. Mitigation plan:
- `_with-provider-key` tests presence only and never prints the key.
- `secretspec run` injects the key only into the wrapped command's process tree.
- SECRETSPEC_REASON makes agent access attributable.
- devenv shell never loads secrets.
- secretspec.toml holds declarations only.
- These are asserted by the Task 1 negative grep, test_repo_layout and secrets-check.
Also add a row to the Trust Boundaries table: OS keyring (secretspec provider) to live recipe process.

E16 (closing sections):
- In `<verification>`, add the bullet: the live recipes resolve the key from an exported variable or from secretspec and never print it, and the no-key path exits 2.
- In "Artifacts this phase produces", extend the Recipes line with the private recipes `_with-provider-key` and `_skeleton-run`, and add the line: Relies on (committed by quick task 261005-nle): `secretspec.toml`.
- In "Flagged assumptions", add the row: REPO-03, unclassified, secretspec is an optional local convenience and the reviewer path needs only an exported CARIMBO_ANTHROPIC_API_KEY.

Finally, run the validations in `<verify>` and fix any error or warning they report. Then stage only this file with `git add .planning/phases/01-walking-skeleton/01-13-PLAN.md` and commit with the message `docs(quick-261005-nle): add secretspec dev procedure to plan 01-13`.
  </action>
  <verify>
    <automated>nix shell nixpkgs#nodejs -c node ~/.claude/gsd-core/bin/gsd-tools.cjs query verify.plan-structure .planning/phases/01-walking-skeleton/01-13-PLAN.md</automated>
    <fails_when>non-zero exit, "valid": false, "task_count" other than 3, or a non-empty "warnings" or "errors" array</fails_when>
    <automated>nix shell nixpkgs#nodejs -c node ~/.claude/gsd-core/bin/gsd-tools.cjs query frontmatter.validate .planning/phases/01-walking-skeleton/01-13-PLAN.md --schema plan</automated>
    <fails_when>"valid": false, or a non-empty "missing" or "invalidValue" array</fails_when>
    <automated>base=$(git show 4488fce:.planning/phases/01-walking-skeleton/01-13-PLAN.md) && diff <(printf '%s\n' "$base" | sed -n '/^wave:/,/^user_setup:/p') <(sed -n '/^wave:/,/^user_setup:/p' .planning/phases/01-walking-skeleton/01-13-PLAN.md) && git diff --exit-code 4488fce -- .planning/phases/01-walking-skeleton/01-10-PLAN.md .planning/phases/01-walking-skeleton/01-12-PLAN.md && echo structure-unchanged</automated>
    <fails_when>any diff output, non-zero exit, or "structure-unchanged" absent (wave, depends_on, files_modified, autonomous, requirements changed, or 01-10/01-12 touched)</fails_when>
    <automated>f=.planning/phases/01-walking-skeleton/01-13-PLAN.md; test "$(grep -c 'secretspec config global init' $f)" -ge 2 && test "$(grep -c 'secretspec set CARIMBO_ANTHROPIC_API_KEY' $f)" -ge 3 && test "$(grep -c 'secretspec run -- claude --continue' $f)" -ge 2 && test "$(grep -c 'SECRETSPEC_REASON' $f)" -ge 3 && test "$(grep -c '_with-provider-key' $f)" -ge 5 && test "$(grep -c 'secretspec.toml' $f)" -ge 4 && test "$(grep -c 'pkgs.secretspec' $f)" -ge 2 && test "$(grep -c 'T-01-37' $f)" -ge 1 && echo counts-ok</automated>
    <fails_when>non-zero exit or "counts-ok" absent</fails_when>
    <automated>! grep -nE 'sk-ant-[A-Za-z0-9_-]{8,}' .planning/phases/01-walking-skeleton/01-13-PLAN.md</automated>
    <fails_when>non-zero exit or any line printed</fails_when>
  </verify>
  <acceptance_criteria>
    - `awk '/<action>/,/<\/action>/' .planning/phases/01-walking-skeleton/01-13-PLAN.md | grep -c 'secretspec:'` prints 0 (the new devenv.yaml negative grep is not echoed into an action body)
    - The `verify.plan-structure` output for 01-13 lists the same three task names as at `4488fce`
    - `git show --stat --format= HEAD` lists exactly `.planning/phases/01-walking-skeleton/01-13-PLAN.md`
    - Every `<automated>` line added to 01-13 that mentions CARIMBO_ANTHROPIC_API_KEY uses it only inside `test -n` or `[ -n ... ]`, or as an `env -u` argument
  </acceptance_criteria>
  <done>When 01-13 executes, it documents and implements the secretspec dev procedure (README, AGENTS.md, devenv.nix, the live recipes and the repo-layout test) with greppable criteria and automated checks. Its wave, dependencies, files and three-task shape are unchanged, and it still validates.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Working tree → git history (and later the public repo) | Anything committed is permanent and public |
| OS keyring (secretspec provider) → process environment | The provider key leaves the keyring only through `secretspec run` |
| Plan text → executor shell | Verify and precondition commands written here are run by agents |

## STRIDE Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-261005-01 | Information disclosure | `secretspec.toml` commit | high | mitigate | Committed byte-for-byte as tested (sha256 gate). The file holds declarations only. A key-shaped negative grep runs over it and over CONTEXT.md. Explicit `git add` paths, never `-A` |
| T-261005-02 | Information disclosure | Verify/precondition commands in this plan and in 01-13 | high | mitigate | Every check on the key is a presence test (`test -n`, `[ -n ]`) or an `env -u` removal, and nothing prints a value. Task 2 has an acceptance bullet that audits the added `<automated>` lines |
| T-261005-03 | Information disclosure | Documented procedure (README, AGENTS.md via 01-13) | medium | mitigate | `secretspec set` uses the prompt, never an argument. The no-secretspec path suggests `read -rs` so the value stays out of shell history. "Never print, log, echo or commit" is stated in both files |
| T-261005-04 | Elevation of privilege | `devenv shell` loading the key into every shell (01-13 devenv.nix) | medium | mitigate | devenv adds the secretspec CLI only. devenv's secretspec integration stays off (`^secretspec:` absent from devenv.yaml, enforced by the 01-13 acceptance criteria), so offline checks never run with a live key |
| T-261005-05 | Repudiation | Agent access to the keyring through the live recipes | low | mitigate | The recipes export `SECRETSPEC_REASON` (a caller's value wins), so secretspec records why an agent accessed the key |
| T-261005-06 | Tampering | Unrelated untracked files swept into commits | low | mitigate | Each task stages named paths only. `.planning/state.json` must remain untracked (acceptance criterion) |
| T-261005-SC | Tampering | secretspec package in 01-13's devenv.nix | low | accept | The nixpkgs attribute is pinned through devenv.lock and maintained by cachix, the devenv authors. There are no npm, pip or cargo installs, so the package-legitimacy gate does not apply |
</threat_model>

<verification>
- `secretspec.toml` is tracked and unchanged, and D-10 is amended in place with the decision IDs intact (Task 1 verify)
- 01-13 validates, keeps its structure, contains the secretspec procedure, the wrapper recipe and its checks, and contains no key-shaped string (Task 2 verify)
- 01-10 and 01-12 are byte-identical to `4488fce`
- `git log --oneline -2` shows the two `docs(quick-261005-nle): ...` commits, and `git status --porcelain` shows only `.planning/state.json` (plus this quick directory, which the orchestrator commits)
</verification>

<success_criteria>
- secretspec.toml committed as is
- D-10 covers the local secretspec run path and the cloud environment, with the lookup order and the never-logged rule unchanged and the key declared optional
- 01-13, when executed, ships the dev procedure in README and AGENTS.md, says that secretspec is optional, and makes the live recipes work with an exported key or through `secretspec run`. The recipes set SECRETSPEC_REASON and never echo the key, and all of this has concrete checks
- No key value appears in any file, command or commit
</success_criteria>

<output>
Create `.planning/quick/261005-nle-document-local-secretspec-setup-for-the-/261005-nle-SUMMARY.md` when done
</output>
