"""Offline grader: reads stored run records and ground-truth XML, never the network.

Imports no HTTP client. Schema validity is graded twice, independently of the .NET side: with
``jsonschema`` over the committed canonical schema and with the generated Pydantic model in
strict JSON mode.
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from decimal import Decimal
from functools import cache
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator
from pydantic import ValidationError

from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.matching import diff_rows, is_within_cent
from carimbo_evals.normalize import normalize_id, normalize_name, parse_decimal
from carimbo_evals.slices import render_hazards
from carimbo_evals.stats import DEFAULT_RESAMPLES
from carimbo_evals.summary import (
    EXCLUDED_STATUSES,
    FIELDS,
    GRADED_STATUSES,
    OUTCOME_STATUSES,
    build_summary,
)
from carimbo_evals.tags import tag_case
from carimbo_models.generated import Invoice

DEFAULT_TOLERANCE = Decimal("0.00")
DEFAULT_SCHEMA_PATH = Path("schema/invoice.schema.json")
_REPO_SCHEMA_PATH = Path(__file__).resolve().parents[3] / "schema" / "invoice.schema.json"

# Fields graded as list lengths: the number of items and of installments.
_COUNT_FIELDS = {"item_count": "items", "installment_count": "installments"}

# The header values that move money or identify who owes whom (D-06). Item totals and installment
# due dates and amounts are payment values too, by column; see ``is_payment_value``.
PAYMENT_HEADER_PATHS = frozenset(
    {"access_key", "issuer.cnpj", "recipient.tax_id", "issue_date", "totals.invoice_total"}
)
_PAYMENT_COLUMNS = {"item": frozenset({"total"}), "installment": frozenset({"due_date", "amount"})}


def is_payment_value(value: dict[str, Any]) -> bool:
    """True when a wrong value touches a payment value (D-06).

    Header: ``access_key``, ``issuer.cnpj``, ``recipient.tax_id``, ``issue_date`` and
    ``totals.invoice_total``. Item rows: the ``total`` column. Installments: ``due_date`` and
    ``amount``. A missing or extra row is reported on the item ``total`` (installment ``amount``),
    so it qualifies without a special case.
    """
    scope = value.get("scope")
    if scope == "header":
        return value.get("path") in PAYMENT_HEADER_PATHS
    return value.get("column") in _PAYMENT_COLUMNS.get(str(scope), frozenset())


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


def total_within_tolerance(predicted: str, truth: Decimal, tolerance: Decimal) -> bool:
    """True when the predicted amount is within ``tolerance`` of the truth, inclusive."""
    value = parse_decimal(predicted)
    return value is not None and abs(value - truth) <= tolerance


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


def _attempt_dicts(response: dict[str, Any]) -> list[dict[str, Any]]:
    attempts = response.get("attempts")
    if not isinstance(attempts, list):
        return []
    return [attempt for attempt in attempts if isinstance(attempt, dict)]


def cache_counts(response: dict[str, Any]) -> tuple[int, int]:
    """``(cache hits, answered attempts)`` of a stored response (D-19).

    An attempt is answered when the model produced a response for it (it has a ``usage`` object);
    it is a hit when its ``cache`` block says ``status: hit``. Both are 0 without an attempts list.
    """
    answered = [a for a in _attempt_dicts(response) if isinstance(a.get("usage"), dict)]
    hits = sum(
        1
        for a in answered
        if isinstance(a.get("cache"), dict) and a["cache"].get("status") == "hit"
    )
    return hits, len(answered)


def _model_latency_ms(response: dict[str, Any]) -> int | None:
    """The sum of the attempts' ``latency_ms`` (the original ones on a replay), ``None`` if none."""
    values = [
        a["latency_ms"]
        for a in _attempt_dicts(response)
        if isinstance(a.get("latency_ms"), int) and not isinstance(a["latency_ms"], bool)
    ]
    return sum(values) if values else None


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


def _header_wrong(
    graded_fields: dict[str, bool], invoice: dict[str, Any], truth: GroundTruth
) -> list[dict[str, Any]]:
    """One wrong value per header field graded False, in ``FIELDS`` order."""
    wrong: list[dict[str, Any]] = []
    for field in FIELDS:
        if graded_fields[field]:
            continue
        expected = truth.expected(field)
        if field in _COUNT_FIELDS:
            value = invoice.get(_COUNT_FIELDS[field])
            predicted: Any = len(value) if isinstance(value, list) else None
        else:
            predicted = _get(invoice, field)
        wrong.append(
            {
                "path": field,
                "scope": "header",
                "column": field,
                "row": None,
                "predicted_row": None,
                "event": None,
                "expected": expected,
                "predicted": predicted,
                "within_cent": field.startswith("totals.") and is_within_cent(predicted, expected),
            }
        )
    return wrong


def diff_invoice(
    status: str, invoice: object, truth: GroundTruth, tolerance: Decimal
) -> dict[str, Any]:
    """Grade one answer against the truth: ``fields``, tagged ``wrong`` values, ``items``,
    ``installments`` statistics and the ``invoice`` dict that was graded.

    A ``schema_invalid`` status, or an answer that is not an object, is wrong on every field and
    on every row (graded as an empty invoice). Every wrong value carries its failure tag and rule
    number (``tags.tag_case``) and a ``payment`` flag (``is_payment_value``). No schema checks and
    no record handling happen here, so self-tests can call it on any invoice dict.
    """
    if status == "schema_invalid" or not isinstance(invoice, dict):
        fields = {field: False for field in FIELDS}
        invoice = {}
    else:
        fields = _field_grades(invoice, truth, tolerance)
    items = diff_rows("item", invoice.get("items"), truth.invoice["items"], tolerance)
    installments = diff_rows(
        "installment", invoice.get("installments"), truth.invoice["installments"], tolerance
    )
    wrong = [
        {**value, "payment": is_payment_value(value)}
        for value in tag_case(
            status,
            invoice,
            truth.invoice,
            items.assignment,
            installments.assignment,
            [*_header_wrong(fields, invoice, truth), *items.wrong, *installments.wrong],
        )
    ]
    return {
        "invoice": invoice,
        "fields": fields,
        "wrong": wrong,
        "items": items.stats,
        "installments": installments.stats,
    }


def grade_case(
    record: dict[str, Any],
    truth: GroundTruth,
    tolerance: Decimal,
    *,
    validator: Draft202012Validator | None = None,
    meta: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Grade one stored record.

    ``success``, ``validation_failed`` and ``schema_invalid`` outcomes get field grades. A
    ``validation_failed`` case is graded on its candidate invoice exactly like a success and is
    flagged ``caught``; it is never counted as a success. A ``schema_invalid`` answer is wrong on
    every field. Refusals, truncations, infrastructure failures and harness errors carry no grades
    at all, so they can never be counted as wrong answers.

    Every case also records its validator outcome (``validator``, from ``outcome.findings``) and
    its ``attempt_count`` and ``attempt_statuses`` (from ``response.attempts``; ``None`` for a
    contract 1 record that has no attempts). From the same attempts it records the cache evidence
    (``cache_hits``, ``answered_attempts``, ``model_latency_ms``) and next to the notional
    ``cost_usd`` the ``incurred_cost_usd`` (D-19, D-29). None of this changes how fields are graded.

    Beyond the 27 header fields, every item column and every installment is graded with rows
    matched by optimal assignment (``matching.diff_rows``). ``wrong`` lists each wrong value and
    ``correct`` is true only for a ``success`` with an empty ``wrong`` list (D-01). ``correct`` is
    null for the excluded statuses (D-02). ``meta`` is the case's manifest entry, if any.
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
    cache_hits, answered_attempts = cache_counts(response)
    meta = meta or {}
    base_case = meta.get("base_case")
    case_tags = meta.get("tags")
    graded: dict[str, Any] = {
        "case_id": record["case_id"],
        "split": meta.get("split"),
        "base_case": base_case if isinstance(base_case, str) else record["case_id"],
        "rendering": meta.get("rendering"),
        "severity": meta.get("severity"),
        "barcode_readable": meta.get("barcode_readable"),
        "case_tags": list(case_tags) if isinstance(case_tags, list) else [],
        # Derived from the ground truth, recorded on every case before any early return. A hazard
        # is reported beside the result and never changes a grade (D-47).
        "render_hazards": render_hazards(truth.invoice),
        "status": status,
        "caught": status == "validation_failed",
        "validator": _validator_block(outcome),
        "attempt_count": None if attempt_statuses is None else len(attempt_statuses),
        "attempt_statuses": attempt_statuses,
        "trace_id": record.get("request", {}).get("trace_id"),
        "cost_usd": response.get("cost_usd"),
        # A contract 3 response says what was paid; an older record paid its cost_usd. An explicit
        # null means unknown and stays null.
        "incurred_cost_usd": (
            response["incurred_cost_usd"]
            if "incurred_cost_usd" in response
            else response.get("cost_usd")
        ),
        "cache_hits": cache_hits,
        "answered_attempts": answered_attempts,
        "model_latency_ms": _model_latency_ms(response),
        "cache_mode": effective.get("cache_mode"),
        "replicate_salt": effective.get("replicate_salt"),
        "latency_ms": response.get("latency_ms"),
        "usage": response.get("usage"),
        "model_requested": effective.get("model"),
        "model_returned": response.get("model_returned"),
        "prompt_version": effective.get("prompt_version"),
        "max_repairs": effective.get("max_repairs"),
        "schema_sha256": effective.get("schema_sha256"),
        "pricing_version": effective.get("pricing_version"),
        "expected": truth.expected_fields(),
    }
    # No grades, so no wrong values: refusals and truncations are not correct but not wrong either,
    # and an excluded status (infrastructure failure, harness error) is neither (D-01, D-02).
    graded["wrong"] = []
    graded["failure_tags"] = []
    graded["correct"] = None if status in EXCLUDED_STATUSES else False
    if status not in GRADED_STATUSES or outcome is None:
        return graded
    diff = diff_invoice(status, outcome.get("invoice"), truth, tolerance)
    invoice = diff["invoice"]
    graded["fields"] = diff["fields"]
    graded["wrong"] = diff["wrong"]
    graded["failure_tags"] = sorted({value["tag"] for value in diff["wrong"]})
    graded["items"] = diff["items"]
    graded["installments"] = diff["installments"]
    graded["correct"] = status == "success" and not graded["wrong"]
    predicted = parse_decimal(_get(invoice, "totals.invoice_total"))
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


def load_case_metadata(cases_dir: Path) -> dict[str, dict[str, Any]]:
    """Per-case ``split``, ``base_case``, ``rendering``, ``severity``, ``barcode_readable`` and
    ``tags`` from ``manifest.json``; empty when there is no manifest or no ``cases`` list.

    Members a manifest lacks are simply absent from that case's dict, so the skeleton manifest
    (no split, no base case) still grades.
    """
    path = cases_dir / "manifest.json"
    if not path.is_file():
        return {}
    cases = json.loads(path.read_bytes()).get("cases")
    if not isinstance(cases, list):
        return {}
    keys = ("split", "base_case", "rendering", "severity", "barcode_readable", "tags")
    return {
        case["case_id"]: {key: case[key] for key in keys if key in case}
        for case in cases
        if isinstance(case, dict) and isinstance(case.get("case_id"), str)
    }


def load_run_state(run_dir: Path) -> dict[str, Any]:
    """The run's own ``run.json``, or ``{}`` when it is missing or damaged.

    A run killed before its end has no ``run.json``, and a torn write can leave a malformed one.
    Statuses alone still mark the infrastructure and harness cases of such a run (T-05-12).
    """
    path = run_dir / "run.json"
    try:
        state = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return state if isinstance(state, dict) else {}


def _cases_not_run(state: dict[str, Any], records: dict[str, dict[str, Any]]) -> int:
    """How many of the run's selected cases have no stored record."""
    selected = state.get("case_ids")
    if not isinstance(selected, list):
        return 0
    return sum(1 for case_id in selected if case_id not in records)


def grade_run(
    run_dir: Path,
    cases_dir: Path,
    *,
    tolerance: Decimal = DEFAULT_TOLERANCE,
    schema_path: Path = DEFAULT_SCHEMA_PATH,
    resamples: int = DEFAULT_RESAMPLES,
) -> dict[str, Any]:
    """Grade ``run_dir/cases.jsonl`` against the XML ground truth and return the summary dict.

    ``resamples`` is the bootstrap resample count of the row-level intervals (D-14).
    """
    validator = load_schema_validator(schema_path.resolve())
    records = _read_records(run_dir / "cases.jsonl")
    metadata = load_case_metadata(cases_dir)
    case_grades = [
        grade_case(
            record,
            load_ground_truth(cases_dir / f"{case_id}.xml"),
            tolerance,
            validator=validator,
            meta=metadata.get(case_id),
        )
        for case_id, record in sorted(records.items())
    ]
    first = next(iter(records.values()), None)
    state = load_run_state(run_dir)
    stopped = state.get("stopped_reason")
    run_meta = {
        "run_id": first["run_id"] if first else run_dir.name,
        "tolerance": tolerance,
        "resamples": resamples,
        "manifest_case_ids": sorted(metadata) if metadata else None,
        "stopped_reason": stopped if isinstance(stopped, str) else None,
        "cases_not_run": _cases_not_run(state, records),
    }
    return build_summary(case_grades, run_meta, _load_manifest(cases_dir))
