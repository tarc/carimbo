"""RED stub: shapes only, behaviour arrives in the GREEN commit."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from decimal import Decimal

MASTER_SEED = 20261004
CASE_IDS = ("case-001", "case-002", "case-003")
MULTIPAGE_ITEMS = 80


@dataclass(frozen=True)
class PartySpec:
    cnpj: str
    name: str
    street: str
    number: str
    district: str
    city: str
    uf: str
    cep: str


@dataclass(frozen=True)
class ItemSpec:
    code: str
    description: str
    ncm: str
    cfop: str
    unit: str
    quantity: Decimal
    unit_price: Decimal

    @property
    def total(self) -> Decimal:
        raise NotImplementedError


@dataclass(frozen=True)
class CaseSpec:
    case_id: str
    issue_datetime: datetime
    series: int
    number: int
    numeric_code: int
    issuer: PartySpec
    recipient: PartySpec
    items: tuple[ItemSpec, ...]
    access_key: str

    @property
    def total_amount(self) -> Decimal:
        raise NotImplementedError


def build_case_spec(master_seed: int, case_id: str) -> CaseSpec:
    raise NotImplementedError
