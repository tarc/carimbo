"""RED stub: signatures only, behaviour arrives in the GREEN commit."""

from __future__ import annotations

import random

ACCESS_KEY_PATTERN = r"[0-9]{6}[A-Z0-9]{12}[0-9]{26}"


def char_value(ch: str) -> int:
    raise NotImplementedError


def cnpj_check_digits(base12: str) -> str:
    raise NotImplementedError


def is_valid_cnpj(cnpj: str) -> bool:
    raise NotImplementedError


def make_cnpj(rng: random.Random, *, alphanumeric: bool) -> str:
    raise NotImplementedError


def access_key_check_digit(key43: str) -> int:
    raise NotImplementedError
