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
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from functools import cache
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator
from pydantic import ValidationError

from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.summary import (
    FIELDS,
    GRADED_STATUSES,
    OUTCOME_STATUSES,
    build_summary,
)
from carimbo_models.generated import Invoice

DEFAULT_TOLERANCE = Decimal("0.01")
DEFAULT_SCHEMA_PATH = Path("schema/invoice.schema.json")
_REPO_SCHEMA_PATH = Path(__file__).resolve().parents[3] / "schema" / "invoice.schema.json"

# Fields graded as list lengths: the number of items and of installments.
_COUNT_FIELDS = {"item_count": "items", "installment_count": "installments"}


@dataclass(frozen=True)
class GroundTruth:
    """The expected invoice in wire form, read from the XML by ``invoice_from_xml``."""

    invoice: dict[str, Any]

    def expected(self, field: str) -> Any:
        """The expected value of one graded field: a dotted path, or a list length for a count."""
        if field in _COUNT_FIELDS:
            return len(self.invoice[_COUNT_FIELDS[field]])
        return _get(self.invoice, field)

    def expected_fields(self) -> dict[str, Any]:
        """The expected value of every graded field, in ``FIELDS`` order."""
        return {field: self.expected(field) for field in FIELDS}

    @property
    def invoice_total(self) -> Decimal:
        return Decimal(self.invoice["totals"]["invoice_total"])


def load_ground_truth(xml_path: Path) -> GroundTruth:
    return GroundTruth(invoice_from_xml(xml_path))


def _get(data: dict[str, Any], path: str) -> Any:
    """The value at a dotted path, or ``None`` when any step is missing or not an object."""
    current: Any = data
    for part in path.split("."):
        if not isinstance(current, dict):
            return None
        current = current.get(part)
    return current


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


def _validator_block(outcome: dict[str, Any] | None) -> dict[str, Any]:
    """Error and warning counts and the sorted unique rule ids of ``outcome.findings``.

    Zeros and an empty list when the member is absent (a contract 1 record) or malformed.
    """
    findings = outcome.get("findings") if outcome is not None else None
    errors = warnings = 0
    rule_ids: set[str] = set()
    for finding in findings if isinstance(findings, list) else []:
        if not isinstance(finding, dict):
            continue
        severity = finding.get("severity")
        if severity == "error":
            errors += 1
        elif severity == "warning":
            warnings += 1
        else:
            continue
        rule_id = finding.get("rule_id")
        if isinstance(rule_id, str):
            rule_ids.add(rule_id)
    return {"errors": errors, "warnings": warnings, "rule_ids": sorted(rule_ids)}


def _attempt_statuses(response: dict[str, Any]) -> list[str | None] | None:
    """The status of each attempt in stored (index) order, or ``None`` without an attempts list."""
    attempts = response.get("attempts")
    if not isinstance(attempts, list):
        return None
    statuses: list[str | None] = []
    for attempt in attempts:
        status = attempt.get("status") if isinstance(attempt, dict) else None
        statuses.append(status if isinstance(status, str) else None)
    return statuses


def _outcome(record: dict[str, Any]) -> dict[str, Any] | None:
    response = record.get("response")
    if record.get("status") != "completed" or not isinstance(response, dict):
        return None
    outcome = response.get("outcome")
    return outcome if isinstance(outcome, dict) else None


_ID_FIELDS = frozenset({"access_key", "issuer.cnpj", "recipient.tax_id"})
_NAME_FIELDS = frozenset({"operation_nature", "issuer.name", "recipient.name"})
_INT_FIELDS = frozenset({"number", "series"})
_NULLABLE_ID_FIELDS = frozenset({"issuer.ie", "recipient.ie"})


def _same_nullable_id(value: object, expected: str | None) -> bool:
    """Both null, or both strings equal after ``normalize_id``."""
    if expected is None:
        return value is None
    return _same_id(value, expected)


def _field_grade(
    field: str, invoice: dict[str, Any], truth: GroundTruth, tolerance: Decimal
) -> bool:
    expected = truth.expected(field)
    if field in _COUNT_FIELDS:
        value = invoice.get(_COUNT_FIELDS[field])
        return isinstance(value, list) and len(value) == expected
    value = _get(invoice, field)
    if field in _ID_FIELDS:
        return _same_id(value, expected)
    if field in _NULLABLE_ID_FIELDS:
        return _same_nullable_id(value, expected)
    if field in _NAME_FIELDS:
        return _same_name(value, expected)
    if field in _INT_FIELDS:
        return _is_int(value, expected)
    if field.startswith("totals."):
        return isinstance(value, str) and total_within_tolerance(
            value, Decimal(expected), tolerance
        )
    # issue_date, recipient.tax_id_kind, issuer.uf and recipient.uf: exact string equality.
    return isinstance(value, str) and value == expected


def _field_grades(
    invoice: dict[str, Any], truth: GroundTruth, tolerance: Decimal
) -> dict[str, bool]:
    return {field: _field_grade(field, invoice, truth, tolerance) for field in FIELDS}


def grade_case(
    record: dict[str, Any],
    truth: GroundTruth,
    tolerance: Decimal,
    *,
    validator: Draft202012Validator | None = None,
) -> dict[str, Any]:
    """Grade one stored record.

    ``success``, ``validation_failed`` and ``schema_invalid`` outcomes get field grades. A
    ``validation_failed`` case is graded on its candidate invoice exactly like a success and is
    flagged ``caught``; it is never counted as a success. A ``schema_invalid`` answer is wrong on
    every field. Refusals, truncations, infrastructure failures and harness errors carry no grades
    at all, so they can never be counted as wrong answers.

    Every case also records its validator outcome (``validator``, from ``outcome.findings``) and
    its ``attempt_count`` and ``attempt_statuses`` (from ``response.attempts``; ``None`` for a
    contract 1 record that has no attempts).
    """
    outcome = _outcome(record)
    status = "harness_error"
    if outcome is not None and outcome.get("status") in OUTCOME_STATUSES:
        status = str(outcome["status"])
    response = record.get("response") if outcome is not None else None
    response = response if isinstance(response, dict) else {}
    effective = response.get("effective")
    effective = effective if isinstance(effective, dict) else {}
    attempt_statuses = _attempt_statuses(response)
    graded: dict[str, Any] = {
        "case_id": record["case_id"],
        "status": status,
        "caught": status == "validation_failed",
        "validator": _validator_block(outcome),
        "attempt_count": None if attempt_statuses is None else len(attempt_statuses),
        "attempt_statuses": attempt_statuses,
        "trace_id": record.get("request", {}).get("trace_id"),
        "cost_usd": response.get("cost_usd"),
        "latency_ms": response.get("latency_ms"),
        "usage": response.get("usage"),
        "model_requested": effective.get("model"),
        "model_returned": response.get("model_returned"),
        "prompt_version": effective.get("prompt_version"),
        "schema_sha256": effective.get("schema_sha256"),
        "pricing_version": effective.get("pricing_version"),
        "expected": truth.expected_fields(),
    }
    if status not in GRADED_STATUSES or outcome is None:
        return graded
    invoice = outcome.get("invoice")
    if status == "schema_invalid" or not isinstance(invoice, dict):
        graded["fields"] = dict.fromkeys(FIELDS, False)
        invoice = {}
    else:
        graded["fields"] = _field_grades(invoice, truth, tolerance)
    predicted = _decimal(_get(invoice, "totals.invoice_total"))
    graded["total_delta"] = None if predicted is None else str(predicted - truth.invoice_total)
    checker = validator or load_schema_validator(_REPO_SCHEMA_PATH)
    graded["schema_valid_jsonschema"], graded["schema_valid_pydantic"] = _schema_grades(
        outcome.get("raw_output"), checker
    )
    return graded


def _read_records(cases_path: Path) -> dict[str, dict[str, Any]]:
    """One record per case id; if a case was run twice, the later line wins.

    Records are split on the newline the runner writes after every record, never on Unicode line
    separators (U+2028, U+2029, U+0085) that ``ensure_ascii=False`` leaves inside strings. A torn
    final line (the run was killed mid-write, so it has no newline) is ignored.
    """
    records: dict[str, dict[str, Any]] = {}
    lines = cases_path.read_text(encoding="utf-8").split("\n")
    for index, line in enumerate(lines):
        if not line.strip():
            continue
        try:
            record = json.loads(line)
        except ValueError:
            if index == len(lines) - 1:
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
