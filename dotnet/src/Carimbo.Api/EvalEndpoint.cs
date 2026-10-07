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
    private const string ContractVersion = "1";
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
            var result = await extractor.ExtractAsync(pdf, context.RequestAborted);
            stopwatch.Stop();
            var pricingVersion = context.RequestServices.GetRequiredService<LlmPricingTable>().Version;
            return Results.Ok(EvalResponse.From(request.CaseId, traceId, result, stopwatch.ElapsedMilliseconds, pricingVersion));
        }
        finally
        {
            ownedActivity?.Stop();
        }
    }

    /// <summary>A 400 naming the offending field, never echoing the payload.</summary>
    private static IResult Invalid(string field, string problem) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [problem] });
}

internal sealed record EvalRequest(string ContractVersion, string CaseId, EvalDocument Document);

internal sealed record EvalDocument(string MediaType, string ContentBase64);

internal sealed record EvalResponse(
    string ContractVersion,
    string CaseId,
    string TraceId,
    EvalEffective Effective,
    EvalOutcome Outcome,
    EvalUsage Usage,
    string? CostUsd,
    [property: JsonPropertyName("cost_warning")] string? CostWarning,
    long LatencyMs,
    string? StopReason,
    string? ModelReturned,
    string? ProviderMessageId)
{
    public static EvalResponse From(
        string caseId,
        string traceId,
        ExtractionResult result,
        long latencyMs,
        string pricingVersion)
    {
        var response = result.Response;
        var usage = response?.Usage ?? LlmUsage.Zero;
        return new EvalResponse(
            "1",
            caseId,
            traceId,
            new EvalEffective(
                result.ModelRequested,
                result.PromptVersion,
                result.SchemaSha256,
                pricingVersion),
            EvalOutcome.From(result),
            new EvalUsage(
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheReadTokens,
                usage.CacheWrite5mTokens,
                usage.CacheWrite1hTokens),
            response?.Cost?.AmountUsd?.ToString("F8", CultureInfo.InvariantCulture),
            response?.Cost?.Warning,
            latencyMs,
            response is null ? null : JsonNamingPolicy.SnakeCaseLower.ConvertName(response.StopReason.ToString()),
            response?.ModelReturned,
            response?.ProviderMessageId);
    }
}

internal sealed record EvalEffective(string Model, string PromptVersion, string SchemaSha256, string PricingVersion);

internal sealed record EvalOutcome(string Status, JsonNode? Invoice, EvalFailure? Failure, string? RawOutput)
{
    public static EvalOutcome From(ExtractionResult result)
    {
        var outcome = result.Outcome;
        return new EvalOutcome(
            outcome.Status,
            outcome is ExtractionOutcome.Success success
                ? JsonSerializer.SerializeToNode(success.Invoice, Wire.Options)
                : null,
            DescribeFailure(outcome),
            result.RawOutput);
    }

    private static EvalFailure? DescribeFailure(ExtractionOutcome outcome) => outcome switch
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

internal sealed record EvalUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    [property: JsonPropertyName("cache_write_5m_tokens")] long CacheWrite5mTokens,
    [property: JsonPropertyName("cache_write_1h_tokens")] long CacheWrite1hTokens);
