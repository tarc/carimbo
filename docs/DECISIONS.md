# Decisions log: invoice-agent

> Decisions already made, with rationale and rejected alternatives. Use during
> discuss phases. A planner must not reverse a decision here without raising
> it explicitly; record any change as a new entry that supersedes the old one.

Format: each entry lists the phase where it first applies.

---

## D-01 Language split: .NET pipeline, Python evals and data
**Phase:** 1

**Decision:** Production pipeline in .NET. Dataset generation, eval harness,
reports and analysis in Python.

**Rationale:** .NET is the author's strongest stack, so the production-grade
parts get built fastest and best. Python has the strongest tooling for data
work and analysis, and demonstrates Python fluency. The split mirrors how many
companies separate production services from eval and data work.

**Rejected:** All-Python (gives up the author's main strength). All-.NET
(weaker analysis tooling; acceptable fallback if time runs short).
Splitting by component for its own sake (two languages with no real boundary).

---

## D-02 Evals call the real pipeline over HTTP
**Refined by:** D-24 (2026-10-08)
**Phase:** 3, 4

**Decision:** The Python harness calls a synchronous eval endpoint on the .NET
service. Python never reimplements extraction, validation or agent logic.

**Rationale:** Eval results must describe the code that runs in production.
A protocol boundary keeps the split clean.

**Detail:** The eval endpoint bypasses Temporal and intake idempotency but
runs the same extraction, validation and agent code. It returns the result,
validator outcomes, attempts, tool calls, tokens, cost, latency and trace ID.

---

## D-03 Schema source of truth is C#
**Superseded in part by:** D-18 (2026-10-04)
**Also refined by:** D-22 (2026-10-08)
**Phase:** 1

**Decision:** C# domain records are the source of truth. JSON Schema is
exported from them and committed. Python models are generated from that
schema; CI fails if the committed schema is stale.

**Rationale:** One schema, used as the model's output contract, the
validation target and the eval ground truth format. No hand-synced copies.

---

## D-04 Domain layer is pure
**Phase:** 1

**Decision:** `Domain` contains records and pure functions only. No I/O, no
model calls, no framework dependencies.

---

## D-05 Deterministic before probabilistic
**Phase:** 1, 3, 7

**Decision:** Anything that can be checked or obtained deterministically is,
and the model is never trusted for it alone:
- CNPJ check digits (mod 11).
- Access key (44 digits) check digit.
- Cross-check: the access key embeds the issuer CNPJ, issue year-month, model,
  series and number; these must agree with the extracted fields.
- Line items sum to totals; tax amounts consistent with bases and rates.
- At intake, decode the DANFE Code 128 barcode to get the access key without
  any model call when possible.

**Rationale:** Regulated finance needs hard guarantees. Deterministic checks
are also cheap, free graders for evals.

---

## D-06 Validators return structured errors; bounded repair loop
**Refined by:** D-23 (2026-10-08)
**Phase:** 3

**Decision:** Validators return a list of typed errors (field, rule, expected,
actual), never throw. On failure, extraction retries with the errors fed back
to the model, up to a configured maximum (default 2 repair attempts). If still
invalid, the result is a typed failure that the workflow escalates.

**Rationale:** Structured errors make good repair prompts and good eval data.
Bounding attempts bounds cost and latency.

---

## D-07 LLM gateway abstraction with request-hash cache
**Superseded in part by:** D-19 (2026-10-04)
**Phase:** 3

**Decision:** All model calls go through a gateway that handles retries with
backoff, token and cost accounting, and an OpenTelemetry span per call. A
response cache keyed by a hash of the normalized request (model, parameters,
messages, schema) is enabled in dev and eval, disabled in the production path.

**Rationale:** The cache makes rerunning evals after grader changes nearly
free. Centralizing accounting enables per-invoice cost attribution.

---

## D-08 Hand-written agent loop
**Refined by:** D-26 (2026-10-15)
**Phase:** 6

**Decision:** The agent is an explicit loop (call model, execute tool, append
result, repeat) with a step budget and a typed final `Decision`. No agent
framework.

**Rationale:** Small (about 100 lines), fully understood, easy to test with a
fake model, and the author can explain every line in an interview.

**Rejected:** Microsoft Agent Framework, Semantic Kernel agents (hide the loop
being demonstrated).

---

## D-09 The agent decides, the workflow acts
**Refined by:** D-26 (2026-10-15)
**Phase:** 5, 6, 7

**Decision:** MCP tools are read-only. The agent's output is a `Decision`
(approve / reject / escalate, reason, evidence). Executing the decision is a
separate workflow activity, not a tool the agent can call.

**Rationale:** Side effects stay outside the non-deterministic component, so
they can be made idempotent, audited and gated. This is the safety boundary.

---

## D-10 Uncertainty escalates
**Refined by:** D-26 (2026-10-15)
**Phase:** 6

**Decision:** Step budget exhaustion, tool failures after retries, extraction
failure and near-duplicates all escalate to a human. The agent never rejects
on a near-duplicate; only exact business duplicates (same access key) are
blocked, and those by the system, not the agent.

**Rationale:** In finance, a false approval or a false rejection both cost
more than a human review.

---

## D-11 Temporal: deterministic workflows, everything else is an activity
**Phase:** 7

**Decision:** Workflow code only orchestrates. Every model call, tool call,
database write and side effect is an activity.

**Rationale:** Temporal replays workflow code, so it must be deterministic.

---

## D-12 Idempotency in three layers, plus side-effect keys
**Phase:** 7

**Decision:**

1. **Request layer:** `Idempotency-Key` header (IETF draft pattern). Stored
   with a hash of the request body. Same key and body: replay the stored
   response with `Idempotent-Replayed: true`. Same key, different body: 422.
   First request still in flight: 409. Keys expire after 24 hours.
   Claimed atomically with `INSERT ... ON CONFLICT DO NOTHING`, never
   check-then-insert.
2. **Content layer:** SHA-256 of uploaded bytes, unique index.
3. **Business layer:** NF-e access key, unique constraint on persisted
   invoices. Taken from the barcode at intake when decodable, otherwise
   enforced when the extracted invoice is persisted.

**Workflow ID:** `invoice-{accessKey}` when the barcode is decoded, else
`invoice-sha256-{hash}`. Conflict policy `UseExisting`; reuse policy
`RejectDuplicate`.

**Side effects:** every effectful activity uses a deterministic key such as
`{accessKey}:approve`, enforced by a unique constraint.

**Business duplicate response:** 409 with a link to the existing invoice, and
a recorded "duplicate submission" event.

**Rationale:** Each layer catches a different duplicate (client retry, double
upload, re-scan). Side-effect keys prevent double approval under activity
retries, which is where real double payments come from.

---

## D-13 Async intake
**Phase:** 7

**Decision:** `POST /invoices` returns `202 Accepted` with a `Location`
header; clients poll `GET /invoices/{id}`. Webhooks are out of scope.

---

## D-14 Synthetic data only
**Phase:** 2

**Decision:** All CNPJs, names and addresses are generated. No real invoices,
even scrubbed. Generation is seeded and reproducible; datasets are versioned.

---

## D-15 Eval artifacts and gating
**Superseded in part by:** D-20 (2026-10-04)
**Phase:** 4, 6

**Decision:** Each run writes one JSONL record per case (inputs reference,
outputs, grader scores, tokens, cost, latency, trace ID) plus a summary.
Summaries are committed under `evals/reports/`; full JSONL is not. CI runs a
fixed small subset on PRs and fails below configured thresholds per grader.

**Rationale:** Committed summaries give a visible quality history in git.
Trace IDs link any failing case to its full trace.

---

## D-16 LLM-as-judge is calibrated
**Phase:** 6

**Decision:** Judge-based graders (reason quality) are checked against a
human-labeled sample before being used for gating; agreement is reported in
the README.

**Rationale:** An unvalidated judge is an unmeasured metric.

---

## D-17 Observability with OpenTelemetry end to end
**Refined by:** D-31 (2026-10-15)
**Phase:** 3, 8

**Decision:** One trace per invoice spans API, workflow, activities, model and
tool calls. Spans carry token counts and cost. Eval records carry trace IDs.

---

## D-18 Canonical schema, model-facing projection and generated models (refines DECISIONS D-03)
**Phase:** 1

**Decision:**
- The canonical JSON Schema is exported from the C# records by the pure
  `CanonicalSchema` exporter and committed as `schema/invoice.schema.json`.
- A pure projector derives `schema/invoice.model.schema.json` (unsupported
  keywords stripped, `oneOf` rewritten to `anyOf`, `additionalProperties`
  false everywhere, `$schema` removed), and that file is byte-for-byte what is
  sent to the model.
- A test keeps it within 24 optional and 16 union properties, counting each
  `$ref` per use.
- Pydantic models are generated from the canonical schema with a pinned
  datamodel-code-generator.
- All three artifacts fail CI when stale.
- Money is a pattern-constrained decimal string on the wire.
- The extraction target is the DANFE-visible projection, which Phase 2
  completes.

**Rationale:** Provider schema limits would otherwise strip constraints
silently, and a measured-quality project must be able to name the exact
contract the model saw.

**Rejected:** One schema for every purpose; NJsonSchema (kept only as a
fallback); money as a JSON number.

---

## D-19 Request-hash cache keys, modes and reporting (refines DECISIONS D-07)
**Refined by:** D-27 (2026-10-15)
**Phase:** 3

**Decision:**
- The cache key is SHA-256 over the canonical final request: model,
  parameters, schema hash, prompt version, PDF SHA-256 and a replicate salt.
- Modes are read-write, read-only (replay) and refresh.
- Hits report the original latency and cost, and summaries show incurred
  versus notional cost and the hit rate.
- Truncated or refused responses are never cached.
- The production configuration runs with the cache off.

**Rationale:** Current models reject sampling controls, so the cache is the
reproducibility mechanism, and a cache that hides cost or variance would
corrupt measurements.

**Rejected:** The built-in distributed-cache chat client (no control over the
key, no original cost).

---

## D-20 Committed per-case scores and replay fixtures (refines DECISIONS D-15)
**Phase:** 6

**Decision:** Each published run commits `summary.json` plus a compact
per-case score table under `evals/reports/`; raw JSONL stays out of git as a CI
artifact; cache fixtures for the published run are committed so PR CI and
reviewers replay at no cost without an API key.

**Rationale:** Paired statistics and reviewer reproduction need per-case
results and replayable model outputs.

**Rejected:** Committing only aggregate summaries; committing raw JSONL.

---

## D-21 LLM gateway shape and retry ownership (LLM-06 outcome)
**Refined by:** D-28 (2026-10-15)
**Phase:** 1

**Decision:**
- The bottom adapter is the official Anthropic SDK used directly
  (`client.Messages.Create`) behind `ILlmGateway`, implemented once as
  `AnthropicLlmGateway` in `Carimbo.Llm`. Provider types never leave that
  project.
- Phase 1 sets the SDK `MaxRetries` to 0. One HTTP attempt per call keeps cost
  and latency exactly attributable under the US$5 cap (D-09). The adapter
  counts attempts per call through a handler and reports them, so a later
  retry setting stays visible.
- Phase 3 (LLM-01) owns the single retry policy, in one place, and must not
  stack a second one on top of the SDK's.
- Model ids: send the alias (for example `claude-haiku-4-5`) in development,
  always record both `model_requested` and `model_returned`, and pin the dated
  snapshot (`claude-haiku-4-5-20251001`) for published eval runs so a moved
  alias cannot change results silently. `claude-sonnet-5-5` returns no dated id
  and stays on its alias.
- Schema keywords: the live API accepted the model-facing schema unchanged
  (`$defs`/`$ref`, `pattern`, `format: date`, `title`), so no keyword is moved
  or stripped and there is no projector fallback. The adapter sends the
  committed `schema/invoice.model.schema.json` verbatim and never rewrites it.
- The request carries no sampling, tool-choice, thinking or cache-control
  fields in Phase 1.

**Rationale:** `docs/spikes/01-llm-gateway.md` (plan 01-10, live evidence).
Through `IChatClient` the usage decomposition is not lossless (the adapter sums
cache creation into the input count and hides the 5m/1h split, so uncached
input is recoverable only by subtraction), a refusal collapses to
`content_filter` with `stop_details` reachable only by casting the raw
representation back to the SDK type, and the request id is not visible. Step 7
showed a 4xx is never retried and a transient 5xx is retried only when
`MaxRetries` is raised. The decision rule picks the direct SDK unless the
`IChatClient` path is lossless on both, and it was lossless on neither. The
seam is `ILlmGateway`, so the choice is reversible without touching callers.

**Rejected:**
- `ichatclient-raw` (the `IChatClient` adapter with the raw representation
  factory): not lossless on usage or stop details, and it adds a mapping layer
  over the same SDK type.
- `direct-sdk-retries` (the direct SDK with SDK-owned retries in Phase 1):
  hides attempts and multiplies spend while retry policy is still undecided;
  Phase 3 decides it once.

---

## D-22 Invoice v2: the DANFE-visible extraction target (refines D-03 and D-18)
**Phase:** 2

Labels like "phase 2 CONTEXT D-07" below are phase-local ids from
`.planning/phases/02-validated-extraction/02-CONTEXT.md`, not repo decisions.

**Decision:**
- The record set and field names are fixed by plan 02-01: `Invoice`; `Party`
  for the issuer (`cnpj`, `name`, `ie`, `uf`); `Recipient` (`tax_id`,
  `tax_id_kind` cnpj|cpf, `name`, `ie`, `uf`); `LineItem` with 14 columns;
  `Totals` with 11 boxes; `Installment` (`number`, `due_date`, `amount`).
- Every field is required. Only the two `ie` fields are nullable, so the
  model-facing schema has 0 optional and 2 union properties against the 24 and
  16 limits.
- The Phase 1 top-level `total_amount` is removed with no alias. The invoice
  total lives only at `totals.invoice_total`.
- Wire decimals: `Money` (two decimals, signed), `Decimal4` (four decimals,
  unsigned) for `quantity` and `unit_price`, `Rate` (two decimals, unsigned)
  for `icms_rate` and `ipi_rate`. A blank or zero tax column is `0.00`.
- Patterns `Cnpj`, `TaxId` (11 digits or the CNPJ form), `Uf`, `Ncm`, `Cfop`
  and `CstCsosn` use explicit `[0-9]` classes.
- The regime is inferred from the length of `cst_csosn` (3 digits Normal, 4
  digits Simples). There is no `tax_regime` field.
- Half-up means half away from zero to two decimals, in C# and in Python, with
  no negative zero. `data/vectors/validator-vectors.json` specifies it and both
  stacks test against that one file.
- The XML to DANFE to Invoice mapping is `docs/DANFE-MAPPING.md`, implemented
  once in .NET and followed by the Python grader reader.
- The skeleton cases are reworked (phase 2 CONTEXT D-16) and the dataset
  manifest carries an `as_of_date`.

**Rationale:** The extraction target must be everything the DANFE shows, or
the validators have nothing to cross-check and the eval measures a toy. Each
choice keeps the schema inside the provider limits and keeps the ground truth
derivable from the XML by documented rules.

**Rejected:**
- An alias for the old top-level total: double emission plus a consistency
  rule to keep the two equal.
- A `tax_regime` field (phase 2 CONTEXT D-07): not printed on the DANFE, so the
  model would guess it.
- `Money` for quantities and unit prices: drops printed digits.
- Optional tax fields: they spend the optional-property budget for no gain.

---

## D-23 Deterministic validation and bounded repair (refines D-05 and D-06)
**Refined by:** D-25 (2026-10-08), D-29 (2026-10-15)
**Phase:** 2

**Decision:**
- Validators return findings `{field, rule_id, expected, actual, severity}`,
  severity `error` or `warning`, with stable UPPER_SNAKE rule ids listed in
  `Carimbo.Validation`: CNPJ_FORMAT, CNPJ_CHECK_DIGIT, CPF_FORMAT,
  CPF_CHECK_DIGIT, TAX_ID_KIND_MISMATCH, UF_UNKNOWN, KEY_FORMAT,
  KEY_CHECK_DIGIT, KEY_UF_MISMATCH, KEY_ISSUER_CNPJ_MISMATCH,
  KEY_YEAR_MONTH_MISMATCH, KEY_MODEL_MISMATCH, KEY_SERIES_MISMATCH,
  KEY_NUMBER_MISMATCH, DATE_PLAUSIBLE, DUE_DATE_ORDER, ITEMS_EMPTY, ITEM_ARITH,
  REGIME_CODE_MISMATCH, TAX_CODE_UNSUPPORTED, TAX_ARITH_ICMS,
  TAX_NOT_TAXED_AMOUNT, TAX_ARITH_IPI, TOTAL_SUM_PRODUCTS, TOTAL_SUM_ICMS_BASE,
  TOTAL_SUM_ICMS, TOTAL_SUM_IPI, TOTAL_VNF_FORMULA, DUP_SUM, ARITH_OVERFLOW.
- Validators are pure, take a reference date and never throw.
- One tolerance setting, `Validation:Tolerance` (0.01), applies to single
  computed values. Sums over n items allow tolerance times max(n, 1), capped at
  `Validation:SumToleranceCap` (1.00).
- The vNF formula is `products_total - discount + icms_st_amount + freight +
  insurance + other_expenses + ipi_amount`: the DANFE-visible subset of
  rejection 610, with vICMSDeson, vFCPST, vII, vIPIDevol and vServ assumed
  zero.
- Tax families. Taxed: Normal 00, 20, 90 and Simples 900. Not taxed, amount
  `0.00`: Normal 40, 41, 50, 60 and Simples 101, 102, 103, 300, 400, 500.
  Anything else is TAX_CODE_UNSUPPORTED, a warning. The IPI base equals the
  item total.
- DUP_SUM is an error.
- Only error findings trigger repair. Success means schema-valid with zero
  errors.
- Repair continues the conversation with prompt `repair-001` and a
  `cache_control` breakpoint on the PDF when `max_repairs` is above 0.
- Feedback reveals `expected` and `actual` only for derived arithmetic and date
  rules. It never does for identifier, check-digit and key rules, and it
  carries no document text.
- `Extraction:MaxRepairs` defaults to 2, allowed 0 to 5. It is configuration
  only; per-request overrides belong to Phase 3 (API-02).
- Exhausting the budget gives `validation_failed`, carrying the last
  schema-valid candidate and its findings.
- A mid-loop refusal, truncation or infrastructure failure ends the loop and
  becomes the outcome. A `schema_invalid` inside a repair attempt consumes one
  unit; `schema_invalid` on the first attempt is the outcome. Every attempt is
  recorded.

**Rationale:** Findings with stable ids are both the repair prompt and the eval
data. Refusals and infrastructure failures are not quality failures, so they
must not be retried into a score. Hiding the expected check digit keeps repair
from inventing an identifier that merely satisfies the validator.

**Rejected:**
- Repairing warnings.
- Revealing the expected check digits in feedback.
- A fresh single-turn repair call: loses the cached PDF and the model's own
  first answer.
- A per-request `max_repairs`: Phase 3.

---

## D-24 Eval contract version 2 (refines D-02)
**Refined by:** D-29 (2026-10-15)
**Phase:** 2

**Decision:**
- The request carries `contract_version` "2" and an optional `reference_date`
  (`YYYY-MM-DD`, strict; anything else is HTTP 400 naming the field).
- When `reference_date` is absent the endpoint uses the server UTC date through
  `TimeProvider`. The runner sends the manifest `as_of_date`, which makes the
  dataset date the eval default (phase 2 CONTEXT D-11).
- The response carries `contract_version` "2" and:
  - `effective` `{model, prompt_version, repair_prompt_version, schema_sha256,
    pricing_version, max_repairs, reference_date}`;
  - `outcome` `{status, invoice, findings, failure, raw_output}`, with status
    `success`, `validation_failed`, `refused`, `truncated`, `schema_invalid` or
    `infrastructure_failure`; `validation_failed` carries the candidate invoice;
  - `attempts[]` `{index, kind initial|repair, prompt_version, status,
    raw_output, invoice, findings, usage, cost_usd, cost_warning, latency_ms,
    stop_reason, model_returned, provider_message_id, http_attempts, failure}`;
  - `usage` and `cost_usd` summed over attempts: null with a warning when any
    answered attempt is unpriced, never a partial sum;
  - endpoint `latency_ms`, and `stop_reason`, `model_returned` and
    `provider_message_id` taken from the last attempt.
- `validation_failed` is HTTP 200.
- The runner writes `record_version` 2.

**Rationale:** The status vocabulary and the response shape changed, so
consumers must opt in. A partial cost sum would understate spend while looking
exact.

**Rejected:**
- Keeping "1" with additive fields: a consumer that does not know the new
  statuses would grade them wrongly.
- Per-request overrides of the repair budget: Phase 3.

---

## D-25 The parse boundary accepts only what the committed schema allows (refines D-23)
**Phase:** 2

**Decision:**
- Model output holding a null where the schema requires a value, including a
  null element of `items` or `installments`, is `schema_invalid`.
  `Invoice.NullViolations()` lists the paths and the extractor checks it before
  the patterns, so the answer is a typed outcome with HTTP 200, never a
  validator crash.
- The validator stays total: given such an invoice directly it returns one
  `NULL_VALUE` error per path and runs no other rule. `NULL_VALUE` joins the
  D-23 rule ids; its repair sentence states no value; a null invoice reference
  stays an argument error.
- `Wire.Options` reads an enum only from a JSON string equal, ordinally after
  JSON unescaping, to its snake_case wire name. Integers, numeric strings,
  other casings, padded names and comma lists are `schema_invalid`. The
  committed schemas and generated models are unchanged, and the enum schema
  node is built from the converter's own name table (`Wire.EnumNames`), so
  parser and schema cannot drift apart. A parse error names the JSON path and
  never echoes the model's value.

**Rationale:** Success must mean schema-valid, so the .NET outcome and the
Python `schema_valid` grades of the same raw output agree; that agreement is the
measured-quality core value. A validator exception turned a model answer into
HTTP 500 and dropped the paid attempts (VAL-01, EXT-04; review CR-01 and
WR-01).

**Rejected:**
- A catch-all around `Validate`: it hides rule bugs (decisions 01-14 and 02-04).
- Skipping null elements inside the rules: a broken invoice would look clean.
- Keeping the case-insensitive built-in converter plus a post-parse check: the
  check would be hand-listed per enum field.
- A two-list null check written by hand in `Parse`: it misses a list added
  later.

---

## D-26 A thin agent slice joins Milestone 1 after Phase 3 (refines D-08, D-09 and D-10)
**Phase:** 3.1

**Decision:**
- Milestone 1 gains a thin agent slice, delivered as Phase 3.1 and inserted
  after Phase 3 (Replayable Runs). Phase 3 stays first.
- This supersedes the scoping that kept all agent work out of Milestone 1:
  the roadmap overview, the Out of Scope list in `.planning/PROJECT.md`, and
  the brief phase numbers on D-08, D-09 and D-10, which placed their first
  use in Milestone 2. Those three decisions now first apply in Phase 3.1;
  what they require does not change.
- The slice:
  - A separate `Tools` host serves MCP tools over Streamable HTTP, stateless
    (`ModelContextProtocol.AspNetCore`). The tools read a case's extracted
    invoice and its validator findings, and none of them changes state
    (D-09). The slice has no Postgres and no reference data.
  - The hand-written loop of D-08 (call model, run tool, append result,
    repeat) runs within a step budget and ends in a typed `Decision`:
    approve, reject or escalate, with reason and evidence. No agent
    framework.
  - Nothing executes the `Decision` (D-09). In Milestone 1 it is returned
    and recorded only; the workflow that acts on it comes with Milestone 3.
  - Step-budget exhaustion, a failed tool call and an extraction outcome
    other than success escalate (D-10). Near-duplicates need reference data,
    so that trigger stays in Milestone 2.
  - A few synthetic decision cases (D-14), each with one expected decision,
    run through the eval endpoint (D-02) and are graded deterministically
    offline. Milestone 1 has no LLM-as-judge (D-16).
  - Offline tests run the loop against the fake model. Live runs go only
    through a capped `just` recipe (US$1.00 per run, US$5 per phase) and
    replay from the Phase 3 response cache (D-19).
- Phases 4 to 6 will be scoped down to make room. The cuts are decided when
  each of those phases is discussed, not here; until then their requirements
  stand as written.
- The rest of Milestone 2 stays deferred: Postgres reference data and its
  tools (vendor lookup, PO match, duplicate and near-duplicate checks,
  categorization), decision evals at scale and the calibrated LLM-as-judge.
  Milestone 3 stays deferred too.

**Rationale:** The audience is reviewers for an "Operational AI / Agents
Platform" team, and they look for an agent that takes action with tools.
External review (2026-10-09) named the missing agent as the visible gap. A
thin agent slice, measured like extraction, shows that audience more than a
150-case dataset or confidence intervals would. Phase 3 goes first because
its response cache and costed traces make agent runs reproducible and cheap:
current models take no temperature, so without the cache no agent run can be
replayed.

**Rejected:**
- Building the slice before Phase 3: agent runs could be neither replayed
  nor traced, and every rerun would be paid again.
- Keeping Phases 4 to 6 as planned and starting Milestone 2 early by cutting
  Phases 5 and 6: the agent would still wait for the full dataset, and
  Milestone 2 in full (Postgres reference data and its tools) is much more
  than a reviewer needs to see an agent act.

---

## D-27 Response cache: key, entry format, modes and on by default in Development and Eval (refines D-19)
**Phase:** 3

**Decision:**
- The cache is a `CachingLlmGateway` decorator, our own, not a framework cache client.
- The key is the lowercase SHA-256 over canonical JSON with `key_version` `llm-cache-key-1` and these fields: `model` (as sent: an alias and a dated id are different keys), `max_tokens`, `cache_document` (included because it changes the final request and the billed usage, so `max_repairs` 0 and 2 have different initial-attempt keys), `prompt_version`, `repair_prompt_version` (repair attempts only), the SHA-256 of the prompt text, of the schema string and of the document bytes, the document media type, each follow-up turn's role and the SHA-256 of its text, and the replicate salt (empty when absent).
- Excluded from the key: cache mode, API key, base URL, timeout, retry count, reference date, case id and pricing version. A test forces every new `LlmRequest` property to be keyed or explicitly excluded.
- Entry format: one file `<key>.json` per key with `entry_version` `llm-cache-entry-1`, written to a temporary file and moved into place. An unreadable or mismatched entry is a miss.
- Modes: `off`, `read_write`, `read_only`, `refresh`. Only `end_turn` answers are stored; a refresh that is not `end_turn` keeps the previous entry.
- A hit reports the original latency, usage and cost, and `http_attempts` 0. A `read_only` miss is the typed infrastructure failure kind `cache_miss` and calls no provider.
- On by default in Development and Eval (user decision 2026-10-15, matching D-07 and LLM-04, which say enabled in dev and eval): with no `Llm:Cache` settings the host caches in `read_write` mode under `.cache/llm` resolved against the host content root (the process working directory, so `<repo>/.cache/llm` for every just recipe, which runs from the repo root; `.cache/` is git-ignored at any depth).
- `Llm:Cache:Directory` overrides the directory (a relative path resolves against the content root) and `Llm:Cache:Mode` (`off`, `read_write`, `read_only`, `refresh`) the default mode; `off` turns the cache off.
- In every other environment the cache is off, and a non-blank `Llm:Cache:Directory` or an `Llm:Cache:Mode` other than `off` fails startup.
- Hermetic tests turn the cache off explicitly: the Api test host sets `Llm:Cache:Mode` `off` unless a test passes its own cache settings (a per-test temporary directory), and the e2e scripted host gets a per-test `Llm__Cache__Directory`.
- Consequence: a run whose truncated or refused attempts were never cached replays those cases as `cache_miss`; faithful re-grading of them comes from the stored JSONL (EVAL-02), and the Phase 6 discuss must decide whether its replay set (CI-02) accepts that.

**Rationale:** The brief makes the response cache mandatory for dev and eval reruns, and current models take no temperature, so the cache is the reproducibility mechanism (D-07). Keying every request property that changes the final request, and nothing else, keeps hits exact. Storing only `end_turn` answers keeps a replay from turning a typed failure into a graded answer.

**Rejected:**
- The built-in distributed-cache chat client: no control of the key, no original cost.
- opt-in only by Llm:Cache:Directory with no default directory (the research recommendation, overridden by the user on 2026-10-15): the brief makes the response cache mandatory for dev and eval reruns, so a default run must not pay twice; the test order-dependence it guarded against is handled by tests turning the cache off explicitly.
- Caching every non-error answer: a replay could turn a typed failure into a graded answer.

---

## D-28 One retry owner: the SDK, MaxRetries 2 (refines D-21)
**Phase:** 3

**Decision:**
- The Anthropic SDK retry is the single policy: 408, 409, 429, 5xx and connection errors, backoff with jitter, honouring `retry-after`, `retry-after-ms` and `x-should-retry`. Other 4xx are not retried.
- `Llm:Anthropic:MaxRetries` defaults to 2 and is bounded 0 to 5 at startup.
- No Polly, no `Microsoft.Extensions.Http.Resilience` and no hand-written retry handler. Tests show 1 + `MaxRetries` attempts through the composed chain, and a static test rejects resilience packages.
- Worst case for one case with defaults is (`max_repairs` + 1) times (`MaxRetries` + 1) times the 300 s per-attempt timeout, 2700 s, so the runner's read timeout becomes 2700 s. An abandoned request is cancelled server-side through the request-aborted token and charged at the reserve.

**Rationale:** One owner keeps the attempt count, the cost accounting and the `http_attempts` figure honest, and the SDK already implements the provider's retry signals.

**Rejected:**
- A custom retry loop with `MaxRetries` 0: it re-implements `Retry-After` and `x-should-retry`.
- Keeping `MaxRetries` 0: transient 529s become infrastructure failures in measured runs.

---

## D-29 Eval contract 3: per-request overrides, cache block and incurred cost (refines D-24, and D-23 on the repair budget)
**Phase:** 3

**Decision:**
- Request and response carry `contract_version` "3"; "2" is refused.
- The request gains an optional `overrides` object with `model` (must be priced by the pricing table), `prompt_version` (from the prompt catalog), `max_repairs` (integer 0 to 5), `cache_mode` (`off`, `read_write`, `read_only`, `refresh`; anything but `off` needs a host whose cache is on) and `replicate_salt` (1 to 64 characters of A-Z, a-z, 0-9, dot, underscore, hyphen).
- An invalid value is HTTP 400 naming `overrides.<field>` without echoing it, and an unknown member is HTTP 400 `body`.
- `effective` gains `cache_mode` and `replicate_salt`, and `model`, `prompt_version` and `max_repairs` echo the resolved per-request values.
- Each attempt gains `cache` (`mode`, `status` hit, miss or refresh, `key`, `stored`; null when no cache was consulted) and `incurred_cost_usd` ("0.00000000" for a hit or a `cache_miss` failure, equal to `cost_usd` otherwise, null when unknown). The response gains a top-level `incurred_cost_usd`. `cost_usd` stays the notional cost (the original cost on a hit).
- The runner writes `record_version` 3 with `request.overrides`, caps spend on incurred cost, and refuses to resume a run with different overrides. Summaries (`summary_version` 3) report incurred and notional cost and the cache hit rate.

**Rationale:** A cached rerun must report the cost it would have had without hiding that it paid nothing, and a comparison needs the model, prompt and repair budget chosen per request, not per host.

**Rejected:**
- Additive fields under "2": `cost_usd` changes meaning on a hit, so consumers must opt in (the D-24 rule).
- Per-request overrides without validation: an unpriced model would spend with a null cost.

---

## D-30 Prompt catalog (EXT-05)
**Phase:** 3

**Decision:**
- Prompt texts live in a `PromptCatalog` in Carimbo.Extraction. `extract-003` is the default, and `extract-002` is restored byte-exact from the commit before 44c4323 as a second selectable version (assumption A4: it has the known label defects G-02-1 and G-02-2 but is schema-compatible, and it makes the `prompt_version` override and a two-prompt comparison real).
- Each text is pinned by SHA-256 in tests. The repair prompt stays `repair-001`.
- The prompt version is part of the cache key and of `effective` and `attempts[]` metadata.
- Only catalogued versions can be selected; no prompt text crosses the HTTP boundary.

**Rationale:** A prompt override that changes nothing would make the cache key, the contract field and any comparison decorative. A real second version keeps all three honest.

**Rejected:**
- A test-only prompt version: the override would have no real effect.
- Free prompt text in the request.

---

## D-31 Model-call spans and the local trace viewer (refines D-17)
**Phase:** 3

**Decision:**
- `TracingLlmGateway` is the outermost gateway decorator (Tracing over Caching over CostAccounting over the adapter), so every `CompleteAsync` call, a cache hit included, is one span named `chat <model>` of kind client from the ActivitySource `Carimbo.Llm`.
- Attributes are an allowlist owned as constants in `LlmTelemetry.cs`: GenAI semantic-convention names (operation, provider `anthropic`, request model and max tokens, response model, id and finish reasons, usage tokens with `gen_ai.usage.input_tokens` summing every input class), plus `carimbo.llm.*` for cost (decimal string, 8 places), pricing version, cost warning, cache status and mode, `http_attempts`, prompt version and replicate salt, and `error.type` on failure.
- No prompt, response, schema, document or key ever goes into a span, and exceptions are not recorded as events.
- The GenAI conventions are status Development with no release, so the pin is the constants file plus the allowlist test, citing open-telemetry/semantic-conventions-genai and the date read.
- The Api always registers OpenTelemetry tracing with the ASP.NET Core server instrumentation and the `Carimbo.Llm` source; the OTLP exporter is added only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- The tracer provider uses the AlwaysOn sampler set in code: every request is recorded whatever the inbound `traceparent`'s sampled flag (an inbound trace id is still continued), and `OTEL_TRACES_SAMPLER` has no effect because the SDK gives a sampler set in code precedence. So the trace id an eval response reports always names a trace holding its model-call spans; whether spans leave the process is decided by the exporter setting alone.
- No HTTP client instrumentation (it would add one span per HTTP attempt; `http_attempts` is a tag instead).
- The viewer is Jaeger v2 from `compose.yaml` on loopback, in memory.

**Rationale:** One span per gateway call, with cost and cache status as attributes, answers "what did this case pay and why" in the viewer without content leaving the process. The AlwaysOn sampler makes the reported trace id trustworthy.

**Rejected:**
- Microsoft.Extensions.AI `UseOpenTelemetry`: the gateway is not an `IChatClient` (D-21).
- A collector in Milestone 1.
- Cost as a double attribute.
- The SDK default parent-based sampler: an inbound `traceparent` with flags 00, or a ratio sampler from `OTEL_TRACES_SAMPLER`, leaves a reported trace id with no model-call spans (measured with OpenTelemetry 1.19.1 on 2026-10-15).

---

## D-32 Golden eval-response fixtures (API-04)
**Phase:** 3

**Decision:**
- `GoldenResponseTests` in Carimbo.Api.Tests posts a fixed scenario matrix through the real endpoint with scripted gateways and writes `data/contract/eval-responses/<scenario>.json` (indented, LF, endpoint `latency_ms` normalised to 0).
- In normal runs the test compares bytes and fails with "Run: just golden"; `CARIMBO_UPDATE_GOLDEN=1` (`just golden`) rewrites them. The files are generated and never hand-edited.
- Strict Pydantic models in `carimbo_evals/contract.py` (extra forbid) parse every fixture in pytest, so a renamed or added .NET field fails CI.
- `data/contract/eval-request.json` is a hand-written contract 3 request that the .NET test posts and the runner's payload builder must reproduce.

**Rationale:** The wire contract between the .NET endpoint and the Python runner is the seam where silent drift is most likely, and a committed response per scenario makes any change visible in review and in CI.

**Rejected:**
- Generating Pydantic response models from a response JSON Schema: no exporter exists, and strict hand models plus fixtures cover the contract with far less machinery.
- A separate golden-check recipe: the compare-mode test already fails `just dotnet-check`.

---

## D-33 Repair prompt version on model-call spans (refines D-31)
**Phase:** 3

**Decision:**
- A model-call span carries its `LlmRequest`'s prompt labels one to one. `carimbo.llm.prompt_version` stays the extraction prompt version on every call, and the new allowlisted `carimbo.llm.repair_prompt_version` is set only when the request has a repair prompt version (every repair call, `repair-001` today), error spans included.
- The prompt a call used, as `attempts[].prompt_version` reports it, is `carimbo.llm.repair_prompt_version` when present and `carimbo.llm.prompt_version` otherwise: the same rule the extractor applies to the record.
- The two names match the cache-key fields `prompt_version` and `repair_prompt_version` (D-27) and `effective.prompt_version` and `effective.repair_prompt_version` (D-29), so one vocabulary covers key, record and trace.
- The value is a catalogued version label, never prompt text. The allowlist grows by exactly this name, and the rest of D-31 stands.

**Rationale:** UAT gap G-03-1 found every repair span labelled with the extraction version, so the viewer told a repair call from the first call only by order. A repair call sends both prompts (the first user turn holds the extraction prompt, the follow-ups the repair feedback), so both labels belong on it, and mirroring the request keeps the tracing decorator observe-only with no extraction rule in Carimbo.Llm.

**Rejected:**
- Making `carimbo.llm.prompt_version` the per-call version: it changes the meaning of an attribute the 03-10 traces already exported, diverges from the cache-key and `effective` names, moves the extractor's labelling rule into the gateway, and drops the extraction version from repair spans.
- An attempt index or kind attribute: the gateway does not know attempts, and span start order already matches `attempts[]` order (D-31).

---

## D-34 Decision cases and the labelling rule (refines D-14 and D-26)
**Phase:** 3.1

**Decision:**
- A decision case starts from a committed extraction outcome (status, invoice when present, validator findings), never from a PDF, so only the agent is measured.
- Cases are generated by `dotnet/tools/DecisionCases` from specs kept in code: a skeleton base case mapped by `NfeXmlMapper`, a mutation, and a forced status for typed failures. The real `InvoiceValidator` validates them at the skeleton `as_of_date`.
- Output goes to `data/decision/` (`cases/<case_id>.json`, `policy.json`, `manifest.json`, `expected.json`), and `just decision-cases-check` fails on any byte difference.
- Case ids are neutral (`dc-001` to `dc-008`), so the id the model sees never names its label.
- The buyer policy is one generated `policy.json` with `buyer_cnpj` (a synthetic alphanumeric CNPJ with valid check digits) and `approval_limit` (money string `"200000.00"`).
- Labelling rule `decision-labels-1` is ordered and the first match wins:
  1. extraction status other than `success` gives escalate, cause `extraction_not_success`;
  2. recipient `tax_id` other than `buyer_cnpj` gives reject;
  3. any warning finding gives escalate, cause `policy_warning`;
  4. `totals.invoice_total` strictly greater than `approval_limit` gives escalate, cause `over_limit` (a total equal to the limit is not over it);
  5. otherwise approve.
  Approve and reject carry cause `none`.
- `DecisionRules.Label` in Carimbo.Decisions is the single implementation. The generator labels every case with it and the agent prompt states the same list in prose.
- Labels live only in `expected.json` and case intents only in `manifest.json`; the Tools host reads neither.
- The eight cases are one per rule path: approve on a Simples invoice, approve on a Regime Normal invoice, reject (recipient not the buyer), escalate on a `TAX_CODE_UNSUPPORTED` warning, escalate over the limit, escalate on `validation_failed`, escalate on a typed extraction failure (refused), and the ordering trap (recipient not the buyer and over the limit gives reject).
- Step-budget exhaustion and tool failure are runtime faults injected by offline tests, not dataset cases.
- Reversibility: case bytes feed tool results and so every agent cache key, and the rule order fixes every label, so a change is a new labelling version, never an edit of `decision-labels-1`.

**Rationale:** AGT-04 asks for cases labelled by a documented rule, so the rule is written here before any case exists (phase decision D-09). Starting from committed outcomes keeps extraction misreads out of decision accuracy and keeps a live decision run from paying for extraction (D-26). One implementation of the rule, used by the generator and stated in the prompt, keeps labels, cases and prompt from drifting apart.

**Rejected:**
- Hand-written case files: findings could disagree with the validators.
- Descriptive case ids: the id would leak the label.
- Labels inside served case files: they would be one tool call away.
- Starting from PDFs: extraction misreads would blur decision accuracy and every live run would pay for extraction.

---

## D-35 Tools host and agent loop (refines D-08, D-09 and D-10)
**Phase:** 3.1

**Decision:**
- Tools host: a separate Carimbo.Tools process (`ModelContextProtocol.AspNetCore` 2.2.0) serves four read-only MCP tools over Streamable HTTP at `/mcp`, `HttpServerSessionMode.Stateless`, default URL `http://127.0.0.1:5090`, `AllowedHosts` restricted to loopback names. It reads `Tools:CasesDirectory` (default `data/decision`) and never writes.
- The tools are `get_extraction_status(case_id)`, `get_invoice(case_id)`, `get_validator_findings(case_id)` and `get_policy()`. An unknown or malformed case id is a tool error; a case without an invoice answers normally with invoice null.
- The host has no compose service and is started as a process by the recipes and tests, like the Api.
- Agent: Carimbo.Agent, a hand-written loop inside the Api. It opens an MCP client per run (`Agent:ToolsUrl`, default `http://127.0.0.1:5090/mcp`, loopback only) and refuses a listed tool whose `ReadOnlyHint` is not true.
- One step is one model call. `Agent:MaxSteps` is configuration only, 1 to 10, default 6, echoed as `effective.max_steps`.
- Every request carries the pinned system prompt `agent-001` (the D-34 policy in prose), a first user turn of exactly `case_id: <case_id>` then a newline and the task sentence, the four tools, and the Decision model-facing schema as `output_config.format`, with no `tool_choice` and no sampling parameters.
- An answer without tool calls is the final answer. It is parsed strictly with `Wire.Options` plus `Decision.NullViolations` plus the outcome-cause rule: approve and reject carry cause `none`; escalate carries `extraction_not_success`, `policy_warning` or `over_limit`; every other pair is `schema_invalid`.
- Structured-output path: D-38 records whether the live probe confirmed tools combined with `output_config.format`. A probe that rejects the combination makes phase decision D-13's strict local `submit_decision` tool (input schema the Decision, `tool_choice` auto) the final-answer path. That switch is an explicit re-planning gate: the agent loop, endpoint and e2e plans are re-planned for it before they run.
- The tool calls of one answer run in block order and their results return in one user message.
- Terminal states made by code:
  - a tool failure (MCP error result, unknown tool, transport failure or timeout, while listing, during the loop or in the guard) ends the run at once with the code decision escalate / `tool_failed`, no retry, the tool name and error kind recorded;
  - a `tool_use` answer on step `MaxSteps` ends with escalate / `step_budget_exhausted` and its tools are not run.
- Typed failures with no decision, never escalate causes: a model refusal, `max_tokens`, an unparseable or schema-violating Decision, an outcome-cause pair outside the rule, a `tool_use` answer with no tool call, and any gateway failure including `cache_miss`. They surface as status `refused`, `truncated`, `schema_invalid` or `infrastructure_failure` (user decision 1).
- Guard (source `guard`, user decision 2): after a parsed model decision the guard calls `get_extraction_status` itself. This is not a model step and not citable. When the status is not `success` the final decision is the guard's escalate / `extraction_not_success`, with `guard.overridden` true when the model said approve or reject and the model's own decision kept as `model_decision`.
- Code-made decisions carry a fixed reason and no evidence.
- One Decision type `{evidence, reason, outcome, cause}`. The cause enum includes the code-only values; the prompt allows the model `none`, `extraction_not_success`, `policy_warning` and `over_limit`, and the parse boundary enforces the pairs.
- Default model `claude-haiku-4-5`. Any priced model may be chosen by override because assistant turns echo thinking blocks (D-37); D-38 records whether the echo was verified live.
- Nothing executes a Decision: it is returned and recorded only.

**Rationale:** The terminal-state table keeps decision accuracy about the agent: model reliability problems stay typed failures (EXT-02, EVAL-04), while faults of the environment (a tool that fails, a loop that runs out of steps) are escalations the system itself makes. The guard's own recorded call removes trust in the model having fetched the status, which it may skip by answering on its first turn. One Decision type with a pair rule at the parse boundary avoids two schemas for one prompt.

**Rejected:**
- An agent framework or the `IChatClient` tool loop: against D-08 and D-21.
- Passing tool errors back to the model: deferred by the phase discussion.
- Escalating on model failures: it would mix model reliability into decision accuracy, against EXT-02 and EVAL-04.
- Trusting the model to have fetched the status: it may answer on its first turn.
- A separate model-facing draft type: two schemas and two generated models for one prompt rule.
- A per-request `max_steps`: deferred.

---

## D-36 Eval contract decision-1 and decision grading (refines D-02 and D-29)
**Phase:** 3.1

**Decision:**
- Route `POST /eval/decisions` with the gating, static key, auth-before-binding and per-field 400s of `/eval/extractions`. Typed failures are HTTP 200.
- Request: `contract_version` `"decision-1"`, `case_id` matching `^[a-z0-9][a-z0-9-]{0,63}$`, and optional `overrides` `{model (priced by the table), cache_mode, replicate_salt}`. There is no `prompt_version` override: one pinned prompt, `agent-001`.
- Response members:
  - `contract_version`, `case_id`, `trace_id`;
  - `effective` `{model, prompt_version, max_steps, schema_sha256, tools_sha256, pricing_version, cache_mode, replicate_salt}`;
  - `outcome` `{status (decided, refused, truncated, schema_invalid, infrastructure_failure), decision, model_decision, decided_by (model, guard, tool_failure, step_budget), guard {extraction_status, overridden, tool_call}, tool_failure {tool, source, error_kind, error_message}, failure {kind, http_status, request_id, message}, raw_output}`;
  - `steps[]` `{index, usage, cost_usd, cost_warning, incurred_cost_usd, latency_ms, stop_reason, model_returned, provider_message_id, http_attempts, cache, failure, tool_calls[]}`, where each tool call is `{id, name, arguments, source (model or guard), outcome (ok, error or skipped), error_kind, error_message, latency_ms}`;
  - `usage`, `cost_usd`, `cost_warning`, `incurred_cost_usd`, `latency_ms` for the whole run.
- Incurred versus notional cost follows D-29 per step. Extraction contract 3 and its goldens are untouched.
- The offline grader (`carimbo-evals grade-decisions`) scores `decision_correct` (final outcome equals the expected label) as primary, `cause_correct` when the expected decision is escalate, and `citations_valid` (every `model_decision` evidence source is a tool the model itself called in that run; guard calls never count). It reports `model_decision_correct`, guard overrides, steps, tool calls, tokens, incurred and notional cost, cache hit rate and latency.
- Typed failures and harness errors are counted apart and never as wrong decisions. There is no LLM judge (D-16).
- The runner is the generalised `run_cases` (`carimbo-evals decisions`, decision `record_version` 1 with `record_kind` `"decision"`).
- `just decisions [max_cost] [cache_mode]` is the only live path (US$1.00 default cap; `read_only` replays free and needs the Tools host running).
- Golden decision-1 responses live in `data/contract/decision-responses` (`just golden`) and are parsed by strict Pydantic models. `data/contract/decision-request.json` is hand-written like `eval-request.json` and documented as such in AGENTS.md.

**Rationale:** The decision endpoint is a second contract beside extraction, and the same seam-drift argument as D-32 applies: a committed response per scenario makes any change visible. Scoring the final outcome as primary measures the system, while `model_decision_correct` and the override count measure how often the model alone would have broken policy.

**Rejected:**
- Extending contract 3: it would churn the extraction goldens and records.
- Grading the guard's final cause only: `model_decision` measures how often the model alone would have broken policy.

---

## D-37 Response cache v2 for tool-use conversations (refines D-27)
**Phase:** 3.1

**Decision:**
- A request with no system prompt, no tool definitions, a document and plain-text follow-ups keeps the exact `llm-cache-key-1` canonical JSON, so Phase 3 entries keep replaying.
- Any other request is keyed by `llm-cache-key-2`, which adds `system_sha256`, the tool definitions (name, description SHA-256, input schema SHA-256, in order), null document members when there is no document, and per follow-up turn its content blocks (kind, SHA-256 of text, signature and data, tool-use id, name and input SHA-256) and tool results (`tool_use_id`, content SHA-256, `is_error`).
- `end_turn` and `tool_use` answers are stored. This supersedes D-27's "only `end_turn` answers are stored". Refusal, `max_tokens`, `stop_sequence` and other answers never are.
- Entries are written as `llm-cache-entry-2` with the response's ordered content blocks (text, thinking with its signature, `redacted_thinking` data, `tool_use` id, name and input). `llm-cache-entry-1` entries are still read.
- An assistant turn echoes the model's content blocks unchanged and in order, thinking signatures included, so a thinking model can continue a tool conversation.
- Provider tool-use ids stay in the key, so refreshing an early step makes later steps miss instead of replaying under a different history.
- A replay needs the Tools host running, because tool definitions and results are inputs of every key.

**Rationale:** Tool conversations carry inputs that key 1 cannot express (system prompt, tools, tool results), so they need their own key, while leaving key 1 byte-identical for extraction keeps the committed Phase 3 replays valid. A `tool_use` answer is a valid, replayable step, unlike a refusal or truncation, which would turn a typed failure into a graded answer.

**Rejected:**
- Changing the v1 shape: it breaks Phase 3 replays and the pinned key.
- A second cache for agent calls: D-19 keeps a single cache.
- Dropping thinking blocks: the next request would be rejected or degraded on thinking models.

---

## Open questions (resolve in discuss phases)

- **Local services:** devenv services, docker-compose, or .NET Aspire.
  Constraint: must work on NixOS-WSL and for an external reviewer.
- **.NET model SDK:** Anthropic C# SDK directly, `Microsoft.Extensions.AI`
  as the abstraction, or both. Verify current maturity first.
- **DANFE rendering and barcode decoding libraries** (Python for rendering,
  .NET for decoding).
- **Which two models** to compare in the first results table.
- **Exact CI eval subset size** and threshold values.
