"""Value normalization shared by the grader, the row matching and the failure tags.

Pure and dependency-free, so ``grader``, ``matching`` and later ``tags`` can all import it without
a cycle. Nothing here is fuzzy: a normalized value is equal to another or it is not (D-42).
"""

from __future__ import annotations

import re
import unicodedata
from decimal import Decimal, InvalidOperation


def normalize_id(value: str) -> str:
    """Strip spaces, dots, slashes and hyphens, then uppercase."""
    return re.sub(r"[ ./-]", "", value).upper()


def normalize_name(value: str) -> str:
    """NFKC, casefold, punctuation to spaces, collapse whitespace.

    Accents are kept (``Comércio`` is not ``Comercio``); only their Unicode form is normalized.
    """
    folded = unicodedata.normalize("NFKC", value).casefold()
    return re.sub(r"[\W_]+", " ", folded).strip()


def parse_decimal(value: object) -> Decimal | None:
    """The finite ``Decimal`` a string holds, or ``None`` for anything else.

    A float, an int or a bool is not a money string and returns ``None``, so no float ever reaches
    a graded comparison.
    """
    if not isinstance(value, str):
        return None
    try:
        number = Decimal(value)
    except InvalidOperation:
        return None
    return number if number.is_finite() else None
