"""The dataset quota table and the case planner (CONTEXT D-03, D-14, D-15, D-16).

``QUOTA`` is the single source of what the dataset contains: each cell fixes a regime, a length
class, a base-case count, and the slots that carry a trait, a pinned item count or an explicit
split. ``plan_cases`` turns the table into numbered base cases (``nfe-0001`` upward, in cell then
slot order) and, continuing the numbering, one image-only twin per curve base and rendering. Case
ids name neither the rendering nor the base (RESEARCH Pitfall 10); that lives in the manifest.

Splits and the CI subset are drawn from the master seed with SHA-256 orderings, so they never depend
on ``hash()``, set order or any module-level RNG.
"""

from __future__ import annotations

import hashlib
import json
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from typing import Any

DATASET_SEED = 20261022

_REGIMES = ("normal", "simples")
_LENGTHS = ("short", "medium", "many_items", "multi_page")
_PAGES = ("single", "multi")
_SPLITS = ("dev", "test")


@dataclass(frozen=True)
class Cell:
    """One row of the quota table."""

    name: str
    regime: str
    crt: int
    length: str
    count: int
    item_band: tuple[int, int]
    pages: str
    traits: Mapping[str, tuple[int, ...]]
    pinned_items: Mapping[int, int]
    curve: Mapping[int, str]


def _cell(
    name: str,
    regime: str,
    length: str,
    count: int,
    item_band: tuple[int, int],
    pages: str,
    *,
    traits: Mapping[str, tuple[int, ...]],
    pinned_items: Mapping[int, int],
    curve: Mapping[int, str] | None = None,
) -> Cell:
    return Cell(
        name=name,
        regime=regime,
        crt=3 if regime == "normal" else 1,
        length=length,
        count=count,
        item_band=item_band,
        pages=pages,
        traits=traits,
        pinned_items=pinned_items,
        curve=curve or {},
    )


# The slot tuples below are fixed by plan 04-05 and asserted literally by test_dataset_spec.py:
# the table fixes case ids, bytes and slice sizes (CONTEXT D-03), so a change of any slot is a
# reviewed change. The item bands leave 41 to 49 items out on purpose (RESEARCH Pitfall 2: 45
# one-line items still fit one page, 50 spill to a second).
QUOTA: tuple[Cell, ...] = (
    _cell(
        "normal-short",
        "normal",
        "short",
        14,
        (1, 8),
        "single",
        traits={"st_deferral": (0, 3, 5, 9), "mixed_tax_codes": (3, 4, 6, 9, 10)},
        pinned_items={1: 1},
        curve={0: "dev"},
    ),
    _cell(
        "normal-medium",
        "normal",
        "medium",
        14,
        (9, 25),
        "single",
        traits={"st_deferral": (0, 6, 8, 10), "mixed_tax_codes": (1, 3, 6, 9, 11)},
        pinned_items={2: 9, 3: 25},
    ),
    _cell(
        "normal-many",
        "normal",
        "many_items",
        7,
        (26, 40),
        "single",
        traits={"st_deferral": (0, 3), "mixed_tax_codes": (1, 4)},
        pinned_items={1: 26, 2: 40},
    ),
    _cell(
        "normal-multi",
        "normal",
        "multi_page",
        10,
        (50, 80),
        "multi",
        traits={"st_deferral": (0, 2, 4, 7), "mixed_tax_codes": (1, 4, 8)},
        pinned_items={1: 50, 2: 80},
    ),
    _cell(
        "simples-short",
        "simples",
        "short",
        14,
        (1, 8),
        "single",
        traits={"mixed_tax_codes": (0, 4, 7, 10, 11)},
        pinned_items={1: 1},
    ),
    _cell(
        "simples-medium",
        "simples",
        "medium",
        14,
        (9, 25),
        "single",
        traits={"mixed_tax_codes": (0, 5, 6, 8, 11)},
        pinned_items={2: 9, 3: 25},
    ),
    _cell(
        "simples-many",
        "simples",
        "many_items",
        7,
        (26, 40),
        "single",
        traits={"mixed_tax_codes": (2, 4)},
        pinned_items={1: 26, 2: 40},
    ),
    _cell(
        "simples-multi",
        "simples",
        "multi_page",
        10,
        (50, 80),
        "multi",
        traits={"mixed_tax_codes": (2, 4, 7)},
        pinned_items={1: 50, 2: 80},
    ),
)
TWIN_RENDERINGS: tuple[tuple[str, str], ...] = (("rotation", "mild"),)
CI_CELLS: tuple[str, ...] = ("st_warning", "rotation_mild")


@dataclass(frozen=True)
class BasePlan:
    """A text-layer base case: where it sits in the quota table and what it must carry."""

    case_id: str
    cell: str
    slot: int
    regime: str
    crt: int
    length: str
    item_band: tuple[int, int]
    pages: str
    traits: frozenset[str]
    pinned_items: int | None
    curve_split: str | None


@dataclass(frozen=True)
class TwinPlan:
    """An image-only twin: a byte copy of its base's XML and a degraded PDF."""

    case_id: str
    base_case: str
    rendering: str
    severity: str | None


@dataclass(frozen=True)
class CasePlan:
    bases: tuple[BasePlan, ...]
    twins: tuple[TwinPlan, ...]


def _validate(cell: Cell) -> None:
    where = f"quota cell {cell.name!r}"
    if cell.regime not in _REGIMES:
        raise ValueError(f"{where}: unknown regime {cell.regime!r}")
    if cell.length not in _LENGTHS:
        raise ValueError(f"{where}: unknown length {cell.length!r}")
    if cell.pages not in _PAGES:
        raise ValueError(f"{where}: unknown pages {cell.pages!r}")
    if cell.count < 2:
        raise ValueError(f"{where}: count must be at least 2 (both splits need a base)")
    low, high = cell.item_band
    if not 1 <= low <= high:
        raise ValueError(f"{where}: item_band {cell.item_band} is not a valid range")
    slots = range(cell.count)
    for trait, trait_slots in cell.traits.items():
        if any(slot not in slots for slot in trait_slots):
            raise ValueError(f"{where}: trait {trait!r} names a slot outside 0..{cell.count - 1}")
        if trait == "st_deferral" and cell.regime != "normal":
            raise ValueError(f"{where}: st_deferral applies to normal cells only")
        if trait == "mixed_tax_codes":
            for slot in trait_slots:
                if cell.pinned_items.get(slot) == 1:
                    raise ValueError(
                        f"{where}: mixed_tax_codes on slot {slot}, which is pinned to 1 item"
                    )
    for slot in cell.pinned_items:
        if slot not in slots:
            raise ValueError(f"{where}: pinned_items names slot {slot} outside the cell")
    for slot, split in cell.curve.items():
        if slot not in slots:
            raise ValueError(f"{where}: curve names slot {slot} outside the cell")
        if split not in _SPLITS:
            raise ValueError(f"{where}: curve slot {slot} has unknown split {split!r}")
        if cell.pages != "single":
            raise ValueError(f"{where}: curve slots belong to single-page cells only")


def _declared_band(cell: Cell, slot: int) -> tuple[int, int]:
    """The band a base's item count is drawn from and tested against.

    A mixed_tax_codes slot needs at least two items to carry two distinct codes, so its low end is
    raised to 2; a band that cannot hold two items is rejected.
    """
    low, high = cell.item_band
    if slot in cell.traits.get("mixed_tax_codes", ()):
        low = max(2, low)
        if low > high:
            raise ValueError(
                f"quota cell {cell.name!r}: mixed_tax_codes slot {slot} needs a band with room "
                f"for 2 items, got {cell.item_band}"
            )
    return low, high


def plan_cases() -> CasePlan:
    """Number the bases in QUOTA order, then the twins of the curve bases."""
    bases: list[BasePlan] = []
    for cell in QUOTA:
        _validate(cell)
        for slot in range(cell.count):
            bases.append(
                BasePlan(
                    case_id=f"nfe-{len(bases) + 1:04d}",
                    cell=cell.name,
                    slot=slot,
                    regime=cell.regime,
                    crt=cell.crt,
                    length=cell.length,
                    item_band=_declared_band(cell, slot),
                    pages=cell.pages,
                    traits=frozenset(
                        trait for trait, slots in cell.traits.items() if slot in slots
                    ),
                    pinned_items=cell.pinned_items.get(slot),
                    curve_split=cell.curve.get(slot),
                )
            )
    twins: list[TwinPlan] = []
    number = len(bases)
    for base in bases:
        if base.curve_split is None:
            continue
        for rendering, severity in TWIN_RENDERINGS:
            number += 1
            twins.append(TwinPlan(f"nfe-{number:04d}", base.case_id, rendering, severity))
    return CasePlan(bases=tuple(bases), twins=tuple(twins))


def _digest(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def assign_splits(plan: CasePlan, seed: int) -> dict[str, str]:
    """Per cell, a third of the bases (at least one) are dev; twins follow their base."""
    splits: dict[str, str] = {}
    for cell in QUOTA:
        members = [base for base in plan.bases if base.cell == cell.name]
        dev_count = max(1, (cell.count + 1) // 3)
        explicit_dev = [b for b in members if b.curve_split == "dev"]
        if len(explicit_dev) > dev_count:
            raise ValueError(
                f"quota cell {cell.name!r}: {len(explicit_dev)} explicit dev curve slots "
                f"exceed the {dev_count} dev places"
            )
        for base in members:
            if base.curve_split is not None:
                splits[base.case_id] = base.curve_split
        remaining = dev_count - len(explicit_dev)
        others = sorted(
            (b for b in members if b.curve_split is None),
            key=lambda b: (_digest(f"{seed}:split:{b.case_id}"), b.case_id),
        )
        for index, base in enumerate(others):
            splits[base.case_id] = "dev" if index < remaining else "test"
    for twin in plan.twins:
        splits[twin.case_id] = splits[twin.base_case]
    return splits


# The cell predicates of the CI subset (D-15); plan 04-07 adds the other cells.
_CI_PREDICATES: dict[str, Callable[[Mapping[str, Any]], bool]] = {
    "st_warning": lambda e: e["rendering"] == "text" and "st_deferral" in e["tags"],
    "rotation_mild": lambda e: e["rendering"] == "rotation" and e["severity"] == "mild",
}


def select_ci_subset(entries: Sequence[Mapping[str, Any]], seed: int) -> list[str]:
    """One dev case per CI cell, the smallest seeded SHA-256 among the matches."""
    chosen: list[str] = []
    for cell in CI_CELLS:
        predicate = _CI_PREDICATES[cell]
        matches = [
            e
            for e in entries
            if e["split"] == "dev" and e["case_id"] not in chosen and predicate(e)
        ]
        if not matches:
            raise ValueError(f"no dev case matches CI cell {cell!r}")
        pick = min(
            matches, key=lambda e: (_digest(f"{seed}:ci:{cell}:{e['case_id']}"), e["case_id"])
        )
        chosen.append(pick["case_id"])
    return sorted(chosen)


def quota_document() -> dict[str, Any]:
    """Everything that defines the dataset's shape, as plain JSON types."""
    return {
        "cells": [
            {
                "name": cell.name,
                "regime": cell.regime,
                "crt": cell.crt,
                "length": cell.length,
                "count": cell.count,
                "item_band": list(cell.item_band),
                "pages": cell.pages,
                "traits": {k: list(v) for k, v in sorted(cell.traits.items())},
                "pinned_items": {str(k): v for k, v in sorted(cell.pinned_items.items())},
                "curve": {str(k): v for k, v in sorted(cell.curve.items())},
            }
            for cell in QUOTA
        ],
        "twin_renderings": [list(pair) for pair in TWIN_RENDERINGS],
        "ci_cells": list(CI_CELLS),
    }


def quota_sha256() -> str:
    text = json.dumps(quota_document(), sort_keys=True, separators=(",", ":"))
    return _digest(text)
