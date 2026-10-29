"""DATA-02 / DATA-04 evidence: every ICMS group the validators know, written, printed, read back.

Each tax code is built into a spec, written by ``build_nfe_xml``, checked against the official NF-e
4.00 XSD bundled in nfelib (with a dummy signature appended and digits-only CEPs, RESEARCH Pitfall
9), rendered to a DANFE with BrazilFiscalReport and mapped back by ``invoice_from_xml``. The XSD is
a unit test of the writers here, not a dataset gate (the gate is the JSON Schema plus the .NET
validators, CONTEXT D-18).
"""

from __future__ import annotations

import copy
import json
import unicodedata
from dataclasses import replace
from decimal import ROUND_HALF_UP, Decimal
from functools import cache
from importlib import resources
from pathlib import Path

import lxml.etree as etree
import pypdfium2 as pdfium

from carimbo_datagen.danfe import render_danfe
from carimbo_datagen.nfe_xml import NFE_NS, build_nfe_xml
from carimbo_datagen.spec import CASE_IDS, MASTER_SEED, CaseSpec, build_case_spec
from carimbo_evals.grader import load_schema_validator
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_models.generated import Invoice

_REPO_ROOT = Path(__file__).resolve().parents[2]
_SCHEMA_PATH = _REPO_ROOT / "schema" / "invoice.schema.json"
_DS_NS = "http://www.w3.org/2000/09/xmldsig#"
_NS = {"n": NFE_NS}
_C14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315"
_RSA_SHA1 = "http://www.w3.org/2000/09/xmldsig#rsa-sha1"
_SHA1 = "http://www.w3.org/2000/09/xmldsig#sha1"
_ENVELOPED = "http://www.w3.org/2000/09/xmldsig#enveloped-signature"
_CENT = Decimal("0.01")


@cache
def _xsd() -> etree.XMLSchema:
    path = resources.files("nfelib").joinpath("nfe/schemas/v4_0/nfe_v4.00.xsd")
    return etree.XMLSchema(etree.parse(str(path)))


def _ds(parent: etree._Element, tag: str, text: str | None = None, **attrs: str) -> etree._Element:
    node = etree.SubElement(parent, f"{{{_DS_NS}}}{tag}", attrs)
    node.text = text
    return node


def _xsd_errors(xml: bytes) -> list[str]:
    """XSD messages for the NFe element of ``xml``, with a dummy ds:Signature appended."""
    root = etree.fromstring(xml)
    nfe = root if etree.QName(root).localname == "NFe" else root.find("n:NFe", _NS)
    assert nfe is not None
    nfe = copy.deepcopy(nfe)
    inf = nfe.find("n:infNFe", _NS)
    assert inf is not None
    signature = _ds(nfe, "Signature")
    signed_info = _ds(signature, "SignedInfo")
    _ds(signed_info, "CanonicalizationMethod", Algorithm=_C14N)
    _ds(signed_info, "SignatureMethod", Algorithm=_RSA_SHA1)
    reference = _ds(signed_info, "Reference", URI="#" + str(inf.get("Id")))
    transforms = _ds(reference, "Transforms")
    _ds(transforms, "Transform", Algorithm=_ENVELOPED)
    _ds(transforms, "Transform", Algorithm=_C14N)
    _ds(reference, "DigestMethod", Algorithm=_SHA1)
    _ds(reference, "DigestValue", "AAAAAAAAAAAAAAAAAAAAAAAAAAA=")
    _ds(signature, "SignatureValue", "AAAA")
    x509 = _ds(_ds(signature, "KeyInfo"), "X509Data")
    _ds(x509, "X509Certificate", "AAAA")
    _xsd().validate(nfe)
    return [error.message for error in _xsd().error_log]


def _spec_with(case_id: str, codes: tuple[str, ...], crt: int | None = None) -> CaseSpec:
    """A skeleton spec with the item tax codes cycled from ``codes``.

    Installments are dropped and the CEPs lose their hyphen, because the XSD pattern is eight
    digits and the generator's own CEP format is not changed by this plan.
    """
    spec = build_case_spec(MASTER_SEED, case_id)
    items = tuple(
        replace(item, tax_code=codes[index % len(codes)]) for index, item in enumerate(spec.items)
    )
    return replace(
        spec,
        items=items,
        installments=(),
        issuer=replace(spec.issuer, cep=spec.issuer.cep.replace("-", "")),
        recipient=replace(spec.recipient, cep=spec.recipient.cep.replace("-", "")),
        crt=spec.crt if crt is None else crt,
    )


def _collapse(text: str) -> str:
    return " ".join(unicodedata.normalize("NFC", text).split())


def _page_text(xml: bytes, tmp_path: Path) -> str:
    out = tmp_path / "danfe.pdf"
    render_danfe(xml, out)
    pdf = pdfium.PdfDocument(str(out))
    try:
        return _collapse(pdf[0].get_textpage().get_text_range())
    finally:
        pdf.close()


def _mapped(xml: bytes, tmp_path: Path) -> dict[str, object]:
    path = tmp_path / "case.xml"
    path.write_bytes(xml)
    return invoice_from_xml(path)


def _assert_valid_invoice(invoice: dict[str, object]) -> None:
    Invoice.model_validate_json(json.dumps(invoice), strict=True)
    errors = [e.message for e in load_schema_validator(_SCHEMA_PATH).iter_errors(invoice)]
    assert errors == []


def _money(value: Decimal) -> str:
    return format(value.quantize(_CENT, rounding=ROUND_HALF_UP), "f")


def _round(value: Decimal) -> Decimal:
    return value.quantize(_CENT, rounding=ROUND_HALF_UP)


def _group(xml: bytes, position: int) -> etree._Element:
    root = etree.fromstring(xml)
    icms = root.findall(".//n:det/n:imposto/n:ICMS", _NS)[position]
    return icms[0]


def _children(group: etree._Element) -> list[str]:
    return [etree.QName(child).localname for child in group]


def _value(group: etree._Element, tag: str) -> Decimal:
    node = group.find(f"n:{tag}", _NS)
    assert node is not None and node.text is not None, f"{tag} missing"
    return Decimal(node.text)


def _header(xml: bytes, tag: str) -> Decimal:
    node = etree.fromstring(xml).find(f".//n:total/n:ICMSTot/n:{tag}", _NS)
    assert node is not None and node.text is not None
    return Decimal(node.text)


# --- Task 1 (tracer): one CST 10 item with ICMS ST ---------------------------------------------


def test_skeleton_profiles_carry_no_st_and_keep_their_total() -> None:
    for case_id in CASE_IDS:
        spec = build_case_spec(MASTER_SEED, case_id)
        assert spec.st_base_total == Decimal("0.00")
        assert spec.st_total == Decimal("0.00")
        assert spec.invoice_total == (
            spec.products_total - spec.discount + spec.freight + spec.ipi_total
        )


def test_cst10_invoice_is_xsd_valid() -> None:
    xml = build_nfe_xml(_spec_with("case-002", ("00", "10")))
    assert _xsd_errors(xml) == []


def test_cst10_group_has_the_xsd_order_and_the_st_arithmetic() -> None:
    spec = _spec_with("case-002", ("00", "10"))
    xml = build_nfe_xml(spec)
    group = _group(xml, 1)
    assert _children(group) == [
        "orig",
        "CST",
        "modBC",
        "vBC",
        "pICMS",
        "vICMS",
        "modBCST",
        "pMVAST",
        "vBCST",
        "pICMSST",
        "vICMSST",
    ]
    item = spec.items[1]
    assert item.tax_code == "10"
    assert _value(group, "vBCST") == _round(item.total * Decimal("1.40"))
    assert _value(group, "vICMSST") == _round(_value(group, "vBCST") * Decimal("0.18")) - _value(
        group, "vICMS"
    )


def test_cst10_header_carries_the_st_sums_and_vnf_is_exact() -> None:
    spec = _spec_with("case-002", ("00", "10"))
    xml = build_nfe_xml(spec)
    root = etree.fromstring(xml)
    st_base = sum(
        (Decimal(n.text or "0") for n in root.findall(".//n:det//n:vBCST", _NS)), Decimal("0.00")
    )
    st_amount = sum(
        (Decimal(n.text or "0") for n in root.findall(".//n:det//n:vICMSST", _NS)),
        Decimal("0.00"),
    )
    assert st_amount > 0
    assert _header(xml, "vBCST") == st_base == spec.st_base_total
    assert _header(xml, "vST") == st_amount == spec.st_total
    assert _header(xml, "vNF") == (
        _header(xml, "vProd")
        - _header(xml, "vDesc")
        + _header(xml, "vST")
        + _header(xml, "vFrete")
        + _header(xml, "vSeg")
        + _header(xml, "vOutro")
        + _header(xml, "vIPI")
    )


def test_cst10_is_printed_between_the_ncm_and_the_cfop(tmp_path: Path) -> None:
    spec = _spec_with("case-002", ("00", "10"))
    item = spec.items[1]
    page = _page_text(build_nfe_xml(spec), tmp_path)
    assert f"{item.ncm} 010 {item.cfop}" in page


def test_cst10_maps_back_with_exact_st_totals(tmp_path: Path) -> None:
    spec = _spec_with("case-002", ("00", "10"))
    invoice = _mapped(build_nfe_xml(spec), tmp_path)
    items = invoice["items"]
    assert isinstance(items, list)
    item = spec.items[1]
    assert items[1]["cst_csosn"] == "010"
    assert items[1]["icms_base"] == _money(item.icms_base)
    assert items[1]["icms_rate"] == _money(item.icms_rate)
    assert items[1]["icms_amount"] == _money(item.icms_amount)
    totals = invoice["totals"]
    assert isinstance(totals, dict)
    assert totals["icms_st_base"] == _money(spec.st_base_total)
    assert totals["icms_st_amount"] == _money(spec.st_total)
    _assert_valid_invoice(invoice)
