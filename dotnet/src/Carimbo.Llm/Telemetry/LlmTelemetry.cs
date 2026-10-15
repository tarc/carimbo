using System.Collections.Frozen;
using System.Diagnostics;

namespace Carimbo.Llm;

/// <summary>
/// The span vocabulary of a model call (D-31): the <see cref="ActivitySource"/>, every attribute name the gateway
/// may set, and the allowlist the tests hold it to.
/// </summary>
/// <remarks>
/// The <c>gen_ai.*</c> names follow github.com/open-telemetry/semantic-conventions-genai (docs/gen-ai/gen-ai-spans.md
/// and docs/registry/attributes/gen-ai.md), read on 2026-10-15. That specification has status Development and no
/// published release or schema URL, so there is no version to pin: these constants plus the allowlist test in
/// TracingGatewayTests are the pin (D-31). The <c>carimbo.llm.*</c> names are ours. Content-bearing attributes of
/// the same specification (<c>gen_ai.input.messages</c>, <c>gen_ai.output.messages</c>,
/// <c>gen_ai.system_instructions</c>) are deliberately absent and must never be added.
/// <c>carimbo.llm.prompt_version</c> is the extraction prompt on every call, and <c>carimbo.llm.repair_prompt_version</c>
/// is present only on repair calls, mirroring <c>LlmRequest</c> and the cache-key fields (D-33); the constants plus
/// the allowlist test remain the pin.
/// </remarks>
public static class LlmTelemetry
{
    /// <summary>The source name a tracer provider listens to.</summary>
    public const string SourceName = "Carimbo.Llm";

    public const string OperationName = "gen_ai.operation.name";
    public const string ProviderName = "gen_ai.provider.name";
    public const string RequestModel = "gen_ai.request.model";
    public const string RequestMaxTokens = "gen_ai.request.max_tokens";
    public const string ResponseModel = "gen_ai.response.model";
    public const string ResponseId = "gen_ai.response.id";
    public const string ResponseFinishReasons = "gen_ai.response.finish_reasons";
    public const string UsageInputTokens = "gen_ai.usage.input_tokens";
    public const string UsageOutputTokens = "gen_ai.usage.output_tokens";
    public const string UsageCacheReadInputTokens = "gen_ai.usage.cache_read.input_tokens";
    public const string UsageCacheWriteInputTokens = "gen_ai.usage.cache_write.input_tokens";

    public const string CostUsd = "carimbo.llm.cost_usd";
    public const string PricingVersion = "carimbo.llm.pricing_version";
    public const string CostWarning = "carimbo.llm.cost_warning";
    public const string CacheStatus = "carimbo.llm.cache.status";
    public const string CacheMode = "carimbo.llm.cache.mode";
    public const string HttpAttempts = "carimbo.llm.http_attempts";
    public const string PromptVersion = "carimbo.llm.prompt_version";
    public const string RepairPromptVersion = "carimbo.llm.repair_prompt_version";
    public const string ReplicateSalt = "carimbo.llm.replicate_salt";

    public const string ErrorType = "error.type";

    /// <summary>The one source of every model-call span.</summary>
    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>Every attribute key a model-call span may carry. Anything else is a bug (T-03-17).</summary>
    public static FrozenSet<string> AllowedTags { get; } = new[]
    {
        OperationName,
        ProviderName,
        RequestModel,
        RequestMaxTokens,
        ResponseModel,
        ResponseId,
        ResponseFinishReasons,
        UsageInputTokens,
        UsageOutputTokens,
        UsageCacheReadInputTokens,
        UsageCacheWriteInputTokens,
        CostUsd,
        PricingVersion,
        CostWarning,
        CacheStatus,
        CacheMode,
        HttpAttempts,
        PromptVersion,
        RepairPromptVersion,
        ReplicateSalt,
        ErrorType,
    }.ToFrozenSet(StringComparer.Ordinal);
}
