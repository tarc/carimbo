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
FIXTURE = REPO_ROOT / "data" / "vectors" / "valid-invoice.json"
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
    """The hand-checked shared fixture (also read by the xUnit tests), freshly loaded."""
    loaded = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert isinstance(loaded, dict)
    return loaded


def _models() -> Any:
    from carimbo_models import generated

    return generated


def _validate(payload: dict[str, Any]) -> Any:
    return _models().Invoice.model_validate_json(json.dumps(payload), strict=True)


def test_valid_invoice_is_accepted_in_strict_json_mode() -> None:
    invoice = _validate(_valid_invoice())
    assert invoice.number == 123
    assert invoice.totals.invoice_total == "155.00"
    assert invoice.issue_date.isoformat() == "2026-03-15"
    assert len(invoice.items) == 2
    assert len(invoice.installments) == 2
    assert invoice.recipient.ie is None
    assert invoice.items[0].quantity == "2.0000"


def test_issuer_is_a_party_and_recipient_is_a_recipient() -> None:
    models = _models()
    invoice = _validate(_valid_invoice())
    assert type(invoice.issuer) is models.Party
    assert type(invoice.recipient) is models.Recipient
    assert invoice.recipient.tax_id_kind is models.TaxIdKind.cnpj


def test_a_cpf_recipient_is_accepted() -> None:
    payload = _valid_invoice()
    payload["recipient"]["tax_id"] = "52998224725"
    payload["recipient"]["tax_id_kind"] = "cpf"
    assert _validate(payload).recipient.tax_id_kind.value == "cpf"


def test_extra_top_level_field_is_rejected() -> None:
    payload = {**_valid_invoice(), "surprise": "x"}
    with pytest.raises(ValidationError):
        _validate(payload)


def test_phase_1_total_amount_member_is_rejected() -> None:
    payload = {**_valid_invoice(), "total_amount": "155.00"}
    with pytest.raises(ValidationError):
        _validate(payload)


def test_extra_party_field_is_rejected() -> None:
    payload = _valid_invoice()
    payload["issuer"]["trade_name"] = "x"
    with pytest.raises(ValidationError):
        _validate(payload)


def test_extra_member_inside_an_item_is_rejected() -> None:
    payload = _valid_invoice()
    payload["items"][1]["discount"] = "1.00"
    with pytest.raises(ValidationError):
        _validate(payload)


@pytest.mark.parametrize("total", ["12,34", "12.3", "12.345", "R$ 12.34", ""])
def test_malformed_money_is_rejected(total: str) -> None:
    payload = _valid_invoice()
    payload["totals"]["invoice_total"] = total
    with pytest.raises(ValidationError):
        _validate(payload)


@pytest.mark.parametrize("quantity", ["2.00", "2,0000", "-1.0000", ""])
def test_malformed_quantity_is_rejected(quantity: str) -> None:
    payload = _valid_invoice()
    payload["items"][0]["quantity"] = quantity
    with pytest.raises(ValidationError):
        _validate(payload)


@pytest.mark.parametrize("rate", ["-1.00", "18.0", "18.000", "18%"])
def test_malformed_rate_is_rejected(rate: str) -> None:
    payload = _valid_invoice()
    payload["items"][0]["icms_rate"] = rate
    with pytest.raises(ValidationError):
        _validate(payload)


def test_access_key_of_43_characters_is_rejected() -> None:
    payload = _valid_invoice()
    payload["access_key"] = payload["access_key"][:43]
    with pytest.raises(ValidationError):
        _validate(payload)


def test_cnpj_with_a_dot_is_rejected() -> None:
    payload = _valid_invoice()
    payload["recipient"]["tax_id"] = "BBBBBBBBBBB.02"
    with pytest.raises(ValidationError):
        _validate(payload)


@pytest.mark.parametrize("tax_id", ["", "1234567890", "123456789012", "529.982.247-25"])
def test_recipient_tax_id_must_be_11_digits_or_14_characters(tax_id: str) -> None:
    payload = _valid_invoice()
    payload["recipient"]["tax_id"] = tax_id
    with pytest.raises(ValidationError):
        _validate(payload)


def test_item_codes_must_match_their_patterns() -> None:
    payload = _valid_invoice()
    payload["items"][1]["ncm"] = "7318150"
    with pytest.raises(ValidationError):
        _validate(payload)


def test_strict_mode_rejects_a_stringly_typed_number() -> None:
    with pytest.raises(ValidationError):
        _validate({**_valid_invoice(), "number": "123"})


def test_generated_models_are_fresh(tmp_path: Path) -> None:
    assert GENERATED.is_file(), "python/src/carimbo_models/generated.py is missing"
    out = tmp_path / "generated.py"
    # The formatters resolve ruff settings from the output directory; give it the project config so
    # the wrap width (line-length 100) matches the in-repo `just schema` run.
    (tmp_path / "pyproject.toml").write_bytes(
        (REPO_ROOT / "python" / "pyproject.toml").read_bytes()
    )
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
