---
phase: "03"
slug: "replayable-runs"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-10-15"
---

# Phase 03 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Api → Anthropic API | Paid calls; each retry may be billed |
| Api → Jaeger on loopback | Span export during the live run |
| Api process → local disk | The default-on cache writes entry files under the content root |
| Api process → OTLP endpoint | Span data leaves the process when an endpoint is configured |
| Cache directory on disk → CachingLlmGateway | Entry files are read back as model answers; a local file is less trusted than the provider response it replaces |
| Configuration → composition root | Llm:Cache:* and the environment decide whether stored answers can be served at all |
| Configuration → gateway | Llm:Anthropic:MaxRetries multiplies attempts |
| Container registry → local Docker | The image is pulled from a public registry |
| Developer shell → Anthropic API via `just skeleton` | Paid calls with the provider key |
| Eval client → overrides | Authenticated but untrusted choices of model, prompt and repair budget |
| Eval client → POST /eval/extractions | Authenticated but untrusted JSON, now with an overrides object |
| Eval response → runner cost cap | The runner trusts the endpoint's cost fields to bound paid spend |
| Host network → Jaeger container | The UI and OTLP receivers have no authentication |
| Inbound traceparent → server span | Client-chosen trace ids are honoured |
| NuGet registry → build | Four new packages |
| Override → paid provider calls | A model choice and a repair budget multiply spend |
| Request fields → cache key | Request-derived values feed the key and the file name |
| Runner CLI → run directory | Flags decide the overrides recorded with every case |
| Run output → git | Copied run directories become committed evidence |
| Run records → published summaries | Summaries become README evidence |
| Test and developer runs → shared cache directory | A default directory could let one run answer another's requests |
| Test output → committed fixtures | Generated files enter git and are read by a second stack |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-03-01 | Tampering | LlmCacheStore.TryRead (poisoned or corrupt entry) | medium | mitigate | Entries carry entry_version and key; a mismatch, unparseable JSON or IO error is a miss; content is only parsed as data, never executed; the directory is local and git-ignored; Task 2 tests the three malformed shapes | closed |
| T-03-02 | Tampering | file name derived from request input (path traversal) | high | mitigate | The file name is only the 64-hex SHA-256 digest validated by ^[0-9a-f]{64}$ before any path is built; model, salt and case id never reach a path | closed |
| T-03-03 | Elevation of privilege | cache configured outside Development or Eval | high | mitigate | No cache is registered outside Development and Eval, and startup throws InvalidOperationException there when Llm:Cache:Directory or a Mode other than off is set; CacheConfigurationTests asserts Production and Staging | closed |
| T-03-04 | Information disclosure | entry files | low | accept | Entries hold model output of synthetic invoices only (D-14) in git-ignored .cache/; prompt, schema, document bytes, key and base URL are never stored (Task 2 test asserts the sentinels are absent) | closed (accepted) |
| T-03-05 | Repudiation | a replay presented as a fresh call | medium | mitigate | A hit reports HttpAttempts 0 and Cache.Status Hit; only end_turn answers are stored, so a truncated or refused outcome can never be replayed as an answer | closed |
| T-03-06 | Information disclosure | Jaeger UI and OTLP ports | high | mitigate | Every port bound to 127.0.0.1 in compose.yaml; the static pytest fails on any other binding; spans carry no content anyway (D-31) | closed |
| T-03-07 | Tampering | floating container image | medium | mitigate | Image pinned to an explicit 2.x.y tag from the official cr.jaegertracing.io registry; the static pytest rejects ":latest" | closed |
| T-03-08 | Tampering | overrides.replicate_salt reaching a path or the cache key unvalidated | medium | mitigate | Salt limited to ^[A-Za-z0-9._-]{1,64}$ at the endpoint and in the CLI; the cache file name is the key digest only (D-27) | closed |
| T-03-09 | Elevation of privilege | overrides.cache_mode enabling a cache the host does not have | high | mitigate | Any mode but off is 400 without a registered LlmCacheSettings; LlmCacheSettings exists only in Development or Eval and never when Llm:Cache:Mode is off (03-03 startup guard); the route itself is absent in Production | closed |
| T-03-10 | Information disclosure | 400 responses echoing the payload | low | mitigate | Invalid() names the field with a fixed message; the sentinel test asserts the submitted value is absent from the body | closed |
| T-03-11 | Repudiation | a run directory mixing cache configurations | medium | mitigate | request.overrides recorded per case and in run.json; resume refuses different overrides (RunConfigMismatchError, exit 2) | closed |
| T-03-12 | Denial of service (wallet) | stacked retries multiplying paid attempts | high | mitigate | Single owner (SDK MaxRetries), bounded 0..5 at startup; composed-chain test shows 1 + MaxRetries attempts; static guard rejects resilience packages and extra DelegatingHandlers | closed |
| T-03-13 | Denial of service (wallet) | retrying non-transient 4xx | medium | mitigate | SDK does not retry 400, 401, 404; matrix test pins it; refusals and truncations are 200 answers and never retried | closed |
| T-03-14 | Information disclosure | provider key in retry logs or exceptions | high | mitigate | Gateway messages carry type name and status only (existing Classify); RetryConfigurationTests asserts no log line contains the dummy key | closed |
| T-03-15 | Denial of service (wallet) | cost cap undercounting real spend | high | mitigate | Incurred is 0 only for a cache hit or a cache_miss (provably no provider call); unpriced and unknown failures stay null and are charged the reserve; records without the field fall back to cost_usd | closed |
| T-03-16 | Repudiation | summaries misreporting spend or quality provenance | medium | mitigate | Incurred and notional reported side by side with the hit rate; per-case cache_hits and cache_mode recorded; field grading unchanged so GRADER_VERSION stays comparable | closed |
| T-03-17 | Information disclosure | span attributes and events | high | mitigate | Allowlist owned by LlmTelemetry; TracingGatewayTests assert key subset and absence of prompt, response, schema, document and base64 sentinels; no exception events | closed |
| T-03-18 | Information disclosure | OTLP export to an unintended endpoint | medium | mitigate | Exporter added only when OTEL_EXPORTER_OTLP_ENDPOINT is set, logged at startup; compose binds the receiver to loopback (03-02); spans carry metadata only | closed |
| T-03-19 | Spoofing | inbound traceparent choosing the trace id | low | accept | Trace ids only correlate eval records with traces in a dev or eval host behind the static key; no authorization decision depends on them | closed (accepted) |
| T-03-20 | Denial of service | always-on span creation overhead (AlwaysOn sampler, D-31) | low | accept | Every request is recorded whatever its inbound sampled flag or OTEL_TRACES_SAMPLER: one span per model call plus one server span per request, negligible next to a model call; spans are exported only when OTEL_EXPORTER_OTLP_ENDPOINT is set (T-03-18) | closed (accepted) |
| T-03-21 | Denial of service (wallet) | expensive model or large repair budget per request | high | mitigate | Model must be priced by the pricing table (Haiku 4.5, Sonnet 5.5 today); max_repairs bounded by ExtractionSettings.MaxRepairsLimit 5 at the edge and in the extractor; runner caps incurred spend (03-06); live recipes keep US$ caps | closed |
| T-03-22 | Tampering | prompt selection used to inject instructions | medium | mitigate | Only catalogued versions are selectable; texts are compiled in and pinned by SHA-256; no prompt text crosses the HTTP boundary | closed |
| T-03-23 | Repudiation | an unpriced model hiding spend behind a null cost | high | mitigate | CanPrice allowlist rejects unpriced models before any call, so every override's cost is priced and capped | closed |
| T-03-24 | Information disclosure | key-shaped strings or local paths in fixtures | medium | mitigate | Scenarios use the test key only in headers, never in bodies; pytest asserts no fixture contains the eval key, "sk-ant-" or "X-Api-Key"; `just secrets-check` runs in `just check`; temp directory paths never reach a response (the key is a digest) | closed |
| T-03-25 | Tampering | fixture drift hiding a breaking contract change | medium | mitigate | Compare mode in `just dotnet-check` fails on any byte difference, missing or extra file; strict Pydantic (extra forbid) fails pytest on any added or renamed member | closed |
| T-03-26 | Denial of service (wallet) | live seeding run | high | mitigate | Blocking human decision before any spend; runner cap US$1.00 with incurred-cost accounting; one seeding run; Phase 3 cap US$5 checked in the SUMMARY; the replay is read_only and cannot call the provider | closed |
| T-03-27 | Information disclosure | provider or eval key in run files or published results | high | mitigate | Ephemeral eval key minted by the recipe; the runner never serializes headers; `just secrets-check` after copying; key presence checked with set/unset only | closed |
| T-03-28 | Information disclosure | content in exported traces | medium | mitigate | Allowlisted span attributes (03-07); the verify script asserts no span attribute contains the case's raw output; Jaeger bound to loopback | closed |
| T-03-29 | Information disclosure | default cache directory committed to git | low | mitigate | The default .cache/llm sits under the existing .cache/ ignore rule, which matches at any depth; Task 3's git check-ignore verify covers the repo root, the Api project directory and a test output directory; entries hold synthetic data only (D-14) | closed |
| T-03-30 | Repudiation | a stale default cache answering where a fresh model call was expected | medium | mitigate | Every hit reports http_attempts 0 and cache status hit (03-01); the startup log names the mode and full directory; TestHost runs with the cache off unless a test passes its own directory and every e2e host gets a per-test directory, so no test result depends on entries left by another run | closed |
| T-03-SC (01) | Tampering | package installs | low | mitigate | No package is added in this plan | closed |
| T-03-SC (02) | Tampering | package installs | low | mitigate | No NuGet, PyPI or npm package added; the only new artifact is a pinned container image (T-03-07) | closed |
| T-03-SC (03) | Tampering | package installs | low | mitigate | No package is added in this plan | closed |
| T-03-SC (04) | Tampering | package installs | low | mitigate | No package added | closed |
| T-03-SC (05) | Tampering | package installs | low | mitigate | No package added; the static guard forbids resilience packages | closed |
| T-03-SC (06) | Tampering | package installs | low | mitigate | No package added | closed |
| T-03-SC (07) | Tampering | NuGet installs (OpenTelemetry.Extensions.Hosting 1.19.1, Exporter.OpenTelemetryProtocol 1.19.1, Instrumentation.AspNetCore 1.19.0, Exporter.InMemory 1.19.1) | high | mitigate | Owner "OpenTelemetry" verified, hundreds of millions of downloads, repo open-telemetry/opentelemetry-dotnet (RESEARCH audit); exact pins through central package management; the acceptance count of PackageVersion entries rejects any extra package; the legitimacy gate does not cover NuGet, so no human checkpoint is inserted | closed |
| T-03-SC (08) | Tampering | package installs | low | mitigate | No package added | closed |
| T-03-SC (09) | Tampering | package installs | low | mitigate | No package added (Pydantic is already pinned in uv.lock) | closed |
| T-03-SC (10) | Tampering | package installs | low | mitigate | No package added | closed |
| T-03-31 | Information disclosure | carimbo.llm.repair_prompt_version span attribute | low | mitigate | Value set only from the code-owned LlmRequest.RepairPromptVersion label (TracingLlmGateway.cs:37-39); AllowedTags grows by exactly this name (LlmTelemetry.cs:45,74); TracingGatewayTests content-sentinel test runs with a repair version set | closed |
| T-03-32 | Repudiation | model-call attribution in the trace | low | mitigate | TracingWiringTests asserts each span's call label equals attempts[i].prompt_version of the same response, default prompt and extract-002 override (D-33) | closed |
| T-03-SC (11) | Tampering | package installs | low | mitigate | No package added; Directory.Packages.props still has 7 PackageVersion entries | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above workflow.security_block_on count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

Evidence for every row (file:line and test names) is in the gsd-security-auditor verdict of 2026-10-15; each mitigation was checked in code and backed by passing offline tests (Llm 138, Api 198, Extraction 172, targeted pytest 231) and a live Jaeger lookup of the 03-10 seeding traces.

### Residual notes (non-blocking, candidates for a later fix)

1. **T-03-01:** a cache entry with a valid version and key but `null` in a required member (`text`, `model_requested`) is read as a hit (`LlmCacheStore.Parse` uses `GetString()!`), which likely surfaces as a 500 rather than a miss. Exploiting it needs write access to the local, git-ignored cache directory. Treating null required members as a miss would close the edge.
2. **T-03-18:** the OTLP exporter also reads `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`; the exporter switch and startup log use `OTEL_EXPORTER_OTLP_ENDPOINT` only, so with both set the log line may not name the actual destination.
3. **T-03-06:** the static compose test only parses quoted short-syntax port mappings; an unquoted or long-syntax `ports:` entry would not be checked. The current file is loopback-only.

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-03-01 | T-03-04 | Entries hold model output of synthetic invoices only (D-14), in the git-ignored .cache/; the serializer writes no prompt, schema, document bytes, key or base URL (verified: An_entry_never_contains_the_prompt_the_schema_or_the_document). | Phase 3 plan threat model (disposition accept), confirmed by security audit | 2026-10-15 |
| AR-03-02 | T-03-19 | Trace ids only correlate eval records with traces on a dev/eval host behind the static X-Api-Key; no authorization decision uses the trace id (verified EvalEndpoint.cs:79-84). | Phase 3 plan threat model (disposition accept), confirmed by security audit | 2026-10-15 |
| AR-03-03 | T-03-20 | AlwaysOn sampling (D-31) records one span per request on a dev/eval host; export happens only when OTEL_EXPORTER_OTLP_ENDPOINT is set. | Phase 3 plan threat model (disposition accept), confirmed by security audit | 2026-10-15 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-10-15 | 40 | 40 | 0 | gsd-security-auditor (opus), ASVS L1, block_on high |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-10-15

## Security Audit 2026-10-15

| Metric | Count |
|---|---|
| Threats found | 3 |
| Closed | 3 |
| Open | 0 |
