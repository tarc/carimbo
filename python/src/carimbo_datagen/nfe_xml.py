"""Deterministic NF-e 4.00 ground-truth XML for a ``CaseSpec``. Synthetic data only (D-14).

The layout follows the official NF-e structure (``nfeProc`` > ``NFe`` > ``infNFe``, plus a
``protNFe``) but every value comes from the spec. No ``ds:Signature`` is written: validating
against the official XSD, which requires one, is DATA-04 in Phase 4.
"""

from __future__ import annotations

from datetime import timedelta
from decimal import Decimal

import lxml.etree as etree

from carimbo_datagen.spec import CaseSpec, ItemSpec, PartySpec, municipality_code

NFE_NS = "http://www.portalfiscal.inf.br/nfe"

_ZERO = "0.00"
_NO_GTIN = "SEM GTIN"
_COUNTRY_CODE = "1058"
_COUNTRY_NAME = "BRASIL"
_HOMOLOGATION = "2"
_NOTE = "DOCUMENTO SINTETICO GERADO PARA TESTES - SEM VALOR FISCAL"
_ICMS_TOT_ORDER = (
    "vBC",
    "vICMS",
    "vICMSDeson",
    "vFCP",
    "vBCST",
    "vST",
    "vFCPST",
    "vFCPSTRet",
    "vProd",
    "vFrete",
    "vSeg",
    "vDesc",
    "vII",
    "vIPI",
    "vIPIDevol",
    "vPIS",
    "vCOFINS",
    "vOutro",
    "vNF",
)
_IPI_ENQUADRAMENTO = "999"
_IPI_CST_TAXED = "50"
_MOD_BC_VALUE = "3"


def _qname(tag: str) -> str:
    return f"{{{NFE_NS}}}{tag}"


def _add(parent: etree._Element, tag: str, text: str | None = None, **attrs: str) -> etree._Element:
    node = etree.SubElement(parent, _qname(tag), attrs)
    if text is not None:
        node.text = text
    return node


def _plain(value: Decimal) -> str:
    return format(value, "f")


def _address(parent: etree._Element, tag: str, party: PartySpec) -> None:
    address = _add(parent, tag)
    _add(address, "xLgr", party.street)
    _add(address, "nro", party.number)
    _add(address, "xBairro", party.district)
    _add(address, "cMun", municipality_code(party.uf))
    _add(address, "xMun", party.city)
    _add(address, "UF", party.uf)
    _add(address, "CEP", party.cep)
    _add(address, "cPais", _COUNTRY_CODE)
    _add(address, "xPais", _COUNTRY_NAME)


def _ide(inf: etree._Element, spec: CaseSpec) -> None:
    key = spec.access_key
    ide = _add(inf, "ide")
    _add(ide, "cUF", key[0:2])
    _add(ide, "cNF", f"{spec.numeric_code:08d}")
    _add(ide, "natOp", spec.operation_nature)
    _add(ide, "mod", "55")
    _add(ide, "serie", str(spec.series))
    _add(ide, "nNF", str(spec.number))
    _add(ide, "dhEmi", spec.issue_datetime.isoformat())
    _add(ide, "tpNF", "1")
    _add(ide, "idDest", spec.id_dest)
    _add(ide, "cMunFG", municipality_code(spec.issuer.uf))
    _add(ide, "tpImp", "1")
    _add(ide, "tpEmis", "1")
    _add(ide, "cDV", key[43])
    _add(ide, "tpAmb", _HOMOLOGATION)
    _add(ide, "finNFe", "1")
    _add(ide, "indFinal", "1" if spec.recipient_tax_id_kind == "cpf" else "0")
    _add(ide, "indPres", "1")
    _add(ide, "procEmi", "0")
    _add(ide, "verProc", "carimbo-datagen")


def _parties(inf: etree._Element, spec: CaseSpec) -> None:
    emit = _add(inf, "emit")
    _add(emit, "CNPJ", spec.issuer.tax_id)
    _add(emit, "xNome", spec.issuer.name)
    _address(emit, "enderEmit", spec.issuer)
    if spec.issuer_ie is not None:
        _add(emit, "IE", spec.issuer_ie)
    _add(emit, "CRT", str(spec.crt))
    dest = _add(inf, "dest")
    _add(dest, "CPF" if spec.recipient_tax_id_kind == "cpf" else "CNPJ", spec.recipient.tax_id)
    _add(dest, "xNome", spec.recipient.name)
    _address(dest, "enderDest", spec.recipient)
    _add(dest, "indIEDest", "1" if spec.recipient_ie is not None else "9")
    if spec.recipient_ie is not None:
        _add(dest, "IE", spec.recipient_ie)


def _icms(imposto: etree._Element, item: ItemSpec) -> None:
    group = _add(_add(imposto, "ICMS"), item.icms_group)
    _add(group, "orig", "0")
    if item.icms_group in ("ICMS00", "ICMS20"):
        _add(group, "CST", item.tax_code)
        _add(group, "modBC", _MOD_BC_VALUE)
        if item.icms_group == "ICMS20":
            _add(group, "pRedBC", _plain(item.icms_reduction))
        _add(group, "vBC", _plain(item.icms_base))
        _add(group, "pICMS", _plain(item.icms_rate))
        _add(group, "vICMS", _plain(item.icms_amount))
        return
    _add(group, "CSOSN", item.tax_code)
    if item.icms_group == "ICMSSN101":
        _add(group, "pCredSN", _plain(item.credit_rate))
        _add(group, "vCredICMSSN", _plain(item.credit_amount))


def _ipi(imposto: etree._Element, item: ItemSpec) -> None:
    if not item.ipi_rate:
        return
    ipi = _add(imposto, "IPI")
    _add(ipi, "cEnq", _IPI_ENQUADRAMENTO)
    trib = _add(ipi, "IPITrib")
    _add(trib, "CST", _IPI_CST_TAXED)
    _add(trib, "vBC", _plain(item.total))
    _add(trib, "pIPI", _plain(item.ipi_rate))
    _add(trib, "vIPI", _plain(item.ipi_amount))


def _items(inf: etree._Element, spec: CaseSpec) -> None:
    for position, item in enumerate(spec.items, start=1):
        det = _add(inf, "det", nItem=str(position))
        prod = _add(det, "prod")
        _add(prod, "cProd", item.code)
        _add(prod, "cEAN", _NO_GTIN)
        _add(prod, "xProd", item.description)
        _add(prod, "NCM", item.ncm)
        _add(prod, "CFOP", item.cfop)
        _add(prod, "uCom", item.unit)
        _add(prod, "qCom", _plain(item.quantity))
        _add(prod, "vUnCom", _plain(item.unit_price))
        _add(prod, "vProd", _plain(item.total))
        _add(prod, "cEANTrib", _NO_GTIN)
        _add(prod, "uTrib", item.unit)
        _add(prod, "qTrib", _plain(item.quantity))
        _add(prod, "vUnTrib", _plain(item.unit_price))
        if item.freight:
            _add(prod, "vFrete", _plain(item.freight))
        if item.discount:
            _add(prod, "vDesc", _plain(item.discount))
        _add(prod, "indTot", "1")
        imposto = _add(det, "imposto")
        _icms(imposto, item)
        _ipi(imposto, item)
        _add(_add(_add(imposto, "PIS"), "PISNT"), "CST", "07")
        _add(_add(_add(imposto, "COFINS"), "COFINSNT"), "CST", "07")


def _totals(inf: etree._Element, spec: CaseSpec) -> None:
    # ICMSTot in the official element order; every tax and expense the profiles do not use is 0.00.
    values = {
        "vBC": spec.icms_base_total,
        "vICMS": spec.icms_total,
        "vProd": spec.products_total,
        "vFrete": spec.freight,
        "vDesc": spec.discount,
        "vIPI": spec.ipi_total,
        "vNF": spec.invoice_total,
    }
    icms_tot = _add(_add(inf, "total"), "ICMSTot")
    for tag in _ICMS_TOT_ORDER:
        _add(icms_tot, tag, _plain(values.get(tag, Decimal(_ZERO))))
    _add(_add(inf, "transp"), "modFrete", spec.freight_mode)
    _cobr(inf, spec)
    det_pag = _add(_add(inf, "pag"), "detPag")
    _add(det_pag, "tPag", spec.payment_code)
    _add(det_pag, "vPag", _plain(spec.invoice_total))
    _add(_add(inf, "infAdic"), "infCpl", _NOTE)


def _cobr(inf: etree._Element, spec: CaseSpec) -> None:
    if not spec.installments:
        return
    cobr = _add(inf, "cobr")
    fat = _add(cobr, "fat")
    _add(fat, "nFat", "1")
    _add(fat, "vOrig", _plain(spec.invoice_total))
    _add(fat, "vDesc", _ZERO)
    _add(fat, "vLiq", _plain(spec.invoice_total))
    for installment in spec.installments:
        dup = _add(cobr, "dup")
        _add(dup, "nDup", installment.number)
        _add(dup, "dVenc", installment.due_date.isoformat())
        _add(dup, "vDup", _plain(installment.amount))


def _protocol(proc: etree._Element, spec: CaseSpec) -> None:
    received = spec.issue_datetime + timedelta(minutes=1)
    prot = _add(proc, "protNFe", versao="4.00")
    info = _add(prot, "infProt")
    _add(info, "tpAmb", _HOMOLOGATION)
    _add(info, "verAplic", "carimbo-datagen")
    _add(info, "chNFe", spec.access_key)
    _add(info, "dhRecbto", received.isoformat())
    _add(info, "nProt", f"9{spec.number:09d}{spec.numeric_code % 100_000:05d}")
    _add(info, "cStat", "100")
    _add(info, "xMotivo", "Autorizado o uso da NF-e")


def build_nfe_xml(spec: CaseSpec) -> bytes:
    """Serialize ``spec`` as an NF-e ``nfeProc`` document: UTF-8, declaration, pretty-printed."""
    proc = etree.Element(_qname("nfeProc"), {"versao": "4.00"}, nsmap={None: NFE_NS})
    nfe = _add(proc, "NFe")
    inf = _add(nfe, "infNFe", Id=f"NFe{spec.access_key}", versao="4.00")
    _ide(inf, spec)
    _parties(inf, spec)
    _items(inf, spec)
    _totals(inf, spec)
    _protocol(proc, spec)
    return etree.tostring(proc, xml_declaration=True, encoding="UTF-8", pretty_print=True)
