---
phase: "1"
slug: "walking-skeleton"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-10-08"
---

# Phase 1 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.
> Register authored at plan time (all 16 plans, gap closure 01-14..01-16 included, carry a `<threat_model>`). Verified at ASVS L1 (grep depth) against the implementation on 2026-10-08, plus the per-plan SUMMARY threat flags (none raised new surface).

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| Package registries → repo | PyPI and NuGet installs, GitHub Actions | Third-party code (supply chain) |
| Python runner → .NET eval endpoint (localhost) | `POST /eval/extractions` with static eval key | Synthetic PDFs (base64), eval key header |
| .NET gateway → Anthropic Messages API (paid, keyed) | Live model calls | Synthetic PDFs, prompt, schema, provider key |
| Model output → Domain parser | Untrusted JSON parsed into `Invoice` | Model-generated JSON |
| Run outputs / spike evidence → committed repo | Docs, fixtures, schemas, data/skeleton | Public repository content |
| CI (pull_request) → repo | Untrusted PR code on GitHub runners | Read-only token, no secrets |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-01-01 | Tampering | hatchling build backend (fetched at build time, not in uv.lock) | medium | mitigate | Exact `hatchling==1.32.4` in `[build-system]`, covered by the same checkpoint | closed |
| T-01-02 | Information disclosure | `.env` files or local secrets committed | high | mitigate | `.gitignore` excludes `.env` and `.env.*`; plan 01-13 adds a secrets-check recipe to CI | closed |
| T-01-03 | Tampering | Money parsing (Wire.cs) | high | mitigate | Pattern check before `decimal.Parse` with restricted NumberStyles and InvariantGlobalization, so "12,34" is rejected instead of read as 1234. Tests land in 01-04 | closed |
| T-01-04 | Tampering | Wire.Options deserialization of model output | medium | mitigate | `UnmappedMemberHandling.Disallow`, required constructor parameters and nullable annotations respected, so unknown, missing or null members raise JsonException and become a typed schema_invalid outcome | closed |
| T-01-05 | Elevation of privilege | POST /eval/extractions (Program.cs) | high | mitigate | Route mapped only in Development/Eval with a configured eval key and a registered gateway; X-Api-Key required (401); Kestrel default bind 127.0.0.1 | closed |
| T-01-06 | Information disclosure | eval key comparison | medium | mitigate | SHA-256 both values, then `CryptographicOperations.FixedTimeEquals`; no echo of the key in any response or log | closed |
| T-01-07 | Tampering | prompt (ExtractionContract) | medium | mitigate | Prompt states the document is data and printed instructions are ignored; output constrained to the schema and strictly parsed; anything else becomes schema_invalid. Adversarial cases are Phase 4 | closed |
| T-01-08 | Information disclosure | runner JSONL / summary | high | mitigate | Runner never serializes request headers; e2e asserts the key string is absent from cases.jsonl and summary.json | closed |
| T-01-09 | Elevation of privilege | ScriptedHost | low | accept | Lives under dotnet/tests, is never referenced by Carimbo.Api, and refuses to start without an explicit responses directory | closed |
| T-01-10 | Tampering | schema/invoice.schema.json drift | medium | mitigate | Snapshot test byte-compares a fresh export with the committed file; `SchemaExport --check` for CI (01-13) | closed |
| T-01-11 | Repudiation | docs/DECISIONS.md history | low | mitigate | Append-only superseding entries; acceptance requires zero deletions in the diff | closed |
| T-01-12 | Tampering | model-facing schema (ExtractionContract) | medium | mitigate | Pure projector strips unsupported keywords; budget enforced at contract construction and in tests; committed file equals sent bytes (snapshot test); wire-level equality asserted again on the real adapter in 01-12 | closed |
| T-01-13 | Information disclosure | generated parties (spec.py) | high | mitigate | Own CNPJ generator, collision test against nfelib sample CNPJs, visible SINTETICA marker, tpAmb 2 and a "SEM VALOR FISCAL" note. Residual: a check-digit-valid synthetic CNPJ could coincide with a real one; the names and markers make the DANFE unmistakably synthetic (documented limitation) | closed |
| T-01-14 | Tampering | parsing nfelib sample XML in tests | low | accept | Package-bundled trusted files read only in tests; nothing is written from them | closed |
| T-01-15 | Tampering | data/skeleton bytes | medium | mitigate | Fixed creation date, per-case RNG, manifest SHA-256, committed-vs-regenerated test in the default suite, and the `check` command for CI | closed |
| T-01-16 | Tampering | git line-ending normalization of PDFs | medium | mitigate | `.gitattributes` marks `*.pdf binary` (01-01); verified with `git check-attr` | closed |
| T-01-17 | Denial of service | request body (EvalEndpoint) | medium | mitigate | 10 MB limit via RequestSizeLimit plus an explicit Content-Length check → 413, tested | closed |
| T-01-18 | Tampering | request payload validation | medium | mitigate | Strict DTO binding (unknown fields rejected), base64 check, `%PDF-` magic check, media type and contract version checks → 400, tested | closed |
| T-01-19 | Elevation of privilege | endpoint availability | high | mitigate | Route mapped only in Development/Eval with a configured key and a registered gateway; Production → 404, tested | closed |
| T-01-20 | Information disclosure | provider request content | medium | mitigate | The extractor signature has no case id; a test captures the LlmRequest and asserts it holds only the PDF, prompt and schema | closed |
| T-01-21 | Information disclosure | eval key in CLI or run files | high | mitigate | Key accepted only from the env var (no CLI option); runner never serializes headers; tests assert absence in cases.jsonl, run.json and summary files | closed |
| T-01-22 | Denial of service (wallet) | runner dispatch | high | mitigate | Decimal cost cap with reserve, tested at the boundary, exit code 3; default US$1.00 per run (D-09) | closed |
| T-01-23 | Tampering | offline grading purity | low | mitigate | Grader and summary import no HTTP client; subprocess test with socket disabled | closed |
| T-01-24 | Information disclosure | provider key in docs, fixtures or stdout | high | mitigate | Key read from env and passed explicitly; never printed; headers and request bodies never written; fixture text redacted; verify greps for key-shaped strings, auth header names and base64 PDF bytes | closed |
| T-01-25 | Denial of service (wallet) | live spike spend | medium | mitigate | Conservative pre-call estimate with `--budget-usd 1.00`; steps skipped rather than exceeding; spend recorded | closed |
| T-01-26 | Information disclosure | document content in committed docs | low | mitigate | Only shapes, counts and parse outcomes are recorded; model text and PDF bytes are never written (synthetic data anyway) | closed |
| T-01-27 | Repudiation | cost accounting (LlmPricing) | medium | mitigate | Decimal-only arithmetic, all five token classes, exact-value tests, versioned table recorded in every response, unknown models reported as null plus warning (never zero) | closed |
| T-01-28 | Information disclosure | provider key in logs and exception messages | high | mitigate | Key passed explicitly from the resolver; exception messages built from type and status only; no HTTP or body logging; tests assert the dummy key is absent from messages and logs | closed |
| T-01-29 | Elevation of privilege | SDK reading the default env var implicitly (Claude Code's own key in this environment) | medium | mitigate | D-10 resolution order is explicit; the client is always given ApiKey; resolver behaviour tested | closed |
| T-01-30 | Denial of service (wallet) | stacked retries | low | mitigate | One retry owner per D-21; MaxRetries configured once; attempts counted per call and reported | closed |
| T-01-31 | Tampering | schema rewritten by the adapter | medium | mitigate | Wire test deep-compares the sent schema with the committed model-facing schema | closed |
| T-01-32 | Information disclosure | CI workflow | high | mitigate | Plain `pull_request` trigger only, `permissions: contents: read`, no `secrets.` references; asserted by test_repo_layout and a negative grep | closed |
| T-01-33 | Information disclosure | committed secrets | high | mitigate | `just secrets-check` (git grep for key-shaped strings) runs in CI's contract job; `.gitignore` covers `.env*` (01-01) | closed |
| T-01-34 | Denial of service (wallet) | `just skeleton` | medium | mitigate | Provider-key precondition, runner cap 1.00 USD per run (exit 3 on cap), one run in this plan, cumulative spend tracked in SUMMARYs against the US$5 cap | closed |
| T-01-35 | Elevation of privilege | Api during the skeleton run | medium | mitigate | Bound to 127.0.0.1, ephemeral per-run eval key, Development environment only, process killed by a trap | closed |
| T-01-36 | Information disclosure | key comparison and logging | medium | mitigate | Hardens T-01-06 from 01-03: fixed-time compare over hashes, stateless; tests assert the key never appears in responses or logs | closed |
| T-01-37 | Information disclosure | Live recipes and secretspec fallback | high | mitigate | `_with-provider-key` tests presence only and never prints the key; `secretspec run` injects the key only into the wrapped command's process tree; SECRETSPEC_REASON makes agent access attributable; `devenv shell` never loads secrets; `secretspec.toml` holds declarations only. Asserted by the Task 1 negative grep, test_repo_layout and secrets-check | closed |
| T-01-38 | Denial of service (wallet) | Money.Parse, MoneyJsonConverter.Read, InvoiceExtractor.Parse | high | mitigate | Gap closure 01-14: `decimal.TryParse` in `Money.Parse`, `OverflowException` mapped in the converter and the extractor, so an oversized amount is HTTP 200 `schema_invalid` with `cost_usd`; tests at endpoint, extractor and Domain level | closed |
| T-01-39 | Tampering (typed-outcome integrity) | InvoiceExtractor success path | medium | mitigate | Gap closure 01-14: `Invoice.PatternViolations()` enforced before Success, `Patterns.IsFullMatch` closes the .NET `$` trailing-newline gap, schema-driven drift test | closed |
| T-01-40 | Elevation of privilege (bug masking) | InvoiceExtractor.Parse exception handling | medium | mitigate | Gap closure 01-14: only `JsonException`, `FormatException`, `OverflowException` map to schema_invalid; no catch-all | closed |
| T-01-41 | Information disclosure | SchemaInvalid error text | low | accept | See AR-01-04 | closed |
| T-01-42 | Denial of service (CPU) | Regex evaluation in PatternViolations | low | accept | See AR-01-05 | closed |
| T-01-43 | Denial of service (wallet) | runner cost cap accounting | high | mitigate | Gap closure 01-15: `may_have_reached_provider` charges `reserve_usd` for possibly-paid harness errors, live and for every prior record on resume; `assumed_usd` reported; CLI exit-3 tracer test | closed |
| T-01-44 | Denial of service (grading availability) | grader `_read_records` | medium | mitigate | Gap closure 01-15: split on `"\n"` only; regression test with a raw U+2028 | closed |
| T-01-45 | Tampering (data loss) | runner `_load_existing` on resume | medium | mitigate | Gap closure 01-15: `CorruptRunError` before any truncation, CLI exits 2 naming the line, file bytes asserted unchanged | closed |
| T-01-46 | Information disclosure | `http.error_type` field and CLI summary | low | accept | See AR-01-06 | closed |
| T-01-47 | Information disclosure | e2e run directory and host logs | low | mitigate | Gap closure 01-16: ephemeral key per test, asserted absent from every file under the run directory; host binds 127.0.0.1 | closed |
| T-01-48 | Repudiation | 01-REVIEW-DISPOSITION.md | low | mitigate | Gap closure 01-16: each fixed row cites plan and commit SHA, verified with `git cat-file -e` | closed |
| T-01-SC | Tampering | pip/uv installs (python/uv.lock) | high | mitigate | Exact pins in pyproject, resolved lock committed, a blocking-human checkpoint (Task 2) before the first install, PyPI-only sources asserted by grep, `uv sync --locked` refuses drift. Gap plans 01-14..01-16 change no package, lockfile or project file (`git diff 053b2f0..` empty on them) | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above workflow.security_block_on (high) count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

L1 evidence (git grep, 2026-10-08): `.gitignore` env rules (T-01-02); `NumberStyles` in `Wire.cs` (T-01-03); `UnmappedMemberHandling.Disallow` (T-01-04); environment gating in `Carimbo.Api` (T-01-05/19); `FixedTimeEquals` (T-01-06/36); key-absence asserts in `test_e2e_fake.py`/`test_runner.py` (T-01-08/21); `SINTETICA` marker (T-01-13); `RequestSizeLimit`/413 (T-01-17); cost cap and reserve in `runner.py` (T-01-22); options `ToString` and explicit `ApiKey` in `Carimbo.Llm` (T-01-28/29); wire schema test against `invoice.model.schema.json` (T-01-31); `contents: read`, no `pull_request_target`, no `secrets.` in `ci.yml` (T-01-32); `secrets-check` in justfile and CI (T-01-33); `127.0.0.1` bind (T-01-35); `uv sync --locked` (T-01-SC); `hatchling==` pin (T-01-01); `SECRETSPEC_REASON` (T-01-37); `git check-attr` binary on PDFs (T-01-16); secret-shape grep clean on `docs/spikes` and fixtures (T-01-24). Gap closure (2026-10-08): `decimal.TryParse` in `Wire.cs` and `catch (OverflowException` in `Wire.cs`/`InvoiceExtractor.cs` (T-01-38); `PatternViolations`/`IsFullMatch` in `Invoice.cs` (T-01-39); extractor catches limited to `LlmGatewayException`, `JsonException`, `FormatException`, `OverflowException` (T-01-40); `may_have_reached_provider`/`assumed_usd` in `runner.py` (T-01-43); `.split("\n")` in `grader.py` (T-01-44); `CorruptRunError` in `runner.py`/`cli.py` (T-01-45); `api_key not in` run-file asserts in `test_e2e_fake.py` (T-01-47); SHA-cited fixed rows in `01-REVIEW-DISPOSITION.md` (T-01-48). Remaining mitigations are covered by the green `just check` suite (schema snapshot, datagen regeneration, pricing, endpoint and gateway tests) and the plan SUMMARY threat-flag records.

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-01-01 | T-01-09 | ScriptedHost lives under `dotnet/tests`, is never referenced by `Carimbo.Api`, and refuses to start without an explicit responses directory | plan 01-03 (threat model disposition) | 2026-10-04 |
| AR-01-02 | T-01-14 | nfelib sample XMLs are package-bundled trusted files, read only in tests; nothing is written from them | plan 01-06 (threat model disposition) | 2026-10-04 |
| AR-01-03 | T-01-13 (residual) | A check-digit-valid synthetic CNPJ could coincide with a real one; mitigated by SINTETICA marker, tpAmb 2 and "SEM VALOR FISCAL", residual documented in plan 01-06 | plan 01-06 | 2026-10-04 |
| AR-01-04 | T-01-41 | SchemaInvalid errors name JSON paths and, for money, echo the model's own value, which the response already returns as raw_output; no key, PDF bytes or eval metadata added | plan 01-14 (threat model disposition) | 2026-10-08 |
| AR-01-05 | T-01-42 | Anchored character-class patterns with no nested quantifiers (no catastrophic backtracking); output bounded by max_tokens 4096 | plan 01-14 (threat model disposition) | 2026-10-08 |
| AR-01-06 | T-01-46 | Only an exception class name and a decimal amount are added; request headers never serialized; key-absence tests keep guarding the eval key | plan 01-15 (threat model disposition) | 2026-10-08 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-10-08 | 38 | 38 | 0 | gsd-secure-phase (orchestrator, ASVS L1 short-circuit) |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-10-08

## Security Audit 2026-10-08

| Metric | Count |
|---|---|
| Threats found | 49 |
| Closed | 49 |
| Open | 0 |
