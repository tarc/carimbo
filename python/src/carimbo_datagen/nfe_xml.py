"""RED stub: signature only, behaviour arrives in the GREEN commit."""

from __future__ import annotations

from carimbo_datagen.spec import CaseSpec

NFE_NS = "http://www.portalfiscal.inf.br/nfe"


def build_nfe_xml(spec: CaseSpec) -> bytes:
    raise NotImplementedError
