"""EVAL-05: the statistics behind the headline rows, pinned (D-14, D-16)."""

from __future__ import annotations

import decimal
import math
from decimal import Decimal
from fractions import Fraction

import pytest

from carimbo_evals.stats import (
    BOOTSTRAP_SEED,
    bootstrap_intervals,
    bootstrap_sums,
    is_significant,
    mcnemar_exact,
    nearest_rank,
    rate,
    wilson,
)

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


# --- exact McNemar, nearest rank, the seeded bootstrap ---------------------------------------


@pytest.mark.parametrize(
    ("b", "c", "expected"),
    [
        (5, 0, Fraction(1, 16)),
        (6, 0, Fraction(1, 32)),
        (0, 0, Fraction(1)),
        (3, 3, Fraction(1)),
        (7, 1, Fraction(9, 128)),
        (1, 6, Fraction(1, 8)),
    ],
)
def test_mcnemar_exact_pins(b: int, c: int, expected: Fraction) -> None:
    assert mcnemar_exact(b, c) == expected


def test_significance_is_strictly_below_five_percent_without_a_float() -> None:
    assert is_significant(Fraction(1, 20)) is False
    assert is_significant(Fraction(1, 32)) is True
    assert is_significant(Fraction(1)) is False


def test_mcnemar_exact_equals_scipy_binomtest_for_every_b_and_c_below_40() -> None:
    from scipy.stats import binomtest

    for b in range(40):
        for c in range(40):
            if b + c == 0:
                continue
            expected = float(binomtest(min(b, c), b + c, 0.5).pvalue)
            assert abs(float(mcnemar_exact(b, c)) - expected) < 1e-12, (b, c)


def test_wilson_equals_scipy_proportion_ci_for_every_k_and_n_up_to_60() -> None:
    from scipy.stats import binomtest

    for n in range(1, 61):
        for k in range(n + 1):
            interval = binomtest(k, n).proportion_ci(method="wilson")
            got = wilson(k, n, n)
            assert got is not None
            assert abs(float(got[0]) - float(interval.low)) < 5e-5, (k, n)
            assert abs(float(got[1]) - float(interval.high)) < 5e-5, (k, n)


def test_bootstrap_sums_pins() -> None:
    first = [[3, 1], [2, 2], [3, 1], [1, 3]]
    second = [[10, 16], [10, 16], [4, 16]]
    assert bootstrap_sums([[1, 0], [0, 1], [1, 1]], 20261105, 4) == first
    assert bootstrap_sums([[3, 4], [1, 4], [0, 4], [4, 4]], 20261105, 3) == second


def test_nearest_rank() -> None:
    assert nearest_rank([1, 2, 3, 4], 50) == 2
    assert nearest_rank([1, 2, 3, 4], 95) == 4
    assert nearest_rank([7], 50) == 7
    assert nearest_rank([7], 95) == 7
    assert nearest_rank([], 50) is None
    assert nearest_rank([5, 5, 5, 9], 50) == 5


def test_bootstrap_intervals_are_deterministic_and_none_without_a_denominator() -> None:
    vectors = [[1, 1], [0, 1], [1, 1], [1, 1], [0, 1]]
    first = bootstrap_intervals(vectors, {"r": (0, 1)}, seed=BOOTSTRAP_SEED, resamples=200)
    second = bootstrap_intervals(vectors, {"r": (0, 1)}, seed=BOOTSTRAP_SEED, resamples=200)
    assert first == second
    interval = first["r"]
    assert interval is not None
    assert 0.0 <= interval[0] <= interval[1] <= 1.0

    no_denominator = [[1, 0], [0, 0], [1, 0]]
    assert bootstrap_intervals(
        no_denominator, {"r": (0, 1)}, seed=BOOTSTRAP_SEED, resamples=200
    ) == {"r": None}


def test_bootstrap_intervals_equal_a_fraction_reference_and_empty_vectors_give_none() -> None:
    vectors = [[3, 4], [1, 4], [0, 4], [4, 4], [2, 3], [1, 2], [0, 1], [5, 5]]
    seed, resamples = BOOTSTRAP_SEED, 500
    sums = bootstrap_sums(vectors, seed, resamples)
    ordered = sorted(Fraction(s[0], s[1]) for s in sums if s[1] != 0)
    count = len(ordered)
    low = ordered[max(0, math.ceil(Fraction(25 * count, 1000)) - 1)]
    high = ordered[max(0, math.ceil(Fraction(975 * count, 1000)) - 1)]
    reference = [rate(low.numerator, low.denominator), rate(high.numerator, high.denominator)]

    got = bootstrap_intervals(vectors, {"r": (0, 1)}, seed=seed, resamples=resamples)

    assert got == {"r": reference}
    assert bootstrap_intervals([], {"a": (0, 1), "b": (1, 0)}, seed=seed, resamples=10) == {
        "a": None,
        "b": None,
    }
