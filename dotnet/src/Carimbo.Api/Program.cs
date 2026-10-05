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
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Carimbo.Api;

/// <summary>Composition root. Tests and hosts register their own <see cref="ILlmGateway"/> through the callbacks.</summary>
public static class CarimboApi
{
    private const string EvalKeyConfig = "CARIMBO_EVAL_API_KEY";
    private const string EvalRoute = "/eval/extractions";
    private const string ApiKeyHeader = "X-Api-Key";
    private const string DefaultUrl = "http://127.0.0.1:5080";
    private const long MaxEvalBodyBytes = 10 * 1024 * 1024;

    public static WebApplication CreateApp(
        string[] args,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);

        if (string.IsNullOrEmpty(builder.Configuration["urls"])
            && !builder.Configuration.GetSection("Kestrel:Endpoints").Exists())
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
            options.SerializerOptions.RespectNullableAnnotations = true;
        });

        builder.Services.AddSingleton(ExtractionContract.Default);
        builder.Services.AddSingleton(
            builder.Configuration.GetSection("Extraction").Get<ExtractionSettings>() ?? new ExtractionSettings());
        builder.Services.AddSingleton<IInvoiceExtractor, InvoiceExtractor>();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));

        var evalKey = app.Configuration[EvalKeyConfig];
        var environmentAllowed = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Eval");
        var keyConfigured = !string.IsNullOrEmpty(evalKey);
        var gatewayRegistered = app.Services.GetService<ILlmGateway>() is not null;

        if (environmentAllowed && keyConfigured && gatewayRegistered)
        {
            var expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(evalKey!));
            Func<HttpContext, Task<IResult>> handler = context => HandleEvalAsync(context, expectedKeyHash);
            app.MapPost(EvalRoute, handler);
            app.Logger.LogInformation("Eval endpoint {Route} is enabled.", EvalRoute);
        }
        else
        {
            var missing = new List<string>();
            if (!environmentAllowed)
            {
                missing.Add("environment is not Development or Eval");
            }

            if (!keyConfigured)
            {
                missing.Add($"{EvalKeyConfig} is not set");
            }

            if (!gatewayRegistered)
            {
                missing.Add("no ILlmGateway is registered");
            }

            app.Logger.LogInformation(
                "Eval endpoint {Route} is disabled: {Reasons}.",
                EvalRoute,
                string.Join("; ", missing));
        }

        return app;
    }

    private static async Task<IResult> HandleEvalAsync(HttpContext context, byte[] expectedKeyHash)
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

        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = MaxEvalBodyBytes;
        }

        var jsonOptions = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        EvalRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<EvalRequest>(jsonOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        catch (BadHttpRequestException ex)
        {
            return Results.StatusCode(ex.StatusCode);
        }

        if (request is null
            || request.ContractVersion != "1"
            || request.Document.MediaType != "application/pdf")
        {
            return Results.BadRequest();
        }

        var buffer = new byte[(request.Document.ContentBase64.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(request.Document.ContentBase64, buffer, out var written))
        {
            return Results.BadRequest();
        }

        var pdf = new ReadOnlyMemory<byte>(buffer, 0, written);

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
            return Results.Ok(EvalResponse.From(request.CaseId, traceId, result, stopwatch.ElapsedMilliseconds));
        }
        finally
        {
            ownedActivity?.Stop();
        }
    }
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
    long LatencyMs,
    string? StopReason,
    string? ModelReturned,
    string? ProviderMessageId)
{
    public static EvalResponse From(string caseId, string traceId, ExtractionResult result, long latencyMs)
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
                response?.Cost?.PricingVersion),
            EvalOutcome.From(result),
            new EvalUsage(
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheReadTokens,
                usage.CacheWrite5mTokens,
                usage.CacheWrite1hTokens),
            response?.Cost?.AmountUsd?.ToString(CultureInfo.InvariantCulture),
            latencyMs,
            response is null ? null : JsonNamingPolicy.SnakeCaseLower.ConvertName(response.StopReason.ToString()),
            response?.ModelReturned,
            response?.ProviderMessageId);
    }
}

internal sealed record EvalEffective(string Model, string PromptVersion, string SchemaSha256, string? PricingVersion);

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

internal static class Program
{
    public static void Main(string[] args) => CarimboApi.CreateApp(args).Run();
}
