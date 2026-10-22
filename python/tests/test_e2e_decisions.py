"""E2E: the committed decision cases over two real processes, graded, against a scripted model.

Starts the real Tools host (``dotnet/src/Carimbo.Tools``, an MCP server over Streamable HTTP) and
the real ASP.NET composition with a scripted ``ILlmGateway``
(``dotnet/tests/Carimbo.ScriptedHost``) as separate processes, and drives them through the
documented commands: ``carimbo-evals decisions`` and then ``carimbo-evals grade-decisions``, as
subprocesses. No provider key is needed or read: both provider-key variables are removed from the
host environments.
"""

from __future__ import annotations

import json
import os
import secrets
import shutil
import signal
import socket
import subprocess
import sys
import time
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path
from typing import Any

import httpx2
import pytest

pytestmark = pytest.mark.e2e

REPO_ROOT = Path(__file__).resolve().parents[2]
DECISION_DIR = REPO_ROOT / "data" / "decision"
STARTUP_TIMEOUT_S = 180.0
# The scripted model's reply texts are keyed by case id; these are the eight committed cases.
CASE_IDS = tuple(f"dc-00{n}" for n in range(1, 9))
# Provider keys must never reach a host: a developer's key can then never be used (T-03.1-36).
PROVIDER_KEY_VARIABLES = ("CARIMBO_ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY")


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def _tail(path: Path, lines: int = 40) -> str:
    if not path.exists():
        return "<no output>"
    return "\n".join(path.read_text(encoding="utf-8", errors="replace").splitlines()[-lines:])


def _stop(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait()
    except ProcessLookupError:
        pass


def _host_environment(extra: dict[str, str]) -> dict[str, str]:
    env = {k: v for k, v in os.environ.items() if k not in PROVIDER_KEY_VARIABLES}
    env.update(
        {
            "ASPNETCORE_ENVIRONMENT": "Development",
            "DOTNET_NOLOGO": "1",
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            **extra,
        }
    )
    return env


@contextmanager
def _dotnet_host(project: str, base_url: str, env: dict[str, str], log_dir: Path) -> Iterator[None]:
    """Run ``dotnet run --project <project>`` on ``base_url`` until /healthz answers."""
    if shutil.which("dotnet") is None:
        pytest.fail(
            "dotnet is not on PATH; run this test inside `nix shell nixpkgs#dotnet-sdk_10 -c ...`"
        )
    name = Path(project).name
    log_dir.mkdir(parents=True, exist_ok=True)
    out_path = log_dir / f"{name}.out.log"
    err_path = log_dir / f"{name}.err.log"
    with out_path.open("wb") as out, err_path.open("wb") as err:
        process = subprocess.Popen(
            [
                "dotnet",
                "run",
                "--project",
                project,
                "--no-launch-profile",
                "--",
                "--urls",
                base_url,
            ],
            cwd=REPO_ROOT,
            env=env,
            stdout=out,
            stderr=err,
            start_new_session=True,
        )
        try:
            deadline = time.monotonic() + STARTUP_TIMEOUT_S
            while True:
                if process.poll() is not None:
                    pytest.fail(
                        f"{name} exited early ({process.returncode}):\n"
                        f"{_tail(err_path)}\n{_tail(out_path)}"
                    )
                try:
                    if httpx2.get(f"{base_url}/healthz", timeout=2.0).status_code == 200:
                        break
                except httpx2.TransportError:
                    pass
                if time.monotonic() > deadline:
                    pytest.fail(
                        f"{name} not healthy after {STARTUP_TIMEOUT_S}s:\n{_tail(out_path)}"
                    )
                time.sleep(0.5)
            yield
        finally:
            _stop(process)


@contextmanager
def _scripted_api(
    workdir: Path, responses: Path, tools_mcp_url: str, **settings: str
) -> Iterator[tuple[str, str]]:
    """The ScriptedHost Api wired to a Tools host; yields ``(base_url, eval api key)``."""
    port = _free_port()
    base_url = f"http://127.0.0.1:{port}"
    api_key = secrets.token_urlsafe(32)
    env = _host_environment(
        {
            # The response cache is on by default in Development (D-27); every scripted host gets
            # its own directory so no test reads entries another test or a developer run left.
            "Llm__Cache__Directory": str(workdir / "llm-cache"),
            "Llm__Cache__Mode": "read_write",
            "CARIMBO_EVAL_API_KEY": api_key,
            "CARIMBO_SCRIPTED_RESPONSES_DIR": str(responses),
            "Agent__ToolsUrl": tools_mcp_url,
            **settings,
        }
    )
    with _dotnet_host("dotnet/tests/Carimbo.ScriptedHost", base_url, env, workdir / "logs"):
        yield base_url, api_key


def _evals(*args: str, api_key: str | None = None) -> subprocess.CompletedProcess[str]:
    """Run the documented ``carimbo-evals`` console script from the repo root."""
    executable = Path(sys.executable).parent / "carimbo-evals"
    env = {k: v for k, v in os.environ.items() if k != "CARIMBO_EVAL_API_KEY"}
    if api_key is not None:
        env["CARIMBO_EVAL_API_KEY"] = api_key
    return subprocess.run(
        [str(executable), *args],
        cwd=REPO_ROOT,
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )


@pytest.fixture(scope="module")
def tools_mcp_url(tmp_path_factory: pytest.TempPathFactory) -> Iterator[str]:
    """One Tools host for the module: read-only, stateless, serving the committed decision cases."""
    base_url = f"http://127.0.0.1:{_free_port()}"
    env = _host_environment({"Tools__CasesDirectory": str(DECISION_DIR)})
    with _dotnet_host(
        "dotnet/src/Carimbo.Tools", base_url, env, tmp_path_factory.mktemp("tools-host")
    ):
        yield f"{base_url}/mcp"


# --- scripted model answers ---------------------------------------------------------------------


def _expected() -> dict[str, dict[str, Any]]:
    document = json.loads((DECISION_DIR / "expected.json").read_text(encoding="utf-8"))
    return {entry["case_id"]: entry for entry in document["cases"]}


def _buyer_cnpj() -> str:
    policy = json.loads((DECISION_DIR / "policy.json").read_text(encoding="utf-8"))
    return str(policy["buyer_cnpj"])


def _usage(input_tokens: int = 1500, output_tokens: int = 120) -> dict[str, int]:
    return {
        "input_tokens": input_tokens,
        "output_tokens": output_tokens,
        "cache_read_tokens": 0,
        "cache_write_5m_tokens": 0,
        "cache_write_1h_tokens": 0,
    }


def _tool_use_step(
    case_id: str, step: int, calls: list[tuple[str, dict[str, Any]]]
) -> dict[str, Any]:
    prefix = f"toolu_{case_id.replace('-', '')}_{step}"
    return {
        "stop_reason": "tool_use",
        "text": "",
        "tool_calls": [
            {"id": f"{prefix}_{index}", "name": name, "input": arguments}
            for index, (name, arguments) in enumerate(calls)
        ],
        "model": "scripted-model-1",
        "latency_ms": 700,
        "usage": _usage(),
    }


def _read_case_step(case_id: str, step: int = 0) -> dict[str, Any]:
    """The tool_use turn that reads the whole case: status, invoice, findings and the policy."""
    return _tool_use_step(
        case_id,
        step,
        [
            ("get_extraction_status", {"case_id": case_id}),
            ("get_invoice", {"case_id": case_id}),
            ("get_validator_findings", {"case_id": case_id}),
            ("get_policy", {}),
        ],
    )


def _answer_step(outcome: str, cause: str) -> dict[str, Any]:
    decision = {
        "evidence": [{"source": "get_policy", "ref": "buyer_cnpj", "value": _buyer_cnpj()}],
        "reason": f"Scripted {outcome} ({cause}).",
        "outcome": outcome,
        "cause": cause,
    }
    return {
        "stop_reason": "end_turn",
        "text": json.dumps(decision),
        "model": "scripted-model-1",
        "latency_ms": 500,
        "usage": _usage(1900, 90),
    }


def _write_script(directory: Path, case_id: str, steps: list[dict[str, Any]]) -> None:
    target = directory / "decisions"
    target.mkdir(parents=True, exist_ok=True)
    (target / f"{case_id}.json").write_text(json.dumps({"steps": steps}), encoding="utf-8")


def _write_expected_scripts(directory: Path) -> None:
    """Each case reads the case, then answers its label; dc-008 answers approve instead."""
    for case_id, entry in _expected().items():
        if case_id == "dc-008":
            answer = _answer_step("approve", "none")
        else:
            answer = _answer_step(entry["decision"], entry["cause"])
        _write_script(directory, case_id, [_read_case_step(case_id), answer])


@pytest.fixture
def decision_responses_dir(tmp_path: Path) -> Path:
    directory = tmp_path / "responses"
    directory.mkdir()
    _write_expected_scripts(directory)
    return directory


@pytest.fixture
def decision_host(
    tmp_path: Path, decision_responses_dir: Path, tools_mcp_url: str
) -> Iterator[tuple[str, str]]:
    with _scripted_api(tmp_path, decision_responses_dir, tools_mcp_url) as host:
        yield host


def _run_records(run_dir: Path) -> dict[str, dict[str, Any]]:
    lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").splitlines()
    return {r["case_id"]: r for r in map(json.loads, lines)}


# --- tracer ---------------------------------------------------------------------------------------


def test_tracer_one_decision_case_travels_over_two_processes_and_grades_correct(
    tmp_path: Path, decision_host: tuple[str, str]
) -> None:
    base_url, api_key = decision_host
    run_dir = tmp_path / "runs" / "run-tracer"

    run = _evals(
        "decisions", "--cases", str(DECISION_DIR), "--base-url", base_url,
        "--out", str(run_dir), "--run-id", "run-tracer", "--case", "dc-001",
        api_key=api_key,
    )  # fmt: skip
    assert run.returncode == 0, f"{run.stdout}\n{run.stderr}"

    records = _run_records(run_dir)
    assert set(records) == {"dc-001"}
    record = records["dc-001"]
    assert record["status"] == "completed"
    assert record["http"]["status"] == 200
    response = record["response"]
    assert response["contract_version"] == "decision-1"
    outcome = response["outcome"]
    assert outcome["status"] == "decided", outcome
    assert outcome["decision"]["outcome"] == "approve"
    assert outcome["decided_by"] == "model"
    assert len(response["steps"]) == 2
    model_calls = [
        call
        for step in response["steps"]
        for call in step["tool_calls"]
        if call["source"] == "model"
    ]
    assert [call["name"] for call in model_calls] == [
        "get_extraction_status",
        "get_invoice",
        "get_validator_findings",
        "get_policy",
    ]
    assert all(call["outcome"] == "ok" for call in model_calls)
    assert outcome["guard"]["tool_call"]["source"] == "guard"
    assert outcome["guard"]["overridden"] is False

    graded = _evals("grade-decisions", "--run", str(run_dir), "--cases", str(DECISION_DIR))
    assert graded.returncode == 0, f"{graded.stdout}\n{graded.stderr}"
    summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    assert summary["accuracy"]["n"] == 1
    assert summary["accuracy"]["correct"] == 1
    assert summary["counts"]["decided"] == 1
    # No key reaches a stored artifact.
    for path in run_dir.rglob("*"):
        if path.is_file():
            assert api_key not in path.read_text(encoding="utf-8", errors="replace"), path.name
