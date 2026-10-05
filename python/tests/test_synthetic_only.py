"""DATA-07 evidence: generated identifiers are valid, unique and provably synthetic.

The nfelib sample XMLs are read here only, as an independent oracle for the check-digit math and
as a collision list. Nothing from them is written anywhere.
"""

from __future__ import annotations

import random
import re
from functools import cache
from importlib import resources

import pytest

from carimbo_datagen.ids import (
    ACCESS_KEY_PATTERN,
    access_key_check_digit,
    char_value,
    cnpj_check_digits,
    is_valid_cnpj,
    make_cnpj,
)
from carimbo_datagen.spec import CASE_IDS, MASTER_SEED, CaseSpec, build_case_spec

_KEY_ID = re.compile(r'Id="NFe([0-9A-Z]{44})"')
_CNPJ_TAG = re.compile(r"<CNPJ>([0-9A-Z]{14})</CNPJ>")


@cache
def _nfelib_sample_texts() -> tuple[str, ...]:
    root = resources.files("nfelib")
    texts: list[str] = []
    stack = [root]
    while stack:
        node = stack.pop()
        for child in node.iterdir():
            if child.is_dir():
                stack.append(child)
            elif child.name.endswith(".xml"):
                text = child.read_text(encoding="utf-8")
                if "infNFe" in text:
                    texts.append(text)
    return tuple(texts)


def _all_specs() -> list[CaseSpec]:
    return [build_case_spec(MASTER_SEED, case_id) for case_id in CASE_IDS]


def _all_parties(spec: CaseSpec):
    return (spec.issuer, spec.recipient)


def test_char_value_maps_digits_and_letters_by_ascii_minus_48() -> None:
    assert char_value("0") == 0
    assert char_value("9") == 9
    assert char_value("A") == 17
    assert char_value("Z") == 42


def test_cnpj_check_digits_textbook_vector() -> None:
    assert cnpj_check_digits("112223330001") == "81"
    assert is_valid_cnpj("11222333000181")
    assert not is_valid_cnpj("11222333000182")


def test_cnpj_check_digits_alphanumeric_nt_2025_001_example() -> None:
    assert cnpj_check_digits("12ABC34501DE") == "35"
    assert is_valid_cnpj("12ABC34501DE35")
    assert not is_valid_cnpj("12abc34501de35")
    assert not is_valid_cnpj("12ABC34501DEAB")


def test_access_key_check_digit_matches_every_nfelib_sample_key() -> None:
    keys = [key for text in _nfelib_sample_texts() for key in _KEY_ID.findall(text)]
    assert keys, "no sample access keys found: the oracle would pass vacuously"
    for key in keys:
        assert access_key_check_digit(key[:43]) == int(key[43]), key


def test_make_cnpj_alphanumeric_contains_a_letter_and_is_valid() -> None:
    rng = random.Random("oracle")
    for _ in range(50):
        cnpj = make_cnpj(rng, alphanumeric=True)
        assert is_valid_cnpj(cnpj)
        assert any(ch.isalpha() for ch in cnpj[:8])
        assert cnpj[8:12] == "0001"


def test_make_cnpj_numeric_is_all_digits_and_valid() -> None:
    rng = random.Random("oracle")
    for _ in range(50):
        cnpj = make_cnpj(rng, alphanumeric=False)
        assert cnpj.isdigit()
        assert is_valid_cnpj(cnpj)
        assert len(set(cnpj[:8])) > 1


def test_generated_cnpjs_are_valid_and_absent_from_nfelib_samples() -> None:
    sample_cnpjs = {c for text in _nfelib_sample_texts() for c in _CNPJ_TAG.findall(text)}
    assert sample_cnpjs, "no sample CNPJs found: the collision check would pass vacuously"
    for spec in _all_specs():
        for party in _all_parties(spec):
            assert is_valid_cnpj(party.cnpj)
            assert party.cnpj not in sample_cnpjs


def test_issuer_differs_from_recipient_in_every_case() -> None:
    for spec in _all_specs():
        assert spec.issuer.cnpj != spec.recipient.cnpj
        assert spec.issuer.name != spec.recipient.name


def test_no_cnpj_repeats_across_the_three_cases() -> None:
    cnpjs = [party.cnpj for spec in _all_specs() for party in _all_parties(spec)]
    assert len(cnpjs) == 6
    assert len(set(cnpjs)) == 6


def test_every_party_name_is_non_empty_and_marked_synthetic() -> None:
    for spec in _all_specs():
        for party in _all_parties(spec):
            assert party.name.strip()
            assert "SINTETICA" in party.name
            assert len(party.name) <= 60


def test_case_spec_is_independent_of_build_order() -> None:
    alone = build_case_spec(MASTER_SEED, "case-002")
    build_case_spec(MASTER_SEED, "case-001")
    build_case_spec(MASTER_SEED, "case-003")
    after_others = build_case_spec(MASTER_SEED, "case-002")
    assert alone == after_others


def test_case_spec_changes_with_the_master_seed() -> None:
    assert build_case_spec(MASTER_SEED, "case-001") != build_case_spec(MASTER_SEED + 1, "case-001")


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_access_key_layout_matches_the_spec_fields(case_id: str) -> None:
    spec = build_case_spec(MASTER_SEED, case_id)
    key = spec.access_key
    assert re.fullmatch(ACCESS_KEY_PATTERN, key)
    assert key[6:20] == spec.issuer.cnpj
    assert key[2:6] == f"{spec.issue_datetime:%y%m}"
    assert key[20:22] == "55"
    assert key[22:25] == f"{spec.series:03d}"
    assert key[25:34] == f"{spec.number:09d}"
    assert key[34] == "1"
    assert key[35:43] == f"{spec.numeric_code:08d}"
    assert access_key_check_digit(key[:43]) == int(key[43])
    assert spec.issue_datetime.utcoffset() is not None
    assert spec.issue_datetime.utcoffset().total_seconds() == -3 * 3600  # type: ignore[union-attr]


def test_only_case_002_has_an_alphanumeric_issuer_cnpj() -> None:
    specs = {case_id: build_case_spec(MASTER_SEED, case_id) for case_id in CASE_IDS}
    assert any(ch.isalpha() for ch in specs["case-002"].issuer.cnpj)
    for case_id in ("case-001", "case-003"):
        assert specs[case_id].issuer.cnpj.isdigit()
        assert specs[case_id].recipient.cnpj.isdigit()
    assert specs["case-002"].recipient.cnpj.isdigit()


def test_item_counts_follow_the_profiles() -> None:
    from carimbo_datagen.spec import MULTIPAGE_ITEMS

    assert len(build_case_spec(MASTER_SEED, "case-001").items) == 3
    assert len(build_case_spec(MASTER_SEED, "case-002").items) == 4
    assert len(build_case_spec(MASTER_SEED, "case-003").items) == MULTIPAGE_ITEMS


def test_item_totals_are_rounded_half_up_to_cents() -> None:
    for spec in _all_specs():
        total = sum(item.total for item in spec.items)
        assert spec.total_amount == total
        assert spec.total_amount.as_tuple().exponent == -2
