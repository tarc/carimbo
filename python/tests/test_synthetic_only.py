"""DATA-07 evidence: generated identifiers are valid, unique and provably synthetic.

The nfelib sample XMLs are read here only, as an independent oracle for the check-digit math and
as a collision list. Nothing from them is written anywhere.
"""

from __future__ import annotations

import json
import random
import re
from datetime import timedelta
from functools import cache
from importlib import resources
from pathlib import Path

import pytest

from carimbo_datagen.ids import (
    ACCESS_KEY_PATTERN,
    access_key_check_digit,
    char_value,
    cnpj_check_digits,
    is_valid_cnpj,
    is_valid_cpf,
    make_cnpj,
)
from carimbo_datagen.dataset_spec import build_dataset_spec
from carimbo_datagen.quota import DATASET_SEED, plan_cases
from carimbo_datagen.spec import CASE_IDS, MASTER_SEED, CaseSpec, build_case_spec

_REPO_ROOT = Path(__file__).resolve().parents[2]
_KEY_ID = re.compile(r'Id="NFe([0-9A-Z]{44})"')
_CNPJ_TAG = re.compile(r"<CNPJ>([0-9A-Z]{14})</CNPJ>")
_CPF_TAG = re.compile(r"<CPF>([0-9]{11})</CPF>")


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


def test_generated_identifiers_are_valid_and_absent_from_nfelib_samples() -> None:
    sample_cnpjs = {c for text in _nfelib_sample_texts() for c in _CNPJ_TAG.findall(text)}
    sample_cpfs = {c for text in _nfelib_sample_texts() for c in _CPF_TAG.findall(text)}
    assert sample_cnpjs, "no sample CNPJs found: the collision check would pass vacuously"
    assert sample_cpfs, "no sample CPFs found: the collision check would pass vacuously"
    for spec in _all_specs():
        assert is_valid_cnpj(spec.issuer.tax_id)
        assert spec.issuer.tax_id not in sample_cnpjs
        if spec.recipient_tax_id_kind == "cpf":
            assert is_valid_cpf(spec.recipient.tax_id)
            assert spec.recipient.tax_id not in sample_cpfs
        else:
            assert is_valid_cnpj(spec.recipient.tax_id)
            assert spec.recipient.tax_id not in sample_cnpjs


def test_no_generated_cpf_repeats_a_single_digit() -> None:
    cpfs = [spec.recipient.tax_id for spec in _all_specs() if spec.recipient_tax_id_kind == "cpf"]
    assert cpfs, "no CPF recipient generated: the check would pass vacuously"
    for cpf in cpfs:
        assert len(set(cpf)) > 1


def test_issuer_differs_from_recipient_in_every_case() -> None:
    for spec in _all_specs():
        assert spec.issuer.tax_id != spec.recipient.tax_id
        assert spec.issuer.name != spec.recipient.name


def test_no_identifier_repeats_across_the_three_cases() -> None:
    identifiers = [party.tax_id for spec in _all_specs() for party in _all_parties(spec)]
    assert len(identifiers) == 6
    assert len(set(identifiers)) == 6


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
    assert key[6:20] == spec.issuer.tax_id
    assert key[2:6] == f"{spec.issue_datetime:%y%m}"
    assert key[20:22] == "55"
    assert key[22:25] == f"{spec.series:03d}"
    assert key[25:34] == f"{spec.number:09d}"
    assert key[34] == "1"
    assert key[35:43] == f"{spec.numeric_code:08d}"
    assert access_key_check_digit(key[:43]) == int(key[43])
    assert spec.issue_datetime.utcoffset() == timedelta(hours=-3)


def test_only_case_003_has_an_alphanumeric_issuer_cnpj() -> None:
    specs = {case_id: build_case_spec(MASTER_SEED, case_id) for case_id in CASE_IDS}
    assert any(ch.isalpha() for ch in specs["case-003"].issuer.tax_id)
    for case_id in ("case-001", "case-002"):
        assert specs[case_id].issuer.tax_id.isdigit()
        assert specs[case_id].recipient.tax_id.isdigit()
    assert specs["case-003"].recipient_tax_id_kind == "cpf"
    assert len(specs["case-003"].recipient.tax_id) == 11


def test_item_counts_follow_the_profiles() -> None:
    from carimbo_datagen.spec import MULTIPAGE_ITEMS

    assert len(build_case_spec(MASTER_SEED, "case-001").items) == 3
    assert len(build_case_spec(MASTER_SEED, "case-002").items) == 4
    assert len(build_case_spec(MASTER_SEED, "case-003").items) == MULTIPAGE_ITEMS


def test_item_totals_are_rounded_half_up_to_cents() -> None:
    for spec in _all_specs():
        total = sum(item.total for item in spec.items)
        assert spec.products_total == total
        assert spec.products_total.as_tuple().exponent == -2


def test_case_002_is_a_regime_normal_invoice_with_ipi_freight_discount_and_installments() -> None:
    spec = build_case_spec(MASTER_SEED, "case-002")
    assert spec.crt == 3
    assert spec.regime == "normal"
    assert [item.tax_code for item in spec.items] == ["00", "20", "00", "20"]
    assert [str(item.ipi_rate) for item in spec.items] == ["5.00", "10.00", "5.00", "10.00"]
    assert str(spec.freight) == "25.00"
    assert str(spec.discount) == "10.00"
    assert spec.recipient_ie is not None
    assert spec.issuer_ie is not None
    assert spec.issuer_ie.isdigit() and len(spec.issuer_ie) == 12
    assert [i.number for i in spec.installments] == ["001", "002"]
    issue_day = spec.issue_datetime.date()
    assert [(i.due_date - issue_day).days for i in spec.installments] == [30, 60]
    assert sum(i.amount for i in spec.installments) == spec.invoice_total


def test_totals_follow_the_vnf_formula_and_half_up_item_taxes() -> None:
    from decimal import ROUND_HALF_UP, Decimal

    cent = Decimal("0.01")
    for spec in _all_specs():
        assert spec.invoice_total == (
            spec.products_total - spec.discount + spec.freight + spec.ipi_total
        )
        for item in spec.items:
            assert item.ipi_amount == (item.total * item.ipi_rate / 100).quantize(
                cent, rounding=ROUND_HALF_UP
            )
            assert item.icms_amount == (item.icms_base * item.icms_rate / 100).quantize(
                cent, rounding=ROUND_HALF_UP
            )
    spec = build_case_spec(MASTER_SEED, "case-002")
    reduced = [item for item in spec.items if item.tax_code == "20"]
    assert reduced
    for item in reduced:
        assert item.icms_base == (item.total * (100 - Decimal("33.33")) / 100).quantize(
            cent, rounding=ROUND_HALF_UP
        )
        assert item.icms_base < item.total


def test_case_001_is_a_simples_invoice_on_csosn_101_with_no_installments() -> None:
    spec = build_case_spec(MASTER_SEED, "case-001")
    assert spec.crt == 1
    assert spec.regime == "simples"
    assert {item.tax_code for item in spec.items} == {"101"}
    assert {item.cst_csosn for item in spec.items} == {"0101"}
    assert all(str(item.credit_rate) == "3.10" for item in spec.items)
    assert all(item.icms_amount == 0 and item.icms_base == 0 for item in spec.items)
    assert all(item.ipi_rate == 0 for item in spec.items)
    assert spec.installments == ()
    assert spec.freight == 0 and spec.discount == 0
    assert spec.recipient_tax_id_kind == "cnpj"
    assert spec.recipient_ie is None
    assert spec.issuer_ie is not None and spec.issuer_ie.isdigit() and len(spec.issuer_ie) == 12


def test_case_003_is_the_multi_page_alphanumeric_issuer_and_cpf_recipient_case() -> None:
    spec = build_case_spec(MASTER_SEED, "case-003")
    assert any(ch.isalpha() for ch in spec.issuer.tax_id)
    assert is_valid_cnpj(spec.issuer.tax_id)
    assert spec.issuer_ie == "ISENTO"
    assert spec.recipient_tax_id_kind == "cpf"
    assert is_valid_cpf(spec.recipient.tax_id)
    assert spec.recipient_ie is None
    assert "SINTETICA" in spec.recipient.name
    assert [item.tax_code for item in spec.items[:4]] == ["102", "400", "102", "400"]
    assert {item.cst_csosn for item in spec.items} == {"0102", "0400"}
    assert spec.installments == ()
    assert all(item.ipi_rate == 0 for item in spec.items)
    assert spec.freight == 0 and spec.discount == 0


def test_cfop_first_digit_follows_the_party_ufs() -> None:
    for spec in _all_specs():
        digit = "5" if spec.issuer.uf == spec.recipient.uf else "6"
        assert spec.id_dest == ("1" if digit == "5" else "2")
        suffix = "101" if spec.case_id == "case-002" else "102"
        assert {item.cfop for item in spec.items} == {digit + suffix}


def test_decision_buyer_cnpj_is_valid_alphanumeric_and_absent_from_every_sample() -> None:
    policy = json.loads((_REPO_ROOT / "data" / "decision" / "policy.json").read_text("utf-8"))
    buyer = policy["buyer_cnpj"]
    assert is_valid_cnpj(buyer)
    assert any(ch.isalpha() for ch in buyer)
    assert _nfelib_sample_texts(), "no nfelib samples found: the absence check would pass vacuously"
    assert all(buyer not in text for text in _nfelib_sample_texts())
    skeleton_xml = sorted((_REPO_ROOT / "data" / "skeleton").glob("*.xml"))
    assert skeleton_xml, "no skeleton XML found: the absence check would pass vacuously"
    assert all(buyer not in path.read_text("utf-8") for path in skeleton_xml)


# --- the dataset specs (plan 04-05): the same evidence over the 90 quota-table bases -----------


@cache
def _dataset_specs() -> tuple[CaseSpec, ...]:
    return tuple(build_dataset_spec(DATASET_SEED, base) for base in plan_cases().bases)


def test_dataset_party_names_carry_the_synthetic_marker() -> None:
    assert len(_dataset_specs()) == 90
    for spec in _dataset_specs():
        for party in _all_parties(spec):
            assert "SINTETICA" in party.name, spec.case_id
            assert len(party.name) <= 60, spec.case_id


def test_dataset_identifiers_are_valid_and_absent_from_every_nfelib_sample() -> None:
    sample_cnpjs = {c for text in _nfelib_sample_texts() for c in _CNPJ_TAG.findall(text)}
    sample_cpfs = {c for text in _nfelib_sample_texts() for c in _CPF_TAG.findall(text)}
    sample_keys = {k for text in _nfelib_sample_texts() for k in _KEY_ID.findall(text)}
    assert sample_cnpjs and sample_cpfs and sample_keys, "the collision lists would be vacuous"
    for spec in _dataset_specs():
        assert is_valid_cnpj(spec.issuer.tax_id), spec.case_id
        assert spec.issuer.tax_id not in sample_cnpjs, spec.case_id
        if spec.recipient_tax_id_kind == "cpf":
            assert is_valid_cpf(spec.recipient.tax_id), spec.case_id
            assert spec.recipient.tax_id not in sample_cpfs, spec.case_id
        else:
            assert is_valid_cnpj(spec.recipient.tax_id), spec.case_id
            assert spec.recipient.tax_id not in sample_cnpjs, spec.case_id
        assert re.fullmatch(ACCESS_KEY_PATTERN, spec.access_key), spec.case_id
        assert access_key_check_digit(spec.access_key[:43]) == int(spec.access_key[43])
        assert spec.access_key not in sample_keys, spec.case_id


def test_no_dataset_cnpj_cpf_or_access_key_repeats_across_bases() -> None:
    tax_ids = [party.tax_id for spec in _dataset_specs() for party in _all_parties(spec)]
    assert len(tax_ids) == 180
    assert len(set(tax_ids)) == len(tax_ids)
    keys = [spec.access_key for spec in _dataset_specs()]
    assert len(set(keys)) == len(keys)


def test_dataset_issuer_and_recipient_differ_in_tax_id_and_name() -> None:
    for spec in _dataset_specs():
        assert spec.issuer.tax_id != spec.recipient.tax_id, spec.case_id
        assert spec.issuer.name != spec.recipient.name, spec.case_id


def test_a_dataset_spec_built_alone_equals_the_same_spec_built_after_all_the_others() -> None:
    bases = plan_cases().bases
    alone = build_dataset_spec(DATASET_SEED, bases[41])
    for base in reversed(bases):
        build_dataset_spec(DATASET_SEED, base)
    assert build_dataset_spec(DATASET_SEED, bases[41]) == alone == _dataset_specs()[41]


def test_a_different_master_seed_changes_every_dataset_spec() -> None:
    for base, spec in zip(plan_cases().bases, _dataset_specs(), strict=True):
        assert build_dataset_spec(DATASET_SEED + 1, base) != spec, base.case_id
