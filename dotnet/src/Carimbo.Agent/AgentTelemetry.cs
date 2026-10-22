using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using Carimbo.Domain;

namespace Carimbo.Agent;

/// <summary>
/// The span vocabulary of an agent run (D-31 extended): the <see cref="ActivitySource"/>, every attribute name an
/// agent or tool span may set, and the allowlist the tests hold it to.
/// </summary>
/// <remarks>
/// The <c>gen_ai.*</c> names (<c>invoke_agent</c>, <c>execute_tool</c>, <c>gen_ai.agent.name</c>,
/// <c>gen_ai.tool.name</c>, <c>gen_ai.operation.name</c>) follow github.com/open-telemetry/semantic-conventions-genai
/// (docs/gen-ai/gen-ai-spans.md and docs/registry/attributes/gen-ai.md), read on 2026-10-22. That specification has
/// status Development and no published release, so there is no version to pin: these constants plus the allowlist
/// test in DecisionTracingTests are the pin, as for <c>Carimbo.Llm.LlmTelemetry</c>. The <c>carimbo.agent.*</c> names
/// are ours.
/// Span kinds: <c>invoke_agent</c> is Internal (the agent runs in process). The specification says an
/// <c>execute_tool</c> span SHOULD be Internal; ours is Client because every tool call is an MCP request to the Tools
/// host, a separate process, and Client is the OpenTelemetry kind for an outbound call (phase RESEARCH Pattern 8).
/// Content-bearing attributes of the same specification (tool call arguments and results, messages, system
/// instructions) are deliberately absent and must never be added: no case id, argument, result, reason, tool call id
/// or model text ever reaches a tag or a span name (T-03.1-34).
/// </remarks>
public static class AgentTelemetry
{
    /// <summary>The source name a tracer provider listens to.</summary>
    public const string SourceName = "Carimbo.Agent";

    /// <summary>The agent's name on its spans: <c>gen_ai.agent.name</c> and the tail of the span name.</summary>
    public const string AgentName = "carimbo-decision";

    public const string OperationName = "gen_ai.operation.name";
    public const string AgentNameTag = "gen_ai.agent.name";
    public const string ToolName = "gen_ai.tool.name";

    public const string MaxSteps = "carimbo.agent.max_steps";
    public const string Steps = "carimbo.agent.steps";
    public const string Status = "carimbo.agent.status";
    public const string Outcome = "carimbo.agent.outcome";
    public const string Cause = "carimbo.agent.cause";
    public const string DecidedBy = "carimbo.agent.decided_by";
    public const string GuardOverridden = "carimbo.agent.guard_overridden";
    public const string ToolSource = "carimbo.agent.tool_source";
    public const string ToolOutcome = "carimbo.agent.tool_outcome";

    public const string ErrorType = "error.type";

    /// <summary>The one source of every agent and tool span.</summary>
    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>Every attribute key an agent or tool span may carry. Anything else is a bug (T-03.1-34).</summary>
    public static FrozenSet<string> AllowedTags { get; } = new[]
    {
        OperationName,
        AgentNameTag,
        ToolName,
        MaxSteps,
        Steps,
        Status,
        Outcome,
        Cause,
        DecidedBy,
        GuardOverridden,
        ToolSource,
        ToolOutcome,
        ErrorType,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The wire (snake_case) name of an enum value, as <see cref="Wire.Options"/> writes it.</summary>
    internal static string WireName<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value, Wire.Options).Trim('"');

    /// <summary>Starts the span around one whole run; null when nothing records the source.</summary>
    internal static Activity? StartRun()
    {
        var activity = Source.StartActivity($"invoke_agent {AgentName}", ActivityKind.Internal);
        activity?.SetTag(OperationName, "invoke_agent");
        activity?.SetTag(AgentNameTag, AgentName);
        return activity;
    }

    /// <summary>Records how a run ended: counts, status and the decision's wire names only, never its text.</summary>
    internal static void DescribeRun(Activity? activity, AgentResult result)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(MaxSteps, result.MaxSteps);
        activity.SetTag(Steps, result.Steps.Count);
        activity.SetTag(Status, WireName(result.Status));
        if (result.Decision is { } decision)
        {
            activity.SetTag(Outcome, WireName(decision.Outcome));
            activity.SetTag(Cause, WireName(decision.Cause));
        }

        if (result.DecidedBy is { } decidedBy)
        {
            activity.SetTag(DecidedBy, WireName(decidedBy));
            activity.SetTag(GuardOverridden, result.Guard?.Overridden ?? false);
        }

        if (result.Status != AgentStatus.Decided)
        {
            // The kind is a fixed vocabulary; the failure message may carry provider text and is never recorded.
            activity.SetStatus(ActivityStatusCode.Error);
            activity.SetTag(ErrorType, result.Failure?.Kind ?? WireName(result.Status));
        }
    }

    /// <summary>Marks a span as ended by an exception, naming only its kind (a message could carry content).</summary>
    internal static void Fail(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(ErrorType, exception is OperationCanceledException ? "cancelled" : exception.GetType().Name);
    }
}
