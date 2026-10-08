"""EVAL-02: offline grading of stored runs, field by field, with typed failures kept honest."""

from __future__ import annotations

import copy
import json
import os
import shutil
import subprocess
import sys
import unicodedata
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest
from pydantic import ValidationError
from typer.testing import CliRunner

from carimbo_evals.cli import app
from carimbo_evals.grader import (
    GroundTruth,
    grade_case,
    grade_run,
    load_ground_truth,
    normalize_id,
    normalize_name,
)
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_evals.summary import (
    FIELDS,
    GRADER_VERSION,
    SUMMARY_VERSION,
    build_summary,
    render_markdown,
    write_summary,
)
from carimbo_models.generated import Invoice

REPO_ROOT = Path(__file__).resolve().parents[2]
SKELETON = REPO_ROOT / "data" / "skeleton"
FIXTURE = REPO_ROOT / "data" / "vectors" / "valid-invoice.json"
SCHEMA = REPO_ROOT / "schema" / "invoice.schema.json"
SRC = REPO_ROOT / "python" / "src"
TOLERANCE = Decimal("0.01")
ALL_FIELDS = list(FIELDS)
TOTALS_FIELDS = [name for name in FIELDS if name.startswith("totals.")]


def _truth(case_id: str = "case-001") -> GroundTruth:
    return load_ground_truth(SKELETON / f"{case_id}.xml")


def _fixture_truth() -> GroundTruth:
    """The hand-checked shared fixture as ground truth: two items, two installments, a null ie."""
    return GroundTruth(json.loads(FIXTURE.read_text(encoding="utf-8")))


def _invoice(truth: GroundTruth) -> dict[str, Any]:
    """A fresh, fully correct answer: the ground-truth invoice itself."""
    return copy.deepcopy(truth.invoice)


def _set(invoice: dict[str, Any], path: str, value: Any) -> None:
    *parents, last = path.split(".")
    target = invoice
    for part in parents:
        target = target[part]
    target[last] = value


def _record(
    case_id: str,
    invoice: dict[str, Any] | None,
    *,
    status: str = "success",
    raw_output: str | None = None,
    cost: str | None = "0.0123",
    latency_ms: int = 1500,
) -> dict[str, Any]:
    if raw_output is None and invoice is not None:
        raw_output = json.dumps(invoice)
    return {
        "record_version": 1,
        "run_id": "run-grade",
        "case_id": case_id,
        "status": "completed",
        "request": {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "a" * 32},
        "http": {"status": 200, "wall_ms": latency_ms + 5, "error": None},
        "response": {
            "contract_version": "1",
            "case_id": case_id,
            "trace_id": "a" * 32,
            "effective": {
                "model": "claude-haiku-4-5",
                "prompt_version": "extract-002",
                "schema_sha256": "b" * 64,
                "pricing_version": "pricing-001",
            },
            "outcome": {
                "status": status,
                "invoice": invoice,
                "failure": None if status == "success" else {"kind": status},
                "raw_output": raw_output,
            },
            "usage": {
                "input_tokens": 1000,
                "output_tokens": 100,
                "cache_read_tokens": 10,
                "cache_write_5m_tokens": 20,
                "cache_write_1h_tokens": 30,
            },
            "cost_usd": cost,
            "latency_ms": latency_ms,
            "stop_reason": "end_turn",
            "model_returned": "claude-haiku-4-5",
            "provider_message_id": "msg_1",
        },
    }


def _finding(rule_id: str, severity: str = "error") -> dict[str, Any]:
    return {
        "field": "totals.invoice_total",
        "rule_id": rule_id,
        "expected": "1.00",
        "actual": "2.00",
        "severity": severity,
    }


def _v2_record(
    case_id: str,
    invoice: dict[str, Any] | None,
    *,
    status: str = "success",
    findings: list[Any] | None = None,
    attempt_statuses: list[str] | None = None,
    raw_output: str | None = None,
    cost: str | None = "0.0123",
    latency_ms: int = 1500,
) -> dict[str, Any]:
    """A contract 2 record: ``_record`` plus ``outcome.findings`` and ``response.attempts``."""
    record = _record(
        case_id, invoice, status=status, raw_output=raw_output, cost=cost, latency_ms=latency_ms
    )
    response = record["response"]
    response["contract_version"] = "2"
    response["outcome"]["findings"] = findings if findings is not None else []
    statuses = attempt_statuses if attempt_statuses is not None else [status]
    response["attempts"] = [
        {
            "index": index,
            "kind": "initial" if index == 0 else "repair",
            "status": attempt_status,
            "findings": [],
        }
        for index, attempt_status in enumerate(statuses)
    ]
    return record


def _harness_error(case_id: str) -> dict[str, Any]:
    return {
        "record_version": 1,
        "run_id": "run-grade",
        "case_id": case_id,
        "status": "harness_error",
        "request": {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "c" * 32},
        "http": {"status": 502, "wall_ms": 7, "error": "HTTP 502"},
        "response": None,
    }


def _grade(record: dict[str, Any], truth: GroundTruth | None = None, tol: Decimal = TOLERANCE):
    return grade_case(record, truth or _truth(), tol)


def _write_run(tmp_path: Path, records: list[dict[str, Any]]) -> tuple[Path, Path]:
    """A run dir plus a cases dir where every record's case id has the case-001 ground truth."""
    run_dir = tmp_path / "run"
    cases_dir = tmp_path / "cases"
    run_dir.mkdir()
    cases_dir.mkdir()
    for record in records:
        shutil.copy(SKELETON / "case-001.xml", cases_dir / f"{record['case_id']}.xml")
    # Serialized exactly like runner.run_cases: ensure_ascii=False leaves U+2028, U+2029 and U+0085
    # unescaped inside strings, which is what the grader must survive.
    (run_dir / "cases.jsonl").write_text(
        "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for r in records),
        encoding="utf-8",
    )
    return run_dir, cases_dir


# --- ground truth ---------------------------------------------------------------------------


def test_ground_truth_of_a_skeleton_case_validates_strictly_against_the_generated_model() -> None:
    for case_id in ("case-001", "case-002", "case-003"):
        invoice = invoice_from_xml(SKELETON / f"{case_id}.xml")
        Invoice.model_validate_json(json.dumps(invoice), strict=True)


def test_ground_truth_of_case_001_follows_the_documented_mapping() -> None:
    invoice = invoice_from_xml(SKELETON / "case-001.xml")
    assert invoice["issuer"]["ie"] == "020597585982"
    assert invoice["recipient"]["ie"] is None
    assert invoice["recipient"]["tax_id_kind"] == "cnpj"
    assert invoice["operation_nature"] == "VENDA DE MERCADORIA ADQUIRIDA DE TERCEIROS"
    assert invoice["issue_date"] == "2026-04-04"
    assert invoice["installments"] == []
    assert len(invoice["items"]) == 3
    assert {item["cst_csosn"] for item in invoice["items"]} == {"0101"}  # Simples: orig + CSOSN
    assert invoice["items"][0]["quantity"] == "35.7863"  # printed to four decimals
    assert invoice["items"][1]["unit_price"] == "843.4000"
    for item in invoice["items"]:
        for tax in ("icms_base", "icms_rate", "icms_amount", "ipi_rate", "ipi_amount"):
            assert item[tax] == "0.00"
    assert invoice["totals"]["invoice_total"] == "194615.23"
    assert invoice["totals"]["freight"] == "0.00"


_NORMAL_REGIME_XML = """<?xml version="1.0" encoding="UTF-8"?>
<NFe xmlns="http://www.portalfiscal.inf.br/nfe">
  <infNFe Id="NFe35260311222333000181550010000001231000012346" versao="4.00">
    <ide><natOp>VENDA</natOp><serie>1</serie><nNF>123</nNF><dhEmi>2026-03-31T23:30:00-03:00</dhEmi></ide>
    <emit><CNPJ>11222333000181</CNPJ><xNome>EMITENTE</xNome>
      <enderEmit><UF>SP</UF></enderEmit><IE>123456789012</IE><CRT>3</CRT></emit>
    <dest><CPF>52998224725</CPF><xNome>PESSOA</xNome><enderDest><UF>MG</UF></enderDest></dest>
    <det nItem="1">
      <prod><cProd>P1</cProd><xProd>PARAFUSO</xProd><NCM>73181500</NCM><CFOP>5102</CFOP>
        <uCom>UN</uCom><qCom>2.0000</qCom><vUnCom>50.00005</vUnCom><vProd>100.005</vProd></prod>
      <imposto>
        <ICMS><ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC>
          <vBC>100.00</vBC><pICMS>18.00</pICMS><vICMS>18.00</vICMS></ICMS00></ICMS>
        <IPI><IPITrib><CST>50</CST><vBC>100.00</vBC><pIPI>10.00</pIPI><vIPI>10.00</vIPI></IPITrib></IPI>
      </imposto>
    </det>
    <det nItem="2">
      <prod><cProd>P2</cProd><xProd>CABO</xProd><NCM>85444200</NCM><CFOP>5102</CFOP>
        <uCom>M</uCom><qCom>3.5</qCom><vUnCom>10</vUnCom><vProd>35.00</vProd></prod>
      <imposto><ICMS><ICMS40><orig>1</orig><CST>40</CST></ICMS40></ICMS></imposto>
    </det>
    <total><ICMSTot><vBC>100.00</vBC><vICMS>18.00</vICMS><vProd>135.01</vProd><vNF>155.00</vNF></ICMSTot></total>
    <cobr>
      <dup><nDup>001</nDup><dVenc>2026-04-14</dVenc><vDup>77.50</vDup></dup>
      <dup><nDup>002</nDup><dVenc>2026-05-14</dVenc><vDup>77.5</vDup></dup>
    </cobr>
  </infNFe>
</NFe>
"""


def test_ground_truth_reads_regime_normal_ipi_cpf_installments_and_absent_taxes(
    tmp_path: Path,
) -> None:
    xml = tmp_path / "normal.xml"
    xml.write_text(_NORMAL_REGIME_XML, encoding="utf-8")
    invoice = invoice_from_xml(xml)
    Invoice.model_validate_json(json.dumps(invoice), strict=True)
    assert invoice["recipient"] == {
        "tax_id": "52998224725",
        "tax_id_kind": "cpf",
        "name": "PESSOA",
        "ie": None,
        "uf": "MG",
    }
    assert invoice["issuer"]["ie"] == "123456789012"
    assert invoice["issue_date"] == "2026-03-31"  # the local date, never converted to UTC
    first, second = invoice["items"]
    assert first["cst_csosn"] == "000"  # orig + CST under CRT 3
    assert (first["icms_base"], first["icms_rate"], first["icms_amount"]) == (
        "100.00",
        "18.00",
        "18.00",
    )
    assert (first["ipi_rate"], first["ipi_amount"]) == ("10.00", "10.00")
    assert first["unit_price"] == "50.0001"  # half-up at the fifth decimal
    assert first["total"] == "100.01"  # 100.005 half-up
    assert second["cst_csosn"] == "140"
    assert (second["quantity"], second["unit_price"]) == ("3.5000", "10.0000")
    assert (second["icms_base"], second["ipi_amount"]) == ("0.00", "0.00")
    assert invoice["totals"]["icms_st_base"] == "0.00"  # absent is 0.00
    assert invoice["totals"]["products_total"] == "135.01"
    assert invoice["installments"] == [
        {"number": "001", "due_date": "2026-04-14", "amount": "77.50"},
        {"number": "002", "due_date": "2026-05-14", "amount": "77.50"},
    ]


def test_ground_truth_names_the_missing_element(tmp_path: Path) -> None:
    xml = tmp_path / "broken.xml"
    xml.write_text(_NORMAL_REGIME_XML.replace("<nNF>123</nNF>", ""), encoding="utf-8")
    with pytest.raises(ValueError, match="ide/nNF"):
        invoice_from_xml(xml)


# --- field grades ---------------------------------------------------------------------------


def test_the_summary_grades_exactly_the_27_v2_fields() -> None:
    assert len(ALL_FIELDS) == 27
    assert len(set(ALL_FIELDS)) == 27
    assert len(TOTALS_FIELDS) == 11
    assert ALL_FIELDS[-2:] == ["item_count", "installment_count"]
    assert GRADER_VERSION == "grader-002"
    assert SUMMARY_VERSION == 2


def test_matching_record_has_every_field_and_both_schema_grades_correct() -> None:
    truth = _truth()
    graded = _grade(_record("case-001", _invoice(truth)))
    assert graded["status"] == "success"
    assert graded["fields"] == dict.fromkeys(ALL_FIELDS, True)
    assert graded["expected"] == truth.expected_fields()
    assert graded["schema_valid_jsonschema"] is True
    assert graded["schema_valid_pydantic"] is True


def test_the_shared_fixture_as_truth_is_graded_fully_correct() -> None:
    truth = _fixture_truth()
    graded = _grade(_record("case-001", _invoice(truth)), truth)
    assert graded["fields"] == dict.fromkeys(ALL_FIELDS, True)
    assert graded["expected"]["item_count"] == 2
    assert graded["expected"]["installment_count"] == 2
    assert graded["expected"]["recipient.ie"] is None
    assert graded["schema_valid_jsonschema"] is True
    assert graded["schema_valid_pydantic"] is True


def _transpose(key: str) -> str:
    return key[:10] + key[11] + key[10] + key[12:]


def _next_day(iso: str) -> str:
    year, month, day = (int(part) for part in iso.split("-"))
    return f"{year:04d}-{month:02d}-{day + 1:02d}" if day < 28 else f"{year:04d}-{month:02d}-01"


def _bump(path: str) -> Any:
    def mutate(invoice: dict[str, Any]) -> None:
        *parents, last = path.split(".")
        target = invoice
        for part in parents:
            target = target[part]
        target[last] = format(Decimal(target[last]) + Decimal("5.00"), ".2f")

    return mutate


def _corruptions() -> list[tuple[str, Any]]:
    corruptions: list[tuple[str, Any]] = [
        ("access_key", lambda inv: inv.update(access_key=_transpose(inv["access_key"]))),
        ("series", lambda inv: inv.update(series=inv["series"] + 1)),
        ("number", lambda inv: inv.update(number=inv["number"] + 1)),
        ("issue_date", lambda inv: inv.update(issue_date=_next_day(inv["issue_date"]))),
        ("operation_nature", lambda inv: inv.update(operation_nature="DEVOLUCAO DE VENDA")),
        ("issuer.cnpj", lambda inv: _set(inv, "issuer.cnpj", "ZZZZZZZZZZZZ99")),
        ("issuer.name", lambda inv: _set(inv, "issuer.name", "OUTRA EMPRESA LTDA")),
        ("issuer.ie", lambda inv: _set(inv, "issuer.ie", "999999999999")),
        ("issuer.uf", lambda inv: _set(inv, "issuer.uf", "RJ")),
        ("recipient.tax_id", lambda inv: _set(inv, "recipient.tax_id", "ZZZZZZZZZZZZ99")),
        ("recipient.tax_id_kind", lambda inv: _set(inv, "recipient.tax_id_kind", "cpf")),
        ("recipient.name", lambda inv: _set(inv, "recipient.name", "OUTRA EMPRESA SA")),
        ("recipient.ie", lambda inv: _set(inv, "recipient.ie", "123456")),
        ("recipient.uf", lambda inv: _set(inv, "recipient.uf", "RJ")),
        ("item_count", lambda inv: inv["items"].pop()),
        ("installment_count", lambda inv: inv["installments"].pop()),
    ]
    corruptions += [(name, _bump(name)) for name in TOTALS_FIELDS]
    return corruptions


@pytest.mark.parametrize(("field", "mutate"), _corruptions(), ids=[c[0] for c in _corruptions()])
def test_corrupted_fields_are_graded_wrong_and_only_that_field(field: str, mutate: Any) -> None:
    truth = _fixture_truth()
    invoice = _invoice(truth)
    mutate(invoice)
    fields = _grade(_record("case-001", invoice), truth)["fields"]
    assert fields[field] is False
    assert [name for name, ok in fields.items() if not ok] == [field]


def test_transposed_access_key_really_differs() -> None:
    key = _truth().expected("access_key")
    assert _transpose(key) != key  # guards the corruption above against a no-op


def test_a_dropped_null_ie_and_a_present_ie_are_told_apart() -> None:
    truth = _fixture_truth()
    missing = _invoice(truth)
    _set(missing, "issuer.ie", None)  # the truth has an ie, the answer has none
    assert _grade(_record("case-001", missing), truth)["fields"]["issuer.ie"] is False
    same_form = _invoice(truth)
    _set(same_form, "issuer.ie", "1234-56789012")  # punctuation differs only
    assert _grade(_record("case-001", same_form), truth)["fields"]["issuer.ie"] is True


def test_printed_cnpj_form_is_graded_equal_to_the_plain_form() -> None:
    assert normalize_id("AB.1C2.D3E/0001-30") == normalize_id("AB1C2D3E000130")
    truth = _truth()
    invoice = _invoice(truth)
    plain = truth.expected("issuer.cnpj")
    _set(
        invoice,
        "issuer.cnpj",
        f"{plain[:2]}.{plain[2:5]}.{plain[5:8]}/{plain[8:12]}-{plain[12:]}",
    )
    graded = _grade(_record("case-001", invoice))
    assert graded["fields"]["issuer.cnpj"] is True
    assert graded["schema_valid_jsonschema"] is False  # the printed form violates the pattern
    assert graded["schema_valid_pydantic"] is False


@pytest.mark.parametrize(
    "variant",
    [
        lambda n: unicodedata.normalize("NFD", n),
        lambda n: unicodedata.normalize("NFC", n),
        lambda n: n.lower(),
        lambda n: "  " + n.replace(" ", "   ") + " ",
        lambda n: n.replace(" ", ", ").replace("LTDA", "LTDA."),
    ],
)
def test_names_differing_only_in_form_case_spacing_or_punctuation_are_equal(variant: Any) -> None:
    name = "INDÚSTRIA E COMÉRCIO SINTÉTICA LTDA"
    base = _truth().invoice
    truth = GroundTruth({**base, "issuer": {**base["issuer"], "name": name}})
    invoice = _invoice(truth)
    _set(invoice, "issuer.name", variant(name))
    assert _grade(_record("case-001", invoice), truth)["fields"]["issuer.name"] is True


def test_normalize_name_keeps_accented_letters_distinct_from_their_bases() -> None:
    assert normalize_name("Comércio") != normalize_name("Comercio")


def test_totals_one_cent_off_is_correct_two_cents_off_is_wrong_and_zero_tolerance_is_exact() -> (
    None
):
    truth = _truth()
    total = truth.invoice_total
    one_cent = _invoice(truth)
    _set(one_cent, "totals.invoice_total", format(total + Decimal("0.01"), ".2f"))
    _set(one_cent, "totals.products_total", format(total + Decimal("0.01"), ".2f"))
    two_cents = _invoice(truth)
    _set(two_cents, "totals.invoice_total", format(total + Decimal("0.02"), ".2f"))
    assert _grade(_record("case-001", one_cent))["fields"]["totals.invoice_total"] is True
    assert _grade(_record("case-001", one_cent))["fields"]["totals.products_total"] is True
    assert _grade(_record("case-001", two_cents))["fields"]["totals.invoice_total"] is False
    exact = _grade(_record("case-001", one_cent), tol=Decimal("0"))["fields"]
    assert exact["totals.invoice_total"] is False
    assert exact["totals.products_total"] is False


def test_total_delta_is_the_invoice_total_delta_as_a_decimal_string() -> None:
    truth = _truth()
    invoice = _invoice(truth)
    _set(invoice, "totals.invoice_total", format(truth.invoice_total + Decimal("1.00"), ".2f"))
    _set(invoice, "totals.freight", "999.00")  # only invoice_total feeds the delta
    assert _grade(_record("case-001", invoice))["total_delta"] == "1.00"


def test_a_list_instead_of_an_object_never_crashes_the_grader() -> None:
    truth = _truth()
    invoice = _invoice(truth)
    invoice["issuer"] = []
    invoice["items"] = "none"
    invoice["totals"] = None
    fields = _grade(_record("case-001", invoice))["fields"]
    assert fields["issuer.cnpj"] is False
    assert fields["item_count"] is False
    assert fields["totals.invoice_total"] is False


def test_the_generated_model_rejects_what_the_grader_calls_a_schema_violation() -> None:
    invoice = _invoice(_truth())
    _set(invoice, "recipient.tax_id", "not-an-id")
    with pytest.raises(ValidationError):
        Invoice.model_validate_json(json.dumps(invoice), strict=True)


# --- statuses -------------------------------------------------------------------------------


def test_typed_failures_are_counted_but_never_graded_as_wrong(tmp_path: Path) -> None:
    truth = _truth()
    good = _record("case-a", _invoice(truth))
    wrong_total = _invoice(truth)
    _set(wrong_total, "totals.invoice_total", "0.00")
    records = [
        good,
        _record("case-b", _invoice(truth), status="refused", raw_output=None),
        _record("case-c", None, status="truncated", raw_output='{"access_key": "26'),
        _record("case-d", None, status="infrastructure_failure", raw_output=None, cost=None),
        _harness_error("case-e"),
        _record("case-f", None, status="schema_invalid", raw_output='{"access_key": 1}'),
        _record("case-g", wrong_total),
    ]
    run_dir, cases_dir = _write_run(tmp_path, records)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"] == {
        "success": 2,
        "validation_failed": 0,
        "refused": 1,
        "truncated": 1,
        "schema_invalid": 1,
        "infrastructure_failure": 1,
        "harness_error": 1,
        "total": 7,
    }
    by_case = {c["case_id"]: c for c in summary["cases"]}
    for case_id in ("case-b", "case-c", "case-d", "case-e"):
        assert "fields" not in by_case[case_id]
    # Denominators: 2 successes + 1 schema_invalid. The schema_invalid case is wrong everywhere.
    assert summary["field_accuracy"]["access_key"] == {"correct": 2, "n": 3}
    assert summary["field_accuracy"]["totals.invoice_total"] == {"correct": 1, "n": 3}
    assert by_case["case-f"]["fields"] == dict.fromkeys(ALL_FIELDS, False)
    assert by_case["case-f"]["schema_valid_jsonschema"] is False
    assert by_case["case-f"]["schema_valid_pydantic"] is False


_THREE_FINDINGS = [
    _finding("TOTAL_VNF_FORMULA"),
    _finding("DUP_SUM"),
    _finding("TAX_CODE_UNSUPPORTED", "warning"),
]


def _candidate(truth: GroundTruth) -> dict[str, Any]:
    """The ground truth except invoice_total + 1.00: a validator-caught near miss."""
    candidate = _invoice(truth)
    _set(candidate, "totals.invoice_total", format(truth.invoice_total + Decimal("1.00"), ".2f"))
    return candidate


def _validation_failed_record(case_id: str, truth: GroundTruth) -> dict[str, Any]:
    return _v2_record(
        case_id,
        _candidate(truth),
        status="validation_failed",
        findings=_THREE_FINDINGS,
        attempt_statuses=["validation_failed"] * 3,
    )


def test_a_validation_failed_candidate_is_graded_on_all_fields_and_counted_as_caught() -> None:
    truth = _truth()
    graded = _grade(_validation_failed_record("case-001", truth))
    assert graded["status"] == "validation_failed"
    assert graded["caught"] is True
    assert [name for name, ok in graded["fields"].items() if not ok] == ["totals.invoice_total"]
    assert len(graded["fields"]) == 27
    assert graded["total_delta"] == "1.00"
    assert graded["schema_valid_jsonschema"] is True
    assert graded["schema_valid_pydantic"] is True
    assert graded["validator"] == {
        "errors": 2,
        "warnings": 1,
        "rule_ids": ["DUP_SUM", "TAX_CODE_UNSUPPORTED", "TOTAL_VNF_FORMULA"],
    }
    assert graded["attempt_count"] == 3
    assert graded["attempt_statuses"] == ["validation_failed"] * 3


def test_a_repaired_success_is_not_caught_and_keeps_its_attempt_history() -> None:
    truth = _truth()
    record = _v2_record(
        "case-001",
        _invoice(truth),
        attempt_statuses=["validation_failed", "success"],
    )
    graded = _grade(record)
    assert graded["status"] == "success"
    assert graded["caught"] is False
    assert graded["fields"] == dict.fromkeys(ALL_FIELDS, True)
    assert graded["attempt_count"] == 2
    assert graded["attempt_statuses"] == ["validation_failed", "success"]
    assert graded["validator"] == {"errors": 0, "warnings": 0, "rule_ids": []}


def test_a_refused_attempt_carries_no_field_grades_but_keeps_attempts_and_a_zero_validator() -> (
    None
):
    record = _v2_record("case-001", None, status="refused", attempt_statuses=["refused"])
    graded = _grade(record)
    assert graded["status"] == "refused"
    assert "fields" not in graded
    assert graded["caught"] is False
    assert graded["attempt_count"] == 1
    assert graded["attempt_statuses"] == ["refused"]
    assert graded["validator"] == {"errors": 0, "warnings": 0, "rule_ids": []}


def test_a_contract_1_record_without_findings_or_attempts_grades_with_null_attempts() -> None:
    graded = _grade(_record("case-001", _invoice(_truth())))
    assert graded["status"] == "success"
    assert graded["fields"] == dict.fromkeys(ALL_FIELDS, True)
    assert graded["attempt_count"] is None
    assert graded["attempt_statuses"] is None
    assert graded["caught"] is False
    assert graded["validator"] == {"errors": 0, "warnings": 0, "rule_ids": []}


def test_a_harness_error_has_no_attempts_and_a_zero_validator() -> None:
    graded = _grade(_harness_error("case-001"))
    assert graded["status"] == "harness_error"
    assert graded["attempt_count"] is None
    assert graded["validator"] == {"errors": 0, "warnings": 0, "rule_ids": []}
    assert graded["caught"] is False


def test_malformed_findings_and_attempts_members_never_crash_the_grader() -> None:
    record = _v2_record("case-001", _invoice(_truth()), status="success")
    record["response"]["outcome"]["findings"] = "oops"
    record["response"]["attempts"] = [None, {"status": 3}, {"status": "success"}]
    graded = _grade(record)
    assert graded["validator"] == {"errors": 0, "warnings": 0, "rule_ids": []}
    assert graded["attempt_count"] == 3
    assert graded["attempt_statuses"] == [None, None, "success"]
    mixed = _v2_record("case-001", _invoice(_truth()), findings=[_finding("DUP_SUM"), 7, {}])
    assert _grade(mixed)["validator"] == {"errors": 1, "warnings": 0, "rule_ids": ["DUP_SUM"]}


def test_validation_failed_is_caught_and_graded_while_typed_failures_stay_ungraded(
    tmp_path: Path,
) -> None:
    truth = _truth()
    records = [
        _validation_failed_record("case-a", truth),
        _v2_record("case-b", _invoice(truth), attempt_statuses=["validation_failed", "success"]),
        _v2_record("case-c", None, status="refused", attempt_statuses=["refused"]),
        _v2_record("case-d", None, status="truncated", attempt_statuses=["truncated"]),
        _v2_record("case-e", None, status="infrastructure_failure", cost=None, attempt_statuses=[]),
        _harness_error("case-f"),
    ]
    run_dir, cases_dir = _write_run(tmp_path, records)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    counts = summary["counts"]
    assert counts["validation_failed"] == 1
    assert counts["success"] == 1
    assert counts["total"] == 6
    by_case = {c["case_id"]: c for c in summary["cases"]}
    for case_id in ("case-c", "case-d", "case-e", "case-f"):
        assert "fields" not in by_case[case_id]
        assert by_case[case_id]["caught"] is False
    # Denominator: the success and the validation_failed candidate; the typed failures are absent.
    assert summary["field_accuracy"]["totals.invoice_total"] == {"correct": 1, "n": 2}
    assert summary["field_accuracy"]["access_key"] == {"correct": 2, "n": 2}
    assert summary["schema_validity"]["jsonschema"] == {"correct": 2, "n": 2}


def test_the_last_record_per_case_wins(tmp_path: Path) -> None:
    truth = _truth()
    records = [_harness_error("case-a"), _record("case-a", _invoice(truth))]
    run_dir, cases_dir = _write_run(tmp_path, records)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"]["total"] == 1
    assert summary["counts"]["success"] == 1


def test_raw_output_with_unicode_line_separators_is_graded(tmp_path: Path) -> None:
    truth = _truth()
    separators = "a\u2028b\u2029c\x85d"
    records = [
        _record("case-a", _invoice(truth), raw_output=json.dumps({"note": separators})),
        _harness_error("case-b"),
        _record("case-c", _invoice(truth)),
    ]
    records[1]["http"]["error"] = f"HTTP 502 {separators}"
    run_dir, cases_dir = _write_run(tmp_path, records)
    text = (run_dir / "cases.jsonl").read_text(encoding="utf-8")
    assert "\u2028" in text  # the file really holds the raw character, not an escape
    assert len(text.split("\n")) == len(records) + 1  # one record per newline-delimited line
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"]["total"] == len(records)


def test_a_corrupt_middle_line_fails_grading_but_a_torn_final_line_is_ignored(
    tmp_path: Path,
) -> None:
    truth = _truth()
    good = [_record("case-a", _invoice(truth)), _record("case-b", _invoice(truth))]
    run_dir, cases_dir = _write_run(tmp_path, good)
    lines = (run_dir / "cases.jsonl").read_text(encoding="utf-8").split("\n")[:-1]

    torn = lines[0] + "\n" + lines[1] + "\n" + '{"record_version": 1, "case_id": "case-0'
    (run_dir / "cases.jsonl").write_text(torn, encoding="utf-8")
    assert grade_run(run_dir, cases_dir, schema_path=SCHEMA)["counts"]["total"] == 2

    corrupt = lines[0] + "\n{not json\n" + lines[1] + "\n"
    (run_dir / "cases.jsonl").write_text(corrupt, encoding="utf-8")
    with pytest.raises(ValueError):
        grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    result = CliRunner().invoke(
        app, ["grade", "--run", str(run_dir), "--cases", str(cases_dir), "--schema", str(SCHEMA)]
    )
    assert result.exit_code == 2


# --- summary --------------------------------------------------------------------------------


def _typical_run(tmp_path: Path) -> tuple[Path, Path]:
    truth = _truth()
    wrong = _invoice(truth)
    _set(wrong, "totals.invoice_total", format(truth.invoice_total + Decimal("1.00"), ".2f"))
    records = [
        _record("case-a", _invoice(truth), cost="0.1", latency_ms=1000),
        _record("case-b", wrong, cost="0.2", latency_ms=3000),
        _record("case-c", None, status="refused", cost=None, latency_ms=2000),
    ]
    run_dir, cases_dir = _write_run(tmp_path, records)
    (cases_dir / "manifest.json").write_text(
        json.dumps({"dataset": "skeleton", "dataset_version": "skeleton-001", "cases": []}),
        encoding="utf-8",
    )
    return run_dir, cases_dir


def test_summary_carries_config_totals_latency_and_dataset(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["summary_version"] == SUMMARY_VERSION == 2
    assert summary["grader_version"] == GRADER_VERSION == "grader-002"
    assert summary["run_id"] == "run-grade"
    assert summary["tolerance"] == "0.01"
    assert summary["dataset"]["name"] == "skeleton"
    assert summary["dataset"]["version"] == "skeleton-001"
    assert len(summary["dataset"]["manifest_sha256"]) == 64
    assert summary["config"] == {
        "models_requested": ["claude-haiku-4-5"],
        "models_returned": ["claude-haiku-4-5"],
        "prompt_versions": ["extract-002"],
        "schema_sha256s": ["b" * 64],
        "pricing_versions": ["pricing-001"],
    }
    assert summary["totals"] == {
        "input_tokens": 3000,
        "output_tokens": 300,
        "cache_read_tokens": 30,
        "cache_write_5m_tokens": 60,
        "cache_write_1h_tokens": 90,
        "cost_usd": "0.3",
        "unpriced_cases": 1,
    }
    assert summary["latency_ms"] == {"min": 1000, "median": 2000, "max": 3000}
    refused = next(c for c in summary["cases"] if c["case_id"] == "case-c")
    assert refused["status"] == "refused"
    assert refused["trace_id"] == "a" * 32
    assert refused["latency_ms"] == 2000
    assert [c["case_id"] for c in summary["cases"]] == ["case-a", "case-b", "case-c"]
    assert summary["schema_validity"]["jsonschema"] == {"correct": 2, "n": 2}


def test_summary_files_are_deterministic_apart_from_the_meta_block(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    texts = []
    for _ in range(2):
        write_summary(run_dir, grade_run(run_dir, cases_dir, schema_path=SCHEMA))
        loaded = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
        assert set(loaded["meta"]) == {"graded_at"}
        del loaded["meta"]
        texts.append(json.dumps(loaded, sort_keys=True))
    assert texts[0] == texts[1]
    raw = (run_dir / "summary.json").read_text(encoding="utf-8")
    assert raw.endswith("\n")
    assert '"cost_usd": "0.3"' in raw


def test_summary_markdown_has_a_header_and_a_per_case_table(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    write_summary(run_dir, grade_run(run_dir, cases_dir, schema_path=SCHEMA))
    markdown = (run_dir / "summary.md").read_text(encoding="utf-8")
    assert "run-grade" in markdown
    assert "claude-haiku-4-5" in markdown
    for column in ("case_id", "status", "fields", "total delta", "cost", "latency", "trace"):
        assert column in markdown
    for case_id in ("case-a", "case-b", "case-c"):
        assert case_id in markdown
    assert "a" * 32 in markdown


def _v2_run(tmp_path: Path) -> tuple[Path, Path]:
    """Caught (3 attempts), repaired (2), refused (1), first-try success (1), v1 and harness."""
    truth = _truth()
    records = [
        _validation_failed_record("case-a", truth),
        _v2_record("case-b", _invoice(truth), attempt_statuses=["validation_failed", "success"]),
        _v2_record("case-c", None, status="refused", cost=None, attempt_statuses=["refused"]),
        _v2_record("case-d", _invoice(truth), attempt_statuses=["success"]),
        _record("case-e", _invoice(truth)),
        _harness_error("case-f"),
    ]
    return _write_run(tmp_path, records)


def test_summary_aggregates_what_the_validators_caught_and_what_repair_cost(
    tmp_path: Path,
) -> None:
    run_dir, cases_dir = _v2_run(tmp_path)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["counts"]["validation_failed"] == 1
    assert summary["counts"]["success"] == 3
    assert summary["validation"] == {
        "caught": 1,
        "with_warnings": 1,
        "rule_counts": {"DUP_SUM": 1, "TAX_CODE_UNSUPPORTED": 1, "TOTAL_VNF_FORMULA": 1},
    }
    # 3 + 2 + 1 + 1; the contract 1 record and the harness error contribute nothing.
    assert summary["attempts"] == {"total": 7, "repaired": 1, "max": 3}


def test_summary_of_a_contract_1_run_has_zero_validation_and_null_attempt_maximum(
    tmp_path: Path,
) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    assert summary["validation"] == {"caught": 0, "with_warnings": 0, "rule_counts": {}}
    assert summary["attempts"] == {"total": 0, "repaired": 0, "max": None}


def test_rule_counts_count_cases_not_findings_and_keys_are_sorted() -> None:
    twice = _v2_record(
        "case-001",
        _invoice(_truth()),
        status="validation_failed",
        findings=[_finding("ZED"), _finding("ZED"), _finding("ALPHA", "warning")],
        attempt_statuses=["validation_failed"],
    )
    other = _v2_record(
        "case-002",
        _invoice(_truth()),
        status="validation_failed",
        findings=[_finding("ZED")],
        attempt_statuses=["validation_failed"],
    )
    grades = [_grade(twice), _grade(other)]
    summary = build_summary(grades, {"run_id": "r", "tolerance": TOLERANCE}, None)
    assert list(summary["validation"]["rule_counts"].items()) == [("ALPHA", 1), ("ZED", 2)]
    assert summary["validation"]["with_warnings"] == 1


def test_summary_markdown_shows_attempts_findings_and_the_caught_line(tmp_path: Path) -> None:
    run_dir, cases_dir = _v2_run(tmp_path)
    summary = grade_run(run_dir, cases_dir, schema_path=SCHEMA)
    markdown = render_markdown(summary)
    assert "validation_failed (caught): 1" in markdown
    header = (
        "| case_id | status | fields | attempts | findings | total delta | cost | latency ms "
        "| trace id |"
    )
    assert header in markdown
    rows = {
        line.split("|")[1].strip(): [cell.strip() for cell in line.split("|")[1:-1]]
        for line in markdown.splitlines()
        if line.startswith("| case-")
    }
    assert rows["case-a"][2:5] == ["26/27", "3", "2E/1W"]
    assert rows["case-b"][3:5] == ["2", "0E/0W"]
    assert rows["case-e"][3:5] == ["-", "0E/0W"]  # contract 1: no attempts
    assert rows["case-f"][2:5] == ["-", "-", "0E/0W"]  # harness error: nothing graded or attempted
    legacy = {k: v for k, v in summary["cases"][0].items() if k != "validator"}
    bare = {**summary, "cases": [legacy]}
    assert "| 26/27 | 3 | - |" in render_markdown(bare)  # a grade without a validator block


def test_summary_json_stays_sorted_and_deterministic_with_the_new_blocks(tmp_path: Path) -> None:
    run_dir, cases_dir = _v2_run(tmp_path)
    texts = []
    for _ in range(2):
        write_summary(run_dir, grade_run(run_dir, cases_dir, schema_path=SCHEMA))
        loaded = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
        del loaded["meta"]
        texts.append(json.dumps(loaded, sort_keys=True))
    assert texts[0] == texts[1]
    raw = (run_dir / "summary.json").read_text(encoding="utf-8")
    assert '"validation"' in raw
    assert '"attempts"' in raw


# --- offline purity -------------------------------------------------------------------------

_OFFLINE_SCRIPT = """
import socket, sys
from pathlib import Path


def _deny(*args, **kwargs):
    raise RuntimeError("network access attempted while grading")


socket.socket = _deny  # type: ignore[misc]

from carimbo_evals import grader, summary

run_dir, cases_dir, schema = (Path(a) for a in sys.argv[1:4])
result = grader.grade_run(run_dir, cases_dir, schema_path=schema)
summary.write_summary(run_dir, result)
assert "httpx2" not in sys.modules, "grader/summary pulled in an HTTP client"
assert "httpx" not in sys.modules
print(result["counts"]["total"])
"""


def test_grading_a_stored_run_needs_no_network_and_imports_no_http_client(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    env = {**os.environ, "PYTHONPATH": str(SRC)}
    result = subprocess.run(
        [sys.executable, "-c", _OFFLINE_SCRIPT, str(run_dir), str(cases_dir), str(SCHEMA)],
        capture_output=True,
        text=True,
        env=env,
        cwd=tmp_path,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip() == "3"
    assert (run_dir / "summary.json").is_file()


# --- CLI ------------------------------------------------------------------------------------


def test_grade_command_writes_both_files_and_prints_the_markdown(tmp_path: Path) -> None:
    run_dir, cases_dir = _typical_run(tmp_path)
    result = CliRunner().invoke(
        app,
        [
            "grade",
            "--run",
            str(run_dir),
            "--cases",
            str(cases_dir),
            "--schema",
            str(SCHEMA),
            "--tolerance",
            "0",
        ],
    )
    assert result.exit_code == 0, result.output
    assert "case-a" in result.output
    summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    assert summary["tolerance"] == "0"
    assert (run_dir / "summary.md").is_file()


def test_grade_command_requires_a_run_and_a_readable_run_directory(tmp_path: Path) -> None:
    assert CliRunner().invoke(app, ["grade"]).exit_code == 2
    missing = CliRunner().invoke(app, ["grade", "--run", str(tmp_path / "nope")])
    assert missing.exit_code == 2


def test_grade_help_lists_the_documented_options() -> None:
    env = {"NO_COLOR": "1", "COLUMNS": "200", "TERM": "dumb"}
    result = CliRunner().invoke(app, ["grade", "--help"], env=env)
    assert result.exit_code == 0
    for option in ("--run", "--cases", "--tolerance", "--schema"):
        assert option in result.output
