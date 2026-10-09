"""CNPJ, CPF and NF-e access-key identifier math. Pure, stdlib only.

CNPJ and the access key use a modulo-11 check digit over ASCII-48 character values, which covers
the numeric form and the alphanumeric CNPJ (NT 2025.001) with one rule. CPF is numeric only.
The expected values these routines are tested against live in data/vectors/validator-vectors.json.
"""

from __future__ import annotations

import random
import re

ACCESS_KEY_PATTERN = r"[0-9]{6}[A-Z0-9]{12}[0-9]{26}"

_CNPJ_PATTERN = re.compile(r"[A-Z0-9]{12}[0-9]{2}")
_CPF_PATTERN = re.compile(r"[0-9]{11}")
_ACCESS_KEY_RE = re.compile(ACCESS_KEY_PATTERN)
_CNPJ_WEIGHTS_1 = (5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2)
_CNPJ_WEIGHTS_2 = (6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2)
_CPF_WEIGHTS_1 = (10, 9, 8, 7, 6, 5, 4, 3, 2)
_CPF_WEIGHTS_2 = (11, 10, 9, 8, 7, 6, 5, 4, 3, 2)
_DIGITS = "0123456789"
_ALPHANUMERIC = _DIGITS + "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
_BRANCH = "0001"


def char_value(ch: str) -> int:
    """Check-digit value of one character: its ASCII code minus 48 (valid for 0-9 and A-Z)."""
    return ord(ch) - 48


def _mod11_digit(weighted_sum: int) -> int:
    remainder = weighted_sum % 11
    return 0 if remainder < 2 else 11 - remainder


def _cnpj_digit(chars: str, weights: tuple[int, ...]) -> int:
    return _mod11_digit(sum(char_value(c) * w for c, w in zip(chars, weights, strict=True)))


def cnpj_check_digits(base12: str) -> str:
    """The two check digits for a 12-character CNPJ base (8 root + 4 branch)."""
    first = _cnpj_digit(base12, _CNPJ_WEIGHTS_1)
    second = _cnpj_digit(base12 + str(first), _CNPJ_WEIGHTS_2)
    return f"{first}{second}"


def is_valid_cnpj(cnpj: str) -> bool:
    """Bare 14-character CNPJ with matching check digits; identical characters are rejected.

    14 identical characters pass the arithmetic (00000000000000 computes DV 00) but are not a
    real identifier, so the project rejects them (a project rule, not a cited official one).
    """
    if not _CNPJ_PATTERN.fullmatch(cnpj):
        return False
    if len(set(cnpj)) == 1:
        return False
    return cnpj_check_digits(cnpj[:12]) == cnpj[12:]


def cpf_check_digits(base9: str) -> str:
    """The two check digits for a 9-digit CPF base (weights 10..2, then 11..2 with the first DV)."""
    first = _mod11_digit(sum(char_value(c) * w for c, w in zip(base9, _CPF_WEIGHTS_1, strict=True)))
    second = _mod11_digit(
        sum(char_value(c) * w for c, w in zip(base9 + str(first), _CPF_WEIGHTS_2, strict=True))
    )
    return f"{first}{second}"


def is_valid_cpf(cpf: str) -> bool:
    """Bare 11-digit CPF with matching check digits; 11 identical digits are rejected."""
    if not _CPF_PATTERN.fullmatch(cpf):
        return False
    if len(set(cpf)) == 1:
        return False
    return cpf_check_digits(cpf[:9]) == cpf[9:]


def make_cpf(rng: random.Random) -> str:
    """A check-digit-valid synthetic CPF; bases made of one repeated digit are redrawn."""
    while True:
        base = "".join(rng.choice(_DIGITS) for _ in range(9))
        if len(set(base)) == 1:
            continue
        return base + cpf_check_digits(base)


def make_cnpj(rng: random.Random, *, alphanumeric: bool) -> str:
    """A check-digit-valid synthetic CNPJ with branch 0001.

    The alphanumeric form always has at least one letter in the root. Roots made of a single
    repeated character are rejected.
    """
    while True:
        root = "".join(rng.choice(_ALPHANUMERIC if alphanumeric else _DIGITS) for _ in range(8))
        if len(set(root)) == 1:
            continue
        if alphanumeric and root.isdigit():
            continue
        base = root + _BRANCH
        return base + cnpj_check_digits(base)


def access_key_check_digit(key43: str) -> int:
    """Check digit for the first 43 characters of an access key (weights 2..9, right to left)."""
    total = 0
    weight = 2
    for ch in reversed(key43):
        total += char_value(ch) * weight
        weight = 2 if weight == 9 else weight + 1
    return _mod11_digit(total)


def make_access_key(
    *,
    uf: int,
    year: int,
    month: int,
    cnpj: str,
    model: int,
    series: int,
    number: int,
    emission_type: int,
    numeric_code: int,
) -> str:
    """Lay out cUF(2) AAMM(4) CNPJ(14) mod(2) serie(3) nNF(9) tpEmis(1) cNF(8) cDV(1)."""
    key43 = (
        f"{uf:02d}{year % 100:02d}{month:02d}{cnpj}{model:02d}"
        f"{series:03d}{number:09d}{emission_type:d}{numeric_code:08d}"
    )
    if len(key43) != 43:
        raise ValueError(f"access key body must be 43 characters, got {len(key43)}")
    return key43 + str(access_key_check_digit(key43))


def is_valid_access_key(key: str) -> bool:
    """44-character access key (CNPJ part may be alphanumeric) with a matching check digit."""
    if not _ACCESS_KEY_RE.fullmatch(key):
        return False
    return access_key_check_digit(key[:43]) == int(key[43])
