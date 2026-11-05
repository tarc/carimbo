"""EVAL-05: the statistics behind the headline rows, pinned (D-14, D-16)."""

from __future__ import annotations

import decimal
from decimal import Decimal

import pytest

from carimbo_evals.stats import rate, wilson

WILSON_PINS = [
    ((30, 40, 5), ("0.3360", "0.9468")),
    ((45, 60, 60), ("0.6277", "0.8422")),
    ((0, 60, 60), ("0.0000", "0.0602")),
    ((60, 60, 60), ("0.9398", "1.0000")),
    ((2, 3, 3), ("0.2077", "0.9385")),
    ((1, 1, 1), ("0.2065", "1.0000")),
    ((0, 1, 1), ("0.0000", "0.7935")),
]


@pytest.mark.parametrize(("args", "expected"), WILSON_PINS)
def test_wilson_pins(args: tuple[int, int, int], expected: tuple[str, str]) -> None:
    assert wilson(*args) == (Decimal(expected[0]), Decimal(expected[1]))


def test_wilson_bounds_are_clamped_and_a_single_case_has_an_interval() -> None:
    low, high = wilson(0, 60, 60) or (None, None)
    assert low == Decimal("0.0000")
    assert high == Decimal("0.0602")
    assert wilson(60, 60, 60) == (Decimal("0.9398"), Decimal("1.0000"))
    assert wilson(1, 1, 1) is not None


def test_wilson_and_rate_return_none_without_cases_or_invoices() -> None:
    assert wilson(0, 0, 0) is None
    assert wilson(0, 0, 5) is None
    assert wilson(3, 5, 0) is None
    assert rate(0, 0) is None
    assert rate(3, 0) is None


def test_rate_is_quantized_to_four_places_half_up() -> None:
    assert rate(2, 3) == 0.6667
    assert rate(1, 3) == 0.3333
    assert rate(0, 5) == 0.0
    assert rate(5, 5) == 1.0
    assert rate(1, 8) == 0.125
    assert rate(1, 16) == 0.0625
    assert rate(1, 32) == 0.0313  # 0.03125 rounds half up


def test_the_global_decimal_context_is_untouched() -> None:
    before = decimal.getcontext().prec
    wilson(30, 40, 5)
    rate(2, 3)
    assert decimal.getcontext().prec == before
