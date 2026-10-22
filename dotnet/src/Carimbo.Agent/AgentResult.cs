using Carimbo.Domain;
using Carimbo.Llm;

namespace Carimbo.Agent;

/// <summary>
/// What a run concluded. <see cref="Decided"/> means <see cref="AgentResult.Decision"/> is set; every other status is
/// a typed failure with no decision (D-35, user decision 1): a model that fails is not a wrong answer.
/// </summary>
public enum AgentStatus
{
    Decided,
    Refused,
    Truncated,
    SchemaInvalid,
    InfrastructureFailure,
}

/// <summary>Who made the final decision of a <see cref="AgentStatus.Decided"/> run.</summary>
public enum AgentDecidedBy
{
    /// <summary>The model's own decision stands.</summary>
    Model,

    /// <summary>The status guard replaced it with escalate / extraction_not_success.</summary>
    Guard,

    /// <summary>A tool failed; code escalated with cause tool_failed.</summary>
    ToolFailure,

    /// <summary>The step budget ran out; code escalated with cause step_budget_exhausted.</summary>
    StepBudget,
}

/// <summary>
/// One tool call. <paramref name="Source"/> is "model" or "guard"; <paramref name="Outcome"/> is "ok", "error" or
/// "skipped" (requested but never sent to the toolbox).
/// </summary>
public sealed record AgentToolCall(
    string? Id,
    string Name,
    string ArgumentsJson,
    string Source,
    string Outcome,
    string? ErrorKind,
    string? ErrorMessage,
    TimeSpan? Latency);

/// <summary>A model failure recorded on a step or on the run. <paramref name="Kind"/> is snake_case.</summary>
public sealed record AgentFailure(string Kind, int? HttpStatus, string? RequestId, string Message);

/// <summary>One model call and the tool calls it asked for. The response is null when the call produced none.</summary>
public sealed record AgentStep(
    int Index,
    LlmResponse? Response,
    AgentFailure? Failure,
    IReadOnlyList<AgentToolCall> ToolCalls);

/// <summary>The status guard's result: the status it read, whether it changed the model's verdict, and its own call.</summary>
public sealed record AgentGuard(string ExtractionStatus, bool Overridden, AgentToolCall ToolCall);

/// <summary>The tool failure behind a tool_failed escalation. <paramref name="Source"/> is "model", "guard" or "list".</summary>
public sealed record AgentToolFailure(string Tool, string Source, string ErrorKind, string ErrorMessage);

/// <summary>
/// The result of one run. <see cref="ModelDecision"/> is the model's own parsed decision when there was one, kept
/// when the guard or a tool failure replaced it. <see cref="RawOutput"/> is the last model text, when there is one.
/// </summary>
public sealed record AgentResult(
    AgentStatus Status,
    Decision? Decision,
    Decision? ModelDecision,
    AgentDecidedBy? DecidedBy,
    AgentGuard? Guard,
    AgentToolFailure? ToolFailure,
    AgentFailure? Failure,
    string? RawOutput,
    IReadOnlyList<AgentStep> Steps,
    string Model,
    string PromptVersion,
    int MaxSteps,
    string SchemaSha256,
    string? ToolsSha256);
