"""Tracer: one PDF over real HTTP to a graded summary, against a scripted model.

Starts the real ASP.NET composition (``dotnet/tests/Carimbo.ScriptedHost``) with a scripted
``ILlmGateway`` and drives it with the real runner and the offline grader.
"""

from __future__ import annotations

import hashlib
import json
import os
import secrets
import shutil
import signal
import socket
import subprocess
import time
from collections.abc import Iterator
from dataclasses import dataclass
from decimal import Decimal
from pathlib import Path
from typing import Any

import httpx2
import pytest

from carimbo_evals.grader import grade_run
from carimbo_evals.runner import discover_cases, new_traceparent, run_cases

pytestmark = pytest.mark.e2e

REPO_ROOT = Path(__file__).resolve().parents[2]
NFE_NS = "http://www.portalfiscal.inf.br/nfe"
STARTUP_TIMEOUT_S = 180.0


@dataclass(frozen=True)
class SyntheticCase:
    case_id: str
    access_key: str
    number: int
    series: int
    issue_date: str
    issuer_cnpj: str
    issuer_name: str
    recipient_cnpj: str
    recipient_name: str
    total: str


CASES = [
    SyntheticCase(
        "case-ok",
        "261000AAAAAAAAAAAA" + "1" * 26,
        1001,
        1,
        "2026-03-15",
        "AAAAAAAAAAAA01",
        "EMPRESA SINTETICA EMISSORA LTDA",
        "BBBBBBBBBBBB02",
        "EMPRESA SINTETICA DESTINATARIA SA",
        "1234.50",
    ),
    SyntheticCase(
        "case-wrong",
        "261000CCCCCCCCCCCC" + "2" * 26,
        1002,
        1,
        "2026-03-16",
        "CCCCCCCCCCCC03",
        "OUTRA SINTETICA EMISSORA LTDA",
        "DDDDDDDDDDDD04",
        "OUTRA SINTETICA DESTINATARIA SA",
        "500.00",
    ),
    SyntheticCase(
        "case-refused",
        "261000EEEEEEEEEEEE" + "3" * 26,
        1003,
        2,
        "2026-03-17",
        "EEEEEEEEEEEE05",
        "TERCEIRA SINTETICA EMISSORA LTDA",
        "FFFFFFFFFFFF06",
        "TERCEIRA SINTETICA DESTINATARIA SA",
        "75.25",
    ),
]


def _xml(case: SyntheticCase) -> str:
    return (
        f'<NFe xmlns="{NFE_NS}"><infNFe Id="NFe{case.access_key}" versao="4.00">'
        f"<ide><nNF>{case.number}</nNF><serie>{case.series}</serie>"
        f"<dhEmi>{case.issue_date}T10:30:00-03:00</dhEmi></ide>"
        f"<emit><CNPJ>{case.issuer_cnpj}</CNPJ><xNome>{case.issuer_name}</xNome></emit>"
        f"<dest><CNPJ>{case.recipient_cnpj}</CNPJ><xNome>{case.recipient_name}</xNome></dest>"
        f"<total><ICMSTot><vNF>{case.total}</vNF></ICMSTot></total>"
        "</infNFe></NFe>\n"
    )


def _pdf(case: SyntheticCase) -> bytes:
    return f"%PDF-1.4\n% synthetic {case.case_id}\n%%EOF\n".encode()


def _invoice_json(case: SyntheticCase, total: str) -> str:
    return json.dumps(
        {
            "access_key": case.access_key,
            "number": case.number,
            "series": case.series,
            "issue_date": case.issue_date,
            "issuer": {"cnpj": case.issuer_cnpj, "name": case.issuer_name},
            "recipient": {"cnpj": case.recipient_cnpj, "name": case.recipient_name},
            "total_amount": total,
        }
    )


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
def workspace(tmp_path: Path) -> tuple[Path, Path]:
    cases_dir = tmp_path / "cases"
    responses_dir = tmp_path / "responses"
    cases_dir.mkdir()
    responses_dir.mkdir()
    scripted = {
        "case-ok": _scripted("end_turn", _invoice_json(CASES[0], CASES[0].total)),
        "case-wrong": _scripted("end_turn", _invoice_json(CASES[1], "501.00")),
        "case-refused": _scripted("refusal", ""),
    }
    for case in CASES:
        pdf = _pdf(case)
        (cases_dir / f"{case.case_id}.pdf").write_bytes(pdf)
        (cases_dir / f"{case.case_id}.xml").write_text(_xml(case), encoding="utf-8")
        digest = hashlib.sha256(pdf).hexdigest()
        (responses_dir / f"{digest}.json").write_text(
            json.dumps(scripted[case.case_id]), encoding="utf-8"
        )
    return cases_dir, responses_dir


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
def host(tmp_path: Path, workspace: tuple[Path, Path]) -> Iterator[tuple[str, str]]:
    if shutil.which("dotnet") is None:
        pytest.fail(
            "dotnet is not on PATH; run this test inside `nix shell nixpkgs#dotnet-sdk_10 -c ...`"
        )
    _, responses_dir = workspace
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


async def test_one_pdf_over_http_to_graded_summary(
    tmp_path: Path, workspace: tuple[Path, Path], host: tuple[str, str]
) -> None:
    cases_dir, _ = workspace
    base_url, api_key = host

    # No key: 401, and nothing is echoed back.
    anonymous = httpx2.post(
        f"{base_url}/eval/extractions",
        json={
            "contract_version": "1",
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
        json={"contract_version": "1", "case_id": "x"},
    )
    assert wrong.status_code == 401

    cases = discover_cases(cases_dir)
    assert [c.case_id for c in cases] == ["case-ok", "case-refused", "case-wrong"]

    run_dir = tmp_path / "runs" / "run-e2e"
    report = await run_cases(
        cases, base_url=base_url, api_key=api_key, out_dir=run_dir, run_id="run-e2e"
    )
    assert (report.completed, report.harness_errors) == (3, 0)

    lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").splitlines()
    assert len(lines) == 3
    records = {r["case_id"]: r for r in map(json.loads, lines)}
    assert set(records) == {"case-ok", "case-wrong", "case-refused"}
    for record in records.values():
        assert record["record_version"] == 1
        assert record["status"] == "completed"
        assert record["http"]["status"] == 200
        response = record["response"]
        assert response["contract_version"] == "1"
        assert response["case_id"] == record["case_id"]
        assert response["trace_id"] == record["request"]["trace_id"]
        assert record["request"]["traceparent"].split("-")[1] == response["trace_id"]
        assert response["usage"]["input_tokens"] == 1200
        assert response["usage"]["cache_write_5m_tokens"] == 300
        assert response["effective"]["model"] == "claude-haiku-4-5"
        assert response["model_returned"] == "scripted-model-1"
        assert "raw_output" in response["outcome"]
    assert records["case-ok"]["response"]["outcome"]["status"] == "success"
    assert records["case-wrong"]["response"]["outcome"]["status"] == "success"
    assert records["case-refused"]["response"]["outcome"]["status"] == "refused"
    assert records["case-refused"]["response"]["stop_reason"] == "refusal"
    assert records["case-ok"]["response"]["outcome"]["raw_output"] == _invoice_json(
        CASES[0], CASES[0].total
    )

    summary = grade_run(run_dir, cases_dir)
    graded = {c["case_id"]: c for c in summary["cases"]}
    assert graded["case-ok"]["status"] == "success"
    assert graded["case-ok"]["fields"] == {"access_key": True, "total_amount": True}
    assert graded["case-wrong"]["status"] == "success"
    assert graded["case-wrong"]["fields"] == {"access_key": True, "total_amount": False}
    assert graded["case-refused"]["status"] == "refused"
    assert "fields" not in graded["case-refused"]
    assert summary["counts"]["success"] == 2
    assert summary["counts"]["refused"] == 1
    assert summary["counts"]["harness_error"] == 0
    assert summary["field_accuracy"] == {
        "access_key": {"correct": 2, "n": 2},
        "total_amount": {"correct": 1, "n": 2},
    }

    summary_text = (run_dir / "summary.json").read_text(encoding="utf-8")
    assert json.loads(summary_text)["run_id"] == "run-e2e"
    assert api_key not in summary_text
    assert api_key not in (run_dir / "cases.jsonl").read_text(encoding="utf-8")


def test_traceparent_shape() -> None:
    traceparent, trace_id = new_traceparent()
    version, tid, span, flags = traceparent.split("-")
    assert (version, flags) == ("00", "01")
    assert tid == trace_id
    assert len(tid) == 32 and len(span) == 16
    assert int(tid, 16) != 0


def test_decimal_tolerance_is_inclusive() -> None:
    from carimbo_evals.grader import total_within_tolerance

    assert total_within_tolerance("100.01", Decimal("100.00"), Decimal("0.01"))
    assert not total_within_tolerance("100.02", Decimal("100.00"), Decimal("0.01"))
    assert not total_within_tolerance("not-a-number", Decimal("100.00"), Decimal("0.01"))
