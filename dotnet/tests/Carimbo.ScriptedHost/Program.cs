using System.Security.Cryptography;
using System.Text.Json;
using Carimbo.Api;
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
/// Test-only gateway: answers each request from <c>&lt;responses dir&gt;/&lt;sha256 of the document&gt;.json</c>.
/// A file with an <c>attempts</c> array answers call N from element N, where N is the number of follow-up
/// turns divided by two (stateless and deterministic: the repair loop adds two turns per attempt); a file
/// without it answers every call from its root.
/// Lives under tests, is never referenced by the Api project, and refuses to start without a directory.
/// </summary>
internal sealed class ScriptedLlmGateway(string responsesDir) : ILlmGateway
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(request.Document.Content.Span));
        var path = Path.Combine(responsesDir, digest + ".json");
        if (!File.Exists(path))
        {
            throw new LlmGatewayException(LlmFailureKind.BadRequest, "no scripted response for document");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("attempts", out var scripted)
            && scripted.ValueKind == JsonValueKind.Array)
        {
            var attemptIndex = request.FollowUps.Count / 2;
            if (attemptIndex >= scripted.GetArrayLength())
            {
                throw new LlmGatewayException(LlmFailureKind.BadRequest, $"no scripted response for attempt {attemptIndex}");
            }

            root = scripted[attemptIndex];
        }

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
            StopReason: MapStopReason(OptionalString(root, "stop_reason")),
            StopDetail: OptionalString(root, "stop_detail"),
            Usage: new LlmUsage(
                Count(usage, "input_tokens"),
                Count(usage, "output_tokens"),
                Count(usage, "cache_read_tokens"),
                Count(usage, "cache_write_5m_tokens"),
                Count(usage, "cache_write_1h_tokens")),
            ModelRequested: request.Model,
            ModelReturned: OptionalString(root, "model") ?? "scripted",
            ProviderMessageId: null,
            Latency: TimeSpan.Zero,
            HttpAttempts: 0);
        return Task.FromResult(response);
    }

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

    private static LlmStopReason MapStopReason(string? reason) => reason switch
    {
        "end_turn" => LlmStopReason.EndTurn,
        "max_tokens" => LlmStopReason.MaxTokens,
        "refusal" => LlmStopReason.Refusal,
        "stop_sequence" => LlmStopReason.StopSequence,
        _ => LlmStopReason.Other,
    };
}
