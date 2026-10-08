"""Ground truth: the documented XML-to-DANFE mapping, read in Python.

``invoice_from_xml`` turns an NF-e XML into the wire form of the v2 ``Invoice`` (snake_case keys,
money as two-decimal strings, quantity and unit price as four-decimal strings, rates as two-decimal
strings). It reproduces what the DANFE prints, including its quirks, and nothing smarter: the same
rules the .NET side and the dataset generator follow (docs/DANFE-MAPPING.md, plan 02-02).

Pure and offline: standard library only, no HTTP client.
"""

from __future__ import annotations

import xml.etree.ElementTree as ET
from decimal import ROUND_HALF_UP, Decimal, InvalidOperation
from pathlib import Path
from typing import Any

NFE_NS = "http://www.portalfiscal.inf.br/nfe"

# total/ICMSTot element for each totals.* wire field, in schema order.
_TOTALS = (
    ("icms_base", "vBC"),
    ("icms_amount", "vICMS"),
    ("icms_st_base", "vBCST"),
    ("icms_st_amount", "vST"),
    ("products_total", "vProd"),
    ("freight", "vFrete"),
    ("insurance", "vSeg"),
    ("discount", "vDesc"),
    ("other_expenses", "vOutro"),
    ("ipi_amount", "vIPI"),
    ("invoice_total", "vNF"),
)


def _tag(path: str) -> str:
    return "/".join(f"{{{NFE_NS}}}{part}" for part in path.split("/"))


def _find(parent: ET.Element, path: str) -> ET.Element | None:
    return parent.find(_tag(path))


def _required(parent: ET.Element, path: str) -> str:
    node = _find(parent, path)
    if node is None or node.text is None or not node.text.strip():
        raise ValueError(f"missing element {path!r} in NF-e XML")
    return node.text.strip()


def _optional(parent: ET.Element, path: str) -> str | None:
    node = _find(parent, path)
    if node is None or node.text is None or not node.text.strip():
        return None
    return node.text.strip()


def _fixed(text: str | None, places: int, element: str) -> str:
    """``text`` as a plain decimal with exactly ``places`` fraction digits, half away from zero.

    An absent value is zero (the DANFE prints ``0,00`` for an absent tax tag). A negative zero is
    normalised so ``-0.004`` gives ``0.00``, never ``-0.00``.
    """
    if text is None:
        return format(Decimal(0).quantize(Decimal(1).scaleb(-places)), "f")
    try:
        value = Decimal(text)
        if not value.is_finite():
            raise InvalidOperation
        rounded = value.quantize(Decimal(1).scaleb(-places), rounding=ROUND_HALF_UP)
    except InvalidOperation as error:
        raise ValueError(f"element {element!r} is not a decimal number: {text!r}") from error
    if rounded == 0:
        rounded = abs(rounded)
    return format(rounded, "f")


def _party_ie(parent: ET.Element) -> str | None:
    """The state registration verbatim (for example ``ISENTO``); ``None`` when absent or empty."""
    return _optional(parent, "IE")


def _access_key(inf: ET.Element) -> str:
    """``infNFe/@Id`` without the ``NFe`` prefix."""
    identifier = inf.get("Id")
    if not identifier:
        raise ValueError("missing attribute 'infNFe/@Id' in NF-e XML")
    return identifier.removeprefix("NFe")


def _icms_group(det: ET.Element, crt: str) -> tuple[str, ET.Element]:
    """``(cst_csosn, group)`` from the single child of ``imposto/ICMS``."""
    icms = _find(det, "imposto/ICMS")
    groups = [] if icms is None else list(icms)
    if len(groups) != 1:
        raise ValueError("element 'imposto/ICMS' must hold exactly one ICMS group")
    group = groups[0]
    origin = _required(group, "orig")
    code_tag = "CSOSN" if crt in ("1", "4") else "CST"
    return origin + _required(group, code_tag), group


def _item(det: ET.Element, crt: str) -> dict[str, Any]:
    prod = _find(det, "prod")
    if prod is None:
        raise ValueError("missing element 'prod' in NF-e XML")
    cst_csosn, icms = _icms_group(det, crt)
    return {
        "code": _required(prod, "cProd"),
        "description": _required(prod, "xProd"),
        "ncm": _required(prod, "NCM"),
        "cst_csosn": cst_csosn,
        "cfop": _required(prod, "CFOP"),
        "unit": _required(prod, "uCom"),
        "quantity": _fixed(_required(prod, "qCom"), 4, "qCom"),
        "unit_price": _fixed(_required(prod, "vUnCom"), 4, "vUnCom"),
        "total": _fixed(_required(prod, "vProd"), 2, "vProd"),
        "icms_base": _fixed(_optional(icms, "vBC"), 2, "vBC"),
        "icms_rate": _fixed(_optional(icms, "pICMS"), 2, "pICMS"),
        "icms_amount": _fixed(_optional(icms, "vICMS"), 2, "vICMS"),
        "ipi_rate": _fixed(_optional(det, "imposto/IPI/IPITrib/pIPI"), 2, "pIPI"),
        "ipi_amount": _fixed(_optional(det, "imposto/IPI/IPITrib/vIPI"), 2, "vIPI"),
    }


def invoice_from_xml(xml_path: Path) -> dict[str, Any]:
    """Read an NF-e XML into the wire form of the v2 ``Invoice``.

    Raises ``ValueError`` naming the element when a required element is missing or malformed.
    """
    root = ET.parse(xml_path).getroot()
    inf = root if root.tag == _tag("infNFe") else root.find(f".//{_tag('infNFe')}")
    if inf is None:
        raise ValueError(f"no infNFe element in {xml_path}")

    emit = _find(inf, "emit")
    dest = _find(inf, "dest")
    if emit is None:
        raise ValueError("missing element 'emit' in NF-e XML")
    if dest is None:
        raise ValueError("missing element 'dest' in NF-e XML")

    recipient_cnpj = _optional(dest, "CNPJ")
    recipient_cpf = _optional(dest, "CPF")
    if recipient_cnpj is not None:
        tax_id, tax_id_kind = recipient_cnpj, "cnpj"
    elif recipient_cpf is not None:
        tax_id, tax_id_kind = recipient_cpf, "cpf"
    else:
        raise ValueError("missing element 'dest/CNPJ' or 'dest/CPF' in NF-e XML")

    crt = _required(emit, "CRT")
    icms_tot = _find(inf, "total/ICMSTot")
    if icms_tot is None:
        raise ValueError("missing element 'total/ICMSTot' in NF-e XML")

    installments: list[dict[str, str]] = [
        {
            "number": _required(dup, "nDup"),
            "due_date": _required(dup, "dVenc"),
            "amount": _fixed(_required(dup, "vDup"), 2, "vDup"),
        }
        for dup in inf.findall(_tag("cobr/dup"))
    ]

    return {
        "access_key": _access_key(inf),
        "number": int(_required(inf, "ide/nNF")),
        "series": int(_required(inf, "ide/serie")),
        # The local date as printed: never converted to UTC.
        "issue_date": _required(inf, "ide/dhEmi")[:10],
        "operation_nature": _required(inf, "ide/natOp"),
        "issuer": {
            "cnpj": _required(emit, "CNPJ"),
            "name": _required(emit, "xNome"),
            "ie": _party_ie(emit),
            "uf": _required(emit, "enderEmit/UF"),
        },
        "recipient": {
            "tax_id": tax_id,
            "tax_id_kind": tax_id_kind,
            "name": _required(dest, "xNome"),
            "ie": _party_ie(dest),
            "uf": _required(dest, "enderDest/UF"),
        },
        "items": [_item(det, crt) for det in inf.findall(_tag("det"))],
        "totals": {
            field: _fixed(_optional(icms_tot, element), 2, element) for field, element in _TOTALS
        },
        "installments": installments,
    }
