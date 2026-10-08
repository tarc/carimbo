---
status: testing
phase: 01-walking-skeleton
source: [01-VERIFICATION.md]
started: 2026-10-08T00:00:00Z
updated: 2026-10-08T00:00:00Z
---

## Current Test

number: 1
name: Open the first PR and watch the CI workflow (CI-01)
expected: |
  Jobs dotnet, python and contract all pass on ubuntu-latest; the pinned action majors
  (checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4) resolve
awaiting: user response

## Tests

### 1. Open the first PR and watch the CI workflow (CI-01)
expected: Jobs dotnet, python and contract all pass on ubuntu-latest; the pinned action majors (checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4) resolve
result: [pending]

### 2. Open the repo in Claude Code and in OpenCode and ask "what is the check command?" (REPO-04)
expected: Both answer `just check` (or the per-stack recipes) from AGENTS.md
result: [pending]

### 3. Fresh clone, then `devenv shell -- just check`; and the README "Quick start without Nix" path on a machine without Nix (REPO-03)
expected: Exit 0 on both
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
