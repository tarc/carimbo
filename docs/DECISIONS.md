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
**Phase:** 5, 6, 7

**Decision:** MCP tools are read-only. The agent's output is a `Decision`
(approve / reject / escalate, reason, evidence). Executing the decision is a
separate workflow activity, not a tool the agent can call.

**Rationale:** Side effects stay outside the non-deterministic component, so
they can be made idempotent, audited and gated. This is the safety boundary.

---

## D-10 Uncertainty escalates
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
**Refined by:** D-25 (2026-10-08)
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

## Open questions (resolve in discuss phases)

- **Local services:** devenv services, docker-compose, or .NET Aspire.
  Constraint: must work on NixOS-WSL and for an external reviewer.
- **.NET model SDK:** Anthropic C# SDK directly, `Microsoft.Extensions.AI`
  as the abstraction, or both. Verify current maturity first.
- **DANFE rendering and barcode decoding libraries** (Python for rendering,
  .NET for decoding).
- **Which two models** to compare in the first results table.
- **Exact CI eval subset size** and threshold values.
