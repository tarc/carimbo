"""Offline grader: reads stored run records and ground-truth XML, never the network.

Imports no HTTP client. Schema validity is graded twice, independently of the .NET side: with
``jsonschema`` over the committed canonical schema and with the generated Pydantic model in
strict JSON mode.
"""

from __future__ import annotations

import hashlib
import json
import re
import unicodedata
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from functools import cache
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator
from pydantic import ValidationError

from carimbo_evals.summary import (
    FIELDS,
    GRADED_STATUSES,
    OUTCOME_STATUSES,
    build_summary,
)
from carimbo_models.generated import Invoice

NFE_NS = "http://www.portalfiscal.inf.br/nfe"
DEFAULT_TOLERANCE = Decimal("0.01")
DEFAULT_SCHEMA_PATH = Path("schema/invoice.schema.json")
_REPO_SCHEMA_PATH = Path(__file__).resolve().parents[3] / "schema" / "invoice.schema.json"


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


def normalize_name(value: str) -> str:
    """NFKC, casefold, punctuation to spaces, collapse whitespace.

    Accents are kept (``Comércio`` is not ``Comercio``); only their Unicode form is normalized.
    """
    folded = unicodedata.normalize("NFKC", value).casefold()
    return re.sub(r"[\W_]+", " ", folded).strip()


def total_within_tolerance(predicted: str, truth: Decimal, tolerance: Decimal) -> bool:
    """True when the predicted amount is within ``tolerance`` of the truth, inclusive."""
    value = _decimal(predicted)
    return value is not None and abs(value - truth) <= tolerance


def _decimal(value: object) -> Decimal | None:
    if not isinstance(value, str):
        return None
    try:
        number = Decimal(value)
    except InvalidOperation:
        return None
    return number if number.is_finite() else None


def _is_int(value: object, expected: int) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value == expected


def _same_id(value: object, expected: str) -> bool:
    return isinstance(value, str) and normalize_id(value) == normalize_id(expected)


def _same_name(value: object, expected: str) -> bool:
    return isinstance(value, str) and normalize_name(value) == normalize_name(expected)


@cache
def load_schema_validator(schema_path: Path) -> Draft202012Validator:
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema)


def _schema_grades(raw_output: object, validator: Draft202012Validator) -> tuple[bool, bool]:
    """``(jsonschema_valid, pydantic_valid)`` for the model's raw text."""
    if not isinstance(raw_output, str):
        return False, False
    try:
        instance = json.loads(raw_output)
    except ValueError:
        return False, False
    try:
        Invoice.model_validate_json(raw_output, strict=True)
        pydantic_valid = True
    except ValidationError:
        pydantic_valid = False
    return validator.is_valid(instance), pydantic_valid


def _outcome(record: dict[str, Any]) -> dict[str, Any] | None:
    response = record.get("response")
    if record.get("status") != "completed" or not isinstance(response, dict):
        return None
    outcome = response.get("outcome")
    return outcome if isinstance(outcome, dict) else None


def _party(invoice: dict[str, Any], role: str) -> dict[str, Any]:
    party = invoice.get(role)
    return party if isinstance(party, dict) else {}


def _field_grades(
    invoice: dict[str, Any], truth: GroundTruth, tolerance: Decimal
) -> dict[str, bool]:
    issuer = _party(invoice, "issuer")
    recipient = _party(invoice, "recipient")
    total = invoice.get("total_amount")
    grades = {
        "access_key": _same_id(invoice.get("access_key"), truth.access_key),
        "number": _is_int(invoice.get("number"), truth.number),
        "series": _is_int(invoice.get("series"), truth.series),
        "issue_date": invoice.get("issue_date") == truth.issue_date,
        "issuer.cnpj": _same_id(issuer.get("cnpj"), truth.issuer_cnpj),
        "issuer.name": _same_name(issuer.get("name"), truth.issuer_name),
        "recipient.cnpj": _same_id(recipient.get("cnpj"), truth.recipient_cnpj),
        "recipient.name": _same_name(recipient.get("name"), truth.recipient_name),
        "total_amount": isinstance(total, str)
        and total_within_tolerance(total, truth.total_amount, tolerance),
    }
    return grades


def grade_case(
    record: dict[str, Any],
    truth: GroundTruth,
    tolerance: Decimal,
    *,
    validator: Draft202012Validator | None = None,
) -> dict[str, Any]:
    """Grade one stored record.

    Only ``success`` and ``schema_invalid`` outcomes get field grades; a ``schema_invalid`` answer
    is wrong on every field. Refusals, truncations, infrastructure failures and harness errors
    carry no grades at all, so they can never be counted as wrong answers.
    """
    outcome = _outcome(record)
    status = "harness_error"
    if outcome is not None and outcome.get("status") in OUTCOME_STATUSES:
        status = str(outcome["status"])
    response = record.get("response") if outcome is not None else None
    response = response if isinstance(response, dict) else {}
    effective = response.get("effective")
    effective = effective if isinstance(effective, dict) else {}
    graded: dict[str, Any] = {
        "case_id": record["case_id"],
        "status": status,
        "trace_id": record.get("request", {}).get("trace_id"),
        "cost_usd": response.get("cost_usd"),
        "latency_ms": response.get("latency_ms"),
        "usage": response.get("usage"),
        "model_requested": effective.get("model"),
        "model_returned": response.get("model_returned"),
        "prompt_version": effective.get("prompt_version"),
        "schema_sha256": effective.get("schema_sha256"),
        "pricing_version": effective.get("pricing_version"),
        "expected": {
            "access_key": truth.access_key,
            "number": truth.number,
            "series": truth.series,
            "issue_date": truth.issue_date,
            "issuer.cnpj": truth.issuer_cnpj,
            "issuer.name": truth.issuer_name,
            "recipient.cnpj": truth.recipient_cnpj,
            "recipient.name": truth.recipient_name,
            "total_amount": str(truth.total_amount),
        },
    }
    if status not in GRADED_STATUSES or outcome is None:
        return graded
    invoice = outcome.get("invoice")
    if status == "schema_invalid" or not isinstance(invoice, dict):
        graded["fields"] = dict.fromkeys(FIELDS, False)
        invoice = {}
    else:
        graded["fields"] = _field_grades(invoice, truth, tolerance)
    predicted = _decimal(invoice.get("total_amount"))
    graded["total_delta"] = None if predicted is None else str(predicted - truth.total_amount)
    checker = validator or load_schema_validator(_REPO_SCHEMA_PATH)
    graded["schema_valid_jsonschema"], graded["schema_valid_pydantic"] = _schema_grades(
        outcome.get("raw_output"), checker
    )
    return graded


def _read_records(cases_path: Path) -> dict[str, dict[str, Any]]:
    """One record per case id; if a case was run twice, the later line wins.

    A torn final line (the run was killed mid-write) is ignored.
    """
    records: dict[str, dict[str, Any]] = {}
    lines = cases_path.read_text(encoding="utf-8").splitlines(keepends=True)
    for index, line in enumerate(lines):
        if not line.strip():
            continue
        try:
            record = json.loads(line)
        except ValueError:
            if index == len(lines) - 1 and not line.endswith("\n"):
                break
            raise
        records[record["case_id"]] = record
    return records


def _load_manifest(cases_dir: Path) -> dict[str, Any] | None:
    path = cases_dir / "manifest.json"
    if not path.is_file():
        return None
    raw = path.read_bytes()
    manifest = json.loads(raw)
    return {
        "name": manifest.get("dataset"),
        "version": manifest.get("dataset_version"),
        "manifest_sha256": hashlib.sha256(raw).hexdigest(),
    }


def grade_run(
    run_dir: Path,
    cases_dir: Path,
    *,
    tolerance: Decimal = DEFAULT_TOLERANCE,
    schema_path: Path = DEFAULT_SCHEMA_PATH,
) -> dict[str, Any]:
    """Grade ``run_dir/cases.jsonl`` against the XML ground truth and return the summary dict."""
    validator = load_schema_validator(schema_path.resolve())
    records = _read_records(run_dir / "cases.jsonl")
    case_grades = [
        grade_case(
            record,
            load_ground_truth(cases_dir / f"{case_id}.xml"),
            tolerance,
            validator=validator,
        )
        for case_id, record in sorted(records.items())
    ]
    first = next(iter(records.values()), None)
    run_meta = {"run_id": first["run_id"] if first else run_dir.name, "tolerance": tolerance}
    return build_summary(case_grades, run_meta, _load_manifest(cases_dir))
