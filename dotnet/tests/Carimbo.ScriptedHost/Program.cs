using System.Security.Cryptography;
using System.Text.Json;
using Carimbo.Api;
using Carimbo.Decisions;
using Carimbo.Llm;

const string ResponsesDirVariable = "CARIMBO_SCRIPTED_RESPONSES_DIR";

var responsesDir = Environment.GetEnvironmentVariable(ResponsesDirVariable);
if (string.IsNullOrWhiteSpace(responsesDir))
{
    Console.Error.WriteLine($"{ResponsesDirVariable} is not set. The scripted host needs a directory of <sha256>.json responses.");
    return 2;
}

CarimboApi.CreateApp(args, services => services.AddSingleton<ILlmGateway>(new ScriptedLlmGateway(responsesDir))).Run();
return 0;

/// <summary>
/// Test-only gateway with two kinds of script, both read from the responses directory.
/// <para>
/// An extraction request carries a document and is answered from <c>&lt;responses dir&gt;/&lt;sha256 of the
/// document&gt;.json</c>. A file with an <c>attempts</c> array answers call N from element N, where N is the number
/// of follow-up turns divided by two (stateless and deterministic: the repair loop adds two turns per attempt); a
/// file without it answers every call from its root.
/// </para>
/// <para>
/// An agent step carries no document. Its case id is read from the first line of the prompt (<c>case_id: dc-001</c>)
/// and it is answered from <c>&lt;responses dir&gt;/decisions/&lt;case id&gt;.json</c>, <c>{"steps": [...]}</c>, element
/// N where N is the number of follow-up turns divided by two (the loop adds an assistant and a user turn per tool
/// round). A step has the same members as an extraction answer plus <c>tool_calls</c> (<c>[{id, name, input}]</c>)
/// and <c>thinking</c> (<c>[{thinking, signature}]</c>); a missing script or a step beyond the list is a bad request.
/// </para>
/// <para>
/// An omitted <c>stop_reason</c> is <c>tool_use</c> for a step with tool calls and <c>end_turn</c> otherwise; tool
/// calls with another stop reason are a bad request. Answers carry the real gateway's content blocks (a text block
/// whenever there is text) and a deterministic provider message id. What it does not do is validate the conversation
/// the agent sends: that is pinned by the gateway's own tests (<c>AnthropicGatewayTests</c>), so this e2e shows the
/// loop, the endpoint and the grader working together, not that the history is well formed.
/// </para>
/// Lives under tests, is never referenced by the Api project, and refuses to start without a directory.
/// </summary>
internal sealed class ScriptedLlmGateway(string responsesDir) : ILlmGateway
{
    // A failure is a faulted task, as it is for the real gateway, never a synchronous throw.
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Complete(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<LlmResponse>(ex);
        }
    }

    private LlmResponse Complete(LlmRequest request)
    {
        var (file, root) = request.Document is { } requestDocument
            ? OpenDocumentScript(requestDocument, request)
            : OpenAgentStep(request);
        using var scriptFile = file;

        if (root.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.Object)
        {
            var kind = Enum.Parse<LlmFailureKind>(failure.GetProperty("kind").GetString()!.Replace("_", string.Empty), ignoreCase: true);
            int? httpStatus = failure.TryGetProperty("http_status", out var status) && status.ValueKind == JsonValueKind.Number
                ? status.GetInt32()
                : null;
            throw new LlmGatewayException(kind, "scripted failure", httpStatus);
        }

        var usage = root.TryGetProperty("usage", out var u) ? u : default;
        var response = new LlmResponse(
            Text: OptionalString(root, "text") ?? string.Empty,
            StopReason: StopReasonOf(root),
            StopDetail: OptionalString(root, "stop_detail"),
            Usage: new LlmUsage(
                Count(usage, "input_tokens"),
                Count(usage, "output_tokens"),
                Count(usage, "cache_read_tokens"),
                Count(usage, "cache_write_5m_tokens"),
                Count(usage, "cache_write_1h_tokens")),
            ModelRequested: request.Model,
            ModelReturned: OptionalString(root, "model") ?? "scripted",
            ProviderMessageId: "msg_scripted_" + (request.FollowUps.Count / 2),
            Latency: ScriptedLatency(root),
            HttpAttempts: 1)
        {
            Blocks = BlocksOf(root),
        };
        return response;
    }

    // The extraction script of a document: <sha256>.json, optionally with one element per repair attempt.
    private (JsonDocument File, JsonElement Root) OpenDocumentScript(LlmDocument requestDocument, LlmRequest request)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(requestDocument.Content.Span));
        var path = Path.Combine(responsesDir, digest + ".json");
        if (!File.Exists(path))
        {
            throw new LlmGatewayException(LlmFailureKind.BadRequest, "no scripted response for document");
        }

        var file = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = file.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("attempts", out var scripted)
            && scripted.ValueKind == JsonValueKind.Array)
        {
            var attemptIndex = request.FollowUps.Count / 2;
            if (attemptIndex >= scripted.GetArrayLength())
            {
                file.Dispose();
                throw new LlmGatewayException(LlmFailureKind.BadRequest, $"no scripted response for attempt {attemptIndex}");
            }

            root = scripted[attemptIndex];
        }

        return (file, root);
    }

    // The agent script of a case: decisions/<case id>.json, step N for the loop's Nth model call.
    private (JsonDocument File, JsonElement Root) OpenAgentStep(LlmRequest request)
    {
        if (!AgentPromptCatalog.TryReadCaseId(request.Prompt, out var caseId))
        {
            throw new LlmGatewayException(LlmFailureKind.BadRequest, "no scripted decision for this request");
        }

        // The id passed CaseIds.IsValid, so it cannot name a path outside the decisions directory.
        var path = Path.Combine(responsesDir, "decisions", caseId + ".json");
        if (!File.Exists(path))
        {
            throw new LlmGatewayException(LlmFailureKind.BadRequest, "no scripted decision for this case");
        }

        var file = JsonDocument.Parse(File.ReadAllBytes(path));
        var stepIndex = request.FollowUps.Count / 2;
        if (file.RootElement.ValueKind != JsonValueKind.Object
            || !file.RootElement.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array
            || stepIndex >= steps.GetArrayLength())
        {
            file.Dispose();
            throw new LlmGatewayException(LlmFailureKind.BadRequest, $"no scripted step {stepIndex} for this case");
        }

        return (file, steps[stepIndex]);
    }

    // The blocks the real gateway returns: thinking first, then the text when there is any, then the tool calls. The
    // loop echoes them on the next turn and the cache stores them.
    private static List<LlmContentBlock> BlocksOf(JsonElement root)
    {
        var blocks = new List<LlmContentBlock>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return blocks;
        }

        var hasThinking = root.TryGetProperty("thinking", out var thinking) && thinking.ValueKind == JsonValueKind.Array;
        var hasCalls = root.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array;
        if (hasThinking)
        {
            foreach (var block in thinking.EnumerateArray())
            {
                blocks.Add(LlmContentBlock.OfThinking(
                    OptionalString(block, "thinking") ?? string.Empty,
                    OptionalString(block, "signature") ?? string.Empty));
            }
        }

        var text = OptionalString(root, "text");
        if (!string.IsNullOrEmpty(text))
        {
            blocks.Add(LlmContentBlock.OfText(text));
        }

        if (hasCalls)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var input = call.TryGetProperty("input", out var arguments) ? JsonSerializer.Serialize(arguments) : "{}";
                blocks.Add(LlmContentBlock.OfToolUse(new LlmToolCall(
                    OptionalString(call, "id") ?? string.Empty,
                    OptionalString(call, "name") ?? string.Empty,
                    input)));
            }
        }

        return blocks;
    }

    // A scripted call stands for one provider call, so a replay from the response cache (HttpAttempts 0) is visible.
    // The optional numeric latency_ms lets a script give the call a latency that a replay must reproduce.
    private static TimeSpan ScriptedLatency(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("latency_ms", out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Count(JsonElement usage, string name) =>
        usage.ValueKind == JsonValueKind.Object
        && usage.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;

    // A step that omits stop_reason gets the one the real gateway would report: tool_use when it asks for tools,
    // otherwise end_turn. Tool calls with any other stop reason are a mistake in the script and fail fast.
    private static LlmStopReason StopReasonOf(JsonElement root)
    {
        var hasCalls = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("tool_calls", out var calls)
            && calls.ValueKind == JsonValueKind.Array
            && calls.GetArrayLength() > 0;
        var reason = OptionalString(root, "stop_reason");
        if (reason is null)
        {
            return hasCalls ? LlmStopReason.ToolUse : LlmStopReason.EndTurn;
        }

        var mapped = MapStopReason(reason);
        if (hasCalls && mapped != LlmStopReason.ToolUse)
        {
            throw new LlmGatewayException(LlmFailureKind.BadRequest, "a scripted step with tool_calls must have stop_reason tool_use");
        }

        return mapped;
    }

    private static LlmStopReason MapStopReason(string? reason) => reason switch
    {
        "end_turn" => LlmStopReason.EndTurn,
        "max_tokens" => LlmStopReason.MaxTokens,
        "refusal" => LlmStopReason.Refusal,
        "tool_use" => LlmStopReason.ToolUse,
        "stop_sequence" => LlmStopReason.StopSequence,
        _ => LlmStopReason.Other,
    };
}
