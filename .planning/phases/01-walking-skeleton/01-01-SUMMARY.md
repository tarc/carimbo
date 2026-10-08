---
phase: 01-walking-skeleton
plan: 01
subsystem: infra
tags: [uv, python, supply-chain, ruff, pyright, pytest, repo-hygiene]

requires: []
provides:
  - Single uv Python project `carimbo` 0.1.0 with exact pins and a resolved, human-approved python/uv.lock (64 packages, PyPI only)
  - Project environment (python/.venv, gitignored) where ruff 0.16.10, pyright 1.1.414 and pytest 9.1.1 run clean
  - Repo hygiene files (.gitignore, .gitattributes, .editorconfig) shared by the Python and .NET plans
  - Empty package skeletons carimbo_models, carimbo_datagen, carimbo_evals
affects: [01-03, 01-07, 01-09, 01-13, python-tooling]

actuals:
  tokens: 30000
  tasks: 3
  commits: 1

plan_head_before: c60b617dda9cd15b318128f4299fe51fe5a3a5e9
plan_head_after: cf346021293ca6dafd0070e1d641de21d1c78e00

tech-stack:
  added: [uv, hatchling 1.32.4 (build-time), httpx2 2.13.1, pydantic 2.13.5, typer 0.27.2, jsonschema 4.26.0, nfelib 3.0.0, brazilfiscalreport 1.2.0, faker 40.40.0, lxml 6.1.3, ruff 0.16.10, pyright 1.1.414, pytest 9.1.1, pytest-asyncio 1.4.0, datamodel-code-generator 0.83.0, pypdfium2 5.13.0, zxing-cpp 3.1.1]
  patterns:
    - "Exact pins in pyproject, resolved lock committed, `uv sync --locked` refuses drift"
    - "Install gated behind a blocking-human supply-chain checkpoint"

key-files:
  created:
    - .gitignore
    - .gitattributes
    - .editorconfig
    - python/pyproject.toml
    - python/.python-version
    - python/uv.lock
    - python/src/carimbo_models/__init__.py
    - python/src/carimbo_datagen/__init__.py
    - python/src/carimbo_evals/__init__.py
  modified: []

key-decisions:
  - "Supply chain approved as committed: httpx2 2.13.1, brazilfiscalreport 1.2.0 and hatchling 1.32.4 (build-time), including the emscripten-only transitive httpx2-jsfetch 1.0; no package replaced"
  - "Python commands on this NixOS-WSL host run through `nix shell nixpkgs#uv nixpkgs#python312` with UV_PYTHON_DOWNLOADS=never until the devenv.nix plan lands"

patterns-established:
  - "Run Python tooling as `uv run --project python ...` from the repo root"

requirements-completed: [REPO-02]

duration: multi-session (human gate in between)
completed: 2026-10-05
status: complete
---

# Phase 1 Plan 01: Pinned Python toolchain behind a supply-chain gate Summary

**Exact-pinned uv project with a PyPI-only 64-package lock, installed only after a human approved httpx2, brazilfiscalreport and hatchling; ruff 0.16.10, pyright 1.1.414 and pytest 9.1.1 run clean from the project environment.**

## Performance

- **Tasks:** 3 of 3 (Task 1 auto, Task 2 blocking-human checkpoint, Task 3 auto)
- **Files created:** 9, all in commit cf34602

## Accomplishments

- Task 1 (cf34602): hygiene files, `python/pyproject.toml` with exact pins, console scripts, ruff/pyright/pytest config, three package `__init__.py` files, and `python/uv.lock` resolved without installing anything. A provenance table was printed for the reviewer.
- Task 2 (human gate, no commit): reviewer answered "approved" with no package replacements. The lock (64 packages, only registry `https://pypi.org/simple`) is approved as committed, including the emscripten-only transitive `httpx2-jsfetch` 1.0.
- Task 3 (no commit; produces only the untracked, gitignored `python/.venv`): `uv sync --project python --locked` installed the locked set, then reported "Resolved 64 packages / Checked 61 packages". `python/uv.lock` is unchanged (`git status --porcelain python/uv.lock` prints nothing) and `uv lock --check` exits 0.

## Verification results

| Check | Result |
|---|---|
| `uv sync --locked` | exit 0, lock unchanged |
| `ruff --version` | `ruff 0.16.10` |
| `pyright --version` | `pyright 1.1.414` |
| `pytest --version` | `pytest 9.1.1` |
| import of httpx2, brazilfiscalreport, nfelib, faker, lxml, typer, pydantic, jsonschema, pypdfium2, zxingcpp | `imports ok` (zxingcpp needs `LD_LIBRARY_PATH`, see deviations) |
| `ruff check python`, `ruff format --check python` | all checks passed, 3 files formatted |
| `pyright -p python` | 0 errors, 0 warnings |
| `grep -cE 'source = \{ (git\|url) = ' python/uv.lock` | 0 |
| `grep 'registry = ' python/uv.lock \| sort -u` | only `https://pypi.org/simple` |
| `git status` | no `.venv` entry (`python/.venv` ignored via `.gitignore`) |

## Deviations from Plan

### Environment deviations

**1. [Rule 3 - Blocking] uv and Python not on PATH (NixOS-WSL)**
- **Issue:** The plan assumes `uv` 0.8.17 on PATH and `/usr/bin/python3.12`; neither exists on this host.
- **Fix:** All uv commands ran as `nix shell nixpkgs#uv nixpkgs#python312 -c uv ...` with `UV_PYTHON_DOWNLOADS=never` (uv 0.12.22, CPython 3.12.15). This satisfies `required-version = ">=0.8.17"`. `nix shell` leaves no GC root.

**2. [Rule 3 - Blocking] pyright needs Node, not on PATH**
- **Fix:** Prepended `/nix/store/gxq2cd70i077rah1d4hkzc1lpq8q4pv8-nodejs-24.21.0/bin` to PATH for the pyright runs.

**3. [Rule 3 - Blocking] `import zxingcpp` fails with `libstdc++.so.6: cannot open shared object file`**
- **Found during:** Task 3 import check.
- **Issue:** The manylinux `zxing-cpp` wheel links libstdc++, which NixOS does not expose globally.
- **Fix:** Re-ran the import with `LD_LIBRARY_PATH` pointing at nixpkgs `stdenv.cc.cc.lib` (`/nix/store/j7qx4s4mr17j1wqgvqdzj33lmrnzb387-gcc-16.2.0-lib/lib`); output `imports ok`. No file was changed. This is a real follow-up for the devenv plan: `devenv.nix` (or `nix-ld`) must provide libstdc++ so `zxingcpp` (datagen self-check, plan 01-07) and the PDFium/Skia natives load on NixOS-WSL. The research flagged this as a gap ("NixOS-WSL itself").

**4. [Plan assumption] pyright run over `python/tests`**
- `pyright -p python` printed `File or directory ".../python/tests" does not exist` because `include = ["src", "tests"]` lists a directory later plans create. It still exits 0 with 0 errors. No change made.

### Auto-fixed issues

None. No package was replaced and no pin changed.

## Auth gates

None. The Task 2 checkpoint was a supply-chain human-verify gate, handled as planned.

## Commit accounting

`plan_head_before` is the ledger base c60b617. Plan 01-02 ran in the same branch afterwards (0b61d14, 85238e0, 8846284, 0db567b), so a raw `rev-list c60b617..HEAD` would also count those. Only cf34602 belongs to this plan, so `commits: 1` and `plan_head_after` is cf34602. The docs commit for this SUMMARY follows it. Tasks 2 and 3 produce no commit by design.

## Known Stubs

None. The three `__init__.py` files hold only a module docstring by plan. The `carimbo-datagen` and `carimbo-evals` console scripts point at modules that arrive in plans 01-07 and 01-09 (documented in the plan).

## Threat Flags

None. T-01-SC, T-01-01 and T-01-02 are mitigated as planned: exact pins, committed lock, blocking-human gate before install, PyPI-only sources, `.env` ignored.

## Self-Check: PASSED

- FOUND: .gitignore, .gitattributes, .editorconfig, python/pyproject.toml, python/.python-version, python/uv.lock, the three `__init__.py` files
- FOUND: commit cf34602 is an ancestor of HEAD
