"""The quota table of ``data/dataset`` against the specs it drives (CONTEXT D-03, D-04, D-14).

Spec-level tests build every base spec from ``quota.plan_cases()`` without rendering. The slot
tables below are held as literals on purpose: the quota table fixes case ids, bytes and slice sizes
(CONTEXT D-03, costly to reverse), so a change of any slot is a reviewed change of this file. The
page tests read the committed manifest and the committed PDFs.
"""

from __future__ import annotations

import json
from collections import Counter
from dataclasses import replace
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
    got = tuple(
        (c.name, c.regime, c.crt, c.length, c.count, c.item_band, c.pages) for c in QUOTA
    )
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
        assert dict(cell.curve) == ({0: "dev"} if cell.name == "normal-short" else {}), cell.name


def test_st_deferral_and_mixed_slot_totals() -> None:
    assert sum(len(v) for v in ST_DEFERRAL.values()) == 14
    assert sum(len(v) for v in MIXED_TAX_CODES.values()) == 30


# --- the declared band and the item counts ---------------------------------------------------


def test_declared_band_is_the_cell_band_with_mixed_slots_raised_to_two() -> None:
    cells = {cell.name: cell for cell in QUOTA}
    for base in _bases():
        low, high = cells[base.cell].item_band
        if "mixed_tax_codes" in base.traits:
            low = max(2, low)
        assert base.item_band == (low, high), base.case_id


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
