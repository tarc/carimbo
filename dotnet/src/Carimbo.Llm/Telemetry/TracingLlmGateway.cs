using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Carimbo.Llm;

/// <summary>
/// The outermost gateway decorator (D-31): one client span named <c>chat &lt;model&gt;</c> per
/// <see cref="CompleteAsync"/> call, a cache hit included, so a hit and the miss that stored it are two spans.
/// It only observes: it adds no retry, changes no request and returns the inner response untouched.
/// </summary>
/// <remarks>
/// Spans carry metadata from <see cref="LlmTelemetry.AllowedTags"/> only. No prompt, answer, schema, document,
/// follow-up turn or provider message ever reaches a tag, and exceptions are not recorded as events (T-03-17).
/// A repair call carries both prompt labels of its request: the extraction prompt and the repair prompt (D-33).
/// </remarks>
public sealed class TracingLlmGateway(ILlmGateway inner) : ILlmGateway
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        // Null when nothing records the source (no tracer provider, or a sampler said no): then just call through.
        using var activity = LlmTelemetry.Source.StartActivity("chat " + request.Model, ActivityKind.Client);
        if (activity is null)
        {
            return await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        }

        activity.SetTag(LlmTelemetry.OperationName, "chat");
        activity.SetTag(LlmTelemetry.ProviderName, "anthropic");
        activity.SetTag(LlmTelemetry.RequestModel, request.Model);
        activity.SetTag(LlmTelemetry.RequestMaxTokens, request.MaxTokens);
        if (request.PromptVersion is { } promptVersion)
        {
            activity.SetTag(LlmTelemetry.PromptVersion, promptVersion);
        }

        if (request.RepairPromptVersion is { } repairPromptVersion)
        {
            activity.SetTag(LlmTelemetry.RepairPromptVersion, repairPromptVersion);
        }

        if (request.ReplicateSalt is { } salt)
        {
            activity.SetTag(LlmTelemetry.ReplicateSalt, salt);
        }

        try
        {
            var response = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            Describe(activity, response);
            return response;
        }
        catch (LlmGatewayException ex)
        {
            Fail(activity, Snake(ex.Kind.ToString()));
            throw;
        }
        catch (OperationCanceledException)
        {
            Fail(activity, "cancelled");
            throw;
        }
    }

    private static void Describe(Activity activity, LlmResponse response)
    {
        var usage = response.Usage;
        var cacheWrite = usage.CacheWrite5mTokens + usage.CacheWrite1hTokens;

        if (response.ModelReturned is { } returned)
        {
            activity.SetTag(LlmTelemetry.ResponseModel, returned);
        }

        if (response.ProviderMessageId is { } messageId)
        {
            activity.SetTag(LlmTelemetry.ResponseId, messageId);
        }

        activity.SetTag(LlmTelemetry.ResponseFinishReasons, new[] { Snake(response.StopReason.ToString()) });

        // The convention counts every input class in gen_ai.usage.input_tokens, while LlmUsage.InputTokens is the
        // uncached remainder only.
        activity.SetTag(LlmTelemetry.UsageInputTokens, usage.InputTokens + usage.CacheReadTokens + cacheWrite);
        activity.SetTag(LlmTelemetry.UsageOutputTokens, usage.OutputTokens);
        activity.SetTag(LlmTelemetry.UsageCacheReadInputTokens, usage.CacheReadTokens);
        activity.SetTag(LlmTelemetry.UsageCacheWriteInputTokens, cacheWrite);

        if (response.Cost is { } cost)
        {
            // An unpriced call has no amount: the attribute is absent, never an empty or zero cost.
            if (cost.AmountUsd is { } amount)
            {
                activity.SetTag(LlmTelemetry.CostUsd, amount.ToString("F8", CultureInfo.InvariantCulture));
            }

            activity.SetTag(LlmTelemetry.PricingVersion, cost.PricingVersion);
            if (cost.Warning is { } warning)
            {
                activity.SetTag(LlmTelemetry.CostWarning, warning);
            }
        }

        if (response.Cache is { } cache)
        {
            activity.SetTag(LlmTelemetry.CacheStatus, Snake(cache.Status.ToString()));
            activity.SetTag(LlmTelemetry.CacheMode, LlmCacheModes.ToWire(cache.Mode));
        }
        else
        {
            activity.SetTag(LlmTelemetry.CacheStatus, "off");
        }

        activity.SetTag(LlmTelemetry.HttpAttempts, response.HttpAttempts);
    }

    private static void Fail(Activity activity, string errorType)
    {
        // The status description is left empty on purpose: an exception message could carry provider text.
        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(LlmTelemetry.ErrorType, errorType);
    }

    private static string Snake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
