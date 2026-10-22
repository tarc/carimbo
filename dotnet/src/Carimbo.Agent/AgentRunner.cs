using System.Diagnostics;
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
            var response = await gateway.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StopReason == LlmStopReason.ToolUse)
            {
                var requested = response.ToolCalls;

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

                    var run = await RunToolAsync(session, call.Id, call.Name, call.InputJson, "model", cancellationToken)
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
            var decision = Parse(response.Text);
            var guardRun = await RunToolAsync(
                    session,
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

            var guardCall = guardRun.Record;
            var guardResult = guardRun.Result!;
            var status = ReadStatus(guardResult.Content);
            return new AgentResult(
                AgentStatus.Decided,
                decision,
                decision,
                AgentDecidedBy.Model,
                new AgentGuard(status, false, guardCall),
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

    // Runs one call and turns every way it can fail into a recorded failure: an unlisted name, an error result, a
    // transport or protocol fault from the toolbox, and the call timeout. A cancellation by the caller propagates.
    private async Task<ToolRun> RunToolAsync(
        IAgentToolSession session,
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

    private static Decision Parse(string text) =>
        JsonSerializer.Deserialize<Decision>(text, Wire.Options)
        ?? throw new JsonException("The model output was JSON null.");

    private static string ReadStatus(string statusJson)
    {
        using var document = JsonDocument.Parse(statusJson);
        return document.RootElement.GetProperty("status").GetString() ?? string.Empty;
    }
}
