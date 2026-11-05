"""E2E: all 154 dataset cases over real HTTP to a full graded summary, against a scripted model.

Starts the real ASP.NET composition (``dotnet/tests/Carimbo.ScriptedHost``) with a scripted
``ILlmGateway`` and drives it through the documented commands (``carimbo-evals run``, ``grade``
and ``compare``) as subprocesses. The scripted host reads ``<sha256 of the PDF>.json`` on every
call, so the answers are rewritten between runs. No provider is reachable from here and nothing is
spent (T-05-21).

The seeded answers come from the corruption catalogue the pytest self-tests use
(``carimbo_evals.corruptions.corrupted``), so this proof cannot drift from them (T-05-22).
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
from decimal import Decimal
from pathlib import Path
from typing import Any

import httpx2
import pytest

from carimbo_evals.corruptions import corrupted
from carimbo_evals.grader import load_ground_truth

pytestmark = pytest.mark.e2e

REPO_ROOT = Path(__file__).resolve().parents[2]
CASES_DIR = REPO_ROOT / "data" / "dataset"
STARTUP_TIMEOUT_S = 180.0
# At scripted usage a full run with repairs exceeds the default US$1.00 cap (it stops at case 75
# of 154), so every scripted run here passes a higher cap. Nothing is spent: the model is scripted.
MAX_COST_USD = "100"

# Kinds the real validators let through as success (silent) and kinds they end validation_failed.
SILENT = ("text_near_miss", "text_wrong", "tax_code_wrong", "date_wrong")
CAUGHT = (
    "thousands_misparse",
    "value_from_other_box",
    "digit_transposition",
    "digit_substitution",
    "invented_row",
)
ROTATION: tuple[str | None, ...] = (None, *SILENT, *CAUGHT)
OVERRIDES = {
    "nfe-0002": "refused",
    "nfe-0003": "truncated",
    "nfe-0004": "schema_invalid",
    "nfe-0005": "infrastructure_failure",
}


def _scripted(stop_reason: str, text: str) -> dict[str, Any]:
    return {
        "stop_reason": stop_reason,
        "text": text,
        "stop_detail": "scripted refusal" if stop_reason == "refusal" else None,
        "model": "scripted-model-1",
        "latency_ms": 1234,
        "usage": {
            "input_tokens": 1200,
            "output_tokens": 80,
            "cache_read_tokens": 0,
            "cache_write_5m_tokens": 300,
            "cache_write_1h_tokens": 0,
        },
    }


def _answer(invoice: dict[str, Any]) -> dict[str, Any]:
    return _scripted("end_turn", json.dumps(invoice))


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


def _manifest() -> dict[str, Any]:
    return json.loads((CASES_DIR / "manifest.json").read_text(encoding="utf-8"))


def _truths() -> dict[str, dict[str, Any]]:
    """The ground-truth invoice of every manifest case, by case id."""
    return {
        case["case_id"]: load_ground_truth(CASES_DIR / case["xml"]).invoice
        for case in _manifest()["cases"]
    }


def write_answers(directory: Path, answers: dict[str, dict[str, Any]]) -> None:
    """Write ``<pdf_sha256>.json`` for each manifest case from ``answers`` (by case id)."""
    for case in _manifest()["cases"]:
        script = answers[case["case_id"]]
        (directory / f"{case['pdf_sha256']}.json").write_text(json.dumps(script), encoding="utf-8")


def truth_answers() -> dict[str, dict[str, Any]]:
    return {case_id: _answer(invoice) for case_id, invoice in _truths().items()}


def seeded_answers() -> tuple[dict[str, dict[str, Any]], dict[str, str], dict[str, str | None]]:
    """Scripted answers per case, the expected status per case and the seeded tag per case.

    Walks the manifest cases sorted by id. An id in ``OVERRIDES`` is a refusal, a truncation, a
    ``digit_count`` corruption (schema_invalid) or an infrastructure failure; any other case takes
    ``ROTATION[index % len(ROTATION)]``, ``None`` being the ground truth.
    """
    truths = _truths()
    answers: dict[str, dict[str, Any]] = {}
    statuses: dict[str, str] = {}
    tags: dict[str, str | None] = {}
    for index, case_id in enumerate(sorted(truths)):
        tags[case_id] = None
        override = OVERRIDES.get(case_id)
        if override == "refused":
            answers[case_id], statuses[case_id] = _scripted("refusal", ""), "refused"
        elif override == "truncated":
            answers[case_id], statuses[case_id] = _scripted("max_tokens", "{"), "truncated"
        elif override == "schema_invalid":
            invoice = corrupted("digit_count", truths[case_id], case_id)
            assert invoice is not None
            answers[case_id], statuses[case_id] = _answer(invoice), "schema_invalid"
            tags[case_id] = "schema_invalid"
        elif override == "infrastructure_failure":
            answers[case_id] = {"failure": {"kind": "server_error", "http_status": 500}}
            statuses[case_id] = "infrastructure_failure"
        else:
            tag = ROTATION[index % len(ROTATION)]
            if tag is None:
                answers[case_id], statuses[case_id] = _answer(truths[case_id]), "success"
            else:
                invoice = corrupted(tag, truths[case_id], case_id)
                assert invoice is not None, f"{tag} does not apply to {case_id}"
                answers[case_id] = _answer(invoice)
                statuses[case_id] = "success" if tag in SILENT else "validation_failed"
                tags[case_id] = tag
    return answers, statuses, tags


@pytest.fixture(scope="module")
def responses_dir(tmp_path_factory: pytest.TempPathFactory) -> Path:
    directory = tmp_path_factory.mktemp("responses")
    write_answers(directory, truth_answers())
    return directory


@pytest.fixture(scope="module")
def work_dir(tmp_path_factory: pytest.TempPathFactory) -> Path:
    return tmp_path_factory.mktemp("full-dataset")


@pytest.fixture(scope="module")
def host(work_dir: Path, responses_dir: Path) -> Iterator[tuple[str, str]]:
    if shutil.which("dotnet") is None:
        pytest.fail(
            "dotnet is not on PATH; run this test inside `nix shell nixpkgs#dotnet-sdk_10 -c ...`"
        )
    port = _free_port()
    base_url = f"http://127.0.0.1:{port}"
    api_key = secrets.token_urlsafe(32)
    env = {
        **os.environ,
        "ASPNETCORE_ENVIRONMENT": "Development",
        "Llm__Cache__Directory": str(work_dir / "llm-cache"),
        "Llm__Cache__Mode": "read_write",
        "CARIMBO_EVAL_API_KEY": api_key,
        "CARIMBO_SCRIPTED_RESPONSES_DIR": str(responses_dir),
        "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    }
    out_path = work_dir / "host.out.log"
    err_path = work_dir / "host.err.log"
    with out_path.open("wb") as out, err_path.open("wb") as err:
        process = subprocess.Popen(
            [
                "dotnet",
                "run",
                "--project",
                "dotnet/tests/Carimbo.ScriptedHost",
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
                        f"ScriptedHost exited early ({process.returncode}):\n"
                        f"{_tail(err_path)}\n{_tail(out_path)}"
                    )
                try:
                    if httpx2.get(f"{base_url}/healthz", timeout=2.0).status_code == 200:
                        break
                except httpx2.TransportError:
                    pass
                if time.monotonic() > deadline:
                    pytest.fail(
                        f"ScriptedHost not healthy after {STARTUP_TIMEOUT_S}s:\n{_tail(out_path)}"
                    )
                time.sleep(0.5)
            yield base_url, api_key
        finally:
            _stop(process)


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


def _run_and_grade(
    host: tuple[str, str], run_dir: Path, run_id: str, *extra: str
) -> dict[str, Any]:
    """``run`` then ``grade`` as the documented commands; returns the console script's summary."""
    base_url, api_key = host
    run = _evals(
        "run",
        "--cases",
        str(CASES_DIR),
        "--base-url",
        base_url,
        "--out",
        str(run_dir),
        "--run-id",
        run_id,
        "--concurrency",
        "4",
        "--max-cost-usd",
        MAX_COST_USD,
        "--cache-mode",
        "off",
        *extra,
        api_key=api_key,
    )
    assert run.returncode == 0, f"{run.stdout}\n{run.stderr}"
    grade = _evals("grade", "--run", str(run_dir), "--cases", str(CASES_DIR), "--resamples", "500")
    assert grade.returncode == 0, f"{grade.stdout}\n{grade.stderr}"
    return json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))


def _recorded_ids(run_dir: Path) -> list[str]:
    lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").splitlines()
    return [json.loads(line)["case_id"] for line in lines]


def _row(summary: dict[str, Any], scope: str, name: str) -> tuple[int, int]:
    row = summary["headline"][scope][name]
    return row["cases"], row["invoices"]


def test_ground_truth_for_all_154_cases_grades_to_a_full_summary(
    work_dir: Path, host: tuple[str, str], responses_dir: Path
) -> None:
    write_answers(responses_dir, truth_answers())
    run_dir = work_dir / "runs" / "run-truth"
    summary = _run_and_grade(host, run_dir, "run-truth")

    assert len(_recorded_ids(run_dir)) == 154
    assert summary["counts"]["success"] == 154
    assert summary["counts"]["total"] == 154
    assert all(case["correct"] is True for case in summary["cases"])

    # D-13: the six headline rows (cases, distinct invoices).
    assert _row(summary, "test", "text") == (60, 60)
    assert _row(summary, "test", "image") == (40, 5)
    assert _row(summary, "dev", "text") == (30, 30)
    assert _row(summary, "dev", "image") == (24, 3)
    assert _row(summary, "full", "text") == (90, 90)
    assert _row(summary, "full", "image") == (64, 8)
    test_text = summary["headline"]["test"]["text"]
    assert test_text["rate"] == 1.0
    assert test_text["ci95"][1] == 1.0

    assert summary["incomplete"] is False
    assert summary["failure_tags"]["wrong_values"] == 0
    assert summary["slices"]["test"]["rendering=rotation/mild"]["cases"] == 5
    assert summary["items"]["full"]["recall"]["value"] == 1.0
    assert summary["model_latency_ms"]["p50"] == 1234
    assert summary["model_latency_ms"]["p95"] == 1234
    assert summary["cache"]["answered_attempts"] == 154
    assert Decimal(summary["totals"]["cost_usd"]) > 0
    assert summary["dataset"]["version"] == "dataset-001"
