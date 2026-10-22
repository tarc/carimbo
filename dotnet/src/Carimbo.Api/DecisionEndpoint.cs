using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Carimbo.Agent;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Llm;
using Microsoft.AspNetCore.Mvc;

namespace Carimbo.Api;

/// <summary>
/// The decision endpoint (AGT-03, AGT-04, D-16, D-36): contract <c>decision-1</c> beside the extraction route, with
/// the same availability policy, static-key authentication, strict binding and per-field 400s. The agent's decision is
/// returned and recorded and nothing executes it. Typed failures and escalations are HTTP 200.
/// </summary>
internal static class DecisionEndpoint
{
    internal const string Route = "/eval/decisions";
    internal const string ContractVersion = "decision-1";
    internal const long MaxBodyBytes = 64 * 1024;

    /// <summary>
    /// Maps <c>POST /eval/decisions</c> only in Development or Eval, with a configured
    /// <c>CARIMBO_EVAL_API_KEY</c> and a registered <see cref="ILlmGateway"/>. Logs one line naming each unmet
    /// condition (never a value).
    /// </summary>
    /// <returns><see langword="true"/> when the route was mapped.</returns>
    public static bool TryMap(WebApplication app, ILogger logger)
    {
        var evalKey = app.Configuration[EvalEndpoint.KeyConfig];
        var environmentAllowed = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Eval");
        var keyConfigured = !string.IsNullOrEmpty(evalKey);
        var gatewayRegistered = app.Services.GetService<ILlmGateway>() is not null;

        if (!(environmentAllowed && keyConfigured && gatewayRegistered))
        {
            var unmet = new List<string>();
            if (!environmentAllowed)
            {
                unmet.Add("environment is not Development or Eval");
            }

            if (!keyConfigured)
            {
                unmet.Add($"{EvalEndpoint.KeyConfig} is not set");
            }

            if (!gatewayRegistered)
            {
                unmet.Add("no ILlmGateway is registered");
            }

            logger.LogInformation("Decision endpoint {Route} is disabled: {Reasons}.", Route, string.Join("; ", unmet));
            return false;
        }

        var expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(evalKey!));
        Func<HttpContext, Task<IResult>> handler = context => HandleAsync(context, expectedKeyHash);
        app.MapPost(Route, handler).WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        logger.LogInformation("Decision endpoint {Route} is enabled.", Route);
        return true;
    }

    private static async Task<IResult> HandleAsync(HttpContext context, byte[] expectedKeyHash)
    {
        // Authenticate before touching the body, so an unauthenticated caller learns nothing.
        if (!EvalEndpoint.IsAuthenticated(context, expectedKeyHash))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var (request, rejected) = await EvalEndpoint.ReadBodyAsync<DecisionRequest>(context, MaxBodyBytes);
        if (request is null)
        {
            return rejected!;
        }

        if (!string.Equals(request.ContractVersion, ContractVersion, StringComparison.Ordinal))
        {
            return EvalEndpoint.Invalid("contract_version", $"must be \"{ContractVersion}\".");
        }

        // The ordinal allow-list runs before the id reaches a tool, a log line or a span (T-03.1-32).
        if (!CaseIds.IsValid(request.CaseId))
        {
            return EvalEndpoint.Invalid(
                "case_id",
                $"must be 1 to {CaseIds.MaxLength} characters of a-z, 0-9 or '-', starting with a letter or digit.");
        }

        var cacheSettings = context.RequestServices.GetService<LlmCacheSettings>();
        if (EvalEndpoint.CheckCacheMode(request.Overrides?.CacheMode, cacheSettings, out var cacheMode) is { } badCacheMode)
        {
            return badCacheMode;
        }

        if (EvalEndpoint.CheckReplicateSalt(request.Overrides?.ReplicateSalt, out var replicateSalt) is { } badSalt)
        {
            return badSalt;
        }

        var pricing = context.RequestServices.GetRequiredService<LlmPricingTable>();
        if (EvalEndpoint.CheckModel(request.Overrides?.Model, pricing, out var model) is { } badModel)
        {
            return badModel;
        }

        var ownedActivity = EvalEndpoint.StartOwnedActivity(context, "Carimbo.Eval");
        try
        {
            var traceId = (Activity.Current?.TraceId ?? default).ToString();
            var agent = context.RequestServices.GetRequiredService<IDecisionAgent>();
            var stopwatch = Stopwatch.StartNew();
            var result = await agent.DecideAsync(
                request.CaseId,
                new AgentOverrides { CacheMode = cacheMode, ReplicateSalt = replicateSalt, Model = model },
                context.RequestAborted);
            stopwatch.Stop();
            var effectiveCacheMode = cacheMode ?? cacheSettings?.DefaultMode ?? LlmCacheMode.Off;
            return Results.Ok(DecisionResponse.From(
                request.CaseId,
                traceId,
                result,
                stopwatch.ElapsedMilliseconds,
                pricing.Version,
                LlmCacheModes.ToWire(effectiveCacheMode),
                replicateSalt));
        }
        finally
        {
            ownedActivity?.Stop();
        }
    }
}

/// <summary>The request body. There is deliberately no prompt or step-budget override (D-36).</summary>
internal sealed record DecisionRequest(string ContractVersion, string CaseId, DecisionOverridesBody? Overrides = null);

/// <summary>Per-request overrides; each member stays a raw JSON value so a wrongly typed value is reported against its own field.</summary>
internal sealed record DecisionOverridesBody(
    JsonElement? Model = null,
    JsonElement? CacheMode = null,
    JsonElement? ReplicateSalt = null);

internal sealed record DecisionResponse(
    string ContractVersion,
    string CaseId,
    string TraceId,
    DecisionEffective Effective,
    DecisionOutcomeBody Outcome,
    IReadOnlyList<DecisionStepBody> Steps,
    EvalUsage Usage,
    string? CostUsd,
    [property: JsonPropertyName("cost_warning")] string? CostWarning,
    string? IncurredCostUsd,
    long LatencyMs)
{
    public static DecisionResponse From(
        string caseId,
        string traceId,
        AgentResult result,
        long latencyMs,
        string pricingVersion,
        string cacheMode,
        string? replicateSalt)
    {
        var steps = result.Steps.Select(DecisionStepBody.From).ToArray();
        var answered = result.Steps.Where(s => s.Response is not null).Select(s => s.Response!).ToArray();

        // Top-level usage and cost are views of the step list: sums over the steps that were answered.
        var usage = new EvalUsage(
            answered.Sum(r => r.Usage.InputTokens),
            answered.Sum(r => r.Usage.OutputTokens),
            answered.Sum(r => r.Usage.CacheReadTokens),
            answered.Sum(r => r.Usage.CacheWrite5mTokens),
            answered.Sum(r => r.Usage.CacheWrite1hTokens));

        // F8: an unpriced answered step makes the total unknown, never a partial sum or zero. With no answered step
        // there is nothing to price, so the cost is null without a warning.
        string? costUsd = null;
        string? costWarning = null;
        if (answered.Length > 0)
        {
            var unpriced = answered.FirstOrDefault(r => r.Cost?.AmountUsd is null);
            if (unpriced is null)
            {
                costUsd = answered.Sum(r => r.Cost!.AmountUsd!.Value).ToString("F8", CultureInfo.InvariantCulture);
            }
            else
            {
                costWarning = unpriced.Cost?.Warning;
            }
        }

        // Incurred cost (D-29): unknown (null) as soon as one step's is.
        var incurredAmounts = result.Steps.Select(DecisionStepBody.IncurredAmount).ToArray();
        var incurredCostUsd = incurredAmounts.Any(a => a is null)
            ? null
            : incurredAmounts.Sum(a => a!.Value).ToString("F8", CultureInfo.InvariantCulture);

        return new DecisionResponse(
            DecisionEndpoint.ContractVersion,
            caseId,
            traceId,
            new DecisionEffective(
                result.Model,
                result.PromptVersion,
                result.MaxSteps,
                result.SchemaSha256,
                result.ToolsSha256,
                pricingVersion,
                cacheMode,
                replicateSalt),
            DecisionOutcomeBody.From(result),
            steps,
            usage,
            costUsd,
            costWarning,
            incurredCostUsd,
            latencyMs);
    }
}

internal sealed record DecisionEffective(
    string Model,
    string PromptVersion,
    int MaxSteps,
    string SchemaSha256,
    string? ToolsSha256,
    string PricingVersion,
    string CacheMode,
    string? ReplicateSalt);

internal sealed record DecisionOutcomeBody(
    string Status,
    JsonNode? Decision,
    JsonNode? ModelDecision,
    string? DecidedBy,
    DecisionGuardBody? Guard,
    DecisionToolFailureBody? ToolFailure,
    EvalFailure? Failure,
    string? RawOutput)
{
    public static DecisionOutcomeBody From(AgentResult result) => new(
        Snake(result.Status),
        Serialize(result.Decision),
        Serialize(result.ModelDecision),
        result.DecidedBy is { } decidedBy ? Snake(decidedBy) : null,
        result.Guard is { } guard
            ? new DecisionGuardBody(guard.ExtractionStatus, guard.Overridden, DecisionToolCallBody.From(guard.ToolCall))
            : null,
        result.ToolFailure is { } failure
            ? new DecisionToolFailureBody(failure.Tool, failure.Source, failure.ErrorKind, failure.ErrorMessage)
            : null,
        result.Failure is { } modelFailure
            ? new EvalFailure(modelFailure.Kind, modelFailure.HttpStatus, modelFailure.RequestId, modelFailure.Message)
            : null,
        result.RawOutput);

    /// <summary>The wire name of a status or decided-by value: the enum member in snake_case.</summary>
    private static string Snake<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static JsonNode? Serialize(Decision? decision) =>
        decision is null ? null : JsonSerializer.SerializeToNode(decision, Wire.Options);
}

internal sealed record DecisionGuardBody(string ExtractionStatus, bool Overridden, DecisionToolCallBody ToolCall);

internal sealed record DecisionToolFailureBody(string Tool, string Source, string ErrorKind, string ErrorMessage);

/// <summary>One model call. Usage, cost and latency are null when the call produced no response.</summary>
internal sealed record DecisionStepBody(
    int Index,
    EvalUsage? Usage,
    string? CostUsd,
    [property: JsonPropertyName("cost_warning")] string? CostWarning,
    string? IncurredCostUsd,
    long? LatencyMs,
    string? StopReason,
    string? ModelReturned,
    string? ProviderMessageId,
    int? HttpAttempts,
    EvalCache? Cache,
    EvalFailure? Failure,
    IReadOnlyList<DecisionToolCallBody> ToolCalls)
{
    /// <summary>What this step cost the caller (D-29); the same rule as an extraction attempt.</summary>
    public static decimal? IncurredAmount(AgentStep step) =>
        IncurredCost.Of(step.Response, step.Failure is { Kind: "cache_miss" } ? LlmFailureKind.CacheMiss : null);

    public static DecisionStepBody From(AgentStep step)
    {
        var response = step.Response;
        return new DecisionStepBody(
            step.Index,
            response is null
                ? null
                : new EvalUsage(
                    response.Usage.InputTokens,
                    response.Usage.OutputTokens,
                    response.Usage.CacheReadTokens,
                    response.Usage.CacheWrite5mTokens,
                    response.Usage.CacheWrite1hTokens),
            response?.Cost?.AmountUsd?.ToString("F8", CultureInfo.InvariantCulture),
            response?.Cost?.Warning,
            IncurredAmount(step)?.ToString("F8", CultureInfo.InvariantCulture),
            response is null ? null : (long)response.Latency.TotalMilliseconds,
            response is null ? null : EvalAttempt.StopReasonName(response),
            response?.ModelReturned,
            response?.ProviderMessageId,
            response?.HttpAttempts,
            response?.Cache is { } cache
                ? new EvalCache(
                    LlmCacheModes.ToWire(cache.Mode),
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(cache.Status.ToString()),
                    cache.Key,
                    cache.Stored)
                : null,
            step.Failure is { } failure
                ? new EvalFailure(failure.Kind, failure.HttpStatus, failure.RequestId, failure.Message)
                : null,
            [.. step.ToolCalls.Select(DecisionToolCallBody.From)]);
    }
}

/// <summary>One tool call; <paramref name="Arguments"/> is the parsed JSON the model sent, or null when it was not JSON.</summary>
internal sealed record DecisionToolCallBody(
    string? Id,
    string Name,
    JsonNode? Arguments,
    string Source,
    string Outcome,
    string? ErrorKind,
    string? ErrorMessage,
    long? LatencyMs)
{
    public static DecisionToolCallBody From(AgentToolCall call) => new(
        call.Id,
        call.Name,
        ParseArguments(call.ArgumentsJson),
        call.Source,
        call.Outcome,
        call.ErrorKind,
        call.ErrorMessage,
        call.Latency is { } latency ? (long)latency.TotalMilliseconds : null);

    private static JsonNode? ParseArguments(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
