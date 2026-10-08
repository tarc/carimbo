"""The Python XML reader against the spec-derived manifest, and the mapping rules in isolation.

The manifest ``expected`` blocks are computed from the ``CaseSpec`` without reading any XML
(``carimbo_datagen.cli._expected``), so agreement with ``invoice_from_xml`` is evidence that the
generator and the reader, written separately, follow the same documented mapping
(docs/DANFE-MAPPING.md).
"""

from __future__ import annotations

import json
from datetime import date
from pathlib import Path
from typing import Any

import pytest

from carimbo_datagen.cli import _expected
from carimbo_datagen.spec import CASE_IDS, MASTER_SEED, build_case_spec
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_models.generated import Invoice

SKELETON = Path(__file__).resolve().parents[2] / "data" / "skeleton"


def _manifest() -> dict[str, Any]:
    return json.loads((SKELETON / "manifest.json").read_text(encoding="utf-8"))


def _cases() -> dict[str, dict[str, Any]]:
    return {entry["case_id"]: entry for entry in _manifest()["cases"]}


# --- the reader against the manifest -----------------------------------------------------------


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_mapped_invoice_agrees_with_the_manifest_expected_block(case_id: str) -> None:
    expected = _cases()[case_id]["expected"]
    invoice = invoice_from_xml(SKELETON / f"{case_id}.xml")
    assert len(invoice["items"]) == expected["item_count"]
    assert len(invoice["installments"]) == expected["installment_count"]
    assert invoice["totals"]["invoice_total"] == expected["invoice_total"]
    assert invoice["issuer"]["cnpj"] == expected["issuer_cnpj"]
    assert invoice["recipient"]["tax_id"] == expected["recipient_tax_id"]
    assert invoice["recipient"]["tax_id_kind"] == expected["recipient_tax_id_kind"]
    code_length = {len(item["cst_csosn"]) for item in invoice["items"]}
    assert code_length == ({4} if expected["regime"] == "simples" else {3})


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_mapped_invoice_validates_strictly_against_the_generated_model(case_id: str) -> None:
    invoice = invoice_from_xml(SKELETON / f"{case_id}.xml")
    Invoice.model_validate_json(json.dumps(invoice), strict=True)


def test_manifest_expected_blocks_are_the_spec_computation() -> None:
    cases = _cases()
    assert list(cases) == list(CASE_IDS)
    for case_id in CASE_IDS:
        assert cases[case_id]["expected"] == _expected(build_case_spec(MASTER_SEED, case_id))


def test_manifest_carries_the_version_the_as_of_date_and_the_tags() -> None:
    manifest = _manifest()
    assert manifest["dataset_version"] == "skeleton-002"
    assert manifest["as_of_date"] == "2026-10-01"
    cases = _cases()
    assert cases["case-001"]["tags"] == [
        "csosn_101",
        "numeric_cnpj",
        "simples_nacional",
        "single_page",
    ]
    assert cases["case-002"]["tags"] == [
        "freight_discount",
        "installments",
        "ipi",
        "numeric_cnpj",
        "regime_normal",
        "single_page",
    ]
    assert cases["case-003"]["tags"] == [
        "alphanumeric_cnpj",
        "cpf_recipient",
        "many_items",
        "multi_page",
        "simples_nacional",
    ]


def test_as_of_date_is_later_than_every_issue_date() -> None:
    as_of = date.fromisoformat(_manifest()["as_of_date"])
    for case_id in CASE_IDS:
        invoice = invoice_from_xml(SKELETON / f"{case_id}.xml")
        assert date.fromisoformat(invoice["issue_date"]) < as_of


def test_reworked_cases_cover_the_new_fields() -> None:
    mapped = {case_id: invoice_from_xml(SKELETON / f"{case_id}.xml") for case_id in CASE_IDS}
    normal = mapped["case-002"]
    assert normal["totals"]["freight"] == "25.00"
    assert normal["totals"]["discount"] == "10.00"
    assert normal["totals"]["ipi_amount"] != "0.00"
    assert len(normal["installments"]) == 2
    assert mapped["case-003"]["recipient"]["tax_id_kind"] == "cpf"
    assert any(ch.isalpha() for ch in mapped["case-003"]["issuer"]["cnpj"])
    assert len(mapped["case-003"]["items"]) >= 50


# --- the mapping rules on minimal documents ----------------------------------------------------


def _document(
    *,
    crt: str = "1",
    icms: str = "<ICMSSN102><orig>0</orig><CSOSN>102</CSOSN></ICMSSN102>",
    dest_id: str = "<CNPJ>11222333000181</CNPJ>",
    dest_ie: str = "",
    emit_ie: str = "<IE>ISENTO</IE>",
    dh_emi: str = "2026-03-31T23:30:00-03:00",
    ipi: str = "",
    cobr: str = "",
    q_com: str = "1.0000",
) -> str:
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<NFe xmlns="http://www.portalfiscal.inf.br/nfe">
  <infNFe Id="NFe35260311222333000181550010000001231000012346" versao="4.00">
    <ide><natOp>VENDA</natOp><serie>1</serie><nNF>123</nNF><dhEmi>{dh_emi}</dhEmi></ide>
    <emit><CNPJ>11222333000181</CNPJ><xNome>EMITENTE</xNome>
      <enderEmit><UF>SP</UF></enderEmit>{emit_ie}<CRT>{crt}</CRT></emit>
    <dest>{dest_id}<xNome>DESTINO</xNome><enderDest><UF>MG</UF></enderDest>{dest_ie}</dest>
    <det nItem="1">
      <prod><cProd>P1</cProd><xProd>PARAFUSO</xProd><NCM>73181500</NCM><CFOP>5102</CFOP>
        <uCom>UN</uCom><qCom>{q_com}</qCom><vUnCom>10.0000</vUnCom><vProd>10.00</vProd></prod>
      <imposto><ICMS>{icms}</ICMS>{ipi}</imposto>
    </det>
    <total><ICMSTot><vProd>10.00</vProd><vNF>10.00</vNF></ICMSTot></total>
    {cobr}
  </infNFe>
</NFe>
"""


def _map(tmp_path: Path, **parts: str) -> dict[str, Any]:
    path = tmp_path / "doc.xml"
    path.write_text(_document(**parts), encoding="utf-8")
    return invoice_from_xml(path)


def test_crt_1_reads_the_csosn_and_crt_3_reads_the_cst(tmp_path: Path) -> None:
    simples = _map(tmp_path)
    assert simples["items"][0]["cst_csosn"] == "0102"
    normal = _map(
        tmp_path,
        crt="3",
        icms=(
            "<ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC>"
            "<vBC>10.00</vBC><pICMS>18.00</pICMS><vICMS>1.80</vICMS></ICMS00>"
        ),
    )
    item = normal["items"][0]
    assert item["cst_csosn"] == "000"
    assert (item["icms_base"], item["icms_rate"], item["icms_amount"]) == ("10.00", "18.00", "1.80")


def test_an_absent_ipi_group_maps_to_zero(tmp_path: Path) -> None:
    item = _map(tmp_path)["items"][0]
    assert item["ipi_rate"] == "0.00"
    assert item["ipi_amount"] == "0.00"
    with_ipi = _map(
        tmp_path,
        ipi=(
            "<IPI><cEnq>999</cEnq><IPITrib><CST>50</CST><vBC>10.00</vBC>"
            "<pIPI>5.00</pIPI><vIPI>0.50</vIPI></IPITrib></IPI>"
        ),
    )["items"][0]
    assert (with_ipi["ipi_rate"], with_ipi["ipi_amount"]) == ("5.00", "0.50")


def test_dest_cpf_gives_the_cpf_kind(tmp_path: Path) -> None:
    recipient = _map(tmp_path, dest_id="<CPF>52998224725</CPF>")["recipient"]
    assert recipient["tax_id"] == "52998224725"
    assert recipient["tax_id_kind"] == "cpf"
    assert _map(tmp_path)["recipient"]["tax_id_kind"] == "cnpj"


def test_an_absent_ie_is_none_and_isento_stays_isento(tmp_path: Path) -> None:
    invoice = _map(tmp_path)
    assert invoice["recipient"]["ie"] is None
    assert invoice["issuer"]["ie"] == "ISENTO"
    with_ie = _map(tmp_path, dest_ie="<IE>123456789012</IE>", emit_ie="")
    assert with_ie["recipient"]["ie"] == "123456789012"
    assert with_ie["issuer"]["ie"] is None


def test_quantity_is_printed_with_four_decimals(tmp_path: Path) -> None:
    assert _map(tmp_path, q_com="12.5")["items"][0]["quantity"] == "12.5000"
    assert _map(tmp_path, q_com="198.821")["items"][0]["quantity"] == "198.8210"


def test_issue_date_is_the_printed_local_date_never_utc(tmp_path: Path) -> None:
    assert _map(tmp_path)["issue_date"] == "2026-03-31"


def test_no_cobr_gives_no_installments_and_dup_elements_are_read(tmp_path: Path) -> None:
    assert _map(tmp_path)["installments"] == []
    cobr = (
        "<cobr><fat><nFat>1</nFat></fat>"
        "<dup><nDup>001</nDup><dVenc>2026-04-30</dVenc><vDup>4.00</vDup></dup>"
        "<dup><nDup>002</nDup><dVenc>2026-05-30</dVenc><vDup>6</vDup></dup></cobr>"
    )
    assert _map(tmp_path, cobr=cobr)["installments"] == [
        {"number": "001", "due_date": "2026-04-30", "amount": "4.00"},
        {"number": "002", "due_date": "2026-05-30", "amount": "6.00"},
    ]
