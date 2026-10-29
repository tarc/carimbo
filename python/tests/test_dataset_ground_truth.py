"""The DATA-04 ground-truth gate over every case of ``data/dataset`` (CONTEXT D-18).

The mapped ground truth of each manifest case is validated against the exported JSON Schema
(Draft 2020-12) and strictly against the generated Pydantic model. Both parameter lists come from
the manifest, so a failure names the case id and nothing is skipped. The .NET side of the gate is
``DatasetGroundTruthGateTests``.
"""

from __future__ import annotations

import hashlib
import json
import xml.etree.ElementTree as ET
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from carimbo_datagen.cli import _expected
from carimbo_datagen.dataset_spec import build_dataset_spec
from carimbo_datagen.quota import DATASET_SEED, plan_cases
from carimbo_evals.grader import load_schema_validator
from carimbo_evals.ground_truth import invoice_from_xml
from carimbo_models.generated import Invoice

ROOT = Path(__file__).resolve().parents[2]
DATASET = ROOT / "data" / "dataset"
SCHEMA_PATH = ROOT / "schema" / "invoice.schema.json"

MANIFEST: dict[str, Any] = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
CASES: dict[str, dict[str, Any]] = {entry["case_id"]: entry for entry in MANIFEST["cases"]}
CASE_IDS: list[str] = [entry["case_id"] for entry in MANIFEST["cases"]]


def _xml_path(case_id: str) -> Path:
    return DATASET / CASES[case_id]["xml"]


def test_the_manifest_lists_cases() -> None:
    assert MANIFEST["dataset"] == "dataset"
    assert CASE_IDS


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_mapped_ground_truth_validates_against_the_exported_json_schema(case_id: str) -> None:
    validator = load_schema_validator(SCHEMA_PATH)
    invoice = invoice_from_xml(_xml_path(case_id))
    errors = sorted(validator.iter_errors(invoice), key=lambda error: list(error.absolute_path))
    listing = "\n".join(
        f"  /{'/'.join(str(part) for part in error.absolute_path)}: {error.message}"
        for error in errors
    )
    assert not errors, f"{case_id}: {len(errors)} schema error(s):\n{listing}"


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_mapped_ground_truth_validates_strictly_against_the_generated_model(case_id: str) -> None:
    invoice = invoice_from_xml(_xml_path(case_id))
    Invoice.model_validate_json(json.dumps(invoice), strict=True)


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_mapped_invoice_agrees_with_the_manifest_expected_block(case_id: str) -> None:
    expected = CASES[case_id]["expected"]
    invoice = invoice_from_xml(_xml_path(case_id))
    assert len(invoice["items"]) == expected["item_count"]
    assert len(invoice["installments"]) == expected["installment_count"]
    assert invoice["totals"]["invoice_total"] == expected["invoice_total"]
    assert invoice["issuer"]["cnpj"] == expected["issuer_cnpj"]
    assert invoice["recipient"]["tax_id"] == expected["recipient_tax_id"]
    assert invoice["recipient"]["tax_id_kind"] == expected["recipient_tax_id_kind"]
    code_length = {len(item["cst_csosn"]) for item in invoice["items"]}
    assert code_length == ({4} if expected["regime"] == "simples" else {3})


def test_expected_blocks_are_recomputed_from_the_quota_plan_never_from_the_xml() -> None:
    plan = plan_cases()
    planned = [base.case_id for base in plan.bases] + [twin.case_id for twin in plan.twins]
    assert sorted(CASE_IDS) == sorted(planned), "the manifest and the quota plan list other cases"
    for base in plan.bases:
        spec = build_dataset_spec(DATASET_SEED, base)
        assert CASES[base.case_id]["expected"] == _expected(spec), base.case_id
    for twin in plan.twins:
        assert CASES[twin.case_id]["expected"] == CASES[twin.base_case]["expected"], twin.case_id


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_twin_xml_is_a_byte_copy_of_its_base_xml(case_id: str) -> None:
    base = CASES[case_id]["base_case"]
    assert _xml_path(case_id).read_bytes() == _xml_path(base).read_bytes()


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_files_match_the_manifest_hashes(case_id: str) -> None:
    entry = CASES[case_id]
    assert hashlib.sha256((DATASET / entry["xml"]).read_bytes()).hexdigest() == entry["xml_sha256"]
    assert hashlib.sha256((DATASET / entry["pdf"]).read_bytes()).hexdigest() == entry["pdf_sha256"]


def test_manifest_case_set_equals_the_files_on_disk() -> None:
    names = {path.name for path in DATASET.iterdir()}
    assert "manifest.json" in names
    xml_stems = {Path(name).stem for name in names if name.endswith(".xml")}
    pdf_stems = {Path(name).stem for name in names if name.endswith(".pdf")}
    assert xml_stems == set(CASE_IDS)
    assert pdf_stems == set(CASE_IDS)
    assert len(CASE_IDS) == len(set(CASE_IDS))
    assert names == {"manifest.json"} | {f"{c}.xml" for c in CASE_IDS} | {
        f"{c}.pdf" for c in CASE_IDS
    }


def test_as_of_date_is_later_than_every_issue_date() -> None:
    as_of = date.fromisoformat(MANIFEST["as_of_date"])
    for case_id in CASE_IDS:
        assert date.fromisoformat(invoice_from_xml(_xml_path(case_id))["issue_date"]) < as_of


# --- exact arithmetic over every base XML (DATA-04, SEFAZ 535 and 537) -------------------------

_NS = {"n": "http://www.portalfiscal.inf.br/nfe"}
_BASE_CASE_IDS = [c for c in CASE_IDS if CASES[c]["rendering"] == "text"]
_ICMS = ".//n:det/n:imposto/n:ICMS/*"


def _sum(root: ET.Element, path: str) -> Decimal:
    return sum((Decimal(e.text or "0") for e in root.findall(path, _NS)), Decimal(0))


def _header(root: ET.Element, tag: str) -> Decimal:
    return Decimal(root.findtext(f".//n:ICMSTot/n:{tag}", "0", _NS))


def test_there_are_ninety_base_xml_cases() -> None:
    assert len(_BASE_CASE_IDS) == 90


@pytest.mark.parametrize("case_id", _BASE_CASE_IDS)
def test_header_totals_equal_the_exact_item_sums_and_vnf_and_installments_are_exact(
    case_id: str,
) -> None:
    root = ET.parse(_xml_path(case_id)).getroot()
    item_sums = {
        "vProd": _sum(root, ".//n:det/n:prod/n:vProd"),
        "vBC": _sum(root, f"{_ICMS}/n:vBC"),
        "vICMS": _sum(root, f"{_ICMS}/n:vICMS"),
        "vBCST": _sum(root, f"{_ICMS}/n:vBCST"),
        "vST": _sum(root, f"{_ICMS}/n:vICMSST"),
        "vIPI": _sum(root, ".//n:det/n:imposto/n:IPI/n:IPITrib/n:vIPI"),
        "vDesc": _sum(root, ".//n:det/n:prod/n:vDesc"),
        "vFrete": _sum(root, ".//n:det/n:prod/n:vFrete"),
    }
    for tag, expected in item_sums.items():
        assert _header(root, tag) == expected, (case_id, tag)
    total = (
        _header(root, "vProd")
        - _header(root, "vDesc")
        + _header(root, "vST")
        + _header(root, "vFrete")
        + _header(root, "vSeg")
        + _header(root, "vOutro")
        + _header(root, "vIPI")
    )
    assert _header(root, "vNF") == total, case_id
    duplicates = root.findall(".//n:cobr/n:dup/n:vDup", _NS)
    if duplicates:
        assert sum((Decimal(e.text or "0") for e in duplicates), Decimal(0)) == total, case_id


def _is_adjacent_swap(first: str, second: str) -> bool:
    if len(first) != len(second):
        return False
    diff = [i for i, (a, b) in enumerate(zip(first, second, strict=True)) if a != b]
    return (
        len(diff) == 2
        and diff[1] == diff[0] + 1
        and first[diff[0]] == second[diff[1]]
        and first[diff[1]] == second[diff[0]]
    )


def _mapped_pairs(items: list[dict[str, Any]]) -> list[int]:
    """Adjacent mapped items that repeat a row and swap two adjacent digits of one value."""
    pairs: list[int] = []
    position = 0
    while position < len(items) - 1:
        first, second = items[position], items[position + 1]
        same = all(first[key] == second[key] for key in ("description", "ncm", "unit", "cfop"))
        swapped = (
            _is_adjacent_swap(first["quantity"], second["quantity"])
            and first["unit_price"] == second["unit_price"]
        ) or (
            _is_adjacent_swap(first["unit_price"], second["unit_price"])
            and first["quantity"] == second["quantity"]
        )
        if same and swapped and first["code"] != second["code"]:
            pairs.append(position)
            position += 2
        else:
            position += 1
    return pairs


def test_confusable_pairs_map_to_two_consecutive_items_with_equal_descriptions() -> None:
    confusable = [b for b in plan_cases().bases if "confusable" in b.traits]
    assert len(confusable) == 18
    for base in confusable:
        invoice = invoice_from_xml(_xml_path(base.case_id))
        items = invoice["items"]
        assert len(items) == CASES[base.case_id]["expected"]["item_count"], base.case_id
        assert len(items) >= 4, base.case_id
        pairs = _mapped_pairs(items)
        assert len(pairs) >= 2, base.case_id
        for start in pairs:
            first, second = items[start], items[start + 1]
            assert first["description"] == second["description"], base.case_id
            assert (first["quantity"], first["unit_price"]) != (
                second["quantity"],
                second["unit_price"],
            ), base.case_id


def test_non_confusable_bases_map_to_no_pairs() -> None:
    for base in plan_cases().bases:
        if "confusable" not in base.traits:
            assert not _mapped_pairs(invoice_from_xml(_xml_path(base.case_id))["items"]), (
                base.case_id
            )
