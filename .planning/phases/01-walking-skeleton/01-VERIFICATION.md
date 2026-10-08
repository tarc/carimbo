---
phase: 01-walking-skeleton
verified: 2026-10-08T04:30:00Z
status: human_needed
score: 4/5 must-haves verified
covered_files:
  - ".github/workflows/ci.yml"
  - ".planning/phases/01-walking-skeleton/01-01-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-01-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-02-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-02-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-03-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-03-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-04-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-04-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-05-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-05-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-06-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-06-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-07-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-07-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-08-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-08-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-09-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-09-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-10-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-10-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-11-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-11-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-12-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-12-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-13-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-13-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-14-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-14-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-15-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-15-SUMMARY.md"
  - ".planning/phases/01-walking-skeleton/01-16-PLAN.md"
  - ".planning/phases/01-walking-skeleton/01-16-SUMMARY.md"
  - "dotnet/src/Carimbo.Domain/Invoice.cs"
  - "dotnet/src/Carimbo.Domain/Wire.cs"
  - "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
  - "justfile"
  - "python/src/carimbo_evals/cli.py"
  - "python/src/carimbo_evals/grader.py"
  - "python/src/carimbo_evals/runner.py"
  - "python/tests/test_e2e_fake.py"
covered_digest: "v3:sha256:8ef7c9d511ea9710a3dc6698115704c6b5bec08ae53ee549ee5b85503912ef8d"
behavior_unverified: 0
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 3/5
  gaps_closed:
    - "POST /eval/extractions returns a schema-valid Invoice or a typed failure (CR-01: oversized total_amount no longer ends as HTTP 500)"
    - "The extractor returns a schema-valid Invoice (WR-03: access_key / cnpj patterns are now enforced before success)"
  gaps_remaining: []
  regressions: []
advisory:
  - finding: "WR-06: the Python grader's bare Draft202012Validator is more lenient than .NET (trailing newline passes a pattern; format date unchecked). Reproduced here: issue_date 2026-02-30 is_valid True; access_key + newline is_valid True."
    category: other
    reason: "Affects only the supplementary schema_valid_jsonschema diagnostic, not the field grades, the .NET status, or any success criterion. Fix is in 01-REVIEW.md (re.fullmatch pattern keyword + FormatChecker)."
    evidence_status: "reproduced; no must-have is contradicted"
  - finding: "WR-07: grade on a structurally wrong JSONL record (e.g. [1, 2]) ends in a TypeError traceback instead of exit 2. Reproduced here."
    category: other
    reason: "Needs a hand-edited cases.jsonl; the runner never writes such a record and the runner-side twin (WR-05) is fixed. SC5 outputs are intact on every file the runner produces."
    evidence_status: "reproduced; no must-have is contradicted"
  - finding: "IN-08, IN-09, WR-04, IN-01..IN-07 remain open in 01-REVIEW-DISPOSITION.md"
    category: other
    reason: "Hardening and cosmetic; none touches a roadmap success criterion"
    evidence_status: "read against the code, not escalated"
human_verification:
  - test: "Open the first PR and watch the CI workflow"
    expected: "Jobs dotnet, python and contract all pass on ubuntu-latest; the pinned action majors (checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4) resolve"
    why_human: "CI-01 is 'builds and tests both stacks on every PR'. ci.yml is correct on inspection and calls the same just recipes that pass locally, but nothing has been pushed, so no runner has executed it."
  - test: "Open the repo in Claude Code and in OpenCode and ask 'what is the check command?'"
    expected: "Both answer 'just check' (or the per-stack recipes) from AGENTS.md"
    why_human: "REPO-04 needs both agent runtimes interactively. Locally verified: AGENTS.md is the canonical file, CLAUDE.md contains only '@AGENTS.md', test_repo_layout.py asserts the shape."
  - test: "Fresh clone of the repo, then 'devenv shell -- just check'; and the README 'Quick start without Nix' path on a machine without Nix"
    expected: "Exit 0 on both"
    why_human: "REPO-03. Verified here only in the working copy ('devenv shell -- just check' exit 0, re-run during this verification). A fresh clone and the non-Nix path have not been exercised."
---

# Phase 1: Walking Skeleton Verification Report

**Phase Goal:** As a developer, I want to generate a few synthetic DANFEs, extract them live through the .NET eval endpoint and grade the stored results from Python, so that the whole measurement loop runs end to end on real code before any breadth is added.
**Verified:** 2026-10-08
**Status:** human_needed
**Re-verification:** Yes, after gap closure (plans 01-14, 01-15, 01-16)

## Summary

Both gaps from the first verification are closed in the code, not just in the SUMMARYs. No gap remains, no regression was found, and the only open items are the three human checks that need a push, a second agent runtime or a fresh machine.

Evidence gathered in this session:

- `devenv shell -- just check` was re-run and exited 0: build with `-warnaserror`, format check, 188 xUnit tests passed (4 projects, 0 failed), ruff clean, pyright 0 errors, 144 pytest passed (4 e2e deselected, then the 4 e2e passed against the real ASP.NET host), `schema-check` with `git diff --exit-code` clean, `datagen-check` byte-identical, docs and secrets ok.
- CR-01: read `Money.Parse` (now `decimal.TryParse`, throws `FormatException` on overflow), `MoneyJsonConverter.Read` (also maps `OverflowException` to `JsonException`), and `InvoiceExtractor.Parse` (catches `JsonException`, `FormatException`, `OverflowException` narrowly, so cancellation and programming errors still propagate). Tests exist at all four layers: `DomainTests` (Parse, converter, `decimal.MaxValue` boundary), `ExtractorTests.An_amount_too_large_for_a_decimal_is_schema_invalid_and_the_raw_text_is_kept`, `EvalEndpointTests.An_amount_too_large_for_a_decimal_is_schema_invalid_with_http_200_and_its_cost`, and the cross-stack `TestTypedEdgeOutcomesOverHttp`. All pass in the gate.
- WR-03: `Invoice.PatternViolations()` checks `access_key`, `issuer.cnpj` and `recipient.cnpj` with `Patterns.IsFullMatch` (match must span the whole string, so the .NET `$`-before-final-newline quirk is rejected). `InvoiceExtractor.Parse` returns `SchemaInvalid` naming the field paths (never the values) before `Success`. `ExtractionOutcome.Success` is documented as schema-valid. `Every_pattern_in_the_model_facing_schema_is_enforced_by_the_extractor` walks the real schema, so a new pattern without enforcement fails a test. It is a method, not a property, so the exported schema is unchanged (`schema-check` clean).
- The previously recorded live run (`evals/runs/skeleton-20261008T001300Z`, 3 cases, US$0.0195) is untouched by the fixes; the .NET and Python changes since c459e72 are confined to six source files (Invoice, Wire, InvoiceExtractor, cli, grader, runner).

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria, the contract)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Fresh clone: developer (devenv) and reviewer (non-Nix) each build, lint, type-check and test both stacks with one documented command per stack; one canonical agent file; Actions builds and tests both stacks on every PR | ? UNCERTAIN (WARNING, human) | `just dotnet-check` and `just py-check` recipes pass in the re-run gate; README "Quick start without Nix"; `AGENTS.md` canonical, `CLAUDE.md` = `@AGENTS.md`; `ci.yml` re-read: plain `pull_request`, `contents: read`, no path filters, jobs `dotnet`, `python`, `contract` call the same just recipes. No CI run exists (nothing pushed); agent-runtime discovery and fresh-clone/non-Nix runs are not exercised. See human_verification. |
| 2 | Domain exports committed canonical schema, model-facing schema and generated Pydantic; budget test (<=24 optional / <=16 union); CI fails on stale; DECISIONS records D-03/D-07/D-15 refinements | ✓ VERIFIED (regression check) | `just schema-check` regenerated and `git diff --exit-code` was clean in this run; schema budget tests are in the 188 passing .NET tests; `docs-check` ok. The WR-03 change added a method on `Invoice` and left the exported schema byte-identical. |
| 3 | Same seed twice gives byte-identical `{case}.xml` + `{case}.pdf` with Code 128 barcode; synthetic only | ✓ VERIFIED (regression check) | `carimbo-datagen check`: "ok: data/skeleton matches a regeneration from seed 20261004"; barcode and synthetic-only tests are in the 144 passing pytest tests. |
| 4 | `POST /eval/extractions` returns a schema-valid `Invoice` or a typed refusal / truncation / infrastructure failure, with tokens, versioned cost, latency and trace id; recorded live spike settles gateway shape and retry ownership; unavailable without key or outside dev/eval | ✓ VERIFIED | Previously failing edge paths now typed (CR-01, WR-03 above). Every extractor path ends in a typed `ExtractionOutcome`: success only after parse and pattern enforcement, `schema_invalid` for JSON, conversion, overflow and pattern errors, `refused`, `truncated`, `infrastructure_failure`. HTTP 200 with the cost kept for the oversized amount is asserted at the endpoint and across HTTP in the e2e. Unchanged and still passing: key gate, 401/404/400/413/415, cost from versioned `pricing.json`, `latency_ms`, `trace_id`, spike doc and D-21. The money pattern has no length bound, so a 32-digit amount is still schema-valid for the Python validators while .NET reports `schema_invalid`; see the deferral note below. |
| 5 | One documented command runs the runner (bounded concurrency, cost cap, resumable), one JSONL record per case incl. raw output, grades offline with no model calls, summary with field-level grade per case plus tokens, cost, latency | ✓ VERIFIED | `runner.py`: `reserve = max(reserve_usd, largest)` and `spent + assumed + reserve > max_cost_usd` stops dispatch; possibly-paid harness errors are charged as `assumed_usd` (WR-02); `CorruptRunError` on a corrupt middle line, CLI exit 2 (WR-05); `grader._read_records` splits on `"\n"` only (WR-01). Tests: `test_raw_output_with_unicode_line_separators_is_graded`, `test_cli_stops_at_the_cap_when_server_errors_may_have_cost_money`, `test_cli_resume_on_a_corrupt_run_exits_2_with_the_line_number`, plus the 4 e2e. Live artifacts from the recorded run are intact. WR-07 (grader traceback on a hand-corrupted record) is advisory, see below. |

**Score:** 4/5 truths verified (SC1 pending the human items). 0 present-behavior-unverified.

### Gap re-check (from the first verification)

| Gap | Status now | Evidence |
|-----|-----------|----------|
| CR-01 oversized `total_amount` leaks `OverflowException` into HTTP 500 | CLOSED | Code and four test layers above; gate green. |
| WR-03 `success` does not mean schema-valid | CLOSED (chose enforcement, the first of the two options offered) | `PatternViolations()` + `IsFullMatch` + exhaustive schema-walk test. |

### Assessment of the open review findings against the must-haves

- **WR-06 (grader schema verdict more lenient than .NET)**: reproduced here (`issue_date` 2026-02-30 and `access_key + "\n"` both `is_valid True`). It does not break a success criterion. SC4 is a statement about the .NET endpoint's result, which is strict. SC5 requires "at least one field-level grade per case"; field grades are computed from the outcome and ground truth, and a `schema_invalid` outcome is graded wrong on every field regardless of the Python verdict. `schema_valid_jsonschema` / `schema_valid_pydantic` are supplementary diagnostics in `summary.json`. The narrow claim in plan 01-16 (the grader's schema verdict agrees with .NET for the space-grouped access key it tests) is accurate and asserted; the broader reading in the e2e comment is not. Advisory; it becomes relevant to Phase 5 (EVAL-03/04, graders with self-tests), where the fix in 01-REVIEW.md belongs.
- **WR-07**: reproduced (`[1, 2]` as a line gives `TypeError`, traceback). It needs a hand-edited `cases.jsonl`; the runner cannot produce it and the resume path (WR-05) is hardened. Advisory; cheap to fix with the same validation helper.
- **IN-08, IN-09, WR-04, IN-01..IN-07**: hardening and cosmetic, none in the success-criterion path.

### Deferred Items

| # | Item | Addressed In | Evidence |
|---|------|-------------|----------|
| 1 | Money pattern has no digit-length bound, so Python calls a 32-digit amount schema-valid while .NET returns `schema_invalid` | Phase 2 | REQUIREMENTS.md maps DOM-04 ("Money is a decimal string on the wire (pattern-constrained)...") to Phase 2 (Pending), and ROADMAP Phase 2 Requirements list DOM-04 with SC1 "Money crosses the wire as pattern-constrained decimal strings". The assignment is real, but neither text mentions a length bound explicitly; Phase 2 planning should carry it as a named item in DOM-04. Accepted because Phase 1's own contract is not broken: .NET, the authority for SC4, returns a typed outcome, and the cost is kept. |

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `dotnet/src/Carimbo.Domain/{Invoice,Wire,CanonicalSchema}.cs` | Pure records, wire options, exporter | ✓ VERIFIED | Defects CR-01 and WR-03 fixed; schema output unchanged |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` | Typed outcomes | ✓ VERIFIED | Narrow catches, pattern enforcement, tests |
| `dotnet/src/Carimbo.Llm/*`, `dotnet/src/Carimbo.Api/*` | Gateway, pricing, locked-down endpoint | ✓ VERIFIED | Unchanged since first verification; tests green |
| `schema/*.json`, `python/src/carimbo_models/generated.py` | Contract chain | ✓ VERIFIED | Regenerated with no diff |
| `python/src/carimbo_datagen/*`, `data/skeleton/*` | Seeded generator, 3 cases | ✓ VERIFIED | Byte-identical regeneration |
| `python/src/carimbo_evals/{runner,grader,summary,cli}.py` | Runner, offline grader, summary | ✓ VERIFIED | WR-01/02/05 fixed; WR-07 advisory |
| `docs/DECISIONS.md`, `docs/spikes/01-llm-gateway.md` | Superseding entries, spike | ✓ VERIFIED | `docs-check` ok |
| `.github/workflows/ci.yml`, `AGENTS.md`, `CLAUDE.md`, `README.md` | CI and agent docs | ✓ VERIFIED (exists, wired to recipes) | Never executed on a runner |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| Extractor `Parse` | `Invoice.PatternViolations` | direct call before `Success` | WIRED | `InvoiceExtractor.cs` line ~163 |
| `MoneyJsonConverter` | `Money.Parse` | FormatException/OverflowException mapped to JsonException | WIRED | `Wire.cs` |
| Runner | endpoint | HTTP, `X-Api-Key` | WIRED | 4 e2e passed |
| Runner JSONL | grader | `cases.jsonl` read offline | WIRED | e2e |
| Domain | schema -> Pydantic | `SchemaExport`, `datamodel-codegen` | WIRED | `schema-check` clean |
| `ci.yml` | just recipes | `dotnet-check`, `py-check`, `datagen-check`, `schema-check`, `e2e`, `docs-check`, `secrets-check` | WIRED | Recipes pass locally |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `summary.md` rows | fields, cost, latency | `cases.jsonl` from real Anthropic responses (recorded live run) | Yes | ✓ FLOWING |
| `EvalResponse.cost_usd` | `LlmPricingTable` x usage | `pricing.json` + provider usage | Yes; also positive in the scripted e2e for schema_invalid cases | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full offline gate | `devenv shell -- just check` | exit 0; 188 xUnit passed, 144 pytest + 4 e2e passed | ✓ PASS |
| Oversized amount and pattern violation end as typed `schema_invalid` over HTTP | `TestTypedEdgeOutcomesOverHttp` (inside the gate's `-m e2e` run), plus the .NET endpoint test | passed | ✓ PASS |
| Grader schema verdict on `2026-02-30` / key + newline | `load_schema_validator(...).is_valid` | both True | ✓ confirms WR-06 (advisory) |
| Grader on a `[1, 2]` record | `carimbo-evals grade --run <dir>` | `TypeError` traceback | ✓ confirms WR-07 (advisory) |

### Probe Execution

SKIPPED: the phase declares no `scripts/*/tests/probe-*.sh` probes.

### Requirements Coverage

All 20 phase IDs appear in PLAN `requirements:` frontmatter and in REQUIREMENTS.md; no orphans.

| Requirement | Source Plan(s) | Status | Evidence |
|-------------|----------------|--------|----------|
| REPO-01 | 01-02, 01-13 | ✓ SATISFIED | `just dotnet-check` green |
| REPO-02 | 01-01, 01-13 | ✓ SATISFIED | `just py-check` green |
| REPO-03 | 01-13 | ? NEEDS HUMAN (partial) | devenv path verified in the working copy; fresh clone and non-Nix path unexercised |
| REPO-04 | 01-13 | ? NEEDS HUMAN | File shape verified; runtime discovery not |
| CI-01 | 01-13 | ? NEEDS HUMAN | Workflow correct on inspection; never run. REQUIREMENTS.md Pending, consistent |
| DOM-01 | 01-02, 01-04 | ✓ SATISFIED | Domain has no references; DomainTests |
| DOM-05 | 01-04 | ✓ SATISFIED | Snapshot test + `schema-check` |
| DOM-06 | 01-05 | ✓ SATISFIED | SchemaProjection tests |
| DOM-07 | 01-09 | ✓ SATISFIED | `generated.py`, `test_models.py` |
| DOM-08 | 01-04, 01-05, 01-09, 01-13 | ✓ SATISFIED | `schema-check` in the `contract` job |
| LLM-02 | 01-11, 01-12 | ✓ SATISFIED | Pricing tests, live cost |
| LLM-06 | 01-10, 01-12 | ✓ SATISFIED | Spike doc, D-21 |
| EXT-01 | 01-03, 01-08, 01-12, 01-14, 01-16 | ✓ SATISFIED | Evidence supports the Complete mark: success now means parsed + patterns enforced; every other path typed |
| EXT-02 | 01-03, 01-08, 01-12, 01-14, 01-16 | ✓ SATISFIED | Evidence supports the Complete mark: refusal, truncation, infra, plus schema_invalid for conversion errors, all distinct and tested |
| API-03 | 01-03, 01-08 | ✓ SATISFIED | EvalEndpointTests 404/401 |
| DATA-01 | 01-06, 01-07 | ✓ SATISFIED | `datagen-check`, barcode test |
| DATA-07 | 01-06, 01-07 | ✓ SATISFIED | `test_synthetic_only.py` |
| EVAL-01 | 01-03, 01-09, 01-15, 01-16 | ✓ SATISFIED | Evidence supports the Complete mark: cap charges possibly-paid errors, resume validates the file, e2e shows `spent_usd` = sum of record costs |
| EVAL-02 | 01-03, 01-09, 01-15, 01-16 | ✓ SATISFIED | Evidence supports the Complete mark: offline grader, no HTTP import, U+2028 safe. WR-07 is a hardening gap on a hand-corrupted file, not a failure of re-gradability |
| RES-03 | 01-04 | ✓ SATISFIED | D-18, D-19, D-20 present |

Housekeeping for the orchestrator: REQUIREMENTS.md still shows "Gaps Found" for 14 Phase 1 IDs (REPO-01..04, DOM-01/05..08, DATA-01/07, LLM-02/06, API-03, RES-03). That label came from the first verification; with the gaps closed, the SATISFIED rows above can be marked Complete. REPO-03, REPO-04 and CI-01 stay open until the human checks are done.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `python/src/carimbo_evals/grader.py` | 120-139 | Bare `Draft202012Validator` (WR-06) | Advisory | Supplementary schema diagnostic more lenient than .NET |
| `python/src/carimbo_evals/grader.py` | 248-256 | Unvalidated record shape (WR-07) | Advisory | Traceback instead of exit 2 on a hand-edited file |
| `python/src/carimbo_datagen/cli.py` | 95-104 | `--case` overwrites the manifest (WR-04) | Info | Partial manifest; `datagen-check` is the guard |
| various | - | IN-01..IN-09 | Info | Cosmetic or hardening |

No unreferenced TBD/FIXME/XXX markers in the files changed in this phase's gap closure (checked on the six modified source files).

### Human Verification Required

1. **First PR CI run.** Test: open the PR and watch jobs `dotnet`, `python`, `contract`. Expected: green, with the pinned action majors resolving. Why human: CI-01, nothing pushed yet.
2. **Agent-runtime discovery.** Test: ask "what is the check command?" in Claude Code and OpenCode. Expected: both answer from `AGENTS.md`. Why human: REPO-04 needs interactive runtimes.
3. **Fresh clone and non-Nix path.** Test: clone, run `devenv shell -- just check`; separately follow README "Quick start without Nix". Expected: exit 0. Why human: REPO-03, only the working copy was exercised.

### Gaps Summary

No gaps. CR-01 and WR-03 are closed in the code with regression tests at the Domain, Extraction, Api and cross-stack layers, and the offline gate is green on a re-run. The goal (generate DANFEs, extract them live through the .NET endpoint, grade the stored results from Python) holds, and was demonstrated live by the recorded run. The phase cannot be `passed` only because CI-01, REPO-03 and REPO-04 describe behavior on a CI runner, a fresh machine and two agent runtimes, none of which can be exercised from this working copy. WR-06 and WR-07 are reproduced advisories for Phase 5 grader work and a small follow-up; the money length bound is deferred to Phase 2 (DOM-04), and Phase 2 planning should name it explicitly.

---

_Verified: 2026-10-08_
_Verifier: Claude (gsd-verifier)_
