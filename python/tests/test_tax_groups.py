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
import pytest

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


def _spec_with(
    case_id: str, codes: tuple[str, ...], crt: int | None = None, count: int | None = None
) -> CaseSpec:
    """A skeleton spec with the item tax codes cycled from ``codes``.

    ``count`` grows or keeps the item list to that length by cycling the skeleton items.

    Installments are dropped and the CEPs lose their hyphen, because the XSD pattern is eight
    digits and the generator's own CEP format is not changed by this plan.
    """
    spec = build_case_spec(MASTER_SEED, case_id)
    base = spec.items
    items = tuple(
        replace(
            base[index % len(base)],
            code=base[index % len(base)].code if index < len(base) else f"P{index + 1:03d}00",
            tax_code=codes[index % len(codes)],
        )
        for index in range(len(base) if count is None else count)
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


# --- Task 2: every code the validators classify ---------------------------------------------------

_CST = ("00", "10", "20", "30", "40", "41", "50", "51", "60", "70", "90")
_CSOSN_SIMPLES = ("101", "102", "103", "300", "400", "500", "900")
_CSOSN_MEI = ("102", "103", "400")
_TAXED = {"00", "20", "90", "900"}
_NOT_TAXED_GROUPS = {"40", "41", "50", "60", "102", "103", "300", "400", "500"}
_CASES: list[tuple[int, str, str]] = (
    [(3, "case-002", code) for code in _CST]
    + [(1, "case-001", code) for code in _CSOSN_SIMPLES]
    + [(4, "case-001", code) for code in _CSOSN_MEI]
)
_IDS = [f"crt{crt}-{code}" for crt, _, code in _CASES]


def _single(crt: int, case_id: str, code: str) -> CaseSpec:
    return _spec_with(case_id, (code,), crt=crt)


@pytest.mark.parametrize(("crt", "case_id", "code"), _CASES, ids=_IDS)
def test_every_code_is_xsd_valid(crt: int, case_id: str, code: str) -> None:
    assert _xsd_errors(build_nfe_xml(_single(crt, case_id, code))) == []


@pytest.mark.parametrize(("crt", "case_id", "code"), _CASES, ids=_IDS)
def test_every_code_is_printed_after_the_ncm_and_before_the_cfop(
    crt: int, case_id: str, code: str, tmp_path: Path
) -> None:
    spec = _single(crt, case_id, code)
    page = _page_text(build_nfe_xml(spec), tmp_path)
    header = "NCM/SH CSOSN CFOP" if crt in (1, 4) else "NCM/SH CST CFOP"
    other = "NCM/SH CST CFOP" if crt in (1, 4) else "NCM/SH CSOSN CFOP"
    assert header in page
    assert other not in page
    first = spec.items[0]
    assert f"{first.ncm} 0{code} {first.cfop}" in page


@pytest.mark.parametrize(("crt", "case_id", "code"), _CASES, ids=_IDS)
def test_every_code_maps_to_a_strictly_valid_invoice(
    crt: int, case_id: str, code: str, tmp_path: Path
) -> None:
    spec = _single(crt, case_id, code)
    invoice = _mapped(build_nfe_xml(spec), tmp_path)
    items = invoice["items"]
    assert isinstance(items, list)
    assert len(items) == len(spec.items)
    for mapped, item in zip(items, spec.items, strict=True):
        assert mapped["cst_csosn"] == "0" + code
        assert mapped["icms_base"] == _money(item.icms_base)
        assert mapped["icms_rate"] == _money(item.icms_rate)
        assert mapped["icms_amount"] == _money(item.icms_amount)
    _assert_valid_invoice(invoice)


@pytest.mark.parametrize(("crt", "case_id", "code"), _CASES, ids=_IDS)
def test_every_code_keeps_header_sums_and_vnf_exact(crt: int, case_id: str, code: str) -> None:
    _assert_exact_header(build_nfe_xml(_single(crt, case_id, code)))


def _sum_of(xml: bytes, tag: str) -> Decimal:
    nodes = etree.fromstring(xml).findall(f".//n:det/n:imposto/n:ICMS/*/n:{tag}", _NS)
    return sum((Decimal(n.text or "0") for n in nodes), Decimal("0.00"))


def _assert_exact_header(xml: bytes) -> None:
    assert _header(xml, "vBC") == _sum_of(xml, "vBC")
    assert _header(xml, "vICMS") == _sum_of(xml, "vICMS")
    assert _header(xml, "vBCST") == _sum_of(xml, "vBCST")
    assert _header(xml, "vST") == _sum_of(xml, "vICMSST")
    assert _header(xml, "vNF") == (
        _header(xml, "vProd")
        - _header(xml, "vDesc")
        + _header(xml, "vST")
        + _header(xml, "vFrete")
        + _header(xml, "vSeg")
        + _header(xml, "vOutro")
        + _header(xml, "vIPI")
    )


@pytest.mark.parametrize(("crt", "case_id", "code"), _CASES, ids=_IDS)
def test_taxed_codes_compute_vicms_and_not_taxed_groups_have_none(
    crt: int, case_id: str, code: str
) -> None:
    xml = build_nfe_xml(_single(crt, case_id, code))
    count = len(etree.fromstring(xml).findall(".//n:det", _NS))
    for position in range(count):
        group = _group(xml, position)
        if code in _TAXED:
            assert _value(group, "vICMS") == _round(
                _value(group, "vBC") * _value(group, "pICMS") / 100
            )
        if code in _NOT_TAXED_GROUPS:
            assert group.find("n:vICMS", _NS) is None


def test_cst51_defers_part_of_the_operation_icms() -> None:
    xml = build_nfe_xml(_single(3, "case-002", "51"))
    group = _group(xml, 0)
    assert _children(group) == [
        "orig",
        "CST",
        "modBC",
        "vBC",
        "pICMS",
        "vICMSOp",
        "pDif",
        "vICMSDif",
        "vICMS",
    ]
    operation = _round(_value(group, "vBC") * _value(group, "pICMS") / 100)
    deferred = _round(operation * _value(group, "pDif") / 100)
    assert _value(group, "vICMSOp") == operation
    assert _value(group, "vICMSDif") == deferred > 0
    assert _value(group, "vICMS") == operation - deferred


def test_cst30_carries_only_st_and_cst70_reduces_the_base_before_vbc() -> None:
    group30 = _group(build_nfe_xml(_single(3, "case-002", "30")), 0)
    assert group30.find("n:vBC", _NS) is None
    assert group30.find("n:vICMS", _NS) is None
    assert _value(group30, "vICMSST") > 0
    group70 = _group(build_nfe_xml(_single(3, "case-002", "70")), 0)
    children = _children(group70)
    assert children.index("pRedBC") < children.index("vBC")
    assert _value(group70, "vICMSST") > 0


def test_a_cst_invoice_cycling_all_eleven_codes_is_valid_and_exact(tmp_path: Path) -> None:
    spec = _spec_with("case-002", _CST, count=len(_CST))
    xml = build_nfe_xml(spec)
    assert _xsd_errors(xml) == []
    _assert_exact_header(xml)
    assert spec.st_total > 0
    invoice = _mapped(xml, tmp_path)
    items = invoice["items"]
    assert isinstance(items, list)
    assert {len(str(item["cst_csosn"])) for item in items} == {3}
    assert [str(item["cst_csosn"]) for item in items] == ["0" + code for code in _CST]
    _assert_valid_invoice(invoice)


def test_a_csosn_invoice_cycling_all_seven_codes_is_valid_and_exact(tmp_path: Path) -> None:
    spec = _spec_with("case-001", _CSOSN_SIMPLES, count=len(_CSOSN_SIMPLES))
    xml = build_nfe_xml(spec)
    assert _xsd_errors(xml) == []
    _assert_exact_header(xml)
    invoice = _mapped(xml, tmp_path)
    items = invoice["items"]
    assert isinstance(items, list)
    assert {len(str(item["cst_csosn"])) for item in items} == {4}
    assert [str(item["cst_csosn"]) for item in items] == ["0" + code for code in _CSOSN_SIMPLES]
    _assert_valid_invoice(invoice)


def test_regime_follows_the_family_of_the_crt() -> None:
    base = build_case_spec(MASTER_SEED, "case-001")
    assert replace(base, crt=1).regime == "simples"
    assert replace(base, crt=4).regime == "simples"
    assert replace(base, crt=3).regime == "normal"


def test_the_empty_edge_maps_to_zero_amounts_and_no_installments(tmp_path: Path) -> None:
    spec = _single(1, "case-001", "102")
    invoice = _mapped(build_nfe_xml(spec), tmp_path)
    totals = invoice["totals"]
    assert isinstance(totals, dict)
    assert totals["ipi_amount"] == "0.00"
    assert totals["icms_amount"] == "0.00"
    assert invoice["installments"] == []
    _assert_valid_invoice(invoice)
