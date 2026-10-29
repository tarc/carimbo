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

from carimbo_datagen.ids import make_access_key, make_cnpj
from carimbo_datagen.quota import BasePlan
from carimbo_datagen.spec import (
    _BUSINESS_WORDS,
    _EMISSION_TYPE,
    _ISSUE_DAYS,
    _ISSUE_START,
    _MODEL,
    _NAME_MAX,
    _NAME_SUFFIX,
    _PERSON_SUFFIX,
    IBGE_UF_CODES,
    CaseSpec,
    ItemSpec,
    PartySpec,
    _company,
    _company_name,
    _decimal,
    _party,
    _recipient,
    _state_registration,
    installment_plan,
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

# One family per invoice by construction (RESEARCH Pitfall 7): Normal bases draw CSTs, Simples
# bases CSOSNs. The ST pool is reachable only through the st_deferral trait.
_NORMAL_CODES: tuple[str, ...] = ("00", "20", "90", "40", "41", "50", "60")
_ST_CODES: tuple[str, ...] = ("10", "30", "51", "70")
_SIMPLES_CODES: tuple[str, ...] = ("101", "102", "103", "300", "400", "500", "900")
# An MEI issuer (CRT 4) is on the simplified regime: no credit, no ST, no other-state codes.
_MEI_CODES: tuple[str, ...] = ("102", "103", "400")
_OPERATION_NATURE = "VENDA DE MERCADORIA"

# Item totals stay under about R$ 31,000: the DANFE item table wraps a wider value onto a second
# line (measured from R$ 60,000), and a wrapped row pushes a 40-item invoice onto a second page.
_MAX_QUANTITY = 60
_MAX_UNIT_PRICE = 499

# long_text slots (CONTEXT D-04, D-05): text that wraps on the DANFE but is never cut. Every word
# stays at most 20 code points (RESEARCH Pitfall 3: a longer token is broken mid-token by the
# column), names and natOp at most 60, descriptions 100 to 120.
_DESCRIPTION_MIN = 100
_DESCRIPTION_MAX = 120
_QUALIFIERS: tuple[str, ...] = (
    "REFORCADO",
    "GALVANIZADO",
    "INDUSTRIAL",
    "PROFISSIONAL",
    "EMBALAGEM",
    "COM",
    "CERTIFICADO",
    "DE",
    "ALTA",
    "QUALIDADE",
    "PARA",
    "USO",
    "GERAL",
    "LINHA",
    "COMERCIAL",
    "RESISTENCIA",
    "LOTE",
    "ESPECIAL",
    "IMPORTADO",
    "NACIONAL",
    "PADRAO",
    "BRASILEIRO",
    "MODELO",
    "ATOXICO",
    "ANTICORROSIVO",
    "ELETROMECANICO",
    "INTERCAMBIAVEL",
    "BIODEGRADAVEL",
    "AUTOEXTINGUIVEL",
    "DURABILIDADE",
    "ESTENDIDA",
    "GARANTIA",
    "FABRICANTE",
    "APROVADO",
    "NORMA",
    "TECNICA",
)
_REGION_WORDS: tuple[str, ...] = (
    "NORDESTE",
    "PAULISTA",
    "CENTRAL",
    "ATLANTICA",
    "MERIDIONAL",
    "CONTINENTAL",
    "BRASILEIRA",
    "SUDESTE",
    "SERRANA",
    "LITORANEA",
    "PLANALTO",
    "FRONTEIRA",
)
_LONG_OPERATION_NATURES: tuple[str, ...] = (
    "VENDA DE MERCADORIA ADQUIRIDA OU RECEBIDA DE TERCEIROS",
    "VENDA DE PRODUCAO DO ESTABELECIMENTO COM ENTREGA FUTURA",
    "REMESSA DE MERCADORIA PARA DEMONSTRACAO OU EXPOSICAO",
    "VENDA DE MERCADORIA SUJEITA A SUBSTITUICAO TRIBUTARIA",
    "VENDA DE MERCADORIA ADQUIRIDA PARA INDUSTRIALIZACAO",
    "VENDA DE PRODUTO INDUSTRIALIZADO SOB ENCOMENDA ESPECIAL",
)
_NAME_PICKS = 60

# confusable slots (CONTEXT D-04): near-identical adjacent rows, a many-digit line and similar
# parties. Every value stays under the caps the renderer's float formatting needs (RESEARCH
# Pitfall 8): amounts under 10^13, quantities and unit prices under 10^9.
_CONFUSABLE_MIN_ITEMS = 4
_BIG_QUANTITY = (2_000, 9_999)
_BIG_UNIT_PRICE = (500_000, 999_999)

_IPI_PROBABILITY = 0.4
_IPI_RATES = (Decimal("5.00"), Decimal("10.00"))
_FREIGHT_DISCOUNT_PROBABILITY = 0.35
_FREIGHT_CENTS = (1_000, 50_000)
_DISCOUNT_MIN_CENTS = 500
_DISCOUNT_MAX_CENTS = 30_000
_INSTALLMENT_PROBABILITY = 0.4
_INSTALLMENT_OFFSETS = (30, 60, 90)
_ISENTO_PROBABILITY = 0.2
_RECIPIENT_IE_PROBABILITY = 0.7


def _item_codes(rng: random.Random, plan: BasePlan, count: int) -> list[str]:
    pool: tuple[str, ...]
    if plan.regime == "normal":
        pool = _NORMAL_CODES
    else:
        pool = _MEI_CODES if plan.crt == 4 else _SIMPLES_CODES
    codes: list[str]
    if "mixed_tax_codes" in plan.traits:
        kinds = rng.randint(2, min(4, len(pool), count))
        chosen = rng.sample(pool, kinds)
        codes = chosen + [rng.choice(chosen) for _ in range(count - kinds)]
    else:
        codes = [rng.choice(pool)] * count
    if "st_deferral" in plan.traits:
        st_chosen = rng.sample(_ST_CODES, rng.randint(1, 2))
        # A non-empty subset; with two or more items it is a proper one, so the codes mix.
        size = rng.randint(1, count - 1) if count >= 2 else 1
        for position in rng.sample(range(count), size):
            codes[position] = rng.choice(st_chosen)
    return codes


def build_dataset_spec(master_seed: int, plan: BasePlan) -> CaseSpec:
    """Build the spec of one base case from the master seed and its quota-table plan."""
    rng = random.Random(f"{master_seed}:{plan.case_id}")
    fake = Faker("pt_BR")
    fake.seed_instance(rng.getrandbits(64))

    issuer = _company(fake, rng, alphanumeric="alphanumeric_cnpj" in plan.traits)
    kind = "cpf" if "cpf_recipient" in plan.traits else "cnpj"
    alphanumeric_recipient = "alphanumeric_recipient" in plan.traits
    recipient = _draw_recipient(fake, rng, kind, alphanumeric_recipient)
    while recipient.tax_id == issuer.tax_id or recipient.name == issuer.name:
        recipient = _draw_recipient(fake, rng, kind, alphanumeric_recipient)
    # The text-shaping traits draw from their own generator, so the main stream (and with it every
    # other slot's bytes) is the same with or without them.
    text_rng = random.Random(f"{master_seed}:{plan.case_id}:text")
    text_fake = Faker("pt_BR")
    text_fake.seed_instance(text_rng.getrandbits(64))
    long_text = "long_text" in plan.traits
    confusable = "confusable" in plan.traits
    if confusable:
        recipient = _similar_recipient(text_fake, text_rng, issuer, recipient, kind)
    if long_text:
        issuer = replace(issuer, name=_long_name(text_fake, text_rng, _NAME_SUFFIX, company=True))
        recipient = replace(
            recipient,
            name=_long_name(
                text_fake,
                text_rng,
                _NAME_SUFFIX if kind == "cnpj" else _PERSON_SUFFIX,
                kind == "cnpj",
            ),
        )
        while recipient.name == issuer.name:
            recipient = replace(
                recipient,
                name=_long_name(
                    text_fake,
                    text_rng,
                    _NAME_SUFFIX if kind == "cnpj" else _PERSON_SUFFIX,
                    kind == "cnpj",
                ),
            )
    # The NF-e XSD pattern for CEP is eight digits: the dataset writes them without the hyphen.
    issuer = _plain_cep(issuer)
    recipient = _plain_cep(recipient)
    issuer_ie = "ISENTO" if rng.random() < _ISENTO_PROBABILITY else _state_registration(rng)
    recipient_ie = (
        _state_registration(rng)
        if kind == "cnpj" and rng.random() < _RECIPIENT_IE_PROBABILITY
        else None
    )

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
        low, high = plan.item_band
        item_count = rng.randint(max(low, _CONFUSABLE_MIN_ITEMS) if confusable else low, high)
    codes = _item_codes(rng, plan, item_count)
    items = tuple(
        _item(rng, position, cfop, codes[position - 1]) for position in range(1, item_count + 1)
    )
    if confusable:
        items = _with_confusable_rows(text_rng, items)
    operation_nature = _OPERATION_NATURE
    if long_text:
        operation_nature = text_rng.choice(_LONG_OPERATION_NATURES)
        items = tuple(
            replace(item, description=_long_description(text_rng, item.description))
            for item in items
        )
    if plan.regime == "normal" and rng.random() < _IPI_PROBABILITY:
        items = _with_ipi(rng, items)
    if rng.random() < _FREIGHT_DISCOUNT_PROBABILITY:
        items = _with_freight_and_discount(rng, items)
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
        operation_nature=operation_nature,
        issuer_ie=issuer_ie,
        recipient_ie=recipient_ie,
        recipient_tax_id_kind=kind,
        payment_code="01",
        freight_mode="0" if any(item.freight for item in items) else "9",
    )
    if rng.random() < _INSTALLMENT_PROBABILITY:
        installments = installment_plan(
            rng.randint(1, 3), issue_datetime.date(), spec.invoice_total, _INSTALLMENT_OFFSETS
        )
        spec = replace(spec, installments=installments, payment_code="15")
    return spec


def _long_name(fake: Faker, rng: random.Random, suffix: str, company: bool) -> str:
    """A party name filled word by word up to the 60 code-point cap, suffix included.

    Whole words only, never cut: a word is added when the name stays within the cap. Business and
    region words join a company name; further surnames join a person name.
    """
    limit = _NAME_MAX - len(suffix)
    words: list[str] = [str(fake.last_name()) if company else str(fake.first_name())]
    for _ in range(_NAME_PICKS):
        if len(" ".join(words)) >= limit - 2:
            break
        if company:
            pool = (*_BUSINESS_WORDS, *_REGION_WORDS, str(fake.last_name()))
            word = pool[rng.randrange(len(pool))]
        else:
            word = str(fake.last_name())
        if word not in words and len(" ".join([*words, word])) <= limit:
            words.append(word)
    return " ".join(words) + suffix


def _similar_recipient(
    fake: Faker, rng: random.Random, issuer: PartySpec, recipient: PartySpec, kind: str
) -> PartySpec:
    """A recipient in the issuer's UF whose name shares the issuer's first name word.

    The tax id and the full name still differ, so the two parties are bait for a swap, not equal.
    """
    shared = next(word for word in issuer.name.split() if not word[0].islower())
    if kind == "cnpj":
        taken = set(issuer.name.split())
        word = rng.choice([w for w in _BUSINESS_WORDS if w not in taken])
        name = f"{shared} {word}{_NAME_SUFFIX}"
    else:
        name = f"{fake.first_name()} {shared}{_PERSON_SUFFIX}"
    return replace(recipient, name=name, uf=issuer.uf)


def _swapped(rng: random.Random, value: Decimal) -> Decimal | None:
    """``value`` with two different adjacent digits exchanged, or None when it has none to swap.

    A swap that would put a zero in front of the integer part is skipped, so the printed value
    keeps its length.
    """
    text = f"{value:f}"
    positions = [
        index
        for index in range(len(text) - 1)
        if text[index].isdigit()
        and text[index + 1].isdigit()
        and text[index] != text[index + 1]
        and not (index == 0 and text[1] == "0")
    ]
    if not positions:
        return None
    index = rng.choice(positions)
    swapped = text[:index] + text[index + 1] + text[index] + text[index + 2 :]
    return Decimal(swapped)


def _big_decimal(rng: random.Random, units: tuple[int, int]) -> Decimal:
    places = rng.randint(2, 4)
    return Decimal(f"{rng.randint(*units)}.{rng.randrange(10**places):0{places}d}")


def _confusable_row(rng: random.Random, first: ItemSpec, second: ItemSpec) -> ItemSpec:
    """``second`` rewritten as ``first`` with two adjacent digits of one value exchanged."""
    fields = ["quantity", "unit_price"]
    rng.shuffle(fields)
    for field in fields:
        swapped = _swapped(rng, getattr(first, field))
        if swapped is None:
            continue
        if field == "quantity":
            return replace(first, code=second.code, quantity=swapped)
        return replace(first, code=second.code, unit_price=swapped)
    raise ValueError("neither value has two different adjacent digits to swap")


def _with_confusable_rows(rng: random.Random, items: tuple[ItemSpec, ...]) -> tuple[ItemSpec, ...]:
    """Two adjacent confusable pairs and one many-digit line (the invoice total reaches 10^9).

    The pairs sit at random non-overlapping positions; the second row of a pair repeats the first
    except for its own product code and one value with two adjacent digits swapped. The placement
    must leave the invoice's set of tax codes unchanged, so a mixed or ST invoice keeps its
    traits. The many-digit line is an item outside the pairs, or the first row of the first pair
    when the invoice has only four items.
    """
    count = len(items)
    placements = [(a, b) for a in range(count - 3) for b in range(a + 2, count - 1)]
    rng.shuffle(placements)
    codes = {item.tax_code for item in items}
    for first_pair, second_pair in placements:
        rows = list(items)
        outside = [
            i
            for i in range(count)
            if i not in (first_pair, first_pair + 1, second_pair, second_pair + 1)
        ]
        big = rng.choice(outside) if outside else first_pair
        rows[big] = replace(
            rows[big],
            quantity=_big_decimal(rng, _BIG_QUANTITY),
            unit_price=_big_decimal(rng, _BIG_UNIT_PRICE),
        )
        for start in (first_pair, second_pair):
            rows[start + 1] = _confusable_row(rng, rows[start], rows[start + 1])
        if {row.tax_code for row in rows} == codes:
            return tuple(rows)
    raise ValueError("no placement of two confusable pairs keeps the invoice's tax codes")


def _long_description(rng: random.Random, base: str) -> str:
    """The catalogue description followed by qualifier words until it is 100 to 120 code points.

    The text stops at the first length of 100 or more; the last word added has at most 20 code
    points, so the result never passes 120 and no word is cut.
    """
    words = base.split()
    text = base
    while len(text) < _DESCRIPTION_MIN:
        fresh = [word for word in _QUALIFIERS if word not in words]
        word = (fresh or _QUALIFIERS)[rng.randrange(len(fresh or _QUALIFIERS))]
        words.append(word)
        text = f"{text} {word}"
    assert len(text) <= _DESCRIPTION_MAX
    return text


def _draw_recipient(fake: Faker, rng: random.Random, kind: str, alphanumeric: bool) -> PartySpec:
    if kind == "cnpj" and alphanumeric:
        return _party(
            fake, rng, tax_id=make_cnpj(rng, alphanumeric=True), name=_company_name(fake, rng)
        )
    return _recipient(fake, rng, kind)


def _cents_of(value: Decimal) -> int:
    return int(value * 100)


def _decimal_cents(cents: int) -> Decimal:
    return Decimal(cents).scaleb(-2)


def _shares(rng: random.Random, total: int, caps: list[int]) -> list[int]:
    """Split ``total`` cents over slots so each stays within its cap and the sum is exact."""
    bounds = [0, *sorted(rng.randint(0, total) for _ in range(len(caps) - 1)), total]
    shares = [bounds[i + 1] - bounds[i] for i in range(len(caps))]
    carry = 0
    for index, cap in enumerate(caps):
        if shares[index] > cap:
            carry += shares[index] - cap
            shares[index] = cap
    for index, cap in enumerate(caps):
        take = min(cap - shares[index], carry)
        shares[index] += take
        carry -= take
    if carry:
        raise ValueError("the slots cannot absorb the amount")
    return shares


def _with_ipi(rng: random.Random, items: tuple[ItemSpec, ...]) -> tuple[ItemSpec, ...]:
    chosen = set(rng.sample(range(len(items)), rng.randint(1, len(items))))
    return tuple(
        replace(item, ipi_rate=rng.choice(_IPI_RATES)) if index in chosen else item
        for index, item in enumerate(items)
    )


def _with_freight_and_discount(
    rng: random.Random, items: tuple[ItemSpec, ...]
) -> tuple[ItemSpec, ...]:
    """Draw header freight and discount and spread them in cents over the items.

    SEFAZ rejects an invoice whose ICMSTot vFrete or vDesc differs from the item sums (535, 537),
    so the header is always the sum of the per-item values. vProd stays gross. The discount is
    spread over a non-empty subset whose gross totals can absorb it.
    """
    gross = [_cents_of(item.total) for item in items]
    freight = rng.randint(*_FREIGHT_CENTS)
    discount = 0
    upper = min(_DISCOUNT_MAX_CENTS, sum(gross) // 10)
    if upper >= _DISCOUNT_MIN_CENTS:
        discount = rng.randint(_DISCOUNT_MIN_CENTS, upper)

    order = rng.sample(range(len(items)), len(items))
    freight_slots = order[: rng.randint(1, len(items))]
    freight_shares = _shares(rng, freight, [freight] * len(freight_slots))
    discount_shares: dict[int, int] = {}
    if discount:
        order = rng.sample(range(len(items)), len(items))
        size = rng.randint(1, len(items))
        while sum(gross[i] for i in order[:size]) < discount:
            size += 1
        discount_slots = order[:size]
        discount_shares = dict(
            zip(
                discount_slots,
                _shares(rng, discount, [gross[i] for i in discount_slots]),
                strict=True,
            )
        )
    freight_by_slot = dict(zip(freight_slots, freight_shares, strict=True))
    spread = tuple(
        replace(
            item,
            freight=_decimal_cents(freight_by_slot.get(index, 0)),
            discount=_decimal_cents(discount_shares.get(index, 0)),
        )
        for index, item in enumerate(items)
    )
    return spread


def _plain_cep(party: PartySpec) -> PartySpec:
    return replace(party, cep=party.cep.replace("-", ""))


def _item(rng: random.Random, position: int, cfop: str, tax_code: str) -> ItemSpec:
    description, ncm, unit = rng.choice(DATASET_GOODS)
    quantity = _decimal(rng, _MAX_QUANTITY, rng.randint(2, 4))
    unit_price: Decimal = _decimal(rng, _MAX_UNIT_PRICE, rng.randint(2, 4))
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
