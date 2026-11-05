"""The declared slices of the correct-invoice rate (D-15), versioned with the grader.

A slice is a named subset of a split's cases. The list is fixed in code: changing it resets every
slice comparison, so the version moves with it. Rows are read from a graded case's ``case_tags``,
its manifest ``barcode_readable`` flag, ``rendering`` and ``severity``. No import from the grader
or the summary. ``render_hazards`` lives here too but is not a slice (D-47).
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from decimal import Decimal
from typing import Any

from carimbo_evals.normalize import normalize_name, parse_decimal

SLICES_VERSION = "slices-1"
# An item unit_price or total from this value up wraps mid-number in the DANFE text layer (D-47).
HAZARD_NUMBER_MIN = Decimal("100000")


@dataclass(frozen=True)
class Slice:
    slice_id: str
    dimension: str
    matches: Callable[[dict[str, Any]], bool]


def _tag(tag: str) -> Callable[[dict[str, Any]], bool]:
    def matches(case: dict[str, Any]) -> bool:
        tags = case.get("case_tags")
        return isinstance(tags, list) and tag in tags

    return matches


def _flag(value: bool) -> Callable[[dict[str, Any]], bool]:
    def matches(case: dict[str, Any]) -> bool:
        return case.get("barcode_readable") is value

    return matches


def _rendering(kind: str, severity: str | None = None) -> Callable[[dict[str, Any]], bool]:
    def matches(case: dict[str, Any]) -> bool:
        return case.get("rendering") == kind and case.get("severity") == severity

    return matches


def render_hazards(invoice: dict[str, Any]) -> list[str]:
    """The DANFE wrap hazards the ground truth implies, sorted (D-47).

    ``unit_wrap``: an item unit that reads ``PAR`` once normalised, which the text layer wraps as
    ``PA`` over ``R``. ``number_wrap``: an item ``unit_price`` or ``total`` of at least
    ``HAZARD_NUMBER_MIN``, which wraps mid-number. A hazard is a property of the page, derived with
    no dataset change; it never changes a grade and is not a slice.
    """
    items = invoice.get("items")
    found: set[str] = set()
    for item in items if isinstance(items, list) else []:
        if not isinstance(item, dict):
            continue
        unit = item.get("unit")
        if isinstance(unit, str) and normalize_name(unit) == "par":
            found.add("unit_wrap")
        for column in ("unit_price", "total"):
            number = parse_decimal(item.get(column))
            if number is not None and number >= HAZARD_NUMBER_MIN:
                found.add("number_wrap")
    return sorted(found)


def _tagged(dimension: str, value: str, tag: str) -> Slice:
    return Slice(f"{dimension}={value}", dimension, _tag(tag))


SLICES: tuple[Slice, ...] = (
    _tagged("regime", "normal", "regime_normal"),
    _tagged("regime", "simples", "simples_nacional"),
    _tagged("length", "short", "short"),
    _tagged("length", "medium", "medium"),
    _tagged("length", "many_items", "many_items"),
    _tagged("multi_page", "yes", "multi_page"),
    _tagged("multi_page", "no", "single_page"),
    _tagged("alphanumeric_cnpj", "yes", "alphanumeric_cnpj"),
    _tagged("cpf_recipient", "yes", "cpf_recipient"),
    _tagged("st_deferral", "yes", "st_deferral"),
    _tagged("mixed_tax_codes", "yes", "mixed_tax_codes"),
    _tagged("long_text", "yes", "long_text"),
    _tagged("confusable", "yes", "confusable"),
    Slice("barcode_readable=yes", "barcode_readable", _flag(True)),
    Slice("barcode_readable=no", "barcode_readable", _flag(False)),
    Slice("rendering=text", "rendering", _rendering("text")),
    Slice("rendering=image_clean", "rendering", _rendering("image_clean")),
    Slice("rendering=barcode_damaged", "rendering", _rendering("barcode_damaged")),
    Slice("rendering=rotation/mild", "rendering", _rendering("rotation", "mild")),
    Slice("rendering=rotation/severe", "rendering", _rendering("rotation", "severe")),
    Slice("rendering=blur/mild", "rendering", _rendering("blur", "mild")),
    Slice("rendering=blur/severe", "rendering", _rendering("blur", "severe")),
    Slice("rendering=low_dpi/mild", "rendering", _rendering("low_dpi", "mild")),
    Slice("rendering=low_dpi/severe", "rendering", _rendering("low_dpi", "severe")),
)
