"""Decimal-only money parsing shared by the runner and the offline summary (no I/O, no HTTP)."""

from __future__ import annotations

from decimal import Decimal, InvalidOperation


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
