---
status: complete
phase: 01-walking-skeleton
source: [01-VERIFICATION.md]
started: 2026-10-08T00:00:00Z
updated: 2026-10-08T04:35:26.620Z
---

## Current Test

[testing complete]

## Tests

### 1. Open the first PR and watch the CI workflow (CI-01)
expected: Jobs dotnet, python and contract all pass on ubuntu-latest; the pinned action majors (checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4) resolve
result: pass
evidence: PR #2, run 37725342720 — dotnet, python, contract all success; checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4 resolved

### 2. Open the repo in Claude Code and in OpenCode and ask "what is the check command?" (REPO-04)
expected: Both answer `just check` (or the per-stack recipes) from AGENTS.md
result: skipped
reason: "OpenCode half not tested (user skipped). Claude Code half passed: answered `just check` and listed the offline gates it runs."

### 3. Fresh clone, then `devenv shell -- just check`; and the README "Quick start without Nix" path on a machine without Nix (REPO-03)
expected: Exit 0 on both
result: pass
evidence: "devenv path: fresh clone, exit=0 (188 .NET, 144 pytest, schema/datagen/e2e/docs/secrets ok). No-Nix path: ubuntu:24.04 container, dotnet-install.sh + uv + `uv tool run --from rust-just just check`, exit=0."
note: "A minimal image also needed libicu74 (the .NET runtime prerequisite) and libatomic1 (for the Node that pyright downloads). The README prerequisites mention neither; desktop distros and GitHub runners ship both."

## Summary

total: 3
passed: 2
issues: 0
pending: 0
skipped: 1
blocked: 0

## Gaps
