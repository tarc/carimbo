"""Seeded case specifications for the three skeleton profiles. Synthetic data only (D-14).

Every case draws from its own ``random.Random`` derived from ``(master_seed, case_id)``, so a
case builds to the same bytes alone or after any other case. Party names always carry the visible
"SINTETICA" marker, identifiers come from our own generator, and nothing is read from nfelib.
"""

from __future__ import annotations

import random
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from decimal import ROUND_HALF_UP, Decimal

from faker import Faker

from carimbo_datagen.ids import make_access_key, make_cnpj

MASTER_SEED = 20261004
CASE_IDS = ("case-001", "case-002", "case-003")
# Items on the overflow case. Raised until the DANFE renders to at least two pages.
MULTIPAGE_ITEMS = 80

SYNTHETIC_MARKER = "SINTETICA"
_NAME_SUFFIX = f" {SYNTHETIC_MARKER} LTDA"
_NAME_MAX = 60
_MODEL = 55
_EMISSION_TYPE = 1
_BRT = timezone(timedelta(hours=-3))
_ISSUE_START = datetime(2026, 1, 1, tzinfo=_BRT)
_ISSUE_DAYS = 273  # 2026-01-01 .. 2026-09-30 inclusive

IBGE_UF_CODES = {"SP": 35, "RJ": 33, "MG": 31, "PR": 41, "RS": 43}
# Business words carry pt-BR diacritics so the encoding path is exercised by every party name.
_BUSINESS_WORDS = ("COMÉRCIO", "INDÚSTRIA", "SERVIÇOS", "DISTRIBUIÇÃO", "CONSTRUÇÕES")
_GOODS = (
    ("PARAFUSO SEXTAVADO ACO ZINCADO", "73181500", "UN"),
    ("CABO DE REDE UTP CAT6", "85444200", "M"),
    ("CAIXA DE PAPELAO ONDULADO", "48191000", "UN"),
    ("LUVA DE PROTECAO NITRILICA", "40151900", "PAR"),
    ("FITA ADESIVA TRANSPARENTE", "39191010", "UN"),
    ("TINTA ACRILICA 18L", "32091010", "LT"),
    ("CANETA ESFEROGRAFICA AZUL", "96081000", "CX"),
    ("RESMA PAPEL A4 75G", "48025610", "RM"),
    ("LAMPADA LED 9W", "85395200", "UN"),
    ("MANGUEIRA DE JARDIM 20M", "39173100", "UN"),
)
_CFOP = "5102"
_CENT = Decimal("0.01")


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
        return (self.quantity * self.unit_price).quantize(_CENT, rounding=ROUND_HALF_UP)


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
        return sum((item.total for item in self.items), Decimal("0.00"))


@dataclass(frozen=True)
class _Profile:
    alphanumeric_issuer: bool
    item_count: int


def _profiles() -> dict[str, _Profile]:
    return {
        "case-001": _Profile(alphanumeric_issuer=False, item_count=3),
        "case-002": _Profile(alphanumeric_issuer=True, item_count=4),
        "case-003": _Profile(alphanumeric_issuer=False, item_count=MULTIPAGE_ITEMS),
    }


def municipality_code(uf: str) -> str:
    """Placeholder 7-digit municipality code: the IBGE UF code plus zeros, not a real city."""
    return f"{IBGE_UF_CODES[uf]}00000"


def _company_name(fake: Faker, rng: random.Random) -> str:
    base = f"{fake.last_name()} {rng.choice(_BUSINESS_WORDS)}"
    return base[: _NAME_MAX - len(_NAME_SUFFIX)].rstrip() + _NAME_SUFFIX


def _party(fake: Faker, rng: random.Random, *, alphanumeric: bool) -> PartySpec:
    uf = rng.choice(sorted(IBGE_UF_CODES))
    return PartySpec(
        cnpj=make_cnpj(rng, alphanumeric=alphanumeric),
        name=_company_name(fake, rng),
        street=str(fake.street_name())[:60],
        number=str(fake.building_number())[:60],
        district=str(fake.bairro())[:60],
        city=str(fake.city())[:60],
        uf=uf,
        cep=str(fake.postcode()),
    )


def _decimal(rng: random.Random, max_units: int, places: int) -> Decimal:
    units = rng.randint(1, max_units)
    fraction = rng.randrange(10**places)
    return Decimal(f"{units}.{fraction:0{places}d}")


def _item(rng: random.Random, position: int) -> ItemSpec:
    description, ncm, unit = rng.choice(_GOODS)
    quantity = _decimal(rng, 200, rng.randint(2, 4))
    unit_price = _decimal(rng, 999, rng.randint(2, 4))
    return ItemSpec(
        code=f"P{position:03d}{rng.randint(10, 99)}",
        description=description,
        ncm=ncm,
        cfop=_CFOP,
        unit=unit,
        quantity=quantity,
        unit_price=unit_price,
    )


def build_case_spec(master_seed: int, case_id: str) -> CaseSpec:
    profile = _profiles().get(case_id)
    if profile is None:
        raise ValueError(f"unknown case id {case_id!r}; expected one of {CASE_IDS}")
    rng = random.Random(f"{master_seed}:{case_id}")
    fake = Faker("pt_BR")
    fake.seed_instance(rng.getrandbits(64))

    issuer = _party(fake, rng, alphanumeric=profile.alphanumeric_issuer)
    recipient = _party(fake, rng, alphanumeric=False)
    while recipient.cnpj == issuer.cnpj or recipient.name == issuer.name:
        recipient = _party(fake, rng, alphanumeric=False)

    issue_datetime = _ISSUE_START + timedelta(
        days=rng.randrange(_ISSUE_DAYS),
        hours=rng.randrange(8, 19),
        minutes=rng.randrange(60),
        seconds=rng.randrange(60),
    )
    series = rng.randint(1, 9)
    number = rng.randint(1, 999_999)
    numeric_code = rng.randrange(100_000_000)
    while numeric_code == number:
        numeric_code = rng.randrange(100_000_000)

    items = tuple(_item(rng, position) for position in range(1, profile.item_count + 1))
    access_key = make_access_key(
        uf=IBGE_UF_CODES[issuer.uf],
        year=issue_datetime.year,
        month=issue_datetime.month,
        cnpj=issuer.cnpj,
        model=_MODEL,
        series=series,
        number=number,
        emission_type=_EMISSION_TYPE,
        numeric_code=numeric_code,
    )
    return CaseSpec(
        case_id=case_id,
        issue_datetime=issue_datetime,
        series=series,
        number=number,
        numeric_code=numeric_code,
        issuer=issuer,
        recipient=recipient,
        items=items,
        access_key=access_key,
    )
