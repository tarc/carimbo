"""The DATA-04 ground-truth gate over every case of ``data/dataset`` (CONTEXT D-18).

The mapped ground truth of each manifest case is validated against the exported JSON Schema
(Draft 2020-12) and strictly against the generated Pydantic model. Both parameter lists come from
the manifest, so a failure names the case id and nothing is skipped. The .NET side of the gate is
``DatasetGroundTruthGateTests``.
"""

from __future__ import annotations

import hashlib
import json
from datetime import date
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
