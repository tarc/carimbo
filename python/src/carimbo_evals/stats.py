"""Statistics for the run summary and `compare`: stdlib only, exact where it can be (D-14, D-16).

Rates and interval bounds go through one rounding helper, ``quantize4``: 4 places, ROUND_HALF_UP,
computed in a local Decimal context so the global one is never touched.
"""

from __future__ import annotations

import math
import random
from decimal import ROUND_HALF_UP, Decimal, localcontext
from fractions import Fraction

# 97.5th percentile of the standard normal: a fixed constant, never computed.
Z = Decimal("1.959963984540054")
PLACES = Decimal("0.0001")
PRECISION = 28


def quantize4(value: Decimal) -> Decimal:
    """``value`` rounded to 4 places, half up, in a local context."""
    with localcontext() as ctx:
        ctx.prec = PRECISION
        return value.quantize(PLACES, ROUND_HALF_UP)


def wilson(correct: int, cases: int, invoices: int) -> tuple[Decimal, Decimal] | None:
    """95 percent Wilson interval for ``correct / cases``, with n set to ``invoices``.

    Twins of one base invoice are one draw, so the effective n is the distinct invoices, not the
    cases. Clamped to 0 and 1; None when there is no case or no invoice.
    """
    if cases == 0 or invoices == 0:
        return None
    with localcontext() as ctx:
        ctx.prec = PRECISION
        p, n, z2 = Decimal(correct) / Decimal(cases), Decimal(invoices), Z * Z
        d = 1 + z2 / n
        centre = (p + z2 / (2 * n)) / d
        half = Z * ((p * (1 - p) / n + z2 / (4 * n * n)).sqrt()) / d
        return (
            quantize4(max(Decimal(0), centre - half)),
            quantize4(min(Decimal(1), centre + half)),
        )


def rate(numerator: int, denominator: int) -> float | None:
    """``numerator / denominator`` rounded to 4 places as a float; None for a zero denominator."""
    if denominator == 0:
        return None
    with localcontext() as ctx:
        ctx.prec = PRECISION
        return float(quantize4(Decimal(numerator) / Decimal(denominator)))


# The seed of every bootstrap in a summary or a comparison, and how many resamples they draw.
BOOTSTRAP_SEED = 20261105
DEFAULT_RESAMPLES = 10000


def mcnemar_exact(b: int, c: int) -> Fraction:
    """Two-sided exact McNemar p-value; ``b`` and ``c`` are the cases correct in one run only.

    The binomial tail of the smaller count at probability one half, doubled and capped at 1; 1
    when there is no discordant case.
    """
    n = b + c
    if n == 0:
        return Fraction(1)
    tail = sum(math.comb(n, i) for i in range(min(b, c) + 1))
    return min(Fraction(1), Fraction(2 * tail, 2**n))


def is_significant(p: Fraction) -> bool:
    """``p < 0.05``, tested as ``20 * numerator < denominator`` so no float is compared."""
    return 20 * p.numerator < p.denominator


def nearest_rank(sorted_values: list[int], pct: int) -> int | None:
    """The nearest-rank percentile of an ascending list; None when it is empty."""
    if not sorted_values:
        return None
    return sorted_values[max(0, math.ceil(pct * len(sorted_values) / 100) - 1)]


def bootstrap_sums(vectors: list[list[int]], seed: int, resamples: int) -> list[list[int]]:
    """Counter sums of ``resamples`` resamples of the invoices, drawn with replacement.

    ``vectors`` holds one list of integer counters per invoice, sorted by base_case by the caller.
    Only ``random.Random.random`` is used, the one stream the Python docs guarantee across
    versions for a given seed.
    """
    rnd = random.Random(seed).random
    n = len(vectors)
    out: list[list[int]] = []
    for _ in range(resamples):
        picks = [vectors[int(rnd() * n)] for _ in range(n)]
        out.append(list(map(sum, zip(*picks, strict=True))))
    return out


def bootstrap_intervals(
    vectors: list[list[int]],
    ratios: dict[str, tuple[int, int]],
    *,
    seed: int,
    resamples: int,
) -> dict[str, list[float] | None]:
    """Percentile bootstrap 95 percent intervals of ``numerator / denominator`` per named ratio.

    ``ratios`` maps a name to the counter indices of its numerator and denominator. A resample
    whose denominator is 0 is skipped; a ratio with no usable resample, and every ratio when
    ``vectors`` is empty (no draw is made), is None. The bounds are the nearest-rank 2.5th and
    97.5th percentiles, ranked in exact integer arithmetic and quantized like ``rate``.
    """
    if not vectors:
        return dict.fromkeys(ratios)
    sums = bootstrap_sums(vectors, seed, resamples)
    found: dict[str, list[float] | None] = {}
    for name, (numerator, denominator) in ratios.items():
        # int / int is correctly rounded and these counters stay far below 2**26, so distinct
        # ratios keep distinct keys and equal ratios compare equal.
        pairs = sorted(
            ((s[numerator], s[denominator]) for s in sums if s[denominator] != 0),
            key=lambda pair: pair[0] / pair[1],
        )
        if not pairs:
            found[name] = None
            continue
        count = len(pairs)
        low = pairs[max(0, -(-count // 40) - 1)]  # ceil(2.5 * count / 100)
        high = pairs[max(0, -(-39 * count // 40) - 1)]  # ceil(97.5 * count / 100)
        bounds = (rate(low[0], low[1]), rate(high[0], high[1]))
        found[name] = [bound for bound in bounds if bound is not None]
    return found
