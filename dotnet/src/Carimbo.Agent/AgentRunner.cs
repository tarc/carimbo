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

        await using var session = await toolbox.ConnectAsync(cancellationToken).ConfigureAwait(false);
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
                var calls = new List<AgentToolCall>();
                var results = new List<LlmToolResult>();
                foreach (var call in response.ToolCalls)
                {
                    var (record, result) = await RunToolAsync(session, call.Id, call.Name, call.InputJson, "model", cancellationToken)
                        .ConfigureAwait(false);
                    calls.Add(record);
                    results.Add(new LlmToolResult(call.Id, result.Content, result.IsError));
                }

                steps.Add(new AgentStep(index, response, null, calls));
                followUps.Add(new LlmTurn(LlmTurnRole.Assistant, string.Empty) { Blocks = response.Blocks });
                followUps.Add(new LlmTurn(LlmTurnRole.User, string.Empty) { ToolResults = results });
                continue;
            }

            steps.Add(new AgentStep(index, response, null, []));
            var decision = Parse(response.Text);
            var (guardCall, guardResult) = await RunToolAsync(
                    session,
                    null,
                    CaseToolCatalog.GetExtractionStatus,
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["case_id"] = caseId }),
                    "guard",
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static async Task<(AgentToolCall Record, AgentToolResult Result)> RunToolAsync(
        IAgentToolSession session,
        string? id,
        string name,
        string argumentsJson,
        string source,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await session.CallAsync(name, argumentsJson, cancellationToken).ConfigureAwait(false);
        var latency = Stopwatch.GetElapsedTime(started);
        var record = new AgentToolCall(
            id,
            name,
            argumentsJson,
            source,
            result.IsError ? "error" : "ok",
            result.ErrorKind,
            result.ErrorMessage,
            latency);
        return (record, result);
    }

    private static Decision Parse(string text) =>
        JsonSerializer.Deserialize<Decision>(text, Wire.Options)
        ?? throw new JsonException("The model output was JSON null.");

    private static string ReadStatus(string statusJson)
    {
        using var document = JsonDocument.Parse(statusJson);
        return document.RootElement.GetProperty("status").GetString() ?? string.Empty;
    }
}
