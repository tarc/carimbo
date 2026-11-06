# Phase 5: Comparable Measurement - Pattern Map

**Mapped:** 2026-11-05
**Files analyzed:** 27 (11 new source/test modules, 9 modified source/config, 7 new test files)
**Analogs found:** 25 / 27 (all analog paths verified with `git ls-files`)

All paths below are git-tracked. Paths marked NEW do not exist yet; the intended path is kept.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `python/src/carimbo_evals/grader.py` (modify) | service (grader) | transform | itself (`grade_case`, `_field_grade`, `_load_manifest`) | exact |
| `python/src/carimbo_evals/matching.py` (NEW) | utility | transform | `grader.py` `normalize_*`/`_same_*` helpers | role-match |
| `python/src/carimbo_evals/tags.py` (NEW) | utility (ordered rules) | transform | `dotnet/src/Carimbo.Decisions/DecisionRules.cs` | role-match (cross-language, same shape) |
| `python/src/carimbo_evals/stats.py` (NEW) | utility | transform | `decision_summary.py` `_rate`, `_spread`; `summary.py` `_latency` | partial |
| `python/src/carimbo_evals/slices.py` (NEW) | utility/config | transform | `summary.py` `CONFIG_KEYS`/`FIELDS` constants + `grader._load_manifest` | partial |
| `python/src/carimbo_evals/corruptions.py` (NEW) | utility | transform | `test_e2e_fake.py` `_invoice_json` (corrupted answers) | partial |
| `python/src/carimbo_evals/summary.py` (modify) | service (aggregator) | transform | itself (`build_summary`, `render_markdown`, `write_summary`) | exact |
| `python/src/carimbo_evals/compare.py` (NEW) | service | request-response (file in, report out) | `decision_summary.py` (pure aggregate + render + write) | role-match |
| `python/src/carimbo_evals/cli.py` (modify: `compare`, `run --split`, `grade --out`) | controller (CLI) | request-response | `cli.py` `grade` and `run` | exact |
| `python/src/carimbo_evals/runner.py` (modify: split selection) | service | batch | `runner.discover_cases`, `read_as_of_date` | exact |
| `python/pyproject.toml`, `python/uv.lock` (modify: scipy pin) | config | n/a | pin block in `pyproject.toml`; `04-02-PLAN.md` Task 2 | exact |
| `justfile` (modify: dev recipe, `check`, `docs-check`) | config | batch | `skeleton` + `_skeleton-run` recipes | exact |
| `AGENTS.md`, `README.md`, `docs/DECISIONS.md`, `.planning/REQUIREMENTS.md` (modify) | docs | n/a | prior phase entries | exact |
| `python/tests/test_matching.py` (NEW) | test | unit | `python/tests/test_grader.py` | role-match |
| `python/tests/test_tags.py` (NEW) | test | unit | `python/tests/test_grader.py` | role-match |
| `python/tests/test_stats.py` (NEW) | test | unit | `python/tests/test_grader.py` | role-match |
| `python/tests/test_selftests.py` (NEW) | test | unit over 154 cases | `python/tests/test_dataset_ground_truth.py` | role-match |
| `python/tests/test_silent_errors.py` (NEW) | test | unit | `python/tests/test_grader.py` | role-match |
| `python/tests/test_summary_v4.py` (NEW) | test | unit + tmp run dir | `python/tests/test_grader.py` (build_summary/write_summary tests) | role-match |
| `python/tests/test_compare.py` (NEW) | test | CLI | `python/tests/test_grader.py` (`CliRunner` usage) | role-match |
| `python/tests/test_e2e_full_dataset.py` (NEW, `e2e`) | test | request-response over HTTP | `python/tests/test_e2e_fake.py` | exact |
| `python/tests/test_grader.py`, `test_e2e_fake.py`, `test_vectors.py` (modify pins) | test | n/a | themselves | exact |
| `python/tests/test_runner.py` (modify: `--split`) | test | unit | itself | exact |

## Pattern Assignments

### `python/src/carimbo_evals/grader.py` (modify; service, transform)

**Analog:** itself. Keep `_load_manifest` and `_read_records` signatures (`decision_grader.py` imports them); add a NEW loader for per-case dataset metadata.

**Imports pattern** (lines 8-30):
```python
from __future__ import annotations
import hashlib, json, re, unicodedata
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from functools import cache
from pathlib import Path
from typing import Any
from jsonschema import Draft202012Validator
from pydantic import ValidationError
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.summary import (...)
```
Note `grader.py` imports from `summary.py`, so `summary.py` must not import `grader.py` (no cycle). New modules `matching.py`, `tags.py`, `stats.py` should depend on neither.

**Tolerance default to change** (line 32): `DEFAULT_TOLERANCE = Decimal("0.01")` becomes `Decimal("0.00")` (D-03).

**Normalizers to reuse for D-04 and match scoring** (lines 75-86):
```python
def normalize_id(value: str) -> str:
    return re.sub(r"[ ./-]", "", value).upper()

def normalize_name(value: str) -> str:
    folded = unicodedata.normalize("NFKC", value).casefold()
    return re.sub(r"[\W_]+", " ", folded).strip()
```

**Core per-field pattern to extend to item and installment columns** (lines 216-256): `_ID_FIELDS`/`_NAME_FIELDS`/`_INT_FIELDS` frozensets drive `_field_grade`; numeric uses `total_within_tolerance(value, Decimal(expected), tolerance)` (line 89-92, `abs(value - truth) <= tolerance` inclusive, so tolerance 0.00 means exact). The 27 header grades must stay in `graded["fields"]` as booleans.

**Per-case record shape** (lines 290-334): `grade_case` builds a flat dict, returns early for statuses not in `GRADED_STATUSES`, sets `graded["fields"] = dict.fromkeys(FIELDS, False)` for `schema_invalid`. Add item/installment diff, `wrong` list and `correct` flag alongside, never inside `fields`.

**Manifest loader to leave alone** (lines 359-369): returns `{name, version, manifest_sha256}` with `hashlib.sha256(raw).hexdigest()`. The sha256 is what `compare` refuses on (D-18).

**Runs with no manifest metadata** (Pitfall 8): `data/skeleton/manifest.json` has no `split`/`base_case`/`rendering`/`severity`/`barcode_readable`. Treat all as optional (`base_case` defaults to `case_id`).

---

### `python/src/carimbo_evals/tags.py` (NEW; utility, transform)

**Analog:** `dotnet/src/Carimbo.Decisions/DecisionRules.cs` lines 11-19 (version const, ordered first-match, rule number returned).
```csharp
public sealed record DecisionLabel(DecisionOutcome Outcome, DecisionCause Cause, int Rule);
public const string LabellingVersion = "decision-labels-1";
/// Applies the five ordered rules; the first match wins: 1. ... 2. ... 5. otherwise approve.
```
Python translation: `TAGS_VERSION = "failure-tags-1"`, an ordered tuple of `(tag, predicate)`, return `(tag, rule_number)` per wrong value, and a pinned-order test (D-12). Use RESEARCH.md "Failure tags" table (lines 462-482) for the exact rules, including the added rule 0 `schema_invalid` and the refined order (`merged_rows`, `split_rows` before `trailing_rows_lost`/`dropped_row`/`invented_row`). Use `difflib.SequenceMatcher(None, a, b, autojunk=False).ratio() >= 0.9` for `text_near_miss`. Dates skip the digit rules.

---

### `python/src/carimbo_evals/matching.py` (NEW; utility, transform)

**Analog:** `grader.py` normalizers (above). Copy the RESEARCH.md prototype (lines 258-268): integer weights `{"code":3,"description":3,"ncm":1,"unit":1,"quantity":2,"unit_price":2,"total":2}`, `MATCH_MIN = 6`, cost `(sum(w) - credit) * 10**6 + abs(i - j)`, import `linear_sum_assignment` lazily inside the function (0.37 s import). Installments: `number` 2, `due_date` 2, `amount` 1, min 2. Matching must not depend on `--tolerance`. Pure module: stdlib + numpy + lazy scipy, no HTTP.

---

### `python/src/carimbo_evals/stats.py` (NEW; utility, transform)

**Analog:** `decision_summary.py` lines 48-70 for the small pure-helper style (`_rate`, `_whole`, `_spread`); `summary.py` `_latency` is what `nearest_rank` replaces (its `median` becomes `p50`, adds `p95` and `n`). Copy code verbatim from RESEARCH.md "Code Examples": `wilson` (Decimal, `Z = Decimal("1.959963984540054")`, `ROUND_HALF_UP`, 4 places), `mcnemar_exact` (Fraction, `math.comb`; significant iff `20 * numerator < denominator`), `bootstrap_sums` (`random.Random(seed).random`, resample unit `base_case`), `nearest_rank`. Rounding rule: 4 places, like `hit_rate` today.

---

### `python/src/carimbo_evals/slices.py` (NEW; utility/config)

**Analog:** the module-level constant tuples in `summary.py` (lines 17-82: `SUMMARY_VERSION`, `GRADER_VERSION`, `FIELDS`, `CONFIG_KEYS`). Declare `SLICES_VERSION` and the fixed slice list in code (D-15). Manifest fields come from `data/dataset/manifest.json`: `base_case`, `rendering`, `severity`, `split`, `barcode_readable`, `tags`, `ci_subset`. Slice barcode on the manifest flag.

---

### `python/src/carimbo_evals/summary.py` (modify; service, transform)

**Analog:** itself.

**Version constants** (lines 17-18): `SUMMARY_VERSION = 3` to `4`, `GRADER_VERSION = "grader-002"` to `"grader-003"`.

**`build_summary` call shape that tests pin** (lines 206-215): `build_summary(case_grades, run_meta, manifest)` with `manifest=None` must keep working (test_grader.py lines 883, 1060, 1091, 1104, 1123).

**Summary dict pattern** (lines 232-248): additive keys only; `field_accuracy[...]` entries stay exactly `{"correct","n"}` (e2e tests assert equality); put intervals in a new block. `meta.graded_at` is the only volatile value. Existing `_latency` keeps `latency_ms`/`model_latency_ms` keys; replace `median` with `p50`, add `p95`.

**Write pattern** (lines 306-310):
```python
text = json.dumps(summary, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
(run_dir / "summary.json").write_text(text, encoding="utf-8")
(run_dir / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
```
`grade --out` needs `write_summary` to accept the target dir (it already takes `run_dir`; just pass `--out`).

---

### `python/src/carimbo_evals/compare.py` (NEW; service)

**Analog:** `decision_summary.py` (pure functions over summary dicts, `render_markdown`, no HTTP). Reads two `summary.json` files only. Security (V5): validate shapes and types, any malformed file is a refusal exit 2, no `eval`/pickle. Refusal order: `dataset.manifest_sha256`, `grader_version`, tolerance compared as `Decimal` (not string; `"0"` equals `"0.00"`). Refuse decision summaries (`summary_version == "decision-summary-1"` is a string, unlike the int in extraction summaries). Cases list built from `summary["cases"]` entries (`correct`, `wrong` list with path, tag, payment flag).

---

### `python/src/carimbo_evals/cli.py` (modify; controller)

**Analog:** itself, `grade` command (lines 273-297) for `compare` and `grade --out`; `run` for `--split`.

**Typer option + exit-code pattern** (grade, lines 273-297):
```python
@app.command()
def grade(
    run: Annotated[Path, typer.Option(help="Run directory containing cases.jsonl.")],
    cases: Annotated[Path, typer.Option(help="...")] = Path("data/skeleton"),
    tolerance: Annotated[str, typer.Option(help="...")] = str(DEFAULT_TOLERANCE),
    ...
) -> None:
    allowed = _money("--tolerance", tolerance)
    if not (run / "cases.jsonl").is_file():
        typer.echo(f"error: {run / 'cases.jsonl'} not found", err=True)
        raise typer.Exit(EXIT_USAGE)
    try:
        summary = grade_run(run, cases, tolerance=allowed, schema_path=schema)
    except (OSError, ValueError) as exc:
        typer.echo(f"error: {exc}", err=True)
        raise typer.Exit(EXIT_USAGE) from exc
    write_summary(run, summary)
```
`EXIT_USAGE = 2` (line 38) is the refusal code for `compare`; exit 0 whenever the comparison ran.

**Case selection pattern for `--split`** (run, lines ~214-228): `selected = discover_cases(cases)`, then the `--case` filter with unknown-id check and `EXIT_USAGE`. Add `--split` as a second filter that reads the manifest's `split`.

---

### `python/src/carimbo_evals/runner.py` (modify)

**Analog:** `discover_cases` (lines 115-121) and `read_as_of_date` (lines 124-135, reads `cases_dir / MANIFEST_NAME`, tolerant of missing manifest). Add a split filter helper next to `read_as_of_date` using the same tolerant manifest read. Defaults to keep in mind: `DEFAULT_MAX_COST_USD = Decimal("1.00")`, `DEFAULT_RESERVE_USD = Decimal("0.25")` (lines 37-38), so the dev recipe passes `--max-cost-usd 2.50`.

---

### `justfile` (modify: new dev recipe)

**Analog:** `skeleton` (line 141) and `_skeleton-run` (lines 145-~200). Copy the two-recipe shape: public recipe calling `_with-provider-key ... _<name>-run {{ args }}`; private recipe with provider-key presence check, ephemeral `CARIMBO_EVAL_API_KEY`, `datagen-check` replaced by `dataset-check`, build Api, healthz wait, `carimbo-evals run --cases data/dataset --split dev --out "${run_dir}" --max-cost-usd {{ max_cost }} --cache-mode {{ cache_mode }}`, stop Api, `grade`. Key excerpt:
```bash
uv run --project python carimbo-evals run --cases data/skeleton --out "${run_dir}" \
  --max-cost-usd {{ max_cost }} --cache-mode {{ cache_mode }} || run_rc=$?
stop_api
if [ -f "${run_dir}/cases.jsonl" ]; then
  uv run --project python carimbo-evals grade --run "${run_dir}"
fi
```
The grade call must pass `--cases data/dataset`. Also edit `docs-check` (line 79 loop ends at D-41) to include the new D-ids, and the `check` chain (line 101) if a new gate is added. Default cap for the recipe is `2.50`.

---

### `python/pyproject.toml` + `python/uv.lock` (modify: scipy)

**Analog:** pin block (lines 5-19), exact `==` pins, e.g. `"numpy==2.5.3",`. Add `"scipy==1.18.1",`. Gate: copy the `04-02-PLAN.md` task shape: Task 1 resolve-only (`devenv shell -- uv lock --project python`) with provenance table; Task 2 (lines 115-140 of `.planning/phases/04-synthetic-dataset/04-02-PLAN.md`) `type="checkpoint:human-verify" gate="blocking-human"`, resume signal "approved", `uv lock --project python --check` verify, never auto-approved; Task 3 `uv sync --project python --locked`. Lock diff must show scipy as the only new package.

---

### `python/tests/test_e2e_full_dataset.py` (NEW; e2e)

**Analog:** `python/tests/test_e2e_fake.py` (exact).

**Marker and fixtures** (lines 1-60): `pytestmark = pytest.mark.e2e`; `REPO_ROOT = Path(__file__).resolve().parents[2]`; `responses_dir` fixture writes `<sha256 of PDF>.json` scripted answers (lines 69-89):
```python
digest = hashlib.sha256((CASES_DIR / f"{case_id}.pdf").read_bytes()).hexdigest()
(directory / f"{digest}.json").write_text(json.dumps(scripted[case_id]), encoding="utf-8")
```
Use `CASES_DIR = REPO_ROOT / "data" / "dataset"` and the manifest's per-case `pdf_sha256`; corrupted answers come from `carimbo_evals/corruptions.py`. `host` fixture (lines 100-160): free port, `dotnet run --project dotnet/tests/Carimbo.ScriptedHost --no-launch-profile`, env `CARIMBO_SCRIPTED_RESPONSES_DIR`, `Llm__Cache__Directory` per test, healthz poll, `_stop` via process group. `_evals(*args, api_key=...)` runs the `carimbo-evals` console script as a subprocess. Scripted answer helper `_scripted(stop_reason, text)` at ~line 45-67. A root answer repeats on each attempt (ends `validation_failed` after the repair budget).

**No shared test helpers:** `python/tests` has no `conftest.py`/`__init__.py` and uses `--import-mode=importlib`; tests cannot import each other, so shared helpers go in `src/carimbo_evals/`.

---

### Unit test files (NEW: `test_matching`, `test_tags`, `test_stats`, `test_selftests`, `test_silent_errors`, `test_summary_v4`, `test_compare`)

**Analog:** `python/tests/test_grader.py`. Imports pattern (lines 1-40):
```python
from __future__ import annotations
from decimal import Decimal
from pathlib import Path
import pytest
from typer.testing import CliRunner
from carimbo_evals.cli import app
from carimbo_evals.grader import GroundTruth, grade_case, grade_run, load_ground_truth, normalize_id, normalize_name
from carimbo_evals.summary import FIELDS, GRADER_VERSION, SUMMARY_VERSION, build_summary, render_markdown, write_summary
REPO_ROOT = Path(__file__).resolve().parents[2]
```
Use `CliRunner` + `app` for `compare`, `grade --out` and `run --split` tests. For all-154-case self-tests, load ground truth with `load_ground_truth(REPO_ROOT / "data/dataset" / f"{case_id}.xml")`, as `test_dataset_ground_truth.py` does. Pure self-tests build records with status forced to `success` (no validators). Keep the corruption battery near 15 s (see RESEARCH.md time budget); bootstrap tests use 200-500 resamples except one pinned test.

---

### Modified test pins

| File | Lines | Change |
|------|-------|--------|
| `python/tests/test_grader.py` | 343-346, 760-762 | `GRADER_VERSION == "grader-003"`, `SUMMARY_VERSION == 4` |
| `python/tests/test_grader.py` | 765 | `summary["tolerance"] == "0.00"` |
| `python/tests/test_grader.py` | tests pinned to tolerance 0.01 | pass `Decimal("0.01")` explicitly |
| `python/tests/test_e2e_fake.py` | 325-326, 698 | unchanged; do not add keys to `field_accuracy` entries |
| `python/tests/test_e2e_fake.py` | 449 | `summary_version == 4` |
| `python/tests/test_vectors.py` | 107-108 | delete or rewrite `test_grader_default_tolerance_matches_the_vector_file` (grader default is now independent of validator tolerance); do not touch `data/vectors/validator-vectors.json` |

## Shared Patterns

### Determinism and rounding
**Source:** `summary.py` (`hit_rate` rounded to 4 places, `json.dumps(..., sort_keys=True, indent=2)`), RESEARCH.md Pitfall 7.
**Apply to:** `stats.py`, `summary.py`, `compare.py`. Use `Decimal` for money, `decimal.localcontext()` with explicit `ROUND_HALF_UP`, rates and bounds rounded to 4 places, normalise tolerance as `Decimal` before comparing. A regrade must be byte-identical apart from `meta.graded_at`.

### Offline grading, status semantics
**Source:** `grader.py` `grade_case` docstring (lines 266-279) and `summary.py` lines 20-32 (`OUTCOME_STATUSES`, `GRADED_STATUSES`).
**Apply to:** correct-invoice flag, silent-error rate, `compare`. Only `success` can be correct; refused/truncated/infrastructure/harness never count as wrong; infrastructure and harness leave the denominator and mark the run `incomplete`.

### Ordered first-match rule with version
**Source:** `dotnet/src/Carimbo.Decisions/DecisionRules.cs:11-19`.
**Apply to:** `tags.py` (and optionally slice assignment).

### Pure modules, no HTTP, lazy heavy imports
**Source:** `summary.py` docstring ("Pure and offline: no HTTP client, no sockets"); scipy lazy import per RESEARCH.md.
**Apply to:** `matching.py`, `stats.py`, `tags.py`, `slices.py`, `compare.py`, `corruptions.py`.

### Capped live recipe and verbatim results
**Source:** `justfile` `_skeleton-run`; AGENTS.md "Secrets and live calls".
**Apply to:** the dev recipe. Results get copied verbatim to `evals/results/`; regrade through `grade --out`, never in place. Update AGENTS.md commands table, "Live paid steps" list (new recipe cap US$2.50) and `docs-check` range.

### Supply-chain gate
**Source:** `.planning/phases/04-synthetic-dataset/04-02-PLAN.md` lines 115-140.
**Apply to:** the scipy plan (blocking human checkpoint, resolve-only first, `uv sync --locked` after approval).

### README drift
**Source:** `python/tests/test_readme_drift.py` (enforced in `just py-check`).
**Apply to:** last plan of the phase: Status row `Complete (YYYY-MM-DD)`, a `- Phase 5:` verification bullet linking `*-VERIFICATION.md`, links to any new `evals/results/` run directory, `docs-check` range matching justfile and AGENTS.md.

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `python/src/carimbo_evals/stats.py` (Wilson, McNemar, bootstrap) | utility | transform | No statistics code exists; only `_rate`/`_spread` helpers. Use RESEARCH.md "Code Examples" verbatim. |
| `python/src/carimbo_evals/compare.py` (run-to-run diff and refusal contract) | service | file in, report out | No two-artifact comparison exists. Use `decision_summary.py` for the render/pure-function style and RESEARCH.md for the contract. |

## Metadata

**Analog search scope:** `python/src/carimbo_evals`, `python/tests`, `dotnet/src/Carimbo.Decisions`, `dotnet/tests/Carimbo.ScriptedHost`, `justfile`, `python/pyproject.toml`, `.planning/phases/04-synthetic-dataset`
**Files scanned:** about 25 (grader.py, summary.py, cli.py, runner.py, decision_summary.py, test_e2e_fake.py, test_grader.py, DecisionRules.cs, justfile, pyproject.toml, 04-02-PLAN.md, plus listings)
**Tracked-source check:** `git ls-files` confirmed tracked: `grader.py`, `summary.py`, `cli.py`, `runner.py`, `decision_summary.py`, `decision_grader.py`, `DecisionRules.cs`, `justfile`, `pyproject.toml`, `uv.lock`, `test_e2e_fake.py`, `test_grader.py`, `test_vectors.py`, `test_readme_drift.py`, `04-02-PLAN.md`, `docs/DECISIONS.md`, `AGENTS.md`, `README.md`, ScriptedHost files. No mirror paths used.
**Pattern extraction date:** 2026-11-05
