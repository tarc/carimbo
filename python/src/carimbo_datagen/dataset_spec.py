"""Seeded case specifications for the dataset, one per quota-table base case. Synthetic data only.

Every case draws from its own ``random.Random`` derived from ``(master_seed, case_id)``, so a case
builds to the same bytes alone or after any other case. The party, IE, decimal and access-key
helpers are the skeleton's (``spec.py``); this module adds the dataset goods catalogue and the
quota-driven choices: item count, tax codes and traits.
"""

from __future__ import annotations

import random
from dataclasses import replace
from datetime import timedelta
from decimal import Decimal

from faker import Faker

from carimbo_datagen.ids import make_access_key
from carimbo_datagen.quota import BasePlan
from carimbo_datagen.spec import (
    _EMISSION_TYPE,
    _ISSUE_DAYS,
    _ISSUE_START,
    _MODEL,
    IBGE_UF_CODES,
    CaseSpec,
    ItemSpec,
    PartySpec,
    _company,
    _decimal,
    _recipient,
    _state_registration,
)

# One DANFE line per item: descriptions stay within 30 characters and words within 20.
DATASET_GOODS: tuple[tuple[str, str, str], ...] = (
    ("PARAFUSO SEXTAVADO ACO ZINC", "73181500", "UN"),
    ("CABO DE REDE UTP CAT6", "85444200", "M"),
    ("CAIXA DE PAPELAO ONDULADO", "48191000", "UN"),
    ("LUVA DE PROTECAO NITRILICA", "40151900", "PAR"),
    ("FITA ADESIVA TRANSPARENTE", "39191010", "UN"),
    ("TINTA ACRILICA 18L", "32091010", "LT"),
    ("CANETA ESFEROGRAFICA AZUL", "96081000", "CX"),
    ("RESMA PAPEL A4 75G", "48025610", "RM"),
    ("LAMPADA LED 9W", "85395200", "UN"),
    ("MANGUEIRA DE JARDIM 20M", "39173100", "UN"),
    ("PORCA SEXTAVADA M8", "73181600", "UN"),
    ("ARRUELA LISA ACO 1/4", "73182200", "UN"),
    ("DISJUNTOR BIPOLAR 32A", "85362000", "UN"),
    ("TOMADA 2P+T 20A", "85366910", "UN"),
    ("FIO FLEXIVEL 2,5MM AZUL", "85444900", "M"),
    ("CANO PVC SOLDAVEL 25MM", "39172300", "M"),
    ("JOELHO PVC 90 GRAUS 25MM", "39174090", "UN"),
    ("REGISTRO ESFERA LATAO 1/2", "84818095", "UN"),
    ("TORNEIRA CROMADA BANCADA", "84818099", "UN"),
    ("DETERGENTE NEUTRO 5L", "34022000", "LT"),
    ("DESINFETANTE CONCENTRADO 2L", "38089419", "LT"),
    ("SACO DE LIXO 100L PRETO", "39232190", "PCT"),
    ("PAPEL TOALHA INTERFOLHADO", "48182000", "PCT"),
    ("CADEIRA ESCRITORIO GIRATORIA", "94013090", "UN"),
    ("MESA ESCRITORIO 120X60", "94033000", "UN"),
    ("TECLADO USB ABNT2", "84716052", "UN"),
    ("MOUSE OPTICO USB", "84716053", "UN"),
    ("MONITOR LED 21 POLEGADAS", "85285200", "UN"),
    ("TONER PRETO COMPATIVEL", "84439933", "UN"),
    ("PEN DRIVE 32GB", "85234920", "UN"),
    ("BATERIA ALCALINA AA", "85061010", "CX"),
    ("EXTENSAO ELETRICA 5M", "85444200", "UN"),
)

_BASE_CODES = ("00", "20", "90", "40", "41", "50", "60")
_ST_CODES = ("10", "30", "51", "70")
_SIMPLES_CODES = ("101", "102", "400")
_OPERATION_NATURE = "VENDA DE MERCADORIA"


def _item_codes(rng: random.Random, plan: BasePlan, count: int) -> list[str]:
    if plan.regime == "simples":
        return [rng.choice(_SIMPLES_CODES)] * count
    base = rng.choice(_BASE_CODES)
    codes = [base] * count
    if "st_deferral" in plan.traits:
        chosen = rng.sample(_ST_CODES, rng.randint(1, 2))
        # A non-empty subset; with two or more items it is a proper one, so the codes mix.
        size = rng.randint(1, count - 1) if count >= 2 else 1
        for position in rng.sample(range(count), size):
            codes[position] = rng.choice(chosen)
    return codes


def build_dataset_spec(master_seed: int, plan: BasePlan) -> CaseSpec:
    """Build the spec of one base case from the master seed and its quota-table plan."""
    rng = random.Random(f"{master_seed}:{plan.case_id}")
    fake = Faker("pt_BR")
    fake.seed_instance(rng.getrandbits(64))

    issuer = _company(fake, rng, alphanumeric="alphanumeric_cnpj" in plan.traits)
    kind = "cpf" if "cpf_recipient" in plan.traits else "cnpj"
    recipient = _recipient(fake, rng, kind)
    while recipient.tax_id == issuer.tax_id or recipient.name == issuer.name:
        recipient = _recipient(fake, rng, kind)
    # The NF-e XSD pattern for CEP is eight digits: the dataset writes them without the hyphen.
    issuer = _plain_cep(issuer)
    recipient = _plain_cep(recipient)
    issuer_ie = _state_registration(rng)
    recipient_ie = _state_registration(rng) if kind == "cnpj" else None

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

    suffix = "102" if plan.regime == "simples" else "101"
    cfop = ("5" if issuer.uf == recipient.uf else "6") + suffix
    if plan.pinned_items is not None:
        item_count = plan.pinned_items
    else:
        item_count = rng.randint(*plan.item_band)
    codes = _item_codes(rng, plan, item_count)
    items = tuple(
        _item(rng, position, cfop, codes[position - 1]) for position in range(1, item_count + 1)
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
    return CaseSpec(
        case_id=plan.case_id,
        issue_datetime=issue_datetime,
        series=series,
        number=number,
        numeric_code=numeric_code,
        issuer=issuer,
        recipient=recipient,
        items=items,
        access_key=access_key,
        crt=plan.crt,
        operation_nature=_OPERATION_NATURE,
        issuer_ie=issuer_ie,
        recipient_ie=recipient_ie,
        recipient_tax_id_kind=kind,
        payment_code="01",
        freight_mode="9",
    )


def _plain_cep(party: PartySpec) -> PartySpec:
    return replace(party, cep=party.cep.replace("-", ""))


def _item(rng: random.Random, position: int, cfop: str, tax_code: str) -> ItemSpec:
    description, ncm, unit = rng.choice(DATASET_GOODS)
    quantity = _decimal(rng, 200, rng.randint(2, 4))
    unit_price: Decimal = _decimal(rng, 999, rng.randint(2, 4))
    return ItemSpec(
        code=f"D{position:03d}{rng.randint(10, 99)}",
        description=description,
        ncm=ncm,
        cfop=cfop,
        unit=unit,
        quantity=quantity,
        unit_price=unit_price,
        tax_code=tax_code,
    )
