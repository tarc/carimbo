"""Hand-written models of eval contract decision-1 (D-36).

Strict like the extraction models (``extra="forbid"``): an added, renamed or retyped member of the
endpoint's response fails pytest. Every member is required, and nullable where the endpoint writes
``null``. Money is a decimal string or ``None``. The generated ``Decision`` model validates the
decision objects.

The models are written from D-36 before the endpoint exists; plan 03.1-10's golden fixtures parsed
with them are the cross-stack check.
"""

from __future__ import annotations

from typing import Any, Literal

from carimbo_evals.contract import CacheInfo, CacheMode, Failure, Usage, _Strict
from carimbo_models.decision_generated import Decision

DecisionStatus = Literal[
    "decided", "refused", "truncated", "schema_invalid", "infrastructure_failure"
]
DecidedBy = Literal["model", "guard", "tool_failure", "step_budget"]
ToolSource = Literal["model", "guard"]
ToolOutcome = Literal["ok", "error", "skipped"]
FailureSource = Literal["model", "guard", "list"]


class DecisionOverrides(_Strict):
    """Every member is optional on the wire; the runner sends only the ones it was given."""

    model: str | None = None
    cache_mode: CacheMode | None = None
    replicate_salt: str | None = None


class DecisionRequest(_Strict):
    contract_version: Literal["decision-1"]
    case_id: str
    overrides: DecisionOverrides | None = None


class DecisionEffective(_Strict):
    model: str
    prompt_version: str
    max_steps: int
    schema_sha256: str
    tools_sha256: str | None
    pricing_version: str
    cache_mode: CacheMode
    replicate_salt: str | None


class ToolCall(_Strict):
    id: str | None
    name: str
    arguments: dict[str, Any]
    source: ToolSource
    outcome: ToolOutcome
    error_kind: str | None
    error_message: str | None
    latency_ms: int | None


class Guard(_Strict):
    extraction_status: str
    overridden: bool
    tool_call: ToolCall


class ToolFailure(_Strict):
    tool: str
    source: FailureSource
    error_kind: str
    error_message: str


class OutcomeBlock(_Strict):
    status: DecisionStatus
    decision: Decision | None
    model_decision: Decision | None
    decided_by: DecidedBy | None
    guard: Guard | None
    tool_failure: ToolFailure | None
    failure: Failure | None
    raw_output: str | None


class Step(_Strict):
    index: int
    usage: Usage | None
    cost_usd: str | None
    cost_warning: str | None
    incurred_cost_usd: str | None
    latency_ms: int | None
    stop_reason: str | None
    model_returned: str | None
    provider_message_id: str | None
    http_attempts: int | None
    cache: CacheInfo | None
    failure: Failure | None
    tool_calls: list[ToolCall]


class DecisionResponse(_Strict):
    contract_version: Literal["decision-1"]
    case_id: str
    trace_id: str
    effective: DecisionEffective
    outcome: OutcomeBlock
    steps: list[Step]
    usage: Usage
    cost_usd: str | None
    cost_warning: str | None
    incurred_cost_usd: str | None
    latency_ms: int
