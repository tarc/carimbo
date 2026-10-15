using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Carimbo.Domain;
using Carimbo.Extraction;
using Carimbo.Llm;
using Carimbo.Validation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Carimbo.Api;

/// <summary>
/// The eval endpoint: availability policy, static-key authentication, request validation and
/// response mapping. The route exists only where <see cref="TryMap"/> says so; elsewhere it is a 404.
/// </summary>
internal static class EvalEndpoint
{
    internal const string Route = "/eval/extractions";
    internal const string KeyConfig = "CARIMBO_EVAL_API_KEY";
    internal const long MaxBodyBytes = 10 * 1024 * 1024;

    private const string ApiKeyHeader = "X-Api-Key";
    private const string ContractVersion = "3";
    private const string PdfMediaType = "application/pdf";

    /// <summary>
    /// Maps <c>POST /eval/extractions</c> only in Development or Eval, with a configured
    /// <c>CARIMBO_EVAL_API_KEY</c> and a registered <see cref="ILlmGateway"/>. Logs one line naming
    /// each unmet condition (never a value).
    /// </summary>
    /// <returns><see langword="true"/> when the route was mapped.</returns>
    public static bool TryMap(WebApplication app, ILogger logger)
    {
        var evalKey = app.Configuration[KeyConfig];
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
                unmet.Add($"{KeyConfig} is not set");
            }

            if (!gatewayRegistered)
            {
                unmet.Add("no ILlmGateway is registered");
            }

            logger.LogInformation("Eval endpoint {Route} is disabled: {Reasons}.", Route, string.Join("; ", unmet));
            return false;
        }

        // Only the hash of the key is kept, and nothing about a request is remembered between calls.
        var expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(evalKey!));
        Func<HttpContext, Task<IResult>> handler = context => HandleAsync(context, expectedKeyHash);
        app.MapPost(Route, handler).WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
        logger.LogInformation("Eval endpoint {Route} is enabled.", Route);
        return true;
    }

    private static async Task<IResult> HandleAsync(HttpContext context, byte[] expectedKeyHash)
    {
        // Authenticate before touching the body, so an unauthenticated caller learns nothing.
        var provided = context.Request.Headers[ApiKeyHeader].ToString();
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        if (provided.Length == 0 || !CryptographicOperations.FixedTimeEquals(providedHash, expectedKeyHash))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        if (!context.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        // TestServer does not enforce the server body limit, and a client can lie or omit the header,
        // so the declared length is checked here and the server limit is set as well.
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        var jsonOptions = context.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        EvalRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<EvalRequest>(jsonOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            return Invalid("body", "is not a valid eval request: unknown, missing or malformed field.");
        }
        catch (BadHttpRequestException ex)
        {
            return Results.StatusCode(ex.StatusCode);
        }

        if (request is null)
        {
            return Invalid("body", "is required.");
        }

        if (request.ContractVersion != ContractVersion)
        {
            return Invalid("contract_version", $"must be \"{ContractVersion}\".");
        }

        if (request.Document.MediaType != PdfMediaType)
        {
            return Invalid("document.media_type", $"must be \"{PdfMediaType}\".");
        }

        // Strict form only (T-02-23): a looser parse would let two spellings of one date reach the validator.
        DateOnly referenceDate;
        if (request.ReferenceDate is { } suppliedDate)
        {
            if (suppliedDate.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(
                    suppliedDate.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out referenceDate))
            {
                return Invalid("reference_date", "must be a date in YYYY-MM-DD form.");
            }
        }
        else
        {
            referenceDate = DateOnly.FromDateTime(
                context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
        }

        // Overrides stay raw JSON values until here, so a wrongly typed value is reported against its own field
        // and never echoed (T-03-10).
        LlmCacheMode? cacheMode = null;
        var cacheSettings = context.RequestServices.GetService<LlmCacheSettings>();
        if (request.Overrides?.CacheMode is { } suppliedMode)
        {
            if (suppliedMode.ValueKind != JsonValueKind.String
                || !LlmCacheModes.TryParse(suppliedMode.GetString(), out var parsedMode))
            {
                return Invalid("overrides.cache_mode", "must be one of off, read_write, read_only, refresh.");
            }

            // A request can never switch on a cache the host does not have (T-03-09).
            if (parsedMode != LlmCacheMode.Off && cacheSettings is null)
            {
                return Invalid("overrides.cache_mode", "needs a response cache: this host runs with the cache off.");
            }

            cacheMode = parsedMode;
        }

        // The salt only perturbs the cache key (D-27: a file name is the key digest, never the salt), but it is
        // still a tight allow-list so nothing odd reaches a key, a log line or a span (T-03-08).
        string? replicateSalt = null;
        if (request.Overrides?.ReplicateSalt is { } suppliedSalt)
        {
            if (suppliedSalt.ValueKind != JsonValueKind.String || !IsValidSalt(suppliedSalt.GetString()))
            {
                return Invalid(
                    "overrides.replicate_salt",
                    "must be 1 to 64 characters of A-Z, a-z, 0-9, '.', '_' or '-'.");
            }

            replicateSalt = suppliedSalt.GetString();
        }

        var buffer = new byte[(request.Document.ContentBase64.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(request.Document.ContentBase64, buffer, out var written))
        {
            return Invalid("document.content_base64", "is not valid base64.");
        }

        var pdf = new ReadOnlyMemory<byte>(buffer, 0, written);
        if (!pdf.Span.StartsWith("%PDF-"u8))
        {
            return Invalid("document.content_base64", "does not decode to a PDF (missing %PDF- header).");
        }

        // Normally ASP.NET already started an activity that honours an inbound traceparent. When
        // nothing did, start one here so the response always carries a real trace id.
        Activity? ownedActivity = null;
        if (Activity.Current is null)
        {
            ownedActivity = new Activity("Carimbo.Eval");
            if (ActivityContext.TryParse(context.Request.Headers["traceparent"].ToString(), null, out var parent))
            {
                ownedActivity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
            }

            ownedActivity.Start();
        }

        try
        {
            var traceId = (Activity.Current?.TraceId ?? default).ToString();
            var extractor = context.RequestServices.GetRequiredService<IInvoiceExtractor>();
            var stopwatch = Stopwatch.StartNew();
            var extractionContext = new ExtractionContext(referenceDate)
            {
                Overrides = new ExtractionOverrides { CacheMode = cacheMode, ReplicateSalt = replicateSalt },
            };
            var result = await extractor.ExtractAsync(pdf, extractionContext, context.RequestAborted);
            stopwatch.Stop();
            var pricingVersion = context.RequestServices.GetRequiredService<LlmPricingTable>().Version;
            var effectiveCacheMode = cacheMode ?? cacheSettings?.DefaultMode ?? LlmCacheMode.Off;
            return Results.Ok(EvalResponse.From(
                ContractVersion,
                request.CaseId,
                traceId,
                result,
                stopwatch.ElapsedMilliseconds,
                pricingVersion,
                LlmCacheModes.ToWire(effectiveCacheMode),
                replicateSalt));
        }
        finally
        {
            ownedActivity?.Stop();
        }
    }

    /// <summary>Ordinal ASCII allow-list; a regex <c>$</c> would also accept a trailing newline.</summary>
    private static bool IsValidSalt(string? salt) =>
        salt is { Length: >= 1 and <= 64 }
        && salt.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-');

    /// <summary>A 400 naming the offending field, never echoing the payload.</summary>
    private static IResult Invalid(string field, string problem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [problem] });
}

/// <summary>
/// The request body. <see cref="ReferenceDate"/> is optional and stays a raw JSON value here so that a
/// wrongly typed date (a number, say) is reported as a <c>reference_date</c> problem like any other bad date.
/// </summary>
internal sealed record EvalRequest(
    string ContractVersion,
    string CaseId,
    EvalDocument Document,
    JsonElement? ReferenceDate = null,
    EvalOverrides? Overrides = null);

/// <summary>
/// Per-request overrides (D-29). Each member stays a raw JSON value so a wrongly typed value is reported against
/// its own <c>overrides.&lt;field&gt;</c> name, like <see cref="EvalRequest.ReferenceDate"/>.
/// </summary>
internal sealed record EvalOverrides(JsonElement? CacheMode = null, JsonElement? ReplicateSalt = null);

internal sealed record EvalDocument(string MediaType, string ContentBase64);

internal sealed record EvalResponse(
    string ContractVersion,
    string CaseId,
    string TraceId,
    EvalEffective Effective,
    EvalOutcome Outcome,
    IReadOnlyList<EvalAttempt> Attempts,
    EvalUsage Usage,
    string? CostUsd,
    [property: JsonPropertyName("cost_warning")] string? CostWarning,
    long LatencyMs,
    string? StopReason,
    string? ModelReturned,
    string? ProviderMessageId,
    string? IncurredCostUsd)
{
    public static EvalResponse From(
        string contractVersion,
        string caseId,
        string traceId,
        ExtractionResult result,
        long latencyMs,
        string pricingVersion,
        string cacheMode,
        string? replicateSalt)
    {
        var attempts = result.Attempts.Select(EvalAttempt.From).ToArray();
        var answered = result.Attempts.Where(a => a.Response is not null).Select(a => a.Response!).ToArray();

        // Top-level usage and cost are views of the attempt list: sums over the attempts that were answered.
        var usage = new EvalUsage(
            answered.Sum(r => r.Usage.InputTokens),
            answered.Sum(r => r.Usage.OutputTokens),
            answered.Sum(r => r.Usage.CacheReadTokens),
            answered.Sum(r => r.Usage.CacheWrite5mTokens),
            answered.Sum(r => r.Usage.CacheWrite1hTokens));

        // F8: an unpriced answered attempt makes the total unknown, never a partial sum or zero. With no
        // answered attempt there is nothing to price, so the cost is null without a warning.
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

        // Incurred cost (D-29): what the run paid the provider. It is unknown (null) as soon as one attempt's
        // is, a provider failure after an answered attempt included, so the runner charges its reserve.
        var incurredAmounts = result.Attempts.Select(EvalAttempt.IncurredAmount).ToArray();
        var incurredCostUsd = incurredAmounts.Any(a => a is null)
            ? null
            : incurredAmounts.Sum(a => a!.Value).ToString("F8", CultureInfo.InvariantCulture);

        var last = answered.Length > 0 ? answered[^1] : null;
        return new EvalResponse(
            contractVersion,
            caseId,
            traceId,
            new EvalEffective(
                result.ModelRequested,
                result.PromptVersion,
                result.RepairPromptVersion,
                result.MaxRepairs,
                result.SchemaSha256,
                pricingVersion,
                result.ReferenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                cacheMode,
                replicateSalt),
            EvalOutcome.From(result),
            attempts,
            usage,
            costUsd,
            costWarning,
            latencyMs,
            last is null ? null : EvalAttempt.StopReasonName(last),
            last?.ModelReturned,
            last?.ProviderMessageId,
            incurredCostUsd);
    }
}

internal sealed record EvalEffective(
    string Model,
    string PromptVersion,
    string RepairPromptVersion,
    int MaxRepairs,
    string SchemaSha256,
    string PricingVersion,
    string ReferenceDate,
    string CacheMode,
    string? ReplicateSalt);

internal sealed record EvalOutcome(
    string Status,
    JsonNode? Invoice,
    IReadOnlyList<EvalFinding> Findings,
    EvalFailure? Failure,
    string? RawOutput)
{
    public static EvalOutcome From(ExtractionResult result) => new(
        result.Outcome.Status,
        EvalAttempt.InvoiceOf(result.Outcome),
        [.. result.Findings.Select(EvalFinding.From)],
        EvalAttempt.DescribeFailure(result.Outcome),
        result.RawOutput);
}

internal sealed record EvalFinding(string Field, string RuleId, string Expected, string Actual, string Severity)
{
    public static EvalFinding From(ValidationFinding finding) => new(
        finding.Field,
        finding.RuleId,
        finding.Expected,
        finding.Actual,
        JsonNamingPolicy.SnakeCaseLower.ConvertName(finding.Severity.ToString()));
}

/// <summary>One model call. Usage, cost and latency are null when the call produced no response.</summary>
internal sealed record EvalAttempt(
    int Index,
    string Kind,
    string PromptVersion,
    string Status,
    string? RawOutput,
    JsonNode? Invoice,
    IReadOnlyList<EvalFinding> Findings,
    EvalUsage? Usage,
    string? CostUsd,
    [property: JsonPropertyName("cost_warning")] string? CostWarning,
    long? LatencyMs,
    string? StopReason,
    string? ModelReturned,
    string? ProviderMessageId,
    int? HttpAttempts,
    EvalFailure? Failure,
    EvalCache? Cache,
    string? IncurredCostUsd)
{
    /// <summary>
    /// What this attempt cost the caller (D-29): zero for a cache hit and for a cache miss that made no provider
    /// call, the notional cost for any other answered call (null when unpriced), null for every other failure,
    /// which may or may not have been billed.
    /// </summary>
    public static decimal? IncurredAmount(ExtractionAttempt attempt)
    {
        var response = attempt.Response;
        if (response is null)
        {
            return attempt.Outcome is ExtractionOutcome.InfrastructureFailure { Kind: LlmFailureKind.CacheMiss }
                ? 0m
                : null;
        }

        return response.Cache?.Status == LlmCacheStatus.Hit ? 0m : response.Cost?.AmountUsd;
    }

    public static EvalAttempt From(ExtractionAttempt attempt)
    {
        var response = attempt.Response;
        return new EvalAttempt(
            attempt.Index,
            JsonNamingPolicy.SnakeCaseLower.ConvertName(attempt.Kind.ToString()),
            attempt.PromptVersion,
            attempt.Outcome.Status,
            attempt.RawOutput,
            InvoiceOf(attempt.Outcome),
            [.. attempt.Findings.Select(EvalFinding.From)],
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
            response is null ? null : (long)response.Latency.TotalMilliseconds,
            response is null ? null : StopReasonName(response),
            response?.ModelReturned,
            response?.ProviderMessageId,
            response?.HttpAttempts,
            DescribeFailure(attempt.Outcome),
            response?.Cache is { } cache
                ? new EvalCache(
                    LlmCacheModes.ToWire(cache.Mode),
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(cache.Status.ToString()),
                    cache.Key,
                    cache.Stored)
                : null,
            IncurredAmount(attempt)?.ToString("F8", CultureInfo.InvariantCulture));
    }

    public static string StopReasonName(LlmResponse response) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(response.StopReason.ToString());

    /// <summary>The invoice a client can inspect: the parsed one on success, the candidate when validation failed.</summary>
    public static JsonNode? InvoiceOf(ExtractionOutcome outcome) => outcome switch
    {
        ExtractionOutcome.Success success => JsonSerializer.SerializeToNode(success.Invoice, Wire.Options),
        ExtractionOutcome.ValidationFailed failed => JsonSerializer.SerializeToNode(failed.Candidate, Wire.Options),
        _ => null,
    };

    public static EvalFailure? DescribeFailure(ExtractionOutcome outcome) => outcome switch
    {
        ExtractionOutcome.InfrastructureFailure f => new EvalFailure(
            JsonNamingPolicy.SnakeCaseLower.ConvertName(f.Kind.ToString()),
            f.HttpStatus,
            f.RequestId,
            f.Message),
        ExtractionOutcome.SchemaInvalid s => new EvalFailure("schema_invalid", null, null, s.Error),
        ExtractionOutcome.Refused r => new EvalFailure("refused", null, null, r.Detail ?? string.Empty),
        ExtractionOutcome.Truncated => new EvalFailure("truncated", null, null, "The model hit the output token cap."),
        _ => null,
    };
}

internal sealed record EvalFailure(string Kind, int? HttpStatus, string? RequestId, string Message);

/// <summary>What the response cache did for one attempt (D-29); the attempt's <c>cache</c> is null when none was consulted.</summary>
internal sealed record EvalCache(string Mode, string Status, string Key, bool Stored);

internal sealed record EvalUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    [property: JsonPropertyName("cache_write_5m_tokens")] long CacheWrite5mTokens,
    [property: JsonPropertyName("cache_write_1h_tokens")] long CacheWrite1hTokens);
