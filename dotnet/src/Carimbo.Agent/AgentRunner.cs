using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Llm;

namespace Carimbo.Agent;

/// <summary>Decides one case. The Decision is returned and nothing else happens (D-09): no method acts on it.</summary>
public interface IDecisionAgent
{
    Task<AgentResult> DecideAsync(string caseId, AgentOverrides overrides, CancellationToken cancellationToken);
}

/// <summary>
/// The hand-written decision loop (AGT-02, D-08, phase D-12, D-13). One step is one <see cref="ILlmGateway"/> call:
/// the model asks for tools, the tools run over MCP in block order, the assistant blocks and one user turn of
/// results are appended, and the loop repeats until an answer without tool calls, which is the Decision. No agent
/// framework and no retry of any kind (D-28: the SDK is the only retry owner).
/// </summary>
public sealed class AgentRunner(ILlmGateway gateway, IAgentToolbox toolbox, AgentSettings settings, AgentContract contract)
    : IDecisionAgent
{
    public async Task<AgentResult> DecideAsync(string caseId, AgentOverrides overrides, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        // The whole run is one span, so the chat spans of TracingLlmGateway (which start from Activity.Current)
        // and the tool spans below become its children. Null when nothing records the source.
        using var activity = AgentTelemetry.StartRun();
        try
        {
            var result = await DecideCoreAsync(caseId, overrides, cancellationToken).ConfigureAwait(false);
            AgentTelemetry.DescribeRun(activity, result);
            return result;
        }
        catch (Exception ex)
        {
            AgentTelemetry.Fail(activity, ex);
            throw;
        }
    }

    private async Task<AgentResult> DecideCoreAsync(string caseId, AgentOverrides overrides, CancellationToken cancellationToken)
    {
        if (!CaseIds.IsValid(caseId))
        {
            throw new ArgumentException("The case id is not valid.", nameof(caseId));
        }

        if (settings.MaxSteps is < 1 or > AgentSettings.MaxStepsLimit)
        {
            throw new InvalidOperationException($"Agent:MaxSteps must be between 1 and {AgentSettings.MaxStepsLimit}.");
        }

        // The model is resolved once per call: nothing is shared between concurrent runs.
        var model = overrides.Model ?? settings.Model;
        var prompt = AgentPromptCatalog.UserTurn(caseId);

        IAgentToolSession session;
        try
        {
            session = await toolbox.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AgentToolException ex)
        {
            // No tools, no model call: the run ends before it starts.
            return ToolFailed(
                new AgentToolFailure(ListTool, "list", ex.ErrorKind, ex.Message),
                null,
                [],
                null,
                model,
                null);
        }

        await using (session.ConfigureAwait(false))
        {
            return await RunAsync(session, caseId, prompt, model, overrides, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AgentResult> RunAsync(
        IAgentToolSession session,
        string caseId,
        string prompt,
        string model,
        AgentOverrides overrides,
        CancellationToken cancellationToken)
    {
        var tools = session.Tools;
        var toolsSha256 = AgentContract.ToolsSha256(tools);

        var steps = new List<AgentStep>();
        var followUps = new List<LlmTurn>();
        for (var index = 0; index < settings.MaxSteps; index++)
        {
            var request = new LlmRequest(model, settings.MaxTokens, prompt, null, contract.OutputSchemaJson)
            {
                SystemPrompt = contract.SystemPrompt,
                Tools = tools,
                FollowUps = [.. followUps],
                PromptVersion = contract.PromptVersion,
                CacheMode = overrides.CacheMode,
                ReplicateSalt = overrides.ReplicateSalt,
            };
            LlmResponse response;
            try
            {
                response = await gateway.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (LlmGatewayException ex)
            {
                // Any gateway failure, a cache_miss included, is a typed failure with no decision (user decision 1).
                var failure = new AgentFailure(SnakeCase(ex.Kind), ex.HttpStatus, ex.RequestId, ex.Message);
                steps.Add(new AgentStep(index, null, failure, []));
                return Typed(AgentStatus.InfrastructureFailure, failure, null, steps, model, toolsSha256);
            }

            if (response.StopReason == LlmStopReason.ToolUse)
            {
                var requested = response.ToolCalls;

                // An empty tool_use answer is malformed output, never a tool round with nothing to run and never
                // budget exhaustion, even on the last allowed step.
                if (requested.Count == 0)
                {
                    steps.Add(new AgentStep(index, response, null, []));
                    return Typed(
                        AgentStatus.SchemaInvalid,
                        new AgentFailure("schema_invalid", null, null, EmptyToolUse),
                        TextOrNull(response.Text),
                        steps,
                        model,
                        toolsSha256);
                }

                // The last allowed step never runs its tools: the budget is spent (phase D-12).
                if (index == settings.MaxSteps - 1)
                {
                    steps.Add(new AgentStep(index, response, null, [.. requested.Select(c => Skipped(c))]));
                    return BudgetExhausted(steps, model, toolsSha256);
                }

                var calls = new List<AgentToolCall>();
                var results = new List<LlmToolResult>();
                AgentToolFailure? failure = null;
                foreach (var call in requested)
                {
                    if (failure is not null)
                    {
                        calls.Add(Skipped(call));
                        continue;
                    }

                    var run = await RunToolAsync(session, caseId, call.Id, call.Name, call.InputJson, "model", cancellationToken)
                        .ConfigureAwait(false);
                    calls.Add(run.Record);
                    failure = run.Failure;
                    if (run.Result is { } result)
                    {
                        results.Add(new LlmToolResult(call.Id, result.Content, result.IsError));
                    }
                }

                steps.Add(new AgentStep(index, response, null, calls));
                if (failure is not null)
                {
                    return ToolFailed(failure, null, steps, null, model, toolsSha256);
                }

                followUps.Add(new LlmTurn(LlmTurnRole.Assistant, string.Empty) { Blocks = response.Blocks });
                followUps.Add(new LlmTurn(LlmTurnRole.User, string.Empty) { ToolResults = results });
                continue;
            }

            steps.Add(new AgentStep(index, response, null, []));

            // Only end_turn is a complete answer. Every other stop reason (stop_sequence, pause_turn, a context-window
            // stop, anything the gateway maps to Other) is an incomplete answer and is never parsed (D-39).
            switch (response.StopReason)
            {
                case LlmStopReason.EndTurn:
                    break;
                case LlmStopReason.Refusal:
                    return Typed(
                        AgentStatus.Refused,
                        new AgentFailure("refused", null, null, response.StopDetail ?? string.Empty),
                        TextOrNull(response.Text),
                        steps,
                        model,
                        toolsSha256);
                case LlmStopReason.MaxTokens:
                    return Typed(
                        AgentStatus.Truncated,
                        new AgentFailure("truncated", null, null, "The model reached the output token limit before it finished."),
                        TextOrNull(response.Text),
                        steps,
                        model,
                        toolsSha256);
                default:
                    return Typed(
                        AgentStatus.Truncated,
                        new AgentFailure("unexpected_stop", null, null, "The model stopped for a reason the agent does not accept as a final answer."),
                        TextOrNull(response.Text),
                        steps,
                        model,
                        toolsSha256);
            }

            var (decision, error) = Parse(response.Text);
            if (decision is null)
            {
                return Typed(
                    AgentStatus.SchemaInvalid,
                    new AgentFailure("schema_invalid", null, null, error!),
                    response.Text,
                    steps,
                    model,
                    toolsSha256);
            }

            // The guard reads the extraction status itself: the model may have answered without ever asking for it.
            var guardRun = await RunToolAsync(
                    session,
                    caseId,
                    null,
                    CaseToolCatalog.GetExtractionStatus,
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["case_id"] = caseId }),
                    "guard",
                    cancellationToken)
                .ConfigureAwait(false);
            if (guardRun.Failure is { } guardFailure)
            {
                return ToolFailed(guardFailure, decision, steps, response.Text, model, toolsSha256);
            }

            var status = ReadStatus(guardRun.Result!.Content);
            if (status is null)
            {
                return ToolFailed(
                    new AgentToolFailure(CaseToolCatalog.GetExtractionStatus, "guard", "invalid_result", "The guard could not read a status from the tool result."),
                    decision,
                    steps,
                    response.Text,
                    model,
                    toolsSha256);
            }

            // Only success lets the model's decision stand. Any other status makes the final decision the
            // code-made escalate / extraction_not_success; the model's own decision is always kept next to it.
            var success = string.Equals(status, "success", StringComparison.Ordinal);
            var overridden = !success && decision.Outcome is DecisionOutcome.Approve or DecisionOutcome.Reject;
            return new AgentResult(
                AgentStatus.Decided,
                success
                    ? decision
                    : CodeDecision(DecisionCause.ExtractionNotSuccess, "The extraction did not succeed, so the case is escalated for review."),
                decision,
                success ? AgentDecidedBy.Model : AgentDecidedBy.Guard,
                new AgentGuard(status, overridden, guardRun.Record),
                null,
                null,
                response.Text,
                steps,
                model,
                contract.PromptVersion,
                settings.MaxSteps,
                contract.OutputSchemaSha256,
                toolsSha256);
        }

        throw new InvalidOperationException("The loop ended without an answer.");
    }

    private const string ListTool = "tools/list";

    private const int MaxToolNameLength = 64;

    private sealed record ToolRun(AgentToolCall Record, AgentToolResult? Result, AgentToolFailure? Failure);

    // One execute_tool span per call that is run, the guard's included. The span name and tag carry a catalog tool
    // name or "unknown", never a name the model made up; no argument, result or id is recorded (T-03.1-34).
    private async Task<ToolRun> RunToolAsync(
        IAgentToolSession session,
        string caseId,
        string? id,
        string name,
        string argumentsJson,
        string source,
        CancellationToken cancellationToken)
    {
        var spanTool = CaseToolCatalog.Names.Contains(name, StringComparer.Ordinal) ? name : "unknown";
        using var activity = AgentTelemetry.Source.StartActivity($"execute_tool {spanTool}", ActivityKind.Client);
        activity?.SetTag(AgentTelemetry.OperationName, "execute_tool");
        activity?.SetTag(AgentTelemetry.ToolName, spanTool);
        activity?.SetTag(AgentTelemetry.ToolSource, source);
        try
        {
            var run = await RunToolCoreAsync(session, caseId, id, name, argumentsJson, source, cancellationToken).ConfigureAwait(false);
            activity?.SetTag(AgentTelemetry.ToolOutcome, run.Record.Outcome);
            if (run.Failure is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag(AgentTelemetry.ErrorType, run.Record.ErrorKind);
            }

            return run;
        }
        catch (Exception ex)
        {
            AgentTelemetry.Fail(activity, ex);
            throw;
        }
    }

    // Runs one call and turns every way it can fail into a recorded failure: an unlisted name, an error result, a
    // transport or protocol fault from the toolbox, and the call timeout. A cancellation by the caller propagates.
    private async Task<ToolRun> RunToolCoreAsync(
        IAgentToolSession session,
        string caseId,
        string? id,
        string name,
        string argumentsJson,
        string source,
        CancellationToken cancellationToken)
    {
        if (!session.Tools.Any(tool => string.Equals(tool.Name, name, StringComparison.Ordinal)))
        {
            return Failed(id, name, argumentsJson, source, "unknown_tool", "The requested tool is not in the list of read-only tools.", null);
        }

        // The Tools host serves every case by id, so a model-initiated call must name the case under decision (D-39).
        // The guard is built by code from the run's own case id and is not checked.
        if (source == "model" && TakesCaseId(name) && !NamesCase(argumentsJson, caseId))
        {
            return Failed(id, name, argumentsJson, source, "invalid_arguments", CaseMismatch, null);
        }

        var timeout = TimeSpan.FromSeconds(settings.ToolCallTimeoutSeconds);
        var started = Stopwatch.GetTimestamp();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            var result = await session.CallAsync(name, argumentsJson, linked.Token).WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            var latency = Stopwatch.GetElapsedTime(started);
            if (result.IsError)
            {
                return Failed(id, name, argumentsJson, source, result.ErrorKind ?? "is_error", result.ErrorMessage ?? "The tool reported an error.", latency);
            }

            return new ToolRun(new AgentToolCall(id, name, argumentsJson, source, "ok", null, null, latency), result, null);
        }
        catch (AgentToolException ex)
        {
            return Failed(id, name, argumentsJson, source, ex.ErrorKind, ex.Message, Stopwatch.GetElapsedTime(started));
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await linked.CancelAsync().ConfigureAwait(false);
            return Failed(
                id,
                name,
                argumentsJson,
                source,
                "timeout",
                $"The tool call did not complete within {settings.ToolCallTimeoutSeconds} seconds.",
                Stopwatch.GetElapsedTime(started));
        }
    }

    private static ToolRun Failed(string? id, string name, string argumentsJson, string source, string errorKind, string message, TimeSpan? latency) =>
        new(
            new AgentToolCall(id, name, argumentsJson, source, "error", errorKind, message, latency),
            null,
            new AgentToolFailure(Clip(name), source, errorKind, message));

    private static AgentToolCall Skipped(LlmToolCall call) =>
        new(call.Id, call.Name, call.InputJson, "model", "skipped", null, null, null);

    private const string CaseMismatch = "The case_id argument is not the case under review.";

    // Every catalog tool but get_policy is addressed by case id (D-35).
    private static bool TakesCaseId(string name) =>
        !string.Equals(name, CaseToolCatalog.GetPolicy, StringComparison.Ordinal);

    // True only for a JSON object whose case_id is exactly the run's case. Anything else, malformed JSON included,
    // is not that case; the message never carries the argument.
    private static bool NamesCase(string argumentsJson, string caseId)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("case_id", out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), caseId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Clip(string name) => name.Length <= MaxToolNameLength ? name : name[..MaxToolNameLength];

    // The run ends in a decision made by code: escalate with a fixed English reason and no evidence (D-35).
    private AgentResult ToolFailed(
        AgentToolFailure failure,
        Decision? modelDecision,
        IReadOnlyList<AgentStep> steps,
        string? rawOutput,
        string model,
        string? toolsSha256)
    {
        var named = failure.Source != "list" && CaseToolCatalog.Names.Contains(failure.Tool, StringComparer.Ordinal);
        var reason = failure.Source == "list"
            ? "The tools could not be listed, so the case is escalated for review."
            : named
                ? $"The tool {failure.Tool} failed, so the case is escalated for review."
                : "A requested tool is not available, so the case is escalated for review.";
        return new AgentResult(
            AgentStatus.Decided,
            CodeDecision(DecisionCause.ToolFailed, reason),
            modelDecision,
            AgentDecidedBy.ToolFailure,
            null,
            failure,
            null,
            rawOutput,
            steps,
            model,
            contract.PromptVersion,
            settings.MaxSteps,
            contract.OutputSchemaSha256,
            toolsSha256);
    }

    private AgentResult BudgetExhausted(IReadOnlyList<AgentStep> steps, string model, string? toolsSha256) =>
        new(
            AgentStatus.Decided,
            CodeDecision(
                DecisionCause.StepBudgetExhausted,
                $"The step budget of {settings.MaxSteps} was exhausted before the model answered, so the case is escalated for review."),
            null,
            AgentDecidedBy.StepBudget,
            null,
            null,
            null,
            null,
            steps,
            model,
            contract.PromptVersion,
            settings.MaxSteps,
            contract.OutputSchemaSha256,
            toolsSha256);

    private static Decision CodeDecision(DecisionCause cause, string reason) =>
        new([], reason, DecisionOutcome.Escalate, cause);

    private const string EmptyToolUse = "tool_use answer without a tool call";

    private AgentResult Typed(
        AgentStatus status,
        AgentFailure failure,
        string? rawOutput,
        IReadOnlyList<AgentStep> steps,
        string model,
        string? toolsSha256) =>
        new(
            status,
            null,
            null,
            null,
            null,
            null,
            failure,
            rawOutput,
            steps,
            model,
            contract.PromptVersion,
            settings.MaxSteps,
            contract.OutputSchemaSha256,
            toolsSha256);

    private static string? TextOrNull(string text) => text.Length == 0 ? null : text;

    private static string SnakeCase(LlmFailureKind kind)
    {
        var name = kind.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    // Narrow catches, as in the extractor: a programming error must surface, cancellation must propagate. The
    // messages carry JSON paths and wire names only, never the model's values (raw_output already holds them).
    private static (Decision? Decision, string? Error) Parse(string text)
    {
        try
        {
            var decision = JsonSerializer.Deserialize<Decision>(text, Wire.Options);
            if (decision is null)
            {
                return (null, "The model output was JSON null.");
            }

            var nulls = decision.NullViolations();
            if (nulls.Count > 0)
            {
                return (null, $"The model output has null where the schema requires a value at: {string.Join(", ", nulls)}.");
            }

            var violation = AgentContract.ModelDecisionViolation(decision);
            return violation is null ? (decision, null) : (null, violation);
        }
        catch (JsonException ex)
        {
            return (null, $"The model output is not a valid decision at {ex.Path ?? "$"}.");
        }
        catch (FormatException)
        {
            return (null, "The model output has a value that does not match its type.");
        }
        catch (OverflowException)
        {
            return (null, "The model output has a number that is out of range.");
        }
    }

    // The status from the guard's tool result, or null when the result is not the expected shape.
    private static string? ReadStatus(string statusJson)
    {
        try
        {
            using var document = JsonDocument.Parse(statusJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                    ? status.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
