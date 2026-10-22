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
from decimal import Decimal
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
    # The scripted gateway reports the provider message id the real one does, one per step.
    assert [step["provider_message_id"] for step in response["steps"]] == [
        "msg_scripted_0",
        "msg_scripted_1",
    ]
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


# --- every decision, every cause, and a free replay ---------------------------------------------


def _decisions_run(
    base_url: str, api_key: str, run_dir: Path, *flags: str
) -> subprocess.CompletedProcess[str]:
    return _evals(
        "decisions", "--cases", str(DECISION_DIR), "--base-url", base_url,
        "--out", str(run_dir), "--run-id", run_dir.name, *flags,
        api_key=api_key,
    )  # fmt: skip


def _grade(run_dir: Path) -> dict[str, Any]:
    graded = _evals("grade-decisions", "--run", str(run_dir), "--cases", str(DECISION_DIR))
    assert graded.returncode == 0, f"{graded.stdout}\n{graded.stderr}"
    return json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))


def _run_json(run_dir: Path) -> dict[str, Any]:
    return json.loads((run_dir / "run.json").read_text(encoding="utf-8"))


def _answered_steps(response: dict[str, Any]) -> list[dict[str, Any]]:
    return [step for step in response["steps"] if step["usage"] is not None]


def test_all_eight_cases_are_decided_correctly_and_a_read_only_rerun_is_free(
    tmp_path: Path, decision_host: tuple[str, str]
) -> None:
    base_url, api_key = decision_host
    first_dir = tmp_path / "runs" / "run-a"
    replay_dir = tmp_path / "runs" / "run-b"

    # Run A: the eight committed cases at concurrency 2 against the host default (read_write).
    first = _decisions_run(base_url, api_key, first_dir, "--concurrency", "2")
    assert first.returncode == 0, f"{first.stdout}\n{first.stderr}"
    run_a = _run_records(first_dir)
    assert set(run_a) == set(CASE_IDS)
    assert _run_json(first_dir)["completed"] == 8

    summary = _grade(first_dir)
    assert summary["counts"]["decided"] == 8
    assert summary["counts"]["total"] == 8
    assert (summary["accuracy"]["n"], summary["accuracy"]["correct"]) == (8, 8)
    assert (summary["cause_accuracy"]["n"], summary["cause_accuracy"]["correct"]) == (4, 4)
    assert (summary["citations"]["n"], summary["citations"]["valid"]) == (8, 8)
    assert summary["guard"]["overridden"] == 1
    # The summary is sorted by case id, whatever order the records completed in.
    assert [case["case_id"] for case in summary["cases"]] == list(CASE_IDS)
    graded = {case["case_id"]: case for case in summary["cases"]}
    decisions = [case["decision"] for case in summary["cases"]]
    assert (decisions.count("approve"), decisions.count("reject"), decisions.count("escalate")) == (
        2,
        2,
        4,
    )
    escalations = {
        c["case_id"]: c["cause"] for c in summary["cases"] if c["decision"] == "escalate"
    }
    assert escalations == {
        "dc-003": "policy_warning",
        "dc-005": "extraction_not_success",
        "dc-006": "over_limit",
        "dc-008": "extraction_not_success",
    }
    expected = _expected()
    for case_id, entry in expected.items():
        assert graded[case_id]["decision"] == entry["decision"], case_id
        label_cause = entry["cause"] if entry["decision"] == "escalate" else "none"
        assert graded[case_id]["cause"] == label_cause, case_id
        assert graded[case_id]["decision_correct"] is True, case_id
    # The guard decides every case whose extraction did not succeed (dc-005, dc-008) and overrode
    # dc-008's scripted approve; the model decides the rest.
    assert graded["dc-008"]["guard_overridden"] is True
    assert graded["dc-008"]["decided_by"] == "guard"
    assert graded["dc-008"]["model_decision_correct"] is False
    assert [c["case_id"] for c in summary["cases"] if c["guard_overridden"]] == ["dc-008"]
    for case_id in CASE_IDS:
        expected_by = "guard" if case_id in ("dc-005", "dc-008") else "model"
        assert graded[case_id]["decided_by"] == expected_by, case_id

    # Every case read the case through the Tools host, then answered: two steps, cache misses that
    # were paid for in full on the recording run.
    for case_id, record in run_a.items():
        response = record["response"]
        assert record["request"]["overrides"] is None, case_id
        assert response["effective"]["cache_mode"] == "read_write", case_id
        assert len(response["steps"]) == 2, case_id
        for step in response["steps"]:
            assert step["http_attempts"] == 1, case_id
            assert step["cache"]["status"] == "miss", case_id
        assert response["incurred_cost_usd"] == response["cost_usd"], case_id
    run_a_json = _run_json(first_dir)
    assert Decimal(run_a_json["spent_usd"]) == Decimal(run_a_json["notional_usd"]) > 0
    dc_008 = run_a["dc-008"]["response"]["outcome"]
    assert dc_008["model_decision"]["outcome"] == "approve"
    assert dc_008["decision"]["outcome"] == "escalate"
    assert dc_008["decision"]["cause"] == "extraction_not_success"
    assert dc_008["guard"]["extraction_status"] != "success"
    assert dc_008["guard"]["overridden"] is True
    assert api_key not in json.dumps(run_a)

    # Run B (AGT-06): the same cases, read_only, into a new directory. Zero model calls.
    replay = _decisions_run(base_url, api_key, replay_dir, "--cache-mode", "read_only")
    assert replay.returncode == 0, f"{replay.stdout}\n{replay.stderr}"
    run_b = _run_records(replay_dir)
    assert set(run_b) == set(CASE_IDS)
    for case_id, record in run_b.items():
        assert record["request"]["overrides"] == {"cache_mode": "read_only"}, case_id
        response = record["response"]
        assert response["outcome"]["status"] == "decided", case_id
        steps = _answered_steps(response)
        assert len(steps) == len(run_a[case_id]["response"]["steps"]) == 2, case_id
        for before, after in zip(run_a[case_id]["response"]["steps"], steps, strict=True):
            assert after["cache"]["status"] == "hit", case_id
            assert after["cache"]["mode"] == "read_only", case_id
            assert after["http_attempts"] == 0, case_id
            assert after["cost_usd"] == before["cost_usd"], case_id
            assert after["latency_ms"] == before["latency_ms"], case_id
        assert response["incurred_cost_usd"] == "0.00000000", case_id
        assert response["cost_usd"] == run_a[case_id]["response"]["cost_usd"], case_id
        original = run_a[case_id]["response"]["outcome"]
        assert response["outcome"]["decision"] == original["decision"], case_id
        assert response["outcome"]["decided_by"] == original["decided_by"], case_id
    run_b_json = _run_json(replay_dir)
    assert Decimal(run_b_json["spent_usd"]) == 0
    assert Decimal(run_b_json["assumed_usd"]) == 0
    assert run_b_json["unpriced_cases"] == 0
    assert Decimal(run_b_json["notional_usd"]) == Decimal(run_a_json["notional_usd"])
    replayed = _grade(replay_dir)
    assert replayed["cache"] == {"hits": 16, "answered_steps": 16, "hit_rate": 1.0}
    assert summary["cache"] == {"hits": 0, "answered_steps": 16, "hit_rate": 0.0}
    assert Decimal(replayed["totals"]["incurred_cost_usd"]) == 0
    assert replayed["totals"]["cost_usd"] == summary["totals"]["cost_usd"]
    assert (replayed["accuracy"]["n"], replayed["accuracy"]["correct"]) == (8, 8)
    assert replayed["config"]["cache_modes"] == ["read_only"]
    assert api_key not in (replay_dir / "cases.jsonl").read_text(encoding="utf-8")


def _budget_script(case_id: str) -> list[dict[str, Any]]:
    """Six tool_use turns and never an answer: the whole step budget is spent reading."""
    return [
        _tool_use_step(
            case_id, step, [("get_policy", {}), ("get_extraction_status", {"case_id": case_id})]
        )
        for step in range(6)
    ]


@pytest.fixture(scope="module")
def fault_host(
    tmp_path_factory: pytest.TempPathFactory, tools_mcp_url: str
) -> Iterator[tuple[str, str]]:
    workdir = tmp_path_factory.mktemp("fault-host")
    responses = workdir / "responses"
    responses.mkdir()
    _write_script(responses, "dc-001", _budget_script("dc-001"))
    _write_script(responses, "dc-004", [_tool_use_step("dc-004", 0, [("approve_payment", {})])])
    _write_script(responses, "dc-005", [_answer_step("approve", "none")])
    _write_script(
        responses,
        "dc-007",
        [
            {
                "stop_reason": "refusal",
                "text": "",
                "stop_detail": "scripted refusal",
                "model": "scripted-model-1",
                "latency_ms": 300,
                "usage": _usage(1200, 10),
            }
        ],
    )
    _write_script(
        responses,
        "dc-999",
        [_tool_use_step("dc-999", 0, [("get_invoice", {"case_id": "dc-999"})])],
    )
    with _scripted_api(workdir, responses, tools_mcp_url) as host:
        yield host


class TestFaultsAndEdges:
    """Typed failures and escalations that are not a model's call, one scripted host for the module.

    The scripts are keyed by case id, so one host serves all of them: dc-001 never answers,
    dc-004 calls an unlisted tool, dc-005 answers approve for a case whose extraction failed,
    dc-007 is refused and dc-999 (an id the Tools host does not hold) reads a missing case.
    """

    def test_each_fault_is_a_typed_outcome_and_the_summary_excludes_the_refusal(
        self, tmp_path: Path, fault_host: tuple[str, str]
    ) -> None:
        base_url, api_key = fault_host
        run_dir = tmp_path / "runs" / "run-faults"
        done = _decisions_run(
            base_url, api_key, run_dir,
            "--case", "dc-001", "--case", "dc-004", "--case", "dc-005", "--case", "dc-007",
        )  # fmt: skip
        assert done.returncode == 0, f"{done.stdout}\n{done.stderr}"
        records = _run_records(run_dir)
        assert set(records) == {"dc-001", "dc-004", "dc-005", "dc-007"}
        for record in records.values():
            assert record["status"] == "completed"
            assert record["http"]["status"] == 200

        # Step budget: exactly six recorded steps, the last one's calls skipped, not run.
        budget = records["dc-001"]["response"]
        assert budget["outcome"]["status"] == "decided"
        assert budget["outcome"]["decision"]["outcome"] == "escalate"
        assert budget["outcome"]["decision"]["cause"] == "step_budget_exhausted"
        assert budget["outcome"]["decided_by"] == "step_budget"
        assert budget["outcome"]["model_decision"] is None
        assert budget["outcome"]["guard"] is None
        assert len(budget["steps"]) == 6
        assert budget["effective"]["max_steps"] == 6
        for step in budget["steps"][:5]:
            assert [call["outcome"] for call in step["tool_calls"]] == ["ok", "ok"]
        last = budget["steps"][5]
        assert [call["outcome"] for call in last["tool_calls"]] == ["skipped", "skipped"]
        assert all(call["latency_ms"] is None for call in last["tool_calls"])

        # An unlisted tool is a recorded tool failure, not a decision of the model.
        unlisted = records["dc-004"]["response"]["outcome"]
        assert unlisted["status"] == "decided"
        assert unlisted["decision"]["outcome"] == "escalate"
        assert unlisted["decision"]["cause"] == "tool_failed"
        assert unlisted["decided_by"] == "tool_failure"
        assert unlisted["tool_failure"]["error_kind"] == "unknown_tool"
        assert unlisted["tool_failure"]["source"] == "model"
        assert unlisted["tool_failure"]["tool"] == "approve_payment"
        assert records["dc-004"]["response"]["steps"][0]["tool_calls"][0]["outcome"] == "error"

        # The guard turns an approve for a failed extraction into escalate / extraction_not_success.
        guarded = records["dc-005"]["response"]["outcome"]
        assert guarded["decision"]["outcome"] == "escalate"
        assert guarded["decision"]["cause"] == "extraction_not_success"
        assert guarded["model_decision"]["outcome"] == "approve"
        assert guarded["decided_by"] == "guard"
        assert guarded["guard"]["overridden"] is True
        assert records["dc-005"]["response"]["steps"][0]["tool_calls"] == []

        # A refusal is a typed failure: no decision, no tool calls.
        refused = records["dc-007"]["response"]["outcome"]
        assert refused["status"] == "refused"
        assert refused["decision"] is None
        assert refused["decided_by"] is None
        assert refused["failure"]["kind"] == "refused"

        summary = _grade(run_dir)
        assert summary["counts"]["decided"] == 3
        assert summary["counts"]["refused"] == 1
        assert summary["counts"]["total"] == 4
        # dc-007 is excluded from accuracy; the other three are graded and only dc-005 is right.
        assert (summary["accuracy"]["n"], summary["accuracy"]["correct"]) == (3, 1)
        graded = {case["case_id"]: case for case in summary["cases"]}
        assert graded["dc-007"]["decision_correct"] is None
        assert graded["dc-007"]["status"] == "refused"
        assert graded["dc-001"]["decision_correct"] is False
        assert graded["dc-004"]["decision_correct"] is False
        assert graded["dc-005"]["decision_correct"] is True
        assert graded["dc-005"]["cause_correct"] is True
        assert summary["guard"]["overridden"] == 1

    def test_an_unknown_case_id_is_a_tool_failure_but_the_runner_refuses_it_before_any_request(
        self, tmp_path: Path, fault_host: tuple[str, str]
    ) -> None:
        base_url, api_key = fault_host
        # The documented command knows only the committed cases: it exits 2 before any request.
        run_dir = tmp_path / "runs" / "run-unknown"
        unknown = _decisions_run(base_url, api_key, run_dir, "--case", "dc-999")
        assert unknown.returncode == 2, f"{unknown.stdout}\n{unknown.stderr}"
        assert "dc-999" in unknown.stderr
        assert not (run_dir / "cases.jsonl").exists()

        # A direct POST of the well-formed id the Tools host does not hold is a decided escalation.
        posted = httpx2.post(
            f"{base_url}/eval/decisions",
            headers={"X-Api-Key": api_key},
            json={"contract_version": "decision-1", "case_id": "dc-999"},
            timeout=60.0,
        )
        assert posted.status_code == 200, posted.text
        outcome = posted.json()["outcome"]
        assert outcome["status"] == "decided"
        assert outcome["decision"]["outcome"] == "escalate"
        assert outcome["decision"]["cause"] == "tool_failed"
        assert outcome["decided_by"] == "tool_failure"
        assert outcome["tool_failure"]["error_kind"] == "is_error"
        assert outcome["tool_failure"]["source"] == "model"
        assert outcome["tool_failure"]["tool"] == "get_invoice"
        assert api_key not in posted.text


def test_a_closed_tools_host_is_a_tool_failure_with_no_model_call(
    tmp_path: Path, decision_responses_dir: Path
) -> None:
    closed = f"http://127.0.0.1:{_free_port()}/mcp"
    with _scripted_api(tmp_path, decision_responses_dir, closed) as (base_url, api_key):
        posted = httpx2.post(
            f"{base_url}/eval/decisions",
            headers={"X-Api-Key": api_key},
            json={"contract_version": "decision-1", "case_id": "dc-001"},
            timeout=120.0,
        )
    assert posted.status_code == 200, posted.text
    body = posted.json()
    outcome = body["outcome"]
    assert outcome["status"] == "decided"
    assert outcome["decision"]["outcome"] == "escalate"
    assert outcome["decision"]["cause"] == "tool_failed"
    assert outcome["decided_by"] == "tool_failure"
    assert outcome["tool_failure"]["source"] == "list"
    assert outcome["model_decision"] is None
    assert body["steps"] == []
    assert body["usage"]["input_tokens"] == 0


def test_decisions_without_a_key_exit_2_and_write_no_record(tmp_path: Path) -> None:
    run_dir = tmp_path / "runs" / "run-keyless"
    keyless = _evals(
        "decisions", "--cases", str(DECISION_DIR), "--base-url", "http://127.0.0.1:9",
        "--out", str(run_dir), "--case", "dc-001",
    )  # fmt: skip
    assert keyless.returncode == 2, keyless.stderr
    assert not (run_dir / "cases.jsonl").exists()
