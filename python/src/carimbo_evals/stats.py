"""Statistics for the run summary and `compare`: stdlib only, exact where it can be (D-14, D-16).

Rates and interval bounds go through one rounding helper, ``quantize4``: 4 places, ROUND_HALF_UP,
computed in a local Decimal context so the global one is never touched.
"""

from __future__ import annotations

from decimal import ROUND_HALF_UP, Decimal, localcontext

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
