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

public enum LlmTurnRole
{
    Assistant,
    User,
}

/// <summary>One follow-up turn of a conversation, for example a previous model answer or a repair instruction.</summary>
public sealed record LlmTurn(LlmTurnRole Role, string Text);

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
    string OutputSchemaJson)
{
    /// <summary>
    /// Turns after the first user message (document and prompt): alternating assistant then user, and ending on
    /// a user turn. A conversation that ends on an assistant turn is prefill, which current models reject.
    /// </summary>
    public IReadOnlyList<LlmTurn> FollowUps { get; init; } = [];

    /// <summary>Sets a prompt-cache breakpoint on the document block so follow-up turns can reuse it.</summary>
    public bool CacheDocument { get; init; }
}

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
