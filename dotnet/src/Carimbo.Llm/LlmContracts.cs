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

    /// <summary>
    /// Version label of the extraction prompt that produced <see cref="Prompt"/>. Part of the response-cache key
    /// (LLM-04, EXT-05); the provider never sees it.
    /// </summary>
    public string? PromptVersion { get; init; }

    /// <summary>
    /// Version label of the repair prompt used for follow-up turns. Part of the response-cache key; the provider
    /// never sees it.
    /// </summary>
    public string? RepairPromptVersion { get; init; }

    /// <summary>
    /// Distinguishes deliberate replicates of an otherwise identical request, so two replicates do not share one
    /// cached answer. Part of the response-cache key; the provider never sees it.
    /// </summary>
    public string? ReplicateSalt { get; init; }

    /// <summary>
    /// How the response cache treats this call; null means the host default. Never part of the cache key, so the
    /// same request maps to the same entry in every mode.
    /// </summary>
    public LlmCacheMode? CacheMode { get; init; }
}

/// <summary>How the response cache treats a call (D-19). Wire names: off, read_write, read_only, refresh.</summary>
public enum LlmCacheMode
{
    /// <summary>Bypass the cache: always call the provider, never write.</summary>
    Off,

    /// <summary>Serve a stored answer when there is one; otherwise call the provider and store an end_turn answer.</summary>
    ReadWrite,

    /// <summary>Serve a stored answer or fail with <see cref="LlmFailureKind.CacheMiss"/>; never call the provider.</summary>
    ReadOnly,

    /// <summary>Ignore any stored answer, call the provider and overwrite the entry only with an end_turn answer.</summary>
    Refresh,
}

/// <summary>What the cache did for one call.</summary>
public enum LlmCacheStatus
{
    Hit,
    Miss,
    Refresh,
}

/// <summary>The cache layer's report on one call; <see cref="Stored"/> is true when this call wrote an entry.</summary>
public sealed record LlmCacheInfo(LlmCacheMode Mode, LlmCacheStatus Status, string Key, bool Stored);

/// <summary>Where the response cache lives and which mode applies when a request names none.</summary>
public sealed record LlmCacheSettings(string Directory, LlmCacheMode DefaultMode);

/// <summary>The exact wire names of <see cref="LlmCacheMode"/>: ordinal match only, any other spelling fails.</summary>
public static class LlmCacheModes
{
    public static bool TryParse(string? value, out LlmCacheMode mode)
    {
        switch (value)
        {
            case "off":
                mode = LlmCacheMode.Off;
                return true;
            case "read_write":
                mode = LlmCacheMode.ReadWrite;
                return true;
            case "read_only":
                mode = LlmCacheMode.ReadOnly;
                return true;
            case "refresh":
                mode = LlmCacheMode.Refresh;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static string ToWire(LlmCacheMode mode) => mode switch
    {
        LlmCacheMode.Off => "off",
        LlmCacheMode.ReadWrite => "read_write",
        LlmCacheMode.ReadOnly => "read_only",
        LlmCacheMode.Refresh => "refresh",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown cache mode."),
    };
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

    /// <summary>Set by the cache layer; null when no cache was consulted.</summary>
    public LlmCacheInfo? Cache { get; init; }
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

    /// <summary>A read_only call found no cache entry and made no provider call.</summary>
    CacheMiss,
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
