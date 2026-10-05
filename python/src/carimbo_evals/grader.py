"""Offline grader: reads stored run records and ground-truth XML, never the network."""

from __future__ import annotations

import json
import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any

NFE_NS = "http://www.portalfiscal.inf.br/nfe"
SUMMARY_VERSION = 1
DEFAULT_TOLERANCE = Decimal("0.01")
OUTCOME_STATUSES = (
    "success",
    "refused",
    "truncated",
    "schema_invalid",
    "infrastructure_failure",
    "harness_error",
)
# Statuses whose answer is graded field by field. A schema_invalid answer is a wrong answer;
# refusals, truncations, infrastructure failures and harness errors are not.
GRADED_STATUSES = ("success", "schema_invalid")
FIELDS = ("access_key", "total_amount")


@dataclass(frozen=True)
class GroundTruth:
    access_key: str
    number: int
    series: int
    issue_date: str
    issuer_cnpj: str
    issuer_name: str
    recipient_cnpj: str
    recipient_name: str
    total_amount: Decimal


def _text(parent: ET.Element, path: str) -> str:
    node = parent.find("/".join(f"{{{NFE_NS}}}{part}" for part in path.split("/")))
    if node is None or node.text is None:
        raise ValueError(f"missing element {path!r} in NF-e XML")
    return node.text.strip()


def load_ground_truth(xml_path: Path) -> GroundTruth:
    root = ET.parse(xml_path).getroot()
    inf = root if root.tag == f"{{{NFE_NS}}}infNFe" else root.find(f".//{{{NFE_NS}}}infNFe")
    if inf is None:
        raise ValueError(f"no infNFe element in {xml_path}")
    key = inf.get("Id", "")
    return GroundTruth(
        access_key=key.removeprefix("NFe"),
        number=int(_text(inf, "ide/nNF")),
        series=int(_text(inf, "ide/serie")),
        issue_date=_text(inf, "ide/dhEmi")[:10],
        issuer_cnpj=_text(inf, "emit/CNPJ"),
        issuer_name=_text(inf, "emit/xNome"),
        recipient_cnpj=_text(inf, "dest/CNPJ"),
        recipient_name=_text(inf, "dest/xNome"),
        total_amount=Decimal(_text(inf, "total/ICMSTot/vNF")),
    )


def normalize_id(value: str) -> str:
    """Strip spaces, dots, slashes and hyphens, then uppercase."""
    return re.sub(r"[ ./-]", "", value).upper()


def total_within_tolerance(predicted: str, truth: Decimal, tolerance: Decimal) -> bool:
    """True when the predicted amount is within ``tolerance`` of the truth, inclusive."""
    try:
        value = Decimal(predicted)
    except (InvalidOperation, TypeError, ValueError):
        return False
    if not value.is_finite():
        return False
    return abs(value - truth) <= tolerance


def _outcome(record: dict[str, Any]) -> dict[str, Any] | None:
    response = record.get("response")
    if record.get("status") != "completed" or not isinstance(response, dict):
        return None
    outcome = response.get("outcome")
    return outcome if isinstance(outcome, dict) else None


def grade_case(record: dict[str, Any], truth: GroundTruth, tolerance: Decimal) -> dict[str, Any]:
    outcome = _outcome(record)
    status = "harness_error"
    if outcome is not None and outcome.get("status") in OUTCOME_STATUSES:
        status = str(outcome["status"])
    response = record.get("response") if outcome is not None else None
    response = response if isinstance(response, dict) else {}

    graded: dict[str, Any] = {
        "case_id": record["case_id"],
        "status": status,
        "trace_id": record.get("request", {}).get("trace_id"),
        "cost_usd": response.get("cost_usd"),
        "latency_ms": response.get("latency_ms"),
        "usage": response.get("usage"),
        "expected": {"access_key": truth.access_key, "total_amount": str(truth.total_amount)},
    }
    if status in GRADED_STATUSES:
        invoice = outcome.get("invoice") if outcome is not None else None
        invoice = invoice if isinstance(invoice, dict) else {}
        access_key = invoice.get("access_key")
        total = invoice.get("total_amount")
        graded["fields"] = {
            "access_key": isinstance(access_key, str)
            and normalize_id(access_key) == normalize_id(truth.access_key),
            "total_amount": isinstance(total, str)
            and total_within_tolerance(total, truth.total_amount, tolerance),
        }
    return graded


def _read_records(cases_path: Path) -> dict[str, dict[str, Any]]:
    """One record per case id; if a case was run twice, the later line wins."""
    records: dict[str, dict[str, Any]] = {}
    for line in cases_path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            record = json.loads(line)
            records[record["case_id"]] = record
    return records


def grade_run(
    run_dir: Path, cases_dir: Path, *, tolerance: Decimal = DEFAULT_TOLERANCE
) -> dict[str, Any]:
    """Grade ``run_dir/cases.jsonl`` against the XML ground truth and write ``summary.json``."""
    records = _read_records(run_dir / "cases.jsonl")
    cases = [
        grade_case(record, load_ground_truth(cases_dir / f"{case_id}.xml"), tolerance)
        for case_id, record in sorted(records.items())
    ]
    counts = {status: 0 for status in OUTCOME_STATUSES}
    for case in cases:
        counts[case["status"]] += 1
    field_accuracy = {field: {"correct": 0, "n": 0} for field in FIELDS}
    for case in cases:
        for field, correct in case.get("fields", {}).items():
            field_accuracy[field]["n"] += 1
            field_accuracy[field]["correct"] += int(correct)

    first = next(iter(records.values()), None)
    summary: dict[str, Any] = {
        "summary_version": SUMMARY_VERSION,
        "run_id": first["run_id"] if first else run_dir.name,
        "tolerance": str(tolerance),
        "counts": counts,
        "field_accuracy": field_accuracy,
        "cases": cases,
    }
    text = json.dumps(summary, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    (run_dir / "summary.json").write_text(text, encoding="utf-8")
    return summary
