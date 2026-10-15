---
phase: 03-replayable-runs
plan: 02
subsystem: infra
tags: [docker-compose, jaeger, trace-viewer, decisions, docs-check, loopback]

requires:
  - phase: 03-replayable-runs
    provides: plan 03-01 cache design that D-27 records
provides:
  - compose.yaml with Jaeger v2.22.0 on 127.0.0.1 (16686 UI, 4317 OTLP gRPC, 4318 OTLP HTTP), in-memory storage
  - README "Trace viewer" section and AGENTS.md line documenting `docker compose up -d`
  - Decision records D-27 to D-32 (cache, retry owner, eval contract 3, prompt catalog, spans, golden fixtures) and Refined by lines on D-17, D-19, D-21, D-23, D-24
  - just docs-check requiring D-18 to D-32
  - Static pytest test_compose_starts_a_pinned_loopback_trace_viewer
affects: [03-03, 03-04, 03-08, 03-10]

actuals:
  tokens: 6400
  tasks: 2
  commits: 2
plan_head_before: bdaa92a456bab7b62c7e69591b8751c79b1c77e2
plan_head_after: 915ee32ea154ba22179cd62de9c44f548b68d07c

tech-stack:
  added: []
  patterns:
    - "Container image pinned to an explicit tag and every published port bound to 127.0.0.1, enforced by a text-only pytest (no YAML library)"
    - "Decision records written before the plans that implement them; docs-check greps each heading"

key-files:
  created:
    - compose.yaml
  modified:
    - README.md
    - AGENTS.md
    - docs/DECISIONS.md
    - justfile
    - python/tests/test_repo_layout.py

key-decisions:
  - "D-27: response cache is a CachingLlmGateway decorator, on by default (read_write at .cache/llm under the content root) in Development and Eval, off and refused elsewhere; opt-in only was rejected"
  - "D-28: the Anthropic SDK is the single retry owner, MaxRetries 2 (bounded 0 to 5), runner read timeout 2700 s"
  - "D-29: eval contract 3 adds per-request overrides, an attempt cache block and incurred_cost_usd"
  - "D-30: PromptCatalog with extract-003 default and extract-002 restored as a second selectable version"
  - "D-31: TracingLlmGateway outermost, allowlisted attributes, AlwaysOn sampler set in code, Jaeger v2 from compose.yaml"
  - "D-32: golden eval-response fixtures under data/contract with strict Pydantic models"

patterns-established:
  - "Compose file text contract: pinned jaeger 2.x.y tag, no :latest, loopback-only quoted port mappings"

requirements-completed: [REPO-05]

coverage:
  - id: D1
    description: "compose.yaml starts Jaeger v2 from a pinned tag with every published port on 127.0.0.1; the UI answers HTTP 200 on 127.0.0.1:16686 after docker compose up -d"
    requirement: REPO-05
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_compose_starts_a_pinned_loopback_trace_viewer"
        status: pass
      - kind: integration
        ref: "docker compose config --quiet; docker compose up -d jaeger then curl http://127.0.0.1:16686/ (HTTP 200)"
        status: pass
    human_judgment: false
  - id: D2
    description: "README documents the one command, the UI address, in-memory loss on down and that devenv does not provide Docker"
    requirement: REPO-05
    verification:
      - kind: unit
        ref: "python/tests/test_repo_layout.py#test_compose_starts_a_pinned_loopback_trace_viewer"
        status: pass
    human_judgment: true
    rationale: "The test checks only that the command and URL appear; whether the prose is clear to a reviewer is a judgment"
  - id: D3
    description: "D-27 to D-32 recorded in docs/DECISIONS.md with Refined by lines, and docs-check fails when any of D-18 to D-32 is missing"
    verification:
      - kind: other
        ref: "just docs-check prints docs ok"
        status: pass
    human_judgment: true
    rationale: "docs-check proves the headings exist; whether each record states the intended decision faithfully is a reading task"

duration: 4min
completed: 2026-10-15
status: complete
---

# Phase 3 Plan 02: one-command trace viewer and the Phase 3 decision records Summary

**compose.yaml pins Jaeger v2.22.0 on loopback-only ports behind one documented `docker compose up -d`, and D-27 to D-32 record the Phase 3 contract (default-on response cache, SDK retry owner, eval contract 3, prompt catalog, model-call spans, golden fixtures) with docs-check enforcing D-18 to D-32.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-10-15T07:50:31Z
- **Completed:** 2026-10-15T07:54:00Z
- **Tasks:** 2
- **Files modified:** 6 (1 created, 5 modified)

## Accomplishments
- Tracer slice end to end: `docker compose config --quiet` is valid, `docker compose up -d jaeger` served the UI with HTTP 200 on 127.0.0.1:16686, and the published ports show as `127.0.0.1:4317-4318` and `127.0.0.1:16686` only.
- Static pytest keeps the viewer pinned and loopback-only in CI without Docker (12 tests pass in test_repo_layout.py).
- Six decision records fix the Phase 3 contract before the implementing plans; five earlier records now name their refinement.
- `just docs-check` fails when any of D-18 to D-32 is missing (README and AGENTS rows updated to match).

## Task Commits

1. **Task 1 (tracer): docker compose starts a pinned Jaeger v2 on loopback, README says so** - `fa0943e` (feat)
2. **Task 2: record D-27 to D-32 and make docs-check require them** - `915ee32` (docs)

**Plan metadata:** see the docs(03-02) commits that follow (summary, then state and roadmap).

## Files Created/Modified
- `compose.yaml` - Jaeger v2.22.0 service, ports 16686/4317/4318 on 127.0.0.1, header comments on pinning, loopback and in-memory storage
- `README.md` - "Trace viewer" section; docs-check row now says D-18 to D-32
- `AGENTS.md` - one line for `docker compose up -d`; docs-check row now says D-18 to D-32
- `docs/DECISIONS.md` - D-27 to D-32 before "Open questions"; Refined by lines on D-17, D-19, D-21, D-23 (extended), D-24
- `justfile` - docs-check loop covers D-18 to D-32
- `python/tests/test_repo_layout.py` - test_compose_starts_a_pinned_loopback_trace_viewer

## Decisions Made
The six records above (D-27 to D-32), written exactly from the plan text. Image tag 2.22.0 still existed on the registry, so no substitution was needed (digest sha256:836b967b5abcf1f7758066aa6b348619267a2f1459f72d472840570f9bd88a17).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Plan's UI readiness probe is flaky**
- **Found during:** Task 1 (verify)
- **Issue:** `curl --retry 20 --retry-connrefused` exited 56 (Connection reset by peer) on the first run: Docker's port proxy accepts the connection before Jaeger is listening, and curl does not retry a reset under `--retry-connrefused`.
- **Fix:** Ran the same probe with `--retry-all-errors`; it retried through the reset and exited 0, and a polling loop saw HTTP 200 after about 2 s. The container never failed. Nothing in the repo embeds the probe, so there is no file change; a later plan that scripts the readiness check (03-10) should use `--retry-all-errors`.
- **Files modified:** none
- **Verification:** `docker compose up -d jaeger && curl -fsS --retry 20 --retry-all-errors --retry-delay 1 http://127.0.0.1:16686/` exit 0, then `docker compose down` removed the container and network.

**2. [Format] Decision bullets kept on single lines**
- **Found during:** Task 2
- **Issue:** The acceptance greps ("On by default in Development and Eval", "opt-in only by Llm:Cache:Directory", "AlwaysOn sampler") must each match on one line, so the new records are not hard-wrapped at 76 columns like the older ones. One bullet per sentence group in the plan's prose, with Rationale and Rejected added in the existing layout. The plan gave no Rationale text for D-28 to D-32 beyond the Rejected lists; short rationales were composed from the plan and research content.
- **Files modified:** docs/DECISIONS.md

---

**Total deviations:** 2 (1 blocking verify-probe flake, 1 formatting note). **Impact on plan:** none on scope; no file content differs from the plan's intent.

## Issues Encountered
None beyond the readiness-probe flake above.

## Known Stubs
None.

## Threat Flags
None. The only new surface is the Jaeger container, already covered by T-03-06 (loopback binding, enforced by the pytest) and T-03-07 (pinned tag, `:latest` rejected by the pytest). No package was installed (T-03-SC).

## User Setup Required
None - no external service configuration required. Docker with compose v2 is needed only to run the trace viewer.

## Next Phase Readiness
- Ready for the next Phase 3 plans: the contract (D-27 to D-32) is recorded, so implementing plans can cite it.
- Not run here: nothing live. Spans do not exist yet, so the viewer shows no traces until plan 03-10; README deliberately does not document span export or trace lookup yet.

## Self-Check: PASSED

- compose.yaml, README.md, AGENTS.md, docs/DECISIONS.md, justfile, python/tests/test_repo_layout.py all present and committed.
- Commits fa0943e and 915ee32 are ancestors of HEAD.
- Acceptance criteria re-run: jaeger tag count 1, loopback mappings 3, `docker compose up -d` in README 1, `^## D-(27..32) ` 6, justfile D-27..D-32 1, Refined by D-27 and D-31 present, AlwaysOn sampler 2, On by default 1, opt-in only 1, D-18 to D-32 in README and AGENTS 1 each, `just docs-check` prints docs ok, pytest 12 passed.

---
*Phase: 03-replayable-runs*
*Completed: 2026-10-15*
