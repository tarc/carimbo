namespace Carimbo.Llm;

/// <summary>
/// The single seam between the pipeline and a model provider. Provider types never cross it:
/// callers see only the Carimbo-owned records below.
/// </summary>
public interface ILlmGateway
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <summary>A document attached to a request, for example a DANFE PDF.</summary>
public sealed record LlmDocument(string MediaType, ReadOnlyMemory<byte> Content);

/// <param name="Model">Requested model identifier, sent as given.</param>
/// <param name="MaxTokens">Output token cap, including any thinking tokens.</param>
/// <param name="Prompt">Instruction text.</param>
/// <param name="Document">The attached document.</param>
/// <param name="OutputSchemaJson">The output JSON Schema as the exact string to send, so the bytes are known.</param>
public sealed record LlmRequest(
    string Model,
    int MaxTokens,
    string Prompt,
    LlmDocument Document,
    string OutputSchemaJson);

public enum LlmStopReason
{
    EndTurn,
    MaxTokens,
    Refusal,
    StopSequence,
    Other,
}

/// <summary>Token counts per billing class. <see cref="InputTokens"/> is the uncached remainder only.</summary>
public sealed record LlmUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWrite5mTokens,
    long CacheWrite1hTokens)
{
    public static LlmUsage Zero { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>Cost of one call. A null amount means the cost could not be priced; see <see cref="Warning"/>.</summary>
public sealed record LlmCost(decimal? AmountUsd, string PricingVersion, string? Warning);

public sealed record LlmResponse(
    string Text,
    LlmStopReason StopReason,
    string? StopDetail,
    LlmUsage Usage,
    string ModelRequested,
    string? ModelReturned,
    string? ProviderMessageId,
    TimeSpan Latency,
    int HttpAttempts)
{
    /// <summary>Set by the cost-accounting layer; absent until then.</summary>
    public LlmCost? Cost { get; init; }
}

public enum LlmFailureKind
{
    NotConfigured,
    Auth,
    BadRequest,
    RateLimit,
    Overloaded,
    ServerError,
    Timeout,
    Network,
}

/// <summary>A provider call that did not produce a response, classified without provider types.</summary>
public sealed class LlmGatewayException(
    LlmFailureKind kind,
    string message,
    int? httpStatus = null,
    string? requestId = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public LlmFailureKind Kind { get; } = kind;

    public int? HttpStatus { get; } = httpStatus;

    public string? RequestId { get; } = requestId;
}
