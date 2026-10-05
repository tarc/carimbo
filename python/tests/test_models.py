"""DOM-07: the Pydantic models are generated from the committed canonical schema and stay fresh."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import pytest
from pydantic import ValidationError

REPO_ROOT = Path(__file__).resolve().parents[2]
GENERATED = REPO_ROOT / "python" / "src" / "carimbo_models" / "generated.py"

# The exact codegen flags; identical to `just schema` (plan 01-13) and to the research command.
CODEGEN_ARGS = [
    "--input",
    "schema/invoice.schema.json",
    "--input-file-type",
    "jsonschema",
    "--output-model-type",
    "pydantic_v2.BaseModel",
    "--use-annotated",
    "--field-constraints",
    "--use-standard-collections",
    "--use-union-operator",
    "--target-python-version",
    "3.12",
    "--use-title-as-name",
    "--reuse-model",
    "--disable-timestamp",
    "--formatters",
    "ruff-format",
    "ruff-check",
]


def _valid_invoice() -> dict[str, Any]:
    return {
        "access_key": "261000AAAAAAAAAAAA" + "1" * 26,
        "number": 1001,
        "series": 1,
        "issue_date": "2026-03-15",
        "issuer": {"cnpj": "AAAAAAAAAAAA01", "name": "EMPRESA SINTETICA EMISSORA LTDA"},
        "recipient": {"cnpj": "BBBBBBBBBBBB02", "name": "EMPRESA SINTETICA DESTINATARIA SA"},
        "total_amount": "1234.50",
    }


def _models() -> Any:
    from carimbo_models import generated

    return generated


def _validate(payload: dict[str, Any]) -> Any:
    return _models().Invoice.model_validate_json(json.dumps(payload), strict=True)


def test_valid_invoice_is_accepted_in_strict_json_mode() -> None:
    invoice = _validate(_valid_invoice())
    assert invoice.number == 1001
    assert invoice.total_amount == "1234.50"
    assert invoice.issue_date.isoformat() == "2026-03-15"


def test_issuer_and_recipient_share_the_party_class() -> None:
    models = _models()
    invoice = _validate(_valid_invoice())
    assert type(invoice.issuer) is type(invoice.recipient) is models.Party


def test_extra_top_level_field_is_rejected() -> None:
    payload = {**_valid_invoice(), "surprise": "x"}
    with pytest.raises(ValidationError):
        _validate(payload)


def test_extra_party_field_is_rejected() -> None:
    payload = _valid_invoice()
    payload["issuer"]["trade_name"] = "x"
    with pytest.raises(ValidationError):
        _validate(payload)


@pytest.mark.parametrize("total", ["12,34", "12.3", "12.345", "R$ 12.34", ""])
def test_malformed_money_is_rejected(total: str) -> None:
    with pytest.raises(ValidationError):
        _validate({**_valid_invoice(), "total_amount": total})


def test_access_key_of_43_characters_is_rejected() -> None:
    with pytest.raises(ValidationError):
        _validate({**_valid_invoice(), "access_key": ("261000AAAAAAAAAAAA" + "1" * 26)[:43]})


def test_cnpj_with_a_dot_is_rejected() -> None:
    payload = _valid_invoice()
    payload["recipient"]["cnpj"] = "BBBBBBBBBBB.02"
    with pytest.raises(ValidationError):
        _validate(payload)


def test_strict_mode_rejects_a_stringly_typed_number() -> None:
    with pytest.raises(ValidationError):
        _validate({**_valid_invoice(), "number": "1001"})


def test_generated_models_are_fresh(tmp_path: Path) -> None:
    assert GENERATED.is_file(), "python/src/carimbo_models/generated.py is missing"
    out = tmp_path / "generated.py"
    # Run from the repo root with the identical relative input path (the header embeds the name).
    env = {**os.environ, "PATH": f"{Path(sys.executable).parent}{os.pathsep}{os.environ['PATH']}"}
    result = subprocess.run(
        [sys.executable, "-m", "datamodel_code_generator", *CODEGEN_ARGS, "--output", str(out)],
        cwd=REPO_ROOT,
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, f"codegen failed:\n{result.stdout}\n{result.stderr}"
    assert out.read_bytes() == GENERATED.read_bytes(), (
        "python/src/carimbo_models/generated.py is stale relative to schema/invoice.schema.json. "
        "Regenerate it with `just schema` (recipe added in 01-13) or: "
        "uv run --project python datamodel-codegen " + " ".join(CODEGEN_ARGS) + " "
        "--output python/src/carimbo_models/generated.py"
    )
