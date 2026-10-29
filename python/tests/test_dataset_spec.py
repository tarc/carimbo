"""The quota table of ``data/dataset`` against the specs it drives (CONTEXT D-03, D-04, D-14).

Spec-level tests build every base spec from ``quota.plan_cases()`` without rendering. The slot
tables below are held as literals on purpose: the quota table fixes case ids, bytes and slice sizes
(CONTEXT D-03, costly to reverse), so a change of any slot is a reviewed change of this file. The
page tests read the committed manifest and the committed PDFs.
"""

from __future__ import annotations

import json
import xml.etree.ElementTree as ET
from collections import Counter
from dataclasses import replace
from decimal import Decimal
from functools import cache
from pathlib import Path
from typing import Any

import pypdfium2 as pdfium
import pytest

from carimbo_datagen import quota
from carimbo_datagen.dataset_spec import build_dataset_spec
from carimbo_datagen.quota import DATASET_SEED, QUOTA, BasePlan, assign_splits, plan_cases
from carimbo_datagen.spec import CaseSpec

DATASET = Path(__file__).resolve().parents[2] / "data" / "dataset"

# name, regime, crt, length, count, item_band, pages; in quota order.
STRATA: tuple[tuple[str, str, int, str, int, tuple[int, int], str], ...] = (
    ("normal-short", "normal", 3, "short", 14, (1, 8), "single"),
    ("normal-medium", "normal", 3, "medium", 14, (9, 25), "single"),
    ("normal-many", "normal", 3, "many_items", 7, (26, 40), "single"),
    ("normal-multi", "normal", 3, "multi_page", 10, (50, 80), "multi"),
    ("simples-short", "simples", 1, "short", 14, (1, 8), "single"),
    ("simples-medium", "simples", 1, "medium", 14, (9, 25), "single"),
    ("simples-many", "simples", 1, "many_items", 7, (26, 40), "single"),
    ("simples-multi", "simples", 1, "multi_page", 10, (50, 80), "multi"),
)
PINNED: dict[str, dict[int, int]] = {
    "normal-short": {1: 1},
    "normal-medium": {2: 9, 3: 25},
    "normal-many": {1: 26, 2: 40},
    "normal-multi": {1: 50, 2: 80},
    "simples-short": {1: 1},
    "simples-medium": {2: 9, 3: 25},
    "simples-many": {1: 26, 2: 40},
    "simples-multi": {1: 50, 2: 80},
}
ST_DEFERRAL: dict[str, tuple[int, ...]] = {
    "normal-short": (0, 3, 5, 9),
    "normal-medium": (0, 6, 8, 10),
    "normal-many": (0, 3),
    "normal-multi": (0, 2, 4, 7),
}
MIXED_TAX_CODES: dict[str, tuple[int, ...]] = {
    "normal-short": (3, 4, 6, 9, 10),
    "normal-medium": (1, 3, 6, 9, 11),
    "normal-many": (1, 4),
    "normal-multi": (1, 4, 8),
    "simples-short": (0, 4, 7, 10, 11),
    "simples-medium": (0, 5, 6, 8, 11),
    "simples-many": (2, 4),
    "simples-multi": (2, 4, 7),
}
LONG_TEXT: dict[str, tuple[int, ...]] = {
    "normal-short": (5, 6, 7),
    "simples-short": (6, 7, 8),
    "normal-multi": (3, 4, 5, 6, 7, 8),
    "simples-multi": (3, 4, 5, 6, 7, 8),
}
LONG_TEXT_ITEM_BAND: dict[str, tuple[int, int]] = {
    "normal-multi": (26, 30),
    "simples-multi": (26, 30),
}
CST = frozenset({"00", "10", "20", "30", "40", "41", "50", "51", "60", "70", "90"})
CSOSN = frozenset({"101", "102", "103", "300", "400", "500", "900"})
ST_CODES = frozenset({"10", "30", "51", "70"})
DEV_PER_CELL = {name: (count + 1) // 3 for name, _, _, _, count, _, _ in STRATA}


def _manifest() -> dict[str, Any]:
    return json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))


def _cases() -> dict[str, dict[str, Any]]:
    return {entry["case_id"]: entry for entry in _manifest()["cases"]}


@cache
def _specs() -> dict[str, CaseSpec]:
    return {base.case_id: build_dataset_spec(DATASET_SEED, base) for base in plan_cases().bases}


def _bases() -> tuple[BasePlan, ...]:
    return plan_cases().bases


def _by_id() -> dict[str, BasePlan]:
    return {base.case_id: base for base in _bases()}


# --- the eight strata ------------------------------------------------------------------------


def test_quota_has_the_eight_strata_in_order() -> None:
    got = tuple((c.name, c.regime, c.crt, c.length, c.count, c.item_band, c.pages) for c in QUOTA)
    assert got == STRATA


def test_plan_numbers_ninety_bases_and_one_twin() -> None:
    plan = plan_cases()
    assert [base.case_id for base in plan.bases] == [f"nfe-{n:04d}" for n in range(1, 91)]
    assert [(t.case_id, t.base_case, t.rendering, t.severity) for t in plan.twins] == [
        ("nfe-0091", "nfe-0001", "rotation", "mild")
    ]
    assert dict(Counter(base.cell for base in plan.bases)) == {
        name: count for name, _, _, _, count, _, _ in STRATA
    }


def test_every_stratum_has_at_least_two_bases() -> None:
    assert all(cell.count >= 2 for cell in QUOTA)


def test_the_literal_slot_tables_equal_the_cells() -> None:
    for cell in QUOTA:
        assert dict(cell.pinned_items) == PINNED[cell.name], cell.name
        assert cell.traits.get("st_deferral", ()) == ST_DEFERRAL.get(cell.name, ()), cell.name
        assert cell.traits.get("mixed_tax_codes", ()) == MIXED_TAX_CODES[cell.name], cell.name
        assert cell.traits.get("long_text", ()) == LONG_TEXT.get(cell.name, ()), cell.name
        assert cell.long_text_item_band == LONG_TEXT_ITEM_BAND.get(cell.name), cell.name
        assert dict(cell.curve) == ({0: "dev"} if cell.name == "normal-short" else {}), cell.name


def test_st_deferral_and_mixed_slot_totals() -> None:
    assert sum(len(v) for v in ST_DEFERRAL.values()) == 14
    assert sum(len(v) for v in MIXED_TAX_CODES.values()) == 30
    assert sum(len(v) for v in LONG_TEXT.values()) == 18


# --- the declared band and the item counts ---------------------------------------------------


def test_declared_band_follows_the_long_text_band_then_the_mixed_rule() -> None:
    cells = {cell.name: cell for cell in QUOTA}
    for base in _bases():
        cell = cells[base.cell]
        low, high = cell.item_band
        if "long_text" in base.traits and cell.long_text_item_band is not None:
            low, high = cell.long_text_item_band
        if "mixed_tax_codes" in base.traits:
            low = max(2, low)
        assert base.item_band == (low, high), base.case_id


def test_a_long_text_slot_of_a_short_cell_keeps_the_cell_band_and_a_mixed_one_is_raised() -> None:
    by_slot = {(b.cell, b.slot): b for b in _bases()}
    assert by_slot[("normal-short", 5)].item_band == (1, 8)
    assert by_slot[("normal-short", 6)].item_band == (2, 8)
    assert by_slot[("normal-multi", 3)].item_band == (26, 30)
    assert by_slot[("normal-multi", 4)].item_band == (26, 30)
    assert by_slot[("normal-multi", 0)].item_band == (50, 80)


def test_every_base_item_count_is_inside_its_declared_band() -> None:
    for case_id, spec in _specs().items():
        low, high = _by_id()[case_id].item_band
        assert low <= len(spec.items) <= high, case_id


def test_no_case_has_zero_items() -> None:
    assert all(len(spec.items) >= 1 for spec in _specs().values())


def test_pinned_slots_have_exactly_their_item_counts() -> None:
    for base in _bases():
        expected = PINNED[base.cell].get(base.slot)
        assert base.pinned_items == expected, base.case_id
        if expected is not None:
            assert len(_specs()[base.case_id].items) == expected, base.case_id


def test_each_short_stratum_has_a_one_item_base() -> None:
    for cell in ("normal-short", "simples-short"):
        counts = [len(_specs()[b.case_id].items) for b in _bases() if b.cell == cell]
        assert 1 in counts, cell


def test_mixed_slots_have_two_items_and_two_distinct_codes() -> None:
    mixed = [b for b in _bases() if "mixed_tax_codes" in b.traits]
    assert len(mixed) == 30
    for base in mixed:
        assert base.item_band[0] >= 2, base.case_id
        spec = _specs()[base.case_id]
        assert len(spec.items) >= 2, base.case_id
        assert len({item.tax_code for item in spec.items}) >= 2, base.case_id


def test_a_mixed_slot_on_a_one_item_pin_is_rejected(monkeypatch: pytest.MonkeyPatch) -> None:
    cell = replace(QUOTA[0], traits={"mixed_tax_codes": (1,)}, curve={})
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="mixed_tax_codes"):
        plan_cases()


def test_a_mixed_slot_whose_band_cannot_hold_two_items_is_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cell = replace(
        QUOTA[0], item_band=(1, 1), traits={"mixed_tax_codes": (0,)}, pinned_items={}, curve={}
    )
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="mixed_tax_codes"):
        plan_cases()


def test_st_deferral_outside_a_normal_cell_is_rejected(monkeypatch: pytest.MonkeyPatch) -> None:
    cell = replace(QUOTA[4], traits={"st_deferral": (0,)})
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="st_deferral"):
        plan_cases()


def test_a_cell_with_fewer_than_two_bases_is_rejected(monkeypatch: pytest.MonkeyPatch) -> None:
    cell = replace(QUOTA[0], count=1, traits={}, pinned_items={}, curve={})
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="at least 2"):
        plan_cases()


def test_long_text_in_a_medium_or_many_cell_is_rejected(monkeypatch: pytest.MonkeyPatch) -> None:
    for index in (1, 2):
        cell = replace(QUOTA[index], traits={"long_text": (0,)}, pinned_items={}, curve={})
        monkeypatch.setattr(quota, "QUOTA", (cell,))
        with pytest.raises(ValueError, match="long_text"):
            plan_cases()


def test_long_text_on_a_pinned_slot_of_a_multi_cell_is_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cell = replace(QUOTA[3], traits={"long_text": (1,)})
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="long_text"):
        plan_cases()


# --- one regime family per invoice -----------------------------------------------------------


def test_normal_bases_use_only_cst_and_simples_bases_only_csosn() -> None:
    for base in _bases():
        codes = {item.tax_code for item in _specs()[base.case_id].items}
        assert codes <= (CST if base.regime == "normal" else CSOSN), base.case_id
        assert not (codes & CST and codes & CSOSN), base.case_id


def test_st_deferral_slots_carry_an_st_code_and_nothing_else_does() -> None:
    carrying = {
        case_id
        for case_id, spec in _specs().items()
        if {item.tax_code for item in spec.items} & ST_CODES
    }
    declared = {b.case_id for b in _bases() if "st_deferral" in b.traits}
    assert len(declared) == 14
    assert all(b.regime == "normal" for b in _bases() if b.case_id in declared)
    assert carrying == declared


def test_unmixed_slots_use_one_code_apart_from_the_st_codes() -> None:
    for base in _bases():
        if "mixed_tax_codes" in base.traits:
            continue
        codes = {item.tax_code for item in _specs()[base.case_id].items} - ST_CODES
        assert len(codes) == 1, base.case_id


def test_at_least_thirty_bases_carry_two_or_more_distinct_codes() -> None:
    mixing = [s for s in _specs().values() if len({i.tax_code for i in s.items}) >= 2]
    assert len(mixing) >= 30


# --- the stratified split --------------------------------------------------------------------


def test_each_stratum_has_a_third_of_its_bases_in_dev_and_both_splits() -> None:
    plan = plan_cases()
    splits = assign_splits(plan, DATASET_SEED)
    for cell in QUOTA:
        members = [b.case_id for b in plan.bases if b.cell == cell.name]
        dev = [c for c in members if splits[c] == "dev"]
        assert len(dev) == DEV_PER_CELL[cell.name], cell.name
        assert len(dev) < len(members), cell.name
    assert sum(1 for b in plan.bases if splits[b.case_id] == "dev") == 30
    assert splits["nfe-0001"] == "dev"
    assert splits["nfe-0091"] == splits["nfe-0001"]


# --- the committed dataset: pages ------------------------------------------------------------


def _page_count(path: Path) -> int:
    document = pdfium.PdfDocument(path.read_bytes())
    try:
        return len(document)
    finally:
        document.close()


def test_the_manifest_lists_ninety_one_cases() -> None:
    assert sorted(_cases()) == [f"nfe-{n:04d}" for n in range(1, 92)]


@pytest.mark.parametrize("case_id", sorted(_cases()))
def test_manifest_pages_equal_the_pdfium_page_count(case_id: str) -> None:
    entry = _cases()[case_id]
    assert _page_count(DATASET / entry["pdf"]) == entry["pages"]


@pytest.mark.parametrize("case_id", sorted(_cases()))
def test_manifest_pages_lie_in_the_stratum_page_band(case_id: str) -> None:
    entry = _cases()[case_id]
    base = _by_id().get(entry["base_case"])
    assert base is not None, case_id
    if base.pages == "single":
        assert entry["pages"] == 1, case_id
    else:
        assert entry["pages"] >= 2, case_id


# --- party traits and MEI --------------------------------------------------------------------

ALPHANUMERIC_CNPJ: dict[str, tuple[int, ...]] = {
    "normal-short": (2, 4, 8, 11),
    "normal-medium": (1, 6, 9, 12),
    "normal-many": (0, 4),
    "normal-multi": (1, 5),
    "simples-short": (2, 6, 10, 12),
    "simples-medium": (0, 3, 8, 12),
    "simples-many": (0, 3),
    "simples-multi": (1, 5),
}
ALPHANUMERIC_RECIPIENT: dict[str, tuple[int, ...]] = {
    "normal-short": (6,),
    "normal-medium": (7,),
    "simples-short": (8,),
    "simples-medium": (7,),
}
CPF_RECIPIENT: dict[str, tuple[int, ...]] = {
    "normal-short": (1, 4, 7),
    "normal-medium": (3, 4, 8),
    "normal-many": (2,),
    "normal-multi": (3, 7),
    "simples-short": (1, 5, 9),
    "simples-medium": (2, 6, 10),
    "simples-many": (2,),
    "simples-multi": (3, 7),
}
MEI: dict[str, tuple[int, ...]] = {"simples-short": (4, 5), "simples-medium": (1, 6)}
MEI_CODES = frozenset({"102", "103", "400"})
TRAIT_TABLES: dict[str, dict[str, tuple[int, ...]]] = {
    "alphanumeric_cnpj": ALPHANUMERIC_CNPJ,
    "alphanumeric_recipient": ALPHANUMERIC_RECIPIENT,
    "cpf_recipient": CPF_RECIPIENT,
    "mei": MEI,
}


def _trait_bases(trait: str) -> set[str]:
    return {b.case_id for b in _bases() if trait in b.traits}


def test_the_literal_party_tables_equal_the_cells() -> None:
    for cell in QUOTA:
        for trait, table in TRAIT_TABLES.items():
            assert cell.traits.get(trait, ()) == table.get(cell.name, ()), (cell.name, trait)


def test_party_trait_counts_hold_exactly() -> None:
    assert {trait: len(_trait_bases(trait)) for trait in TRAIT_TABLES} == {
        "alphanumeric_cnpj": 24,
        "alphanumeric_recipient": 4,
        "cpf_recipient": 18,
        "mei": 4,
    }
    assert 24 / 90 >= 0.26
    assert not _trait_bases("cpf_recipient") & _trait_bases("alphanumeric_recipient")


def test_mei_outside_simples_short_and_medium_is_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cell = replace(QUOTA[6], traits={"mei": (0,)})
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="mei"):
        plan_cases()


def test_cpf_and_alphanumeric_recipient_on_one_slot_is_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cell = replace(QUOTA[0], traits={"cpf_recipient": (2,), "alphanumeric_recipient": (2,)})
    monkeypatch.setattr(quota, "QUOTA", (cell,))
    with pytest.raises(ValueError, match="alphanumeric_recipient"):
        plan_cases()


def test_party_identifiers_follow_the_traits() -> None:
    for base in _bases():
        spec = _specs()[base.case_id]
        assert (not spec.issuer.tax_id.isdigit()) == ("alphanumeric_cnpj" in base.traits)
        if "cpf_recipient" in base.traits:
            assert spec.recipient_tax_id_kind == "cpf"
            assert len(spec.recipient.tax_id) == 11 and spec.recipient.tax_id.isdigit()
            assert spec.recipient_ie is None
        else:
            assert spec.recipient_tax_id_kind == "cnpj"
            assert (not spec.recipient.tax_id.isdigit()) == (
                "alphanumeric_recipient" in base.traits
            )


def test_mei_bases_are_crt_4_with_mei_codes_and_every_other_base_keeps_its_cell_crt() -> None:
    crt = {cell.name: cell.crt for cell in QUOTA}
    for base in _bases():
        spec = _specs()[base.case_id]
        if "mei" in base.traits:
            assert base.crt == 4 and spec.crt == 4 and spec.regime == "simples"
            assert {item.tax_code for item in spec.items} <= MEI_CODES, base.case_id
        else:
            assert base.crt == spec.crt == crt[base.cell], base.case_id


# --- commercial details ----------------------------------------------------------------------


def test_ipi_appears_only_on_normal_bases_and_on_some_of_them() -> None:
    with_ipi = [b for b in _bases() if any(i.ipi_rate for i in _specs()[b.case_id].items)]
    assert with_ipi, "no base draws IPI: the check would pass vacuously"
    assert all(b.regime == "normal" for b in with_ipi)
    for base in with_ipi:
        rates = {i.ipi_rate for i in _specs()[base.case_id].items} - {Decimal(0)}
        assert rates <= {Decimal("5.00"), Decimal("10.00")}


def test_freight_and_discount_are_spread_in_cents_over_items_and_sum_to_the_header() -> None:
    cent = Decimal("0.01")
    spread = 0
    for case_id, spec in _specs().items():
        discount, freight = spec.discount, spec.freight
        assert discount == sum((i.discount for i in spec.items), Decimal(0)), case_id
        assert freight == sum((i.freight for i in spec.items), Decimal(0)), case_id
        assert all(i.discount <= i.total for i in spec.items), case_id
        assert all(i.discount >= 0 and i.freight >= 0 for i in spec.items), case_id
        assert discount == discount.quantize(cent) and freight == freight.quantize(cent)
        if freight:
            assert Decimal("10.00") <= freight <= Decimal("500.00"), case_id
            assert spec.freight_mode == "0", case_id
            spread += 1
        else:
            assert spec.freight_mode == "9", case_id
        if discount:
            assert Decimal("5.00") <= discount <= Decimal("300.00"), case_id
            assert discount <= (spec.products_total / 10).quantize(cent, rounding="ROUND_FLOOR")
            assert spec.invoice_total == (
                spec.products_total - discount + spec.st_total + freight + spec.ipi_total
            )
    assert spread, "no base draws freight: the check would pass vacuously"
    assert any(s.discount for s in _specs().values())
    assert any(len([i for i in s.items if i.discount]) >= 2 for s in _specs().values())


def test_installments_are_one_to_three_due_30_60_90_days_after_issue_and_sum_to_vnf() -> None:
    with_installments = [s for s in _specs().values() if s.installments]
    assert with_installments, "no base draws installments: the check would pass vacuously"
    assert {len(s.installments) for s in with_installments} <= {1, 2, 3}
    for spec in _specs().values():
        if not spec.installments:
            assert spec.payment_code == "01", spec.case_id
            continue
        assert spec.payment_code == "15", spec.case_id
        issue_day = spec.issue_datetime.date()
        offsets = [(i.due_date - issue_day).days for i in spec.installments]
        assert offsets == [30, 60, 90][: len(offsets)], spec.case_id
        assert sum((i.amount for i in spec.installments), Decimal(0)) == spec.invoice_total
        assert [i.number for i in spec.installments] == [
            f"{n:03d}" for n in range(1, len(offsets) + 1)
        ]


# --- the committed dataset: traits, XML header sums ------------------------------------------

_NS = {"n": "http://www.portalfiscal.inf.br/nfe"}
_BASE_ENTRIES = {c: e for c, e in _cases().items() if e["rendering"] == "text"}


def test_manifest_trait_tags_have_the_exact_base_counts() -> None:
    tags = Counter(tag for entry in _BASE_ENTRIES.values() for tag in entry["tags"])
    assert len(_BASE_ENTRIES) == 90
    assert tags["alphanumeric_cnpj"] == 24
    assert tags["alphanumeric_recipient"] == 4
    assert tags["cpf_recipient"] == 18
    assert tags["mei"] == 4
    assert tags["st_deferral"] == 14
    assert tags["long_text"] == 18
    assert tags["mixed_tax_codes"] >= 30
    assert not [t for t in tags if "per_item" in t or "item_spread" in t]


def test_manifest_ipi_tags_sit_on_normal_bases_only() -> None:
    for entry in _BASE_ENTRIES.values():
        if "ipi" in entry["tags"]:
            assert "regime_normal" in entry["tags"], entry["case_id"]


def _item_sum(root: ET.Element, tag: str) -> Decimal:
    return sum(
        (Decimal(e.text or "0") for e in root.findall(f".//n:det/n:prod/n:{tag}", _NS)),
        Decimal(0),
    )


@pytest.mark.parametrize("case_id", sorted(_cases()))
def test_icmstot_vdesc_and_vfrete_equal_the_item_sums(case_id: str) -> None:
    root = ET.parse(DATASET / _cases()[case_id]["xml"]).getroot()
    for tag in ("vDesc", "vFrete"):
        header = Decimal(root.findtext(f".//n:ICMSTot/n:{tag}", "0", _NS))
        assert header == _item_sum(root, tag), (case_id, tag)
    for prod in root.findall(".//n:det/n:prod", _NS):
        gross = Decimal(prod.findtext("n:vProd", "0", _NS))
        assert Decimal(prod.findtext("n:vDesc", "0", _NS)) <= gross, case_id


# --- long text: wrapped, never truncated (CONTEXT D-04, D-05) ----------------------------------

_BUSINESS_WORD_SET = frozenset({"COMÉRCIO", "INDÚSTRIA", "SERVIÇOS", "DISTRIBUIÇÃO", "CONSTRUÇÕES"})


def _texts(spec: CaseSpec) -> list[tuple[str, str]]:
    return [
        ("issuer name", spec.issuer.name),
        ("recipient name", spec.recipient.name),
        ("natOp", spec.operation_nature),
        *[("description", item.description) for item in spec.items],
    ]


def test_the_literal_long_text_table_names_eighteen_bases() -> None:
    assert len(_trait_bases("long_text")) == 18
    assert {b.cell for b in _bases() if "long_text" in b.traits} == set(LONG_TEXT)


def test_every_base_keeps_names_nature_descriptions_words_and_codes_within_the_caps() -> None:
    for case_id, spec in _specs().items():
        assert len(spec.issuer.name) <= 60 and len(spec.recipient.name) <= 60, case_id
        assert len(spec.operation_nature) <= 60, case_id
        for label, text in _texts(spec):
            assert all(len(word) <= 20 for word in text.split()), (case_id, label, text)
        assert all(len(item.description) <= 120 for item in spec.items), case_id
        assert all(len(item.code) <= 12 for item in spec.items), case_id
        for party in (spec.issuer, spec.recipient):
            assert " - " not in party.name, (case_id, party.name)


def test_long_text_bases_have_long_names_natures_and_descriptions() -> None:
    for base in _bases():
        if "long_text" not in base.traits:
            continue
        spec = _specs()[base.case_id]
        assert 45 <= len(spec.issuer.name) <= 60, base.case_id
        assert 45 <= len(spec.recipient.name) <= 60, base.case_id
        assert 40 <= len(spec.operation_nature) <= 60, base.case_id
        for item in spec.items:
            assert 100 <= len(item.description) <= 120, (base.case_id, item.description)
        assert spec.issuer.name != spec.recipient.name, base.case_id


def test_other_bases_keep_one_line_descriptions() -> None:
    for base in _bases():
        if "long_text" in base.traits:
            continue
        assert all(len(i.description) <= 30 for i in _specs()[base.case_id].items), base.case_id


def test_long_text_multi_page_bases_have_26_to_30_items_and_two_or_more_pages() -> None:
    long_multi = [b for b in _bases() if "long_text" in b.traits and b.pages == "multi"]
    assert len(long_multi) == 12
    for base in long_multi:
        assert 26 <= len(_specs()[base.case_id].items) <= 30, base.case_id
        assert _cases()[base.case_id]["pages"] >= 2, base.case_id


# --- length tags follow the strata -------------------------------------------------------------

_LENGTH_TAGS = frozenset({"short", "medium", "many_items"})
_STRATUM_TAGS: dict[str, frozenset[str]] = {
    "short": frozenset({"short", "single_page"}),
    "medium": frozenset({"medium", "single_page"}),
    "many_items": frozenset({"many_items", "single_page"}),
    "multi_page": frozenset({"many_items", "multi_page"}),
}


def test_length_tags_follow_the_stratum_of_every_base() -> None:
    cells = {cell.name: cell for cell in QUOTA}
    for case_id, entry in _BASE_ENTRIES.items():
        length = cells[_by_id()[case_id].cell].length
        tags = set(entry["tags"])
        assert tags & (_LENGTH_TAGS | {"single_page", "multi_page"}) == _STRATUM_TAGS[length], (
            case_id
        )


def test_length_tag_counts_over_the_text_cases() -> None:
    tags = Counter(tag for entry in _BASE_ENTRIES.values() for tag in entry["tags"])
    assert {
        tag: tags[tag]
        for tag in ("short", "medium", "many_items", "single_page", "multi_page", "long_text")
    } == {
        "short": 28,
        "medium": 28,
        "many_items": 34,
        "single_page": 70,
        "multi_page": 20,
        "long_text": 18,
    }
