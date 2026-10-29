"""D-05: every graded string of a text-layer DANFE is printed exactly as the ground truth says.

For each case of ``data/dataset/manifest.json`` with rendering ``text``, the XML supplies the graded
strings and the PDF the printed text. Both sides are NFC-normalized and stripped of every whitespace
character, so a wrapped line, a grouped access key or a column break does not matter, while a
truncated or reformatted string fails naming the case and the string. Amounts are formatted from the
XML ``Decimal`` with Decimal arithmetic only (thousands ".", decimal ","), independently of the
renderer's float-based formatting (RESEARCH Pitfall 8).
"""

from __future__ import annotations

import json
import unicodedata
import xml.etree.ElementTree as ET
from decimal import Decimal
from functools import cache
from pathlib import Path
from typing import Any

import pypdfium2 as pdfium
import pytest

DATASET = Path(__file__).resolve().parents[2] / "data" / "dataset"
_NS = {"n": "http://www.portalfiscal.inf.br/nfe"}

_MANIFEST: dict[str, Any] = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
_TEXT_CASES: dict[str, dict[str, Any]] = {
    entry["case_id"]: entry for entry in _MANIFEST["cases"] if entry["rendering"] == "text"
}


def pt_br(value: Decimal, places: int) -> str:
    """Format ``value`` at ``places`` decimals with "." thousands and "," decimal separators."""
    quantized = value.quantize(Decimal(1).scaleb(-places))
    integer, _, fraction = f"{abs(quantized):f}".partition(".")
    groups: list[str] = []
    while len(integer) > 3:
        groups.insert(0, integer[-3:])
        integer = integer[:-3]
    groups.insert(0, integer)
    sign = "-" if quantized < 0 else ""
    text = sign + ".".join(groups)
    return f"{text},{fraction}" if places else text


def strip(text: str) -> str:
    """NFC-normalize and remove every whitespace character."""
    return "".join(unicodedata.normalize("NFC", text).split())


def test_pt_br_formats_with_dot_thousands_and_comma_decimals() -> None:
    assert pt_br(Decimal("1234567.5"), 2) == "1.234.567,50"
    assert pt_br(Decimal("0.1235"), 4) == "0,1235"
    assert pt_br(Decimal("999999.9999"), 4) == "999.999,9999"
    assert pt_br(Decimal("12"), 2) == "12,00"
    assert pt_br(Decimal("1000000000.00"), 2) == "1.000.000.000,00"


def test_strip_removes_whitespace_after_nfc() -> None:
    assert (
        strip("A\N{COMBINING ACUTE ACCENT} B\r\n C\N{NO-BREAK SPACE}D")
        == "\N{LATIN CAPITAL LETTER A WITH ACUTE}BCD"
    )


@cache
def _printed(case_id: str) -> str:
    document = pdfium.PdfDocument(str(DATASET / _TEXT_CASES[case_id]["pdf"]))
    try:
        return strip("\n".join(page.get_textpage().get_text_range() for page in document))
    finally:
        document.close()


def _text(root: ET.Element, path: str) -> str:
    node = root.find(path, _NS)
    assert node is not None and node.text is not None, f"{path} missing in the XML"
    return node.text


def _graded_strings(root: ET.Element) -> list[tuple[str, str]]:
    inf = root.find(".//n:infNFe", _NS)
    assert inf is not None, "infNFe missing in the XML"
    graded = [
        ("issuer name", _text(root, ".//n:emit/n:xNome")),
        ("recipient name", _text(root, ".//n:dest/n:xNome")),
        ("natOp", _text(root, ".//n:ide/n:natOp")),
        ("invoice total", pt_br(Decimal(_text(root, ".//n:ICMSTot/n:vNF")), 2)),
        ("access key", inf.attrib["Id"].removeprefix("NFe")),
    ]
    for position, det in enumerate(root.findall(".//n:det", _NS), start=1):
        prod = det.find("n:prod", _NS)
        assert prod is not None
        label = f"item {position}"
        graded += [
            (f"{label} code", _text(prod, "n:cProd")),
            (f"{label} description", _text(prod, "n:xProd")),
            (f"{label} NCM", _text(prod, "n:NCM")),
            (f"{label} unit", _text(prod, "n:uCom")),
            (f"{label} quantity", pt_br(Decimal(_text(prod, "n:qCom")), 4)),
            (f"{label} unit price", pt_br(Decimal(_text(prod, "n:vUnCom")), 4)),
            (f"{label} total", pt_br(Decimal(_text(prod, "n:vProd")), 2)),
        ]
    return graded


def test_the_manifest_has_ninety_text_layer_cases() -> None:
    assert len(_TEXT_CASES) == 90


@pytest.mark.parametrize("case_id", sorted(_TEXT_CASES))
def test_every_graded_string_is_printed_exactly_as_the_xml_says(case_id: str) -> None:
    root = ET.parse(DATASET / _TEXT_CASES[case_id]["xml"]).getroot()
    printed = _printed(case_id)
    for label, value in _graded_strings(root):
        assert strip(value) in printed, f"{case_id}: {label} {value!r} is not printed in the PDF"
