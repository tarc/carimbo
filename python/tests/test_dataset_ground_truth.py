"""The DATA-04 ground-truth gate over every case of ``data/dataset`` (CONTEXT D-18).

The mapped ground truth of each manifest case is validated against the exported JSON Schema
(Draft 2020-12) and strictly against the generated Pydantic model. Both parameter lists come from
the manifest, so a failure names the case id and nothing is skipped. The .NET side of the gate is
``DatasetGroundTruthGateTests``.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

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
