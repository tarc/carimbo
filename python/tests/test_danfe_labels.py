"""Printed-label facts from the committed skeleton DANFEs, checked against the schema descriptions.

The prompt, the model-facing schema descriptions and docs/DANFE-MAPPING.md quote the labels the
BrazilFiscalReport 1.2.0 renderer prints. These tests read the committed PDFs and XMLs so the quotes
stay honest when the renderer or the generator changes (UAT gaps G-02-1 and G-02-2).
"""

from __future__ import annotations

import json
import unicodedata
import xml.etree.ElementTree as ET
from functools import cache
from pathlib import Path
from typing import Any, cast

import pypdfium2 as pdfium
import pytest

_REPO_ROOT = Path(__file__).resolve().parents[2]
_SKELETON = _REPO_ROOT / "data" / "skeleton"
_SCHEMA_DIR = _REPO_ROOT / "schema"
_MAPPING_DOC = _REPO_ROOT / "docs" / "DANFE-MAPPING.md"
_NS = {"n": "http://www.portalfiscal.inf.br/nfe"}

_NOTICE = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"

# Totals field -> label printed in the CÁLCULO DO IMPOSTO block, the same on every skeleton case.
_TOTALS_LABELS: dict[str, str] = {
    "icms_base": "BASE DE CÁLCULO DO ICMS",
    "icms_amount": "VALOR DO ICMS",
    "icms_st_base": "BASE DE CÁLCULO DO ICMS ST",
    "icms_st_amount": "VALOR DO ICMS ST",
    "products_total": "VALOR TOTAL DOS PRODUTOS",
    "freight": "VALOR DO FRETE",
    "insurance": "VALOR DO SEGURO",
    "discount": "DESCONTO",
    "other_expenses": "OUTRAS DESPESAS ACESSÓRIAS",
    "ipi_amount": "VALOR DO IPI",
    "invoice_total": "VALOR TOTAL DA NOTA",
}

_MANIFEST = json.loads((_SKELETON / "manifest.json").read_text(encoding="utf-8"))
_CASE_IDS: list[str] = [str(case["case_id"]) for case in _MANIFEST["cases"]]
_CASES: dict[str, dict[str, Any]] = {str(case["case_id"]): case for case in _MANIFEST["cases"]}


def _collapse(text: str) -> str:
    """NFC-normalize and collapse every whitespace run to one space; the case is kept."""
    return " ".join(unicodedata.normalize("NFC", text).split())


@cache
def _page_texts(case_id: str) -> tuple[str, ...]:
    pdf = pdfium.PdfDocument(str(_SKELETON / _CASES[case_id]["pdf"]))
    try:
        return tuple(_collapse(page.get_textpage().get_text_range()) for page in pdf)
    finally:
        pdf.close()


@cache
def _xml_root(case_id: str) -> ET.Element:
    return ET.fromstring((_SKELETON / _CASES[case_id]["xml"]).read_bytes())


def _xml_text(case_id: str, path: str) -> str:
    node = _xml_root(case_id).find(path, _NS)
    assert node is not None and node.text is not None, f"{case_id}: {path} missing in the XML"
    return node.text


def _simples_header(case_id: str) -> bool:
    """CSOSN heads the items column when emit/CRT is 1 or 4."""
    return _xml_text(case_id, ".//n:emit/n:CRT") in {"1", "4"}


@pytest.mark.parametrize("case_id", _CASE_IDS)
def test_every_totals_label_is_printed(case_id: str) -> None:
    page = _page_texts(case_id)[0]
    for field, label in _TOTALS_LABELS.items():
        assert label in page, f"{case_id}: label for {field} ({label}) is not printed on page 1"


@pytest.mark.parametrize("case_id", _CASE_IDS)
def test_the_name_box_prints_the_homologation_notice_and_the_stub_prints_the_name(
    case_id: str,
) -> None:
    assert _xml_text(case_id, ".//n:ide/n:tpAmb") == "2"
    page = _page_texts(case_id)[0]
    assert f"NOME / RAZÃO SOCIAL {_NOTICE}" in page
    assert "RECEBEMOS DE" in page
    recipient = _collapse(_xml_text(case_id, ".//n:dest/n:xNome"))
    assert f"DESTINATARIO: {recipient} - " in page


@pytest.mark.parametrize("case_id", _CASE_IDS)
def test_the_items_column_header_follows_crt(case_id: str) -> None:
    expected = "NCM/SH CSOSN CFOP" if _simples_header(case_id) else "NCM/SH CST CFOP"
    other = "NCM/SH CST CFOP" if _simples_header(case_id) else "NCM/SH CSOSN CFOP"
    pages = [text for text in _page_texts(case_id) if "DADOS DO PRODUTO / SERVIÇO" in text]
    assert pages, f"{case_id}: no page carries the products table"
    for text in pages:
        assert expected in text
        assert other not in text


def test_the_skeleton_covers_both_headers_and_both_ie_forms() -> None:
    simples = {_simples_header(case_id) for case_id in _CASE_IDS}
    assert simples == {True, False}
    isento = {_xml_text(case_id, ".//n:emit/n:IE") == "ISENTO" for case_id in _CASE_IDS}
    assert isento == {True, False}
    for case_id in _CASE_IDS:
        ie = _xml_text(case_id, ".//n:emit/n:IE")
        assert ie == "ISENTO" or ie.isdigit()


@pytest.mark.parametrize("case_id", _CASE_IDS)
def test_issuer_ie_is_printed_as_in_the_xml(case_id: str) -> None:
    page = _page_texts(case_id)[0]
    assert f"INSCRIÇÃO ESTADUAL {_xml_text(case_id, './/n:emit/n:IE')}" in page


def _definition(schema_file: str, name: str) -> dict[str, Any]:
    text = (_SCHEMA_DIR / schema_file).read_text(encoding="utf-8")
    schema = cast(dict[str, Any], json.loads(text))
    return cast(dict[str, Any], schema["$defs"][name]["properties"])


@pytest.mark.parametrize("schema_file", ["invoice.schema.json", "invoice.model.schema.json"])
def test_schema_descriptions_quote_the_printed_labels(schema_file: str) -> None:
    totals = _definition(schema_file, "Totals")
    for field, label in _TOTALS_LABELS.items():
        assert totals[field]["description"] == f"{label} with two decimals"

    cst = _definition(schema_file, "LineItem")["cst_csosn"]["description"]
    assert "column headed CST or CSOSN" in cst

    name = _definition(schema_file, "Recipient")["name"]["description"]
    assert "RECEBEMOS DE" in name
    assert "DESTINATARIO:" in name


@cache
def _mapping_rows() -> dict[str, list[str]]:
    """Mapping table rows keyed by schema path: the cells after the path, stripped."""
    rows: dict[str, list[str]] = {}
    for line in _MAPPING_DOC.read_text(encoding="utf-8").splitlines():
        if not line.startswith("| `"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        rows[cells[0].strip("`")] = cells[1:]
    return rows


def _label_cell(path: str) -> str:
    return _mapping_rows()[path][0]


@pytest.mark.parametrize("field", list(_TOTALS_LABELS))
def test_the_mapping_doc_quotes_each_totals_label(field: str) -> None:
    cell = _label_cell(f"totals.{field}")
    if cell.endswith(")") and " (" in cell:
        # one trailing block note, for example (CÁLCULO DO IMPOSTO)
        cell = cell[: cell.rindex(" (")]
    assert cell == _TOTALS_LABELS[field]


def test_the_mapping_doc_points_the_recipient_name_at_the_receipt_stub() -> None:
    cell = _label_cell("recipient.name")
    assert "RECEBEMOS DE" in cell
    assert "DESTINATARIO:" in cell
    assert _NOTICE in cell


def test_the_mapping_doc_names_the_csosn_header() -> None:
    cell = _label_cell("items[].cst_csosn")
    assert "CSOSN" in cell
    assert "CST" in cell
