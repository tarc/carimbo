"""VAL-06: the shared, hand-curated vector file drives the Python identifier and rounding math.

The expected values in data/vectors/validator-vectors.json are published examples or hand
computations recorded in notes; they never come from the code under test. The same file is run
by xUnit against the .NET port. Nothing here writes it, and the sum_tolerance section is read
by xUnit only (the totals validators live in .NET, D-09).
"""

from __future__ import annotations

import json
import random
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from carimbo_datagen.ids import (
    cnpj_check_digits,
    cpf_check_digits,
    is_valid_access_key,
    is_valid_cnpj,
    is_valid_cpf,
    make_cpf,
)
from carimbo_evals.grader import DEFAULT_TOLERANCE
from carimbo_evals.money import round_half_up

VECTORS_PATH = Path(__file__).resolve().parents[2] / "data" / "vectors" / "validator-vectors.json"

with VECTORS_PATH.open(encoding="utf-8") as _handle:
    VECTORS: dict[str, Any] = json.load(_handle)

RULES = {
    "CNPJ_FORMAT",
    "CNPJ_CHECK_DIGIT",
    "CPF_FORMAT",
    "CPF_CHECK_DIGIT",
    "KEY_FORMAT",
    "KEY_CHECK_DIGIT",
}

EXPECTED_SECTIONS = [
    "vector_version",
    "rounding_rule",
    "tolerance",
    "cnpj",
    "cpf",
    "access_key",
    "rounding",
    "sum_tolerance",
]


def _ids(section: str, field: str = "value") -> list[Any]:
    return [pytest.param(entry, id=entry[field]) for entry in VECTORS[section]]


def test_vector_file_has_version_and_every_section() -> None:
    assert VECTORS["vector_version"] == 1
    assert list(VECTORS) == EXPECTED_SECTIONS


@pytest.mark.parametrize("entry", _ids("cnpj"))
def test_cnpj_vectors(entry: dict[str, Any]) -> None:
    value = entry["value"]
    assert is_valid_cnpj(value) is entry["valid"]
    if entry["valid"]:
        assert cnpj_check_digits(value[:12]) == value[12:]
    else:
        assert entry["rule"] in RULES


@pytest.mark.parametrize("entry", _ids("cpf"))
def test_cpf_vectors(entry: dict[str, Any]) -> None:
    value = entry["value"]
    assert is_valid_cpf(value) is entry["valid"]
    if entry["valid"]:
        assert cpf_check_digits(value[:9]) == value[9:]
    else:
        assert entry["rule"] in RULES


@pytest.mark.parametrize("entry", _ids("access_key"))
def test_access_key_vectors(entry: dict[str, Any]) -> None:
    assert is_valid_access_key(entry["value"]) is entry["valid"]
    if not entry["valid"]:
        assert entry["rule"] in RULES


@pytest.mark.parametrize("entry", _ids("rounding", field="input"))
def test_rounding_vectors(entry: dict[str, Any]) -> None:
    result = round_half_up(Decimal(entry["input"]))
    assert str(result) == entry["expected"]
    assert not str(result).startswith("-0.00")


def test_make_cpf_only_yields_valid_cpfs() -> None:
    rng = random.Random(20261008)
    for _ in range(1000):
        cpf = make_cpf(rng)
        assert is_valid_cpf(cpf)
        assert len(set(cpf)) > 1


def test_grader_default_tolerance_matches_the_vector_file() -> None:
    assert Decimal(VECTORS["tolerance"]["tolerance"]) == DEFAULT_TOLERANCE
