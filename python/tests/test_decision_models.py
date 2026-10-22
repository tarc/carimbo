"""AGT-02: the Decision models are generated from the committed decision schema and stay fresh."""

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
GENERATED = REPO_ROOT / "python" / "src" / "carimbo_models" / "decision_generated.py"
INVOICE_GENERATED = REPO_ROOT / "python" / "src" / "carimbo_models" / "generated.py"

# The exact codegen flags; identical to `codegen_decision` in the justfile.
DECISION_CODEGEN_ARGS = [
    "--input",
    "schema/decision.schema.json",
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


def _decision_json() -> dict[str, Any]:
    """A Decision shaped exactly as the .NET Wire.Options writes it (member order included)."""
    return {
        "evidence": [
            {
                "source": "get_invoice",
                "ref": "recipient.tax_id",
                "value": "69979991000100",
            }
        ],
        "reason": "Recipient is not the buyer.",
        "outcome": "reject",
        "cause": "none",
    }


def _models() -> Any:
    from carimbo_models import decision_generated

    return decision_generated


def _validate(payload: dict[str, Any]) -> Any:
    return _models().Decision.model_validate_json(json.dumps(payload), strict=True)


def test_dotnet_shaped_decision_is_accepted_in_strict_json_mode() -> None:
    decision = _validate(_decision_json())
    assert decision.outcome.value == "reject"
    assert decision.cause.value == "none"
    assert decision.reason == "Recipient is not the buyer."
    assert len(decision.evidence) == 1
    assert decision.evidence[0].source == "get_invoice"
    assert decision.evidence[0].ref == "recipient.tax_id"


def test_an_empty_evidence_list_is_accepted() -> None:
    payload = {**_decision_json(), "evidence": [], "outcome": "escalate", "cause": "tool_failed"}
    decision = _validate(payload)
    assert decision.evidence == []
    assert decision.cause.value == "tool_failed"


def test_every_cause_name_is_accepted() -> None:
    for cause in (
        "none",
        "extraction_not_success",
        "policy_warning",
        "over_limit",
        "tool_failed",
        "step_budget_exhausted",
    ):
        assert _validate({**_decision_json(), "cause": cause}).cause.value == cause


def test_extra_top_level_member_is_rejected() -> None:
    with pytest.raises(ValidationError):
        _validate({**_decision_json(), "surprise": "x"})


def test_extra_member_inside_an_evidence_item_is_rejected() -> None:
    payload = _decision_json()
    payload["evidence"][0]["extra"] = "x"
    with pytest.raises(ValidationError):
        _validate(payload)


def test_outcome_in_the_wrong_case_is_rejected() -> None:
    with pytest.raises(ValidationError):
        _validate({**_decision_json(), "outcome": "APPROVE"})


def test_unknown_cause_is_rejected() -> None:
    with pytest.raises(ValidationError):
        _validate({**_decision_json(), "cause": "maybe"})


def test_a_null_evidence_element_is_rejected() -> None:
    with pytest.raises(ValidationError):
        _validate({**_decision_json(), "evidence": [None]})


def test_a_missing_cause_is_rejected() -> None:
    payload = _decision_json()
    del payload["cause"]
    with pytest.raises(ValidationError):
        _validate(payload)


def test_generating_the_decision_model_leaves_the_invoice_model_untouched() -> None:
    # The decision codegen writes its own file; generated.py is a separate artifact (git diff gate).
    assert INVOICE_GENERATED.is_file()
    assert "class Decision" not in INVOICE_GENERATED.read_text(encoding="utf-8")


def test_generated_decision_models_are_fresh(tmp_path: Path) -> None:
    assert GENERATED.is_file(), "python/src/carimbo_models/decision_generated.py is missing"
    out = tmp_path / "decision_generated.py"
    # The formatters resolve ruff settings from the output directory; give it the project config so
    # the wrap width (line-length 100) matches the in-repo `just schema` run.
    (tmp_path / "pyproject.toml").write_bytes(
        (REPO_ROOT / "python" / "pyproject.toml").read_bytes()
    )
    # Run from the repo root with the identical relative input path (the header embeds the name).
    env = {**os.environ, "PATH": f"{Path(sys.executable).parent}{os.pathsep}{os.environ['PATH']}"}
    result = subprocess.run(
        [
            sys.executable,
            "-m",
            "datamodel_code_generator",
            *DECISION_CODEGEN_ARGS,
            "--output",
            str(out),
        ],
        cwd=REPO_ROOT,
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, f"codegen failed:\n{result.stdout}\n{result.stderr}"
    assert out.read_bytes() == GENERATED.read_bytes(), (
        "python/src/carimbo_models/decision_generated.py is stale relative to "
        "schema/decision.schema.json. Regenerate it with `just schema`."
    )
