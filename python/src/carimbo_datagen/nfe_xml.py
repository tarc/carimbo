"""Deterministic NF-e 4.00 ground-truth XML for a ``CaseSpec``. Synthetic data only (D-14).

The layout follows the official NF-e structure (``nfeProc`` > ``NFe`` > ``infNFe``, plus a
``protNFe``) but every value comes from the spec. No ``ds:Signature`` is written: validating
against the official XSD, which requires one, is DATA-04 in Phase 4.
"""

from __future__ import annotations

from datetime import timedelta
from decimal import Decimal

import lxml.etree as etree

from carimbo_datagen.spec import CaseSpec, PartySpec, municipality_code

NFE_NS = "http://www.portalfiscal.inf.br/nfe"

_ZERO = "0.00"
_NO_GTIN = "SEM GTIN"
_COUNTRY_CODE = "1058"
_COUNTRY_NAME = "BRASIL"
_HOMOLOGATION = "2"
_NOTE = "DOCUMENTO SINTETICO GERADO PARA TESTES - SEM VALOR FISCAL"
_ICMS_TOT_ZEROED = (
    "vBC",
    "vICMS",
    "vICMSDeson",
    "vFCP",
    "vBCST",
    "vST",
    "vFCPST",
    "vFCPSTRet",
)
_ICMS_TOT_TAIL_ZEROED = (
    "vFrete",
    "vSeg",
    "vDesc",
    "vII",
    "vIPI",
    "vIPIDevol",
    "vPIS",
    "vCOFINS",
    "vOutro",
)


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
    _add(ide, "natOp", "VENDA DE MERCADORIA")
    _add(ide, "mod", "55")
    _add(ide, "serie", str(spec.series))
    _add(ide, "nNF", str(spec.number))
    _add(ide, "dhEmi", spec.issue_datetime.isoformat())
    _add(ide, "tpNF", "1")
    _add(ide, "idDest", "1")
    _add(ide, "cMunFG", municipality_code(spec.issuer.uf))
    _add(ide, "tpImp", "1")
    _add(ide, "tpEmis", "1")
    _add(ide, "cDV", key[43])
    _add(ide, "tpAmb", _HOMOLOGATION)
    _add(ide, "finNFe", "1")
    _add(ide, "indFinal", "0")
    _add(ide, "indPres", "1")
    _add(ide, "procEmi", "0")
    _add(ide, "verProc", "carimbo-datagen")


def _parties(inf: etree._Element, spec: CaseSpec) -> None:
    emit = _add(inf, "emit")
    _add(emit, "CNPJ", spec.issuer.cnpj)
    _add(emit, "xNome", spec.issuer.name)
    _address(emit, "enderEmit", spec.issuer)
    _add(emit, "IE", "ISENTO")
    _add(emit, "CRT", "1")
    dest = _add(inf, "dest")
    _add(dest, "CNPJ", spec.recipient.cnpj)
    _add(dest, "xNome", spec.recipient.name)
    _address(dest, "enderDest", spec.recipient)
    _add(dest, "indIEDest", "9")


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
        _add(prod, "indTot", "1")
        imposto = _add(det, "imposto")
        icms = _add(_add(imposto, "ICMS"), "ICMSSN102")
        _add(icms, "orig", "0")
        _add(icms, "CSOSN", "102")
        _add(_add(_add(imposto, "PIS"), "PISNT"), "CST", "07")
        _add(_add(_add(imposto, "COFINS"), "COFINSNT"), "CST", "07")


def _totals(inf: etree._Element, spec: CaseSpec) -> None:
    total = _plain(spec.total_amount)
    icms_tot = _add(_add(inf, "total"), "ICMSTot")
    for tag in _ICMS_TOT_ZEROED:
        _add(icms_tot, tag, _ZERO)
    _add(icms_tot, "vProd", total)
    for tag in _ICMS_TOT_TAIL_ZEROED:
        _add(icms_tot, tag, _ZERO)
    _add(icms_tot, "vNF", total)
    _add(_add(inf, "transp"), "modFrete", "9")
    det_pag = _add(_add(inf, "pag"), "detPag")
    _add(det_pag, "tPag", "01")
    _add(det_pag, "vPag", total)
    _add(_add(inf, "infAdic"), "infCpl", _NOTE)


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
