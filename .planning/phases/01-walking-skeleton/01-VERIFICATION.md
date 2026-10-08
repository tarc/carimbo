---
phase: 01-walking-skeleton
verified: 2026-10-08T01:00:00Z
status: gaps_found
score: 3/5 must-haves verified
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
  - "dotnet/src/Carimbo.Api/EvalEndpoint.cs"
  - "dotnet/src/Carimbo.Domain/Invoice.cs"
  - "dotnet/src/Carimbo.Domain/Wire.cs"
  - "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
  - "dotnet/src/Carimbo.Llm/AnthropicLlmGateway.cs"
  - "justfile"
  - "python/src/carimbo_evals/grader.py"
  - "python/src/carimbo_evals/runner.py"
covered_digest: "v3:sha256:3c39a0180014cf5fe59097ee08d54959c91ffeeccfa21a34a752cecc823a6e2e"
behavior_unverified: 0
overrides_applied: 0
gaps:
  - truth: "POST /eval/extractions returns either a schema-valid Invoice or a typed refusal, truncation or infrastructure failure (ROADMAP SC4; EXT-01; the 'typed failures are never wrong answers' guarantee of EXT-02)"
    status: failed
    reason: "CR-01, reproduced independently. A model answer whose total_amount has more than ~28 integer digits (e.g. \"99999999999999999999999999999999.00\") makes Money.Parse throw OverflowException. MoneyJsonConverter.Read maps only FormatException to JsonException, InvoiceExtractor.Parse catches only JsonException, and EvalEndpoint.HandleAsync has no catch around ExtractAsync. The request ends as an HTTP 500 after the paid provider call, not as outcome.status = schema_invalid. The runner records a 500 as harness_error with no cost, so the spend is invisible to the cost cap (WR-02) and --resume pays for the case again."
    artifacts:
      - path: "dotnet/src/Carimbo.Domain/Wire.cs"
        issue: "Money.Parse calls decimal.Parse after the regex check; overflow is not mapped to FormatException/JsonException (lines ~37-40, 58-65)"
      - path: "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
        issue: "Parse() catches only JsonException; no defence in depth for other exceptions from deserialization"
      - path: "dotnet/src/Carimbo.Api/EvalEndpoint.cs"
        issue: "No handler converts an unexpected extractor exception into a typed response"
    missing:
      - "Use decimal.TryParse in Money.Parse (or catch OverflowException in MoneyJsonConverter.Read) so oversized amounts become JsonException -> SchemaInvalid"
      - "Regression tests with a 32-digit amount in DomainTests and ExtractorTests expecting SchemaInvalid"
  - truth: "The extractor returns a schema-valid Invoice (EXT-01 wording; the Wire.Options doc claims the schema describes exactly what the deserializer accepts)"
    status: partial
    reason: "WR-03, reproduced independently. [RegularExpression] attributes on AccessKey and Cnpj feed only the exported schema; JsonSerializer ignores them. access_key \"bad\" and cnpj \"x\" deserialize without error and the extractor reports status success. The pattern IS sent to the model (schema/invoice.model.schema.json keeps pattern on access_key, cnpj and total_amount) and the Python grader independently flags schema_valid_jsonschema=false, so the live runs are not affected, but the .NET success status does not mean schema-valid."
    artifacts:
      - path: "dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs"
        issue: "Success is returned after deserialization only; pattern members are never checked"
      - path: "dotnet/src/Carimbo.Domain/Invoice.cs"
        issue: "Patterns exist only as schema-feeding attributes"
    missing:
      - "Post-deserialization pattern validation returning SchemaInvalid, or an explicit doc statement (ExtractionOutcome.Success, endpoint contract) that success means 'parsed', with pattern/check-digit validation deferred to the Phase 2 validators"
deferred: []
human_verification:
  - test: "Open the first PR and watch the CI workflow"
    expected: "Jobs dotnet, python and contract all pass on ubuntu-latest; the pinned action majors (checkout@v7, setup-dotnet@v6, setup-uv@v7, setup-just@v4) resolve"
    why_human: "CI-01 is 'builds and tests both stacks on every PR'. The workflow file is correct on inspection and calls the same just recipes that pass locally, but nothing has been pushed, so no runner has executed it."
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
**Status:** gaps_found
**Re-verification:** No, initial verification

## Summary

The measurement loop exists, runs and has been run live. `devenv shell -- just check` was re-run during this verification and exited 0: .NET build with `-warnaserror` (0 warnings), format check, all four xUnit v3 projects passed, ruff, pyright (0 errors), 121 pytest tests (3 e2e deselected, then 3 e2e passed against a real HTTP host), schema regeneration with `git diff --exit-code`, datagen byte-identity, docs and secrets checks. The recorded live run `evals/runs/skeleton-20261008T001300Z` has 3 cases, all `success`, field grades 8/9, 8/9, 9/9, US$0.0195, trace ids present, and no key-shaped strings.

One reproducible defect contradicts an explicit success criterion: SC4 promises "either a schema-valid Invoice or a typed refusal, truncation or infrastructure failure", and an oversized `total_amount` produces an HTTP 500 (CR-01). That is a BLOCKER for SC4 and for the EXT-01 / EXT-02 typed-outcome guarantee. WR-03 weakens "schema-valid" in the same area. Everything else that could be checked locally holds; three items need a human or a push.

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria, the contract)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Fresh clone: developer (devenv) and reviewer (non-Nix path) can each build, lint, type-check and test both stacks with one documented command per stack; one canonical agent instruction file for Claude Code and OpenCode; GitHub Actions builds and tests both stacks on every PR | ? UNCERTAIN (WARNING, human) | Verified locally: `just dotnet-check` and `just py-check` recipes exist and pass; README has "Quick start without Nix" (line 7); `AGENTS.md` canonical, `CLAUDE.md` = `@AGENTS.md`; `.github/workflows/ci.yml` uses plain `pull_request`, `contents: read`, no path filters, jobs `dotnet`, `python`, `contract` calling the same just recipes. Not verifiable: any CI run (nothing pushed), interactive discovery by both agent runtimes, fresh-clone and non-Nix runs. See human_verification. |
| 2 | Domain records export committed canonical schema, model-facing schema and generated Pydantic models; test keeps budget within 24 optional / 16 union; CI fails when any is stale; DECISIONS records D-03/D-07/D-15 refinements as superseding entries | ✓ VERIFIED | `schema/invoice.schema.json`, `schema/invoice.model.schema.json`, `python/src/carimbo_models/generated.py` committed. `just schema-check` (regenerate + `git diff --exit-code`) passed in `just check` and runs in the CI `contract` job. `SchemaProjectionTests` / `SchemaBudget` passed. `docs/DECISIONS.md` has `## D-18 ... (refines DECISIONS D-03)`, `## D-19 ... (refines D-07)`, `## D-20 ... (refines D-15)`, plus D-21; `docs-check` passed. |
| 3 | Same seed twice gives byte-identical `{case}.xml` + `{case}.pdf` with Code 128 access-key barcode; synthetic parties only | ✓ VERIFIED | `carimbo-datagen check` printed "ok: data/skeleton matches a regeneration from seed 20261004" (also in CI python job). `data/skeleton/` holds case-001..003 xml/pdf with sha256 in `manifest.json`. `test_datagen.py::test_danfe_code128_barcode_decodes_to_the_access_key` (zxing-cpp) and `test_synthetic_only.py` passed in the 121-test run. |
| 4 | With the static key, `POST /eval/extractions` sends a PDF through the gateway and returns a schema-valid `Invoice` or a typed refusal / truncation / infrastructure failure, with tokens, versioned-pricing cost, latency, trace id; recorded live spike settles gateway shape and retry ownership; without the key or outside dev/eval the endpoint is unavailable | ✗ FAILED (BLOCKER, edge path) | Working: `EvalEndpoint.TryMap` maps the route only in Development/Eval with key and gateway; key compared by SHA-256 + `FixedTimeEquals` before the body is read; 401/404/400/413/415 covered by passing `EvalEndpointTests`. Response carries usage, `cost_usd` from `pricing.json` (`pricing_version: anthropic-2026-10-04`), `latency_ms`, `trace_id`. Live: all 3 cases `success` with real costs and trace ids. Spike: `docs/spikes/01-llm-gateway.md` has a Recommendation (direct SDK), D-21 recorded, US$0.2551 spent. FAILS: CR-01 (HTTP 500 on oversized amount, reproduced) and WR-03 (pattern-violating answer reported as success, reproduced). See gaps. |
| 5 | One documented command runs the Python runner (bounded concurrency, cost cap, resumable), writes one JSONL record per case incl. raw output, then grades offline with no model calls, and writes a summary with a field-level grade per case plus tokens, cost, latency | ✓ VERIFIED (with warnings) | `carimbo-evals` console script; `runner.py` has `concurrency`, `max_cost_usd`, `resume`, `stopped_reason="cost_cap"`; `grader.py` imports no HTTP client. Live artifacts: `cases.jsonl` (3 records, includes `raw_output`), `summary.json`, `summary.md` with per-case fields, cost, latency, trace id. `test_runner.py`, `test_grader.py`, 3 e2e tests passed. Warnings (not failing this truth, see Anti-Patterns): WR-01 grader crashes on a JSONL record containing U+2028/U+2029/U+0085; WR-02 cost cap ignores spend on harness-error records; WR-05 uncaught traceback on a corrupt middle line when resuming. |

**Score:** 3/5 truths verified (SC1 uncertain pending human items, SC4 failed). 0 present-behavior-unverified.

### Assessment of the code-review findings against the must-haves

- **CR-01 contradicts SC4, EXT-01 and the typed-outcome guarantee of EXT-02.** EXT-02 names refusals, `max_tokens` truncation and infrastructure errors; those three are distinct typed outcomes and are tested. But the endpoint's contract (and the `ExtractionOutcome` doc comment, "Typed failures are never wrong answers") is that every extraction ends in a typed outcome; a model-controlled string escapes it. Independent reproduction (scratch project against the real `Wire.Options`): `10.00` parses, `99999999999999999999999999999999.00` throws `OverflowException`, which is not a `JsonException`. Classification: BLOCKER. The fix is small (one `TryParse`, two regression tests) but it is not applied: `01-REVIEW-DISPOSITION.md` shows CR-01 still `open`.
- **WR-03 contradicts the "schema-valid Invoice" wording of SC4/EXT-01** in the .NET status. Reproduced: `access_key: "bad"` deserializes. Mitigations: patterns are in the schema sent to the model, and the Python grader flags the violation independently, so the measured loop is honest. Classification: gap (partial); either enforce or document that `success` means "parsed" and defer to Phase 2 validators.
- WR-01, WR-02, WR-05 weaken SC5 in edge cases (grader crash, uncapped spend on harness errors, resume traceback) but do not break its stated outputs; they are warnings to fix before Phase 3 reruns rely on resume. WR-02 compounds CR-01.

### Deferred Items

None. Phase 2 (validators, bounded repair) does not name oversized-amount handling or pattern enforcement of the parsed record, so neither gap is deferred.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `dotnet/Carimbo.slnx`, `global.json`, `Directory.Packages.props` | Solution, SDK pin, CPM | ✓ VERIFIED | Build with `-warnaserror` clean |
| `dotnet/src/Carimbo.Domain/{Invoice,Wire,CanonicalSchema}.cs` | Pure records, wire options, exporter | ✓ VERIFIED (defects CR-01, WR-03) | Substantive and wired into extractor, endpoint, SchemaExport |
| `dotnet/src/Carimbo.Extraction/{InvoiceExtractor,ModelSchemaProjector,SchemaBudget}.cs` | Contract, projector, budget | ✓ VERIFIED | Contract built from the projected schema, hash recorded in every response |
| `dotnet/src/Carimbo.Llm/{AnthropicLlmGateway,LlmPricing}.cs`, `pricing.json` | Direct-SDK gateway, versioned pricing | ✓ VERIFIED | Live-exercised (smoke-01-12, skeleton run) |
| `dotnet/src/Carimbo.Api/{EvalEndpoint,Program}.cs` | Locked-down endpoint | ✓ VERIFIED | Tests green; live run through it |
| `schema/*.json`, `python/src/carimbo_models/generated.py` | Committed contract chain | ✓ VERIFIED | Regenerated in the gate with no diff |
| `python/src/carimbo_datagen/*`, `data/skeleton/*` | Seeded generator and 3 cases | ✓ VERIFIED | Byte-identical regeneration |
| `python/src/carimbo_evals/{runner,grader,summary,cli}.py` | Runner, offline grader, summary | ✓ VERIFIED (warnings WR-01/02/05) | Live run artifacts present |
| `docs/DECISIONS.md` D-18..D-21, `docs/spikes/01-llm-gateway.md` | Superseding entries, spike | ✓ VERIFIED | `docs-check` passed |
| `.github/workflows/ci.yml`, `AGENTS.md`, `CLAUDE.md`, `README.md` | CI and agent docs | ✓ VERIFIED (exists, wired to recipes) | Execution on a runner unverified |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| Domain records | `schema/invoice.schema.json` | `SchemaExport` + snapshot test | WIRED | Gate regenerates, no diff |
| Canonical schema | `generated.py` | `datamodel-codegen` pinned flags in `just schema` | WIRED | `git diff --exit-code` passed |
| Projector output | model request | `ExtractionContract.Default.OutputSchemaJson`, hash echoed in response | WIRED | `schema/invoice.model.schema.json` carries the patterns |
| Extractor | gateway | `ILlmGateway.CompleteAsync` | WIRED | Live runs |
| Endpoint | extractor + pricing | `IInvoiceExtractor`, `LlmPricingTable` | WIRED | `cost_usd`, `pricing_version` in live records |
| Runner | endpoint | HTTP, `X-Api-Key` | WIRED | e2e + live |
| Runner JSONL | grader | `cases.jsonl` read offline | WIRED | e2e + live |
| `ci.yml` | just recipes | `just dotnet-check`, `py-check`, `datagen-check`, `schema-check`, `e2e`, `docs-check`, `secrets-check` | WIRED | Recipes exist and pass locally |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `summary.md` rows | fields, cost, latency | `cases.jsonl` from real Anthropic responses (haiku-4-5, 17712 in / 356 out tokens) | Yes | ✓ FLOWING |
| `EvalResponse.cost_usd` | `LlmPricingTable` x provider usage | `pricing.json` + usage from the API | Yes | ✓ FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full offline gate | `devenv shell -- just check` | exit 0 (xUnit 4 projects passed, 0 failed; 121 pytest + 3 e2e passed) | ✓ PASS |
| Oversized amount is a typed outcome | scratch console project using real `Wire.Options` | `OverflowException`, not `JsonException` | ✗ FAIL (CR-01) |
| Pattern-violating `access_key` is rejected | same | `access_key=bad` accepted | ✗ FAIL (WR-03) |
| Live skeleton evidence | read `evals/runs/skeleton-20261008T001300Z/summary.md` | 3/3 success, US$0.0195, trace ids, no `sk-ant-`/`x-api-key` in the run directory | ✓ PASS (recorded; live recipes not re-run, they spend money) |

### Probe Execution

SKIPPED: the phase declares no `scripts/*/tests/probe-*.sh` probes.

### Requirements Coverage

All 20 phase IDs appear in at least one PLAN `requirements:` frontmatter and in REQUIREMENTS.md; no orphans.

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|----------|
| REPO-01 | 01-02, 01-13 | .NET build/test one command | ✓ SATISFIED | `just dotnet-check` green |
| REPO-02 | 01-01, 01-13 | Python build/lint/type/test one command | ✓ SATISFIED | `just py-check` green |
| REPO-03 | 01-13 | devenv + documented non-Nix path | ? NEEDS HUMAN (partial) | devenv path verified in working copy; fresh clone and non-Nix path unexercised |
| REPO-04 | 01-13 | One canonical agent file for Claude Code and OpenCode | ? NEEDS HUMAN | File shape verified; runtime discovery not |
| CI-01 | 01-13 | Actions builds and tests both stacks on every PR | ? NEEDS HUMAN | Workflow correct on inspection; never run. REQUIREMENTS.md still marks it Pending, consistent |
| DOM-01 | 01-02, 01-04 | Pure domain records | ✓ SATISFIED | Domain project has no references; DomainTests |
| DOM-05 | 01-04 | Canonical schema committed, deterministic | ✓ SATISFIED | Snapshot test + `schema-check` |
| DOM-06 | 01-05 | Projector + budget test | ✓ SATISFIED | SchemaProjectionTests |
| DOM-07 | 01-09 | Generated Pydantic models | ✓ SATISFIED | `generated.py`, `test_models.py` |
| DOM-08 | 01-04, 01-05, 01-09, 01-13 | CI fails on stale schema/models | ✓ SATISFIED | `schema-check` in the `contract` job |
| LLM-02 | 01-11, 01-12 | Token classes + versioned pricing cost | ✓ SATISFIED | PricingTests, live cost |
| LLM-06 | 01-10, 01-12 | Spike decides gateway shape and retry ownership | ✓ SATISFIED | Spike doc Recommendation, D-21, live evidence |
| EXT-01 | 01-03, 01-08, 01-12 | Schema-valid Invoice or typed failure | ✗ BLOCKED (edge) | CR-01 (HTTP 500), WR-03 (pattern not enforced) |
| EXT-02 | 01-03, 01-08, 01-12 | Refusal / truncation / infra errors typed, distinct | ✓ SATISFIED for the three named classes | ExtractorTests, EvalEndpointTests; CR-01 is a fourth, untyped path |
| API-03 | 01-03, 01-08 | Disabled outside dev/eval, static key | ✓ SATISFIED | EvalEndpointTests (404/401) |
| DATA-01 | 01-06, 01-07 | Paired XML + PDF with Code 128, byte-reproducible | ✓ SATISFIED | `datagen-check`, barcode test |
| DATA-07 | 01-06, 01-07 | Synthetic only | ✓ SATISFIED | `test_synthetic_only.py` |
| EVAL-01 | 01-03, 01-09 | Runner with concurrency, cap, resume, raw output | ✓ SATISFIED (WR-02 weakens the cap) | Live run, `test_runner.py` |
| EVAL-02 | 01-03, 01-09 | Offline re-gradable grading | ✓ SATISFIED (WR-01 crash on U+2028) | `test_grader.py`, no HTTP import |
| RES-03 | 01-04 | DECISIONS superseding entries | ✓ SATISFIED | D-18, D-19, D-20 present |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `dotnet/src/Carimbo.Domain/Wire.cs` | 37-40, 58-65 | Unmapped `OverflowException` (CR-01) | BLOCKER | HTTP 500 after a paid call |
| `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs` | 147-159 | Success without pattern enforcement (WR-03) | WARNING (gap, partial) | `success` != schema-valid |
| `python/src/carimbo_evals/grader.py` | 244-254 | `splitlines` on JSONL (WR-01) | WARNING | Run ungradable if raw output holds U+2028/2029/0085 |
| `python/src/carimbo_evals/runner.py` | 255-260 | Harness errors not charged to the cap (WR-02) | WARNING | Cap bounds recorded spend only |
| `python/src/carimbo_evals/runner.py` | 147-156 | Uncaught error on corrupt middle line (WR-05) | WARNING | Traceback, exit code outside the documented set |
| `python/src/carimbo_datagen/cli.py` | 95-104 | `--case` overwrites the full manifest (WR-04) | WARNING | Partial manifest, confusing `datagen-check` |
| various | - | IN-01..IN-07 (dead enum member, 408/409 mapping, undisposed client, duplicated "1", negative totals accepted, mutable action tags, cwd-relative defaults) | INFO | Cosmetic or hardening |

No TBD/FIXME/XXX debt markers were found in the reviewed implementation files that lack a reference. All 13 review findings are still `open` in `01-REVIEW-DISPOSITION.md`.

### Human Verification Required

1. **First PR CI run.** Test: open the PR and watch jobs `dotnet`, `python`, `contract`. Expected: green, with the pinned action majors resolving. Why human: CI-01, nothing pushed yet.
2. **Agent-runtime discovery.** Test: ask "what is the check command?" in Claude Code and OpenCode. Expected: both answer from `AGENTS.md`. Why human: REPO-04 needs interactive runtimes.
3. **Fresh clone and non-Nix path.** Test: clone, run `devenv shell -- just check`; separately follow README "Quick start without Nix". Expected: exit 0. Why human: REPO-03, only the working copy was exercised.

### Gaps Summary

The phase achieved its goal in the common case: seeded DANFEs are generated byte-reproducibly, extracted live through a locked-down .NET endpoint with real cost and trace ids, and graded offline from Python; the gate is green and a recorded live run proves the loop end to end. It does not meet SC4's literal contract on one model-controlled path (CR-01: oversized `total_amount` leaks an `OverflowException` into an HTTP 500 after a paid call), and "schema-valid" is not enforced in .NET for pattern-bearing fields (WR-03). Both sit in the Domain/Extraction layers that Phase 2 will build the validators and repair loop on, so they should be closed before Phase 2 planning, in a small `--gaps` plan: `TryParse` in `Money.Parse`, post-deserialization pattern validation (or an explicit documented deferral), regression tests in `DomainTests` and `ExtractorTests`, and ideally WR-02 (charge harness errors to the cost cap) and WR-01 (split JSONL on `\n`) in the same pass. CI-01, REPO-03 (fresh clone / non-Nix) and REPO-04 remain human items.

---

_Verified: 2026-10-08_
_Verifier: Claude (gsd-verifier)_
