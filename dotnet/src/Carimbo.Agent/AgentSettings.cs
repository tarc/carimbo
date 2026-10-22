using Carimbo.Llm;

namespace Carimbo.Agent;

/// <summary>
/// Per-host agent settings, bound from configuration section <c>Agent</c> (phase D-12, D-15). The step budget is
/// configuration only and bounded like <c>Extraction:MaxRepairs</c>; the Api validates it against
/// <see cref="MaxStepsLimit"/> at startup.
/// </summary>
public sealed record AgentSettings
{
    /// <summary>The default agent model (phase D-15).</summary>
    public string Model { get; init; } = "claude-haiku-4-5";

    /// <summary>How many model calls one run may make: one step is one model call (phase D-12).</summary>
    public int MaxSteps { get; init; } = 6;

    /// <summary>Output cap per step, including any thinking tokens.</summary>
    public int MaxTokens { get; init; } = 8192;

    /// <summary>The Tools host MCP endpoint. Loopback only; the Api checks this at startup.</summary>
    public string ToolsUrl { get; init; } = "http://127.0.0.1:5090/mcp";

    /// <summary>How long one tool call may take before it counts as a tool failure.</summary>
    public int ToolCallTimeoutSeconds { get; init; } = 30;

    /// <summary>The hard ceiling on <see cref="MaxSteps"/>, shared by the host setting and its validation.</summary>
    public const int MaxStepsLimit = 10;
}

/// <summary>
/// Per-request choices for one decision run: how the response cache treats its calls, a salt that tells deliberate
/// replicates apart, and the model. There is deliberately no step-budget override (phase D-12) and no prompt
/// override (D-36).
/// </summary>
public sealed record AgentOverrides
{
    public static AgentOverrides None { get; } = new();

    public LlmCacheMode? CacheMode { get; init; }

    public string? ReplicateSalt { get; init; }

    /// <summary>A model id to use instead of <see cref="AgentSettings.Model"/>; the endpoint allows priced models only.</summary>
    public string? Model { get; init; }
}
