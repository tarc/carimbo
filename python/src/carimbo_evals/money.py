"""Decimal-only money parsing shared by the runner and the offline summary (no I/O, no HTTP)."""

from __future__ import annotations

from decimal import ROUND_HALF_UP, Decimal, InvalidOperation


def parse_cost(value: object) -> Decimal | None:
    """Parse the endpoint's ``cost_usd`` decimal string; ``None`` when absent or unusable.

    Only strings are accepted: a JSON number would already have been through a float.
    """
    if not isinstance(value, str):
        return None
    try:
        cost = Decimal(value)
    except InvalidOperation:
        return None
    return cost if cost.is_finite() and cost >= 0 else None


def round_half_up(value: Decimal, places: int = 2) -> Decimal:
    """Round half away from zero to ``places`` decimals, never producing a negative zero.

    This is the rounding rule shared with the .NET validators
    (data/vectors/validator-vectors.json). ``ROUND_HALF_UP`` in ``decimal`` rounds ties away
    from zero, and a result equal to zero is returned positive.
    """
    rounded = value.quantize(Decimal(1).scaleb(-places), rounding=ROUND_HALF_UP)
    return abs(rounded) if rounded == 0 else rounded
