# carimbo task runner (D-13): one command per stack, the contract chain, and the live skeleton loop.
# Run every recipe from the repo root. CI calls exactly these recipes (D-14).

set shell := ["bash", "-euo", "pipefail", "-c"]

# Command that regenerates python/src/carimbo_models/generated.py from schema/invoice.schema.json.
# The input path string must stay identical on every run: the generated header records it.
codegen := "datamodel-codegen --input schema/invoice.schema.json --input-file-type jsonschema --output-model-type pydantic_v2.BaseModel --use-annotated --field-constraints --use-standard-collections --use-union-operator --target-python-version 3.12 --use-title-as-name --reuse-model --disable-timestamp --formatters ruff-format ruff-check --output python/src/carimbo_models/generated.py"

# List the recipes.
default:
    @"{{ just_executable() }}" --justfile "{{ justfile() }}" --list

# .NET: build with warnings as errors, verify formatting, run the xUnit v3 tests.
dotnet-check:
    dotnet build dotnet/Carimbo.slnx -warnaserror
    dotnet format dotnet/Carimbo.slnx --verify-no-changes
    cd dotnet && dotnet test

# Python: sync from the lock, lint, format-check, type-check, run the default pytest selection.
py-check:
    uv sync --project python --locked
    uv run --project python ruff check python
    uv run --project python ruff format --check python
    uv run --project python pyright -p python
    uv run --project python pytest python/tests -q

# Regenerate schema/*.json from the .NET Domain and the Pydantic models from the schema.
schema:
    dotnet run --project dotnet/tools/SchemaExport
    uv run --project python {{ codegen }}

# Fail when a committed schema or generated model differs from a fresh regeneration (DOM-08).
schema-check:
    "{{ just_executable() }}" --justfile "{{ justfile() }}" schema
    git diff --exit-code -- schema python/src/carimbo_models

# Regenerate the seeded synthetic dataset in data/skeleton.
datagen:
    uv run --project python carimbo-datagen build

# Fail unless a regeneration from the seed is byte-identical to data/skeleton.
datagen-check:
    uv run --project python carimbo-datagen check

# End to end: committed cases over real HTTP to a graded summary, against a scripted model (needs dotnet).
e2e:
    uv run --project python pytest python/tests -q -m e2e

# Fail unless the decision records D-18 to D-25 and the spike recommendation are present.
docs-check:
    #!/usr/bin/env bash
    set -euo pipefail
    for id in D-18 D-19 D-20 D-21 D-22 D-23 D-24 D-25; do
      grep -qE "^## ${id} " docs/DECISIONS.md || { echo "docs/DECISIONS.md has no '## ${id} ' heading" >&2; exit 1; }
    done
    grep -qE '^## Recommendation' docs/spikes/01-llm-gateway.md \
      || { echo "docs/spikes/01-llm-gateway.md has no Recommendation section" >&2; exit 1; }
    grep -qE '^## Result' docs/spikes/02-schema-probe.md \
      || { echo "docs/spikes/02-schema-probe.md has no Result section" >&2; exit 1; }
    echo "docs ok"

# Fail when a tracked file holds a provider-key-shaped string (sk-ant-<kind><NN>-<8+ key characters>).
secrets-check:
    #!/usr/bin/env bash
    set -euo pipefail
    if git grep -nE 'sk-ant-[a-z]+[0-9]{2}-[A-Za-z0-9_-]{8,}'; then
      echo "secrets-check: key-shaped string in a tracked file (lines above)" >&2
      exit 1
    fi
    echo "secrets ok"

# Every offline gate: both stacks, the contract chain, dataset identity, e2e, docs and secrets.
check: dotnet-check py-check schema-check datagen-check e2e docs-check secrets-check

# Run a command with the provider key available (D-10). Presence is tested, the value is never printed.
[positional-arguments]
_with-provider-key +cmd:
    #!/usr/bin/env bash
    set -euo pipefail
    # 1. Already exported: cloud environment, a reviewer's export, or a session launched with secretspec run.
    if [ -n "${CARIMBO_ANTHROPIC_API_KEY:-}" ] || [ -n "${ANTHROPIC_API_KEY:-}" ]; then
      exec "$@"
    fi
    # 2. secretspec is optional. secretspec.toml declares the key optional, so this branch always
    #    runs the command; the command's own presence check then reports a key that was never stored.
    #    An agent caller must supply an access reason.
    if command -v secretspec >/dev/null 2>&1 && [ -f secretspec.toml ]; then
      export SECRETSPEC_REASON="${SECRETSPEC_REASON:-carimbo live recipe: Anthropic model calls}"
      exec secretspec run -- "$@"
    fi
    # 3. Neither.
    echo "provider key unset: export CARIMBO_ANTHROPIC_API_KEY (fallback ANTHROPIC_API_KEY), or store it with: secretspec set CARIMBO_ANTHROPIC_API_KEY (see README)" >&2
    exit 2

# LLM gateway spike, live (paid, capped at US$1.00). Rewrites docs/spikes/01-llm-gateway.md and its fixtures.
spike-live:
    "{{ just_executable() }}" --justfile "{{ justfile() }}" _with-provider-key dotnet run --project dotnet/tools/LlmSpike -- --live --budget-usd 1.00 --cases data/skeleton --out docs/spikes/01-llm-gateway.md --fixtures dotnet/tests/Carimbo.Llm.Tests/Fixtures

# Live schema probe (paid, capped at US$0.25). Rewrites docs/spikes/02-schema-probe.md.
schema-probe:
    "{{ just_executable() }}" --justfile "{{ justfile() }}" _with-provider-key dotnet run --project dotnet/tools/LlmSpike -- --schema-probe --budget-usd 0.25 --cases data/skeleton --out docs/spikes/02-schema-probe.md

# Live skeleton: generate-check, start the Api, extract the three cases with Claude, grade (paid, capped per run).
# The first argument is the cost cap in USD; the second sets the repair budget (0 disables repair).
skeleton max_cost="1.00" max_repairs="2":
    "{{ just_executable() }}" --justfile "{{ justfile() }}" _with-provider-key "{{ just_executable() }}" --justfile "{{ justfile() }}" _skeleton-run {{ max_cost }} {{ max_repairs }}

# The skeleton steps. Runs inside _with-provider-key, so the provider key is already in the environment.
_skeleton-run max_cost max_repairs:
    #!/usr/bin/env bash
    set -euo pipefail
    just_cmd=("{{ just_executable() }}" --justfile "{{ justfile() }}")
    base_url="http://127.0.0.1:5080"

    # 1. The provider key must be present (value never shown).
    if [ -z "${CARIMBO_ANTHROPIC_API_KEY:-}" ] && [ -z "${ANTHROPIC_API_KEY:-}" ]; then
      echo "provider key unset: export CARIMBO_ANTHROPIC_API_KEY (fallback ANTHROPIC_API_KEY), or store it with: secretspec set CARIMBO_ANTHROPIC_API_KEY" >&2
      exit 2
    fi

    # 2. Ephemeral eval key, valid for this run only.
    CARIMBO_EVAL_API_KEY="$(uv run --project python python -c 'import secrets; print(secrets.token_urlsafe(32))')"
    export CARIMBO_EVAL_API_KEY

    # 3. The committed cases are the inputs.
    "${just_cmd[@]}" datagen-check

    # 4. Build the Api, start it in the background on loopback, and stop it however this script exits.
    if curl -fsS --max-time 2 "${base_url}/healthz" >/dev/null 2>&1; then
      echo "something already answers on ${base_url}; stop it first" >&2
      exit 2
    fi
    work_dir="$(mktemp -d)"
    api_pid=""
    stop_api() {
      if [ -n "${api_pid}" ] && kill -0 "${api_pid}" 2>/dev/null; then
        kill "${api_pid}" 2>/dev/null || true
        wait "${api_pid}" 2>/dev/null || true
      fi
      api_pid=""
    }
    cleanup() { stop_api; rm -rf "${work_dir}"; }
    trap cleanup EXIT
    dotnet build dotnet/src/Carimbo.Api -c Release --nologo -v q -o "${work_dir}/api"
    ASPNETCORE_ENVIRONMENT=Development DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
      Extraction__MaxRepairs={{ max_repairs }} \
      dotnet "${work_dir}/api/Carimbo.Api.dll" --urls "${base_url}" >"${work_dir}/api.log" 2>&1 &
    api_pid=$!

    # 5. Wait for /healthz, at most 120 s.
    healthy=0
    for _ in $(seq 1 240); do
      if ! kill -0 "${api_pid}" 2>/dev/null; then
        echo "the Api exited early; log:" >&2
        tail -n 30 "${work_dir}/api.log" >&2
        exit 1
      fi
      if curl -fsS --max-time 2 "${base_url}/healthz" >/dev/null 2>&1; then
        healthy=1
        break
      fi
      sleep 0.5
    done
    if [ "${healthy}" -ne 1 ]; then
      echo "the Api was not healthy after 120 s; log:" >&2
      tail -n 30 "${work_dir}/api.log" >&2
      exit 1
    fi
    grep -m1 'model gateway' "${work_dir}/api.log" || true

    # 6. Run the cases, stop the Api, then grade whatever was recorded.
    run_dir="evals/runs/skeleton-$(date -u +%Y%m%dT%H%M%SZ)"
    run_rc=0
    uv run --project python carimbo-evals run --cases data/skeleton --out "${run_dir}" \
      --max-cost-usd {{ max_cost }} || run_rc=$?
    stop_api
    if [ -f "${run_dir}/cases.jsonl" ]; then
      uv run --project python carimbo-evals grade --run "${run_dir}"
    fi

    # 7. Where to read the result.
    echo "summary: ${run_dir}/summary.md"
    exit "${run_rc}"
