"""Seeded case specifications for the three skeleton profiles. Synthetic data only (D-14).

Every case draws from its own ``random.Random`` derived from ``(master_seed, case_id)``, so a
case builds to the same bytes alone or after any other case. Party names always carry the visible
"SINTETICA" marker, identifiers come from our own generator, and nothing is read from nfelib.

The three profiles (CONTEXT D-16) are a Simples Nacional invoice on CSOSN 101, a Regime Normal
invoice with IPI, freight, discount and two installments, and a multi-page Simples invoice with an
alphanumeric issuer CNPJ and a CPF recipient. Every total is computed from the items with the same
formulas the validators check (vNF = vProd - vDesc + vST + vFrete + vSeg + vOutro + vIPI, taxes
rounded half up to cents), so the generated ground truth is internally consistent.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, replace
from datetime import date, datetime, timedelta, timezone
from decimal import ROUND_HALF_UP, Decimal

from faker import Faker

from carimbo_datagen.ids import make_access_key, make_cnpj, make_cpf

MASTER_SEED = 20261004
CASE_IDS = ("case-001", "case-002", "case-003")
# Items on the overflow case. Fewer items mean fewer output tokens per extraction and repair
# attempt; 50 items still overflow the DANFE to a second page (checked by the datagen tests).
MULTIPAGE_ITEMS = 50

SYNTHETIC_MARKER = "SINTETICA"
_NAME_SUFFIX = f" {SYNTHETIC_MARKER} LTDA"
_PERSON_SUFFIX = f" {SYNTHETIC_MARKER}"
_NAME_MAX = 60
_MODEL = 55
_EMISSION_TYPE = 1
_BRT = timezone(timedelta(hours=-3))
_ISSUE_START = datetime(2026, 1, 1, tzinfo=_BRT)
_ISSUE_DAYS = 273  # 2026-01-01 .. 2026-09-30 inclusive
_IE_DIGITS = 12
_ISENTO = "ISENTO"

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
_CENT = Decimal("0.01")
_HUNDRED = Decimal(100)
_ZERO = Decimal("0.00")

# Tax parameters of each item tax code (CST for Regime Normal, CSOSN for Simples Nacional).
_ICMS_RATE = {"00": Decimal("18.00"), "20": Decimal("12.00")}
_ICMS_REDUCTION = {"20": Decimal("33.33")}
_CREDIT_RATE = {"101": Decimal("3.10")}
_ICMS_GROUP = {
    "00": "ICMS00",
    "20": "ICMS20",
    "101": "ICMSSN101",
    "102": "ICMSSN102",
    "400": "ICMSSN102",
}
_INSTALLMENT_OFFSETS = (30, 60)
_FREIGHT = Decimal("25.00")
_DISCOUNT = Decimal("10.00")


def _cents(value: Decimal) -> Decimal:
    return value.quantize(_CENT, rounding=ROUND_HALF_UP)


@dataclass(frozen=True)
class PartySpec:
    tax_id: str
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
    tax_code: str = "102"
    ipi_rate: Decimal = _ZERO
    freight: Decimal = _ZERO
    discount: Decimal = _ZERO

    @property
    def total(self) -> Decimal:
        return _cents(self.quantity * self.unit_price)

    @property
    def icms_group(self) -> str:
        """The NF-e ICMS group element this item is written with."""
        return _ICMS_GROUP[self.tax_code]

    @property
    def cst_csosn(self) -> str:
        """The printed code: origin digit 0 followed by the CST or CSOSN."""
        return "0" + self.tax_code

    @property
    def icms_rate(self) -> Decimal:
        return _ICMS_RATE.get(self.tax_code, _ZERO)

    @property
    def icms_reduction(self) -> Decimal:
        return _ICMS_REDUCTION.get(self.tax_code, _ZERO)

    @property
    def credit_rate(self) -> Decimal:
        return _CREDIT_RATE.get(self.tax_code, _ZERO)

    @property
    def icms_base(self) -> Decimal:
        """vBC: the item total, reduced by pRedBC for CST 20; zero for the Simples codes."""
        if self.tax_code not in _ICMS_RATE:
            return _ZERO
        if self.icms_reduction:
            return _cents(self.total * (_HUNDRED - self.icms_reduction) / _HUNDRED)
        return self.total

    @property
    def icms_amount(self) -> Decimal:
        return _cents(self.icms_base * self.icms_rate / _HUNDRED)

    @property
    def credit_amount(self) -> Decimal:
        """vCredICMSSN of a CSOSN 101 item; printed on no DANFE column."""
        return _cents(self.total * self.credit_rate / _HUNDRED)

    @property
    def ipi_amount(self) -> Decimal:
        return _cents(self.total * self.ipi_rate / _HUNDRED)


@dataclass(frozen=True)
class InstallmentSpec:
    number: str
    due_date: date
    amount: Decimal


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
    crt: int = 1
    operation_nature: str = "VENDA DE MERCADORIA"
    issuer_ie: str | None = _ISENTO
    recipient_ie: str | None = None
    recipient_tax_id_kind: str = "cnpj"
    installments: tuple[InstallmentSpec, ...] = ()
    payment_code: str = "01"
    freight_mode: str = "9"

    @property
    def regime(self) -> str:
        return "simples" if self.crt == 1 else "normal"

    @property
    def id_dest(self) -> str:
        """1 inside one UF, 2 between UFs."""
        return "1" if self.issuer.uf == self.recipient.uf else "2"

    @property
    def products_total(self) -> Decimal:
        return sum((item.total for item in self.items), _ZERO)

    @property
    def icms_base_total(self) -> Decimal:
        return sum((item.icms_base for item in self.items), _ZERO)

    @property
    def icms_total(self) -> Decimal:
        return sum((item.icms_amount for item in self.items), _ZERO)

    @property
    def ipi_total(self) -> Decimal:
        return sum((item.ipi_amount for item in self.items), _ZERO)

    @property
    def freight(self) -> Decimal:
        return sum((item.freight for item in self.items), _ZERO)

    @property
    def discount(self) -> Decimal:
        return sum((item.discount for item in self.items), _ZERO)

    @property
    def invoice_total(self) -> Decimal:
        """vNF = vProd - vDesc + vST + vFrete + vSeg + vOutro + vIPI (ST, seg, outro are 0)."""
        return self.products_total - self.discount + self.freight + self.ipi_total


@dataclass(frozen=True)
class _Profile:
    crt: int
    operation_nature: str
    item_count: int
    alphanumeric_issuer: bool
    recipient_tax_id_kind: str
    issuer_isento: bool
    recipient_has_ie: bool
    tax_codes: tuple[str, ...]
    cfop_suffix: str
    ipi_rates: tuple[Decimal, ...] = ()
    freight_discount: bool = False
    installment_count: int = 0
    payment_code: str = "01"
    freight_mode: str = "9"


def _profiles() -> dict[str, _Profile]:
    return {
        "case-001": _Profile(
            crt=1,
            operation_nature="VENDA DE MERCADORIA ADQUIRIDA DE TERCEIROS",
            item_count=3,
            alphanumeric_issuer=False,
            recipient_tax_id_kind="cnpj",
            issuer_isento=False,
            recipient_has_ie=False,
            tax_codes=("101",),
            cfop_suffix="102",
        ),
        "case-002": _Profile(
            crt=3,
            operation_nature="VENDA DE PRODUCAO DO ESTABELECIMENTO",
            item_count=4,
            alphanumeric_issuer=False,
            recipient_tax_id_kind="cnpj",
            issuer_isento=False,
            recipient_has_ie=True,
            tax_codes=("00", "20"),
            cfop_suffix="101",
            ipi_rates=(Decimal("5.00"), Decimal("10.00")),
            freight_discount=True,
            installment_count=2,
            payment_code="15",
            freight_mode="0",
        ),
        "case-003": _Profile(
            crt=1,
            operation_nature="VENDA DE MERCADORIA",
            item_count=MULTIPAGE_ITEMS,
            alphanumeric_issuer=True,
            recipient_tax_id_kind="cpf",
            issuer_isento=True,
            recipient_has_ie=False,
            tax_codes=("102", "400"),
            cfop_suffix="102",
        ),
    }


def municipality_code(uf: str) -> str:
    """Placeholder 7-digit municipality code: the IBGE UF code plus zeros, not a real city."""
    return f"{IBGE_UF_CODES[uf]}00000"


def _company_name(fake: Faker, rng: random.Random) -> str:
    base = f"{fake.last_name()} {rng.choice(_BUSINESS_WORDS)}"
    return base[: _NAME_MAX - len(_NAME_SUFFIX)].rstrip() + _NAME_SUFFIX


def _person_name(fake: Faker) -> str:
    base = f"{fake.first_name()} {fake.last_name()}"
    return base[: _NAME_MAX - len(_PERSON_SUFFIX)].rstrip() + _PERSON_SUFFIX


def _party(fake: Faker, rng: random.Random, *, tax_id: str, name: str) -> PartySpec:
    uf = rng.choice(sorted(IBGE_UF_CODES))
    return PartySpec(
        tax_id=tax_id,
        name=name,
        street=str(fake.street_name())[:60],
        number=str(fake.building_number())[:60],
        district=str(fake.bairro())[:60],
        city=str(fake.city())[:60],
        uf=uf,
        cep=str(fake.postcode()),
    )


def _company(fake: Faker, rng: random.Random, *, alphanumeric: bool) -> PartySpec:
    return _party(
        fake,
        rng,
        tax_id=make_cnpj(rng, alphanumeric=alphanumeric),
        name=_company_name(fake, rng),
    )


def _recipient(fake: Faker, rng: random.Random, kind: str) -> PartySpec:
    if kind == "cpf":
        return _party(fake, rng, tax_id=make_cpf(rng), name=_person_name(fake))
    return _company(fake, rng, alphanumeric=False)


def _state_registration(rng: random.Random) -> str:
    return "".join(str(rng.randrange(10)) for _ in range(_IE_DIGITS))


def _decimal(rng: random.Random, max_units: int, places: int) -> Decimal:
    units = rng.randint(1, max_units)
    fraction = rng.randrange(10**places)
    return Decimal(f"{units}.{fraction:0{places}d}")


def _item(rng: random.Random, position: int, profile: _Profile, cfop: str) -> ItemSpec:
    description, ncm, unit = rng.choice(_GOODS)
    quantity = _decimal(rng, 200, rng.randint(2, 4))
    unit_price = _decimal(rng, 999, rng.randint(2, 4))
    index = position - 1
    first = position == 1
    return ItemSpec(
        code=f"P{position:03d}{rng.randint(10, 99)}",
        description=description,
        ncm=ncm,
        cfop=cfop,
        unit=unit,
        quantity=quantity,
        unit_price=unit_price,
        tax_code=profile.tax_codes[index % len(profile.tax_codes)],
        ipi_rate=profile.ipi_rates[index % len(profile.ipi_rates)] if profile.ipi_rates else _ZERO,
        freight=_FREIGHT if profile.freight_discount and first else _ZERO,
        discount=_DISCOUNT if profile.freight_discount and first else _ZERO,
    )


def _installments(
    profile: _Profile, issue_date: date, invoice_total: Decimal
) -> tuple[InstallmentSpec, ...]:
    if profile.installment_count == 0:
        return ()
    first = _cents(invoice_total / profile.installment_count)
    amounts = [first] * (profile.installment_count - 1)
    amounts.append(invoice_total - first * (profile.installment_count - 1))
    return tuple(
        InstallmentSpec(
            number=f"{index:03d}",
            due_date=issue_date + timedelta(days=_INSTALLMENT_OFFSETS[index - 1]),
            amount=amount,
        )
        for index, amount in enumerate(amounts, start=1)
    )


def build_case_spec(master_seed: int, case_id: str) -> CaseSpec:
    profile = _profiles().get(case_id)
    if profile is None:
        raise ValueError(f"unknown case id {case_id!r}; expected one of {CASE_IDS}")
    rng = random.Random(f"{master_seed}:{case_id}")
    fake = Faker("pt_BR")
    fake.seed_instance(rng.getrandbits(64))

    issuer = _company(fake, rng, alphanumeric=profile.alphanumeric_issuer)
    recipient = _recipient(fake, rng, profile.recipient_tax_id_kind)
    while recipient.tax_id == issuer.tax_id or recipient.name == issuer.name:
        recipient = _recipient(fake, rng, profile.recipient_tax_id_kind)
    issuer_ie = _ISENTO if profile.issuer_isento else _state_registration(rng)
    recipient_ie = _state_registration(rng) if profile.recipient_has_ie else None

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

    # CFOP first digit: 5 inside one UF, 6 between UFs.
    cfop = ("5" if issuer.uf == recipient.uf else "6") + profile.cfop_suffix
    items = tuple(
        _item(rng, position, profile, cfop) for position in range(1, profile.item_count + 1)
    )
    access_key = make_access_key(
        uf=IBGE_UF_CODES[issuer.uf],
        year=issue_datetime.year,
        month=issue_datetime.month,
        cnpj=issuer.tax_id,
        model=_MODEL,
        series=series,
        number=number,
        emission_type=_EMISSION_TYPE,
        numeric_code=numeric_code,
    )
    spec = CaseSpec(
        case_id=case_id,
        issue_datetime=issue_datetime,
        series=series,
        number=number,
        numeric_code=numeric_code,
        issuer=issuer,
        recipient=recipient,
        items=items,
        access_key=access_key,
        crt=profile.crt,
        operation_nature=profile.operation_nature,
        issuer_ie=issuer_ie,
        recipient_ie=recipient_ie,
        recipient_tax_id_kind=profile.recipient_tax_id_kind,
        payment_code=profile.payment_code,
        freight_mode=profile.freight_mode,
    )
    installments = _installments(profile, issue_datetime.date(), spec.invoice_total)
    return replace(spec, installments=installments)
