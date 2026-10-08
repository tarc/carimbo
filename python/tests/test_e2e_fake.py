"""E2E: the committed skeleton cases over real HTTP to a graded summary, against a scripted model.

Starts the real ASP.NET composition (``dotnet/tests/Carimbo.ScriptedHost``) with a scripted
``ILlmGateway`` and drives it through the documented commands: ``carimbo-evals run`` and then
``carimbo-evals grade``, as subprocesses.
"""

from __future__ import annotations

import copy
import hashlib
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

from carimbo_evals.grader import GroundTruth, load_ground_truth, total_within_tolerance
from carimbo_evals.runner import new_traceparent
from carimbo_evals.summary import FIELDS

pytestmark = pytest.mark.e2e

REPO_ROOT = Path(__file__).resolve().parents[2]
CASES_DIR = REPO_ROOT / "data" / "skeleton"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
STARTUP_TIMEOUT_S = 180.0
# 32 integer digits: parses as a money string but does not fit in a decimal.
OVERSIZED_AMOUNT = "99999999999999999999999999999999.00"
ALL_FIELDS = list(FIELDS)
# The skeleton manifest as_of_date, which the runner sends as reference_date (D-11).
AS_OF_DATE = "2026-10-01"


def _invoice_json(truth: GroundTruth, total: Decimal) -> str:
    """The scripted answer: the ground-truth invoice with ``totals.invoice_total`` replaced."""
    invoice = copy.deepcopy(truth.invoice)
    invoice["totals"]["invoice_total"] = format(total, ".2f")
    return json.dumps(invoice)


def _scripted(stop_reason: str, text: str) -> dict[str, Any]:
    return {
        "stop_reason": stop_reason,
        "text": text,
        "stop_detail": "scripted refusal" if stop_reason == "refusal" else None,
        "model": "scripted-model-1",
        "usage": {
            "input_tokens": 1200,
            "output_tokens": 80,
            "cache_read_tokens": 0,
            "cache_write_5m_tokens": 300,
            "cache_write_1h_tokens": 0,
        },
    }


@pytest.fixture
def responses_dir(tmp_path: Path) -> Path:
    """Scripted model replies keyed by the SHA-256 of each committed skeleton PDF.

    case-001 is answered correctly, case-002 with a total that is 1.00 too high (caught by the
    validators: validation_failed), case-003 refused.
    """
    directory = tmp_path / "responses"
    directory.mkdir()
    truths = {case_id: load_ground_truth(CASES_DIR / f"{case_id}.xml") for case_id in _CASE_IDS}
    scripted = {
        "case-001": _scripted(
            "end_turn", _invoice_json(truths["case-001"], truths["case-001"].invoice_total)
        ),
        "case-002": _scripted(
            "end_turn",
            _invoice_json(truths["case-002"], truths["case-002"].invoice_total + Decimal("1.00")),
        ),
        "case-003": _scripted("refusal", ""),
    }
    for case_id in _CASE_IDS:
        digest = hashlib.sha256((CASES_DIR / f"{case_id}.pdf").read_bytes()).hexdigest()
        (directory / f"{digest}.json").write_text(json.dumps(scripted[case_id]), encoding="utf-8")
    return directory


_CASE_IDS = ("case-001", "case-002", "case-003")


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


@pytest.fixture
def host(tmp_path: Path, responses_dir: Path) -> Iterator[tuple[str, str]]:
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
        "CARIMBO_EVAL_API_KEY": api_key,
        "CARIMBO_SCRIPTED_RESPONSES_DIR": str(responses_dir),
        "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    }
    out_path = tmp_path / "host.out.log"
    err_path = tmp_path / "host.err.log"
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


def test_skeleton_cases_over_http_through_the_documented_commands(
    tmp_path: Path, host: tuple[str, str]
) -> None:
    base_url, api_key = host

    # No key: 401, and nothing is echoed back.
    anonymous = httpx2.post(
        f"{base_url}/eval/extractions",
        json={
            "contract_version": "2",
            "case_id": "x",
            "document": {"media_type": "application/pdf", "content_base64": "JVBERg=="},
        },
    )
    assert anonymous.status_code == 401
    assert api_key not in anonymous.text
    # Wrong key: 401 as well.
    wrong = httpx2.post(
        f"{base_url}/eval/extractions",
        headers={"X-Api-Key": "not-the-key"},
        json={"contract_version": "2", "case_id": "x"},
    )
    assert wrong.status_code == 401

    # The runner refuses to start without the key.
    run_dir = tmp_path / "runs" / "run-e2e"
    keyless = _evals("run", "--base-url", base_url, "--out", str(run_dir))
    assert keyless.returncode == 2, keyless.stderr

    run = _evals(
        "run",
        "--cases",
        str(CASES_DIR),
        "--base-url",
        base_url,
        "--out",
        str(run_dir),
        "--run-id",
        "run-e2e",
        api_key=api_key,
    )
    assert run.returncode == 0, f"{run.stdout}\n{run.stderr}"
    for case_id in _CASE_IDS:
        assert case_id in run.stdout

    lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").splitlines()
    assert len(lines) == 3
    records = {r["case_id"]: r for r in map(json.loads, lines)}
    assert set(records) == set(_CASE_IDS)
    for record in records.values():
        assert record["record_version"] == 2
        assert record["request"]["reference_date"] == AS_OF_DATE
        assert record["status"] == "completed"
        assert record["http"]["status"] == 200
        response = record["response"]
        assert response["contract_version"] == "2"
        assert response["effective"]["reference_date"] == AS_OF_DATE
        assert len(response["attempts"]) == 1
        assert response["attempts"][0]["index"] == 0
        assert response["attempts"][0]["kind"] == "initial"
        assert response["case_id"] == record["case_id"]
        assert response["trace_id"] == record["request"]["trace_id"]
        assert record["request"]["traceparent"].split("-")[1] == response["trace_id"]
        assert response["usage"]["input_tokens"] == 1200
        assert response["usage"]["cache_write_5m_tokens"] == 300
        assert response["effective"]["model"] == "claude-haiku-4-5"
        assert response["model_returned"] == "scripted-model-1"
        assert "raw_output" in response["outcome"]
    first = records["case-001"]["response"]["outcome"]
    assert first["status"] == "success"
    assert not [f for f in first["findings"] if f["severity"] == "error"]
    caught = records["case-002"]["response"]
    assert caught["outcome"]["status"] == "validation_failed"
    assert "TOTAL_VNF_FORMULA" in {f["rule_id"] for f in caught["outcome"]["findings"]}
    assert all(f["severity"] == "error" for f in caught["outcome"]["findings"])
    assert caught["outcome"]["invoice"] is not None  # the candidate is returned and graded
    assert caught["attempts"][0]["status"] == "validation_failed"
    assert records["case-003"]["response"]["outcome"]["status"] == "refused"
    assert records["case-003"]["response"]["stop_reason"] == "refusal"
    run_json = json.loads((run_dir / "run.json").read_text(encoding="utf-8"))
    assert run_json["stopped_reason"] is None
    assert run_json["completed"] == 3

    grade = _evals(
        "grade",
        "--run",
        str(run_dir),
        "--cases",
        str(CASES_DIR),
        "--schema",
        str(SCHEMA),
    )
    assert grade.returncode == 0, f"{grade.stdout}\n{grade.stderr}"
    assert "case-001" in grade.stdout

    summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    assert (run_dir / "summary.md").is_file()
    counts = summary["counts"]
    assert (counts["success"], counts["validation_failed"], counts["refused"]) == (1, 1, 1)
    assert counts["total"] == 3
    assert counts["harness_error"] == 0
    graded = {c["case_id"]: c for c in summary["cases"]}
    assert graded["case-001"]["fields"] == dict.fromkeys(ALL_FIELDS, True)
    assert graded["case-001"]["caught"] is False
    assert graded["case-001"]["validator"]["errors"] == 0
    assert graded["case-001"]["attempt_count"] == 1
    assert graded["case-001"]["schema_valid_jsonschema"] is True
    assert graded["case-001"]["schema_valid_pydantic"] is True
    assert graded["case-002"]["status"] == "validation_failed"
    assert graded["case-002"]["caught"] is True
    assert "TOTAL_VNF_FORMULA" in graded["case-002"]["validator"]["rule_ids"]
    assert graded["case-002"]["fields"]["totals.invoice_total"] is False
    assert graded["case-002"]["total_delta"] == "1.00"
    assert [f for f, ok in graded["case-002"]["fields"].items() if not ok] == [
        "totals.invoice_total"
    ]
    assert graded["case-003"]["status"] == "refused"
    assert "fields" not in graded["case-003"]
    assert summary["field_accuracy"]["access_key"] == {"correct": 2, "n": 2}
    assert summary["field_accuracy"]["totals.invoice_total"] == {"correct": 1, "n": 2}
    assert summary["validation"]["caught"] == 1
    assert summary["validation"]["rule_counts"]["TOTAL_VNF_FORMULA"] == 1
    assert summary["dataset"]["version"] == "skeleton-002"
    assert summary["totals"]["input_tokens"] == 3600

    for path in run_dir.rglob("*"):
        if path.is_file():
            assert api_key not in path.read_text(encoding="utf-8", errors="replace"), path.name


class TestTypedEdgeOutcomesOverHttp:
    """Model answers the schema forbids end as typed ``schema_invalid`` records, never HTTP 5xx.

    Covers both gap truths of the phase verification across the HTTP boundary: an amount too large
    for a decimal (CR-01) and a lowercase issuer CNPJ, which breaks the uppercase-only pattern
    (WR-03 family). The class-level ``responses_dir`` replaces the module fixture for the module
    ``host`` fixture, so the ScriptedHost serves these replies.
    """

    @pytest.fixture
    def responses_dir(self, tmp_path: Path) -> Path:
        directory = tmp_path / "responses"
        directory.mkdir()
        replies: dict[str, str] = {}
        for case_id in _CASE_IDS:
            truth = load_ground_truth(CASES_DIR / f"{case_id}.xml")
            answer = json.loads(_invoice_json(truth, truth.invoice_total))
            if case_id == "case-001":
                answer["totals"]["invoice_total"] = OVERSIZED_AMOUNT
            elif case_id == "case-003":
                answer["issuer"]["cnpj"] = answer["issuer"]["cnpj"].lower()
            replies[case_id] = json.dumps(answer)
        for case_id in _CASE_IDS:
            digest = hashlib.sha256((CASES_DIR / f"{case_id}.pdf").read_bytes()).hexdigest()
            (directory / f"{digest}.json").write_text(
                json.dumps(_scripted("end_turn", replies[case_id])), encoding="utf-8"
            )
        self.replies = replies
        return directory

    def test_unrepresentable_amount_and_pattern_violation_are_schema_invalid_end_to_end(
        self, tmp_path: Path, host: tuple[str, str]
    ) -> None:
        base_url, api_key = host
        assert len(OVERSIZED_AMOUNT.split(".")[0]) == 32
        lowercase_cnpj = json.loads(self.replies["case-003"])["issuer"]["cnpj"]
        assert lowercase_cnpj != lowercase_cnpj.upper()  # case-003 has an alphanumeric issuer CNPJ

        run_dir = tmp_path / "runs" / "run-e2e-edge"
        run = _evals(
            "run",
            "--cases",
            str(CASES_DIR),
            "--base-url",
            base_url,
            "--out",
            str(run_dir),
            "--run-id",
            "run-e2e-edge",
            api_key=api_key,
        )
        assert run.returncode == 0, f"{run.stdout}\n{run.stderr}"

        lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").splitlines()
        records = {r["case_id"]: r for r in map(json.loads, lines)}
        assert set(records) == set(_CASE_IDS)
        costs: list[Decimal] = []
        for record in records.values():
            assert record["status"] == "completed"
            assert record["http"]["status"] == 200
            cost = Decimal(record["response"]["cost_usd"])
            assert cost > 0
            costs.append(cost)
        outcomes = {case_id: r["response"]["outcome"] for case_id, r in records.items()}
        for case_id, fragment in (("case-001", "fits in a decimal"), ("case-003", "issuer.cnpj")):
            outcome = outcomes[case_id]
            assert outcome["status"] == "schema_invalid"
            assert outcome["failure"]["kind"] == "schema_invalid"
            assert fragment in outcome["failure"]["message"]
            assert outcome["raw_output"] == self.replies[case_id]
        assert outcomes["case-002"]["status"] == "success"
        assert outcomes["case-002"]["findings"] == []

        run_json = json.loads((run_dir / "run.json").read_text(encoding="utf-8"))
        assert run_json["harness_errors"] == 0
        assert run_json["completed"] == 3
        assert run_json["stopped_reason"] is None
        assert Decimal(run_json["assumed_usd"]) == 0
        assert Decimal(run_json["spent_usd"]) == sum(costs, Decimal(0))

        grade = _evals(
            "grade",
            "--run",
            str(run_dir),
            "--cases",
            str(CASES_DIR),
            "--schema",
            str(SCHEMA),
        )
        assert grade.returncode == 0, f"{grade.stdout}\n{grade.stderr}"

        summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
        counts = summary["counts"]
        assert (counts["success"], counts["schema_invalid"]) == (1, 2)
        assert (counts["harness_error"], counts["total"]) == (0, 3)
        graded = {c["case_id"]: c for c in summary["cases"]}
        assert graded["case-001"]["fields"] == dict.fromkeys(ALL_FIELDS, False)
        assert graded["case-002"]["fields"] == dict.fromkeys(ALL_FIELDS, True)
        assert graded["case-003"]["fields"] == dict.fromkeys(ALL_FIELDS, False)
        # The grader's own schema verdict agrees with .NET for the pattern violation. The oversized
        # amount is NOT asserted: the money pattern has no length bound, so Python still calls it
        # schema-valid (bounding it is deferred to Phase 2, DOM-04).
        assert graded["case-003"]["schema_valid_jsonschema"] is False
        assert graded["case-003"]["schema_valid_pydantic"] is False
        assert summary["field_accuracy"]["issuer.cnpj"] == {"correct": 1, "n": 3}

        for path in run_dir.rglob("*"):
            if path.is_file():
                assert api_key not in path.read_text(encoding="utf-8", errors="replace"), path.name


def test_traceparent_shape() -> None:
    traceparent, trace_id = new_traceparent()
    version, tid, span, flags = traceparent.split("-")
    assert (version, flags) == ("00", "01")
    assert tid == trace_id
    assert len(tid) == 32 and len(span) == 16
    assert int(tid, 16) != 0


def test_decimal_tolerance_is_inclusive() -> None:
    assert total_within_tolerance("100.01", Decimal("100.00"), Decimal("0.01"))
    assert not total_within_tolerance("100.02", Decimal("100.00"), Decimal("0.01"))
    assert not total_within_tolerance("not-a-number", Decimal("100.00"), Decimal("0.01"))
