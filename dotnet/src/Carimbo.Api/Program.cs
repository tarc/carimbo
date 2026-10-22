using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carimbo.Agent;
using Carimbo.Extraction;
using Carimbo.Llm;
using Carimbo.Validation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Carimbo.Api;

/// <summary>Composition root. Tests and hosts register their own <see cref="ILlmGateway"/> through the callbacks.</summary>
public static class CarimboApi
{
    private const string DefaultUrl = "http://127.0.0.1:5080";

    // The repair budget multiplies paid calls, so it has a hard ceiling that configuration cannot lift.

    // One repair chain is up to MaxRepairs + 1 sequential provider calls and the timeout applies to each of them.
    private const int DefaultTimeoutSeconds = 300;

    // D-28: the Anthropic SDK is the single retry owner; nothing else retries a model call. Every retry may be billed,
    // so the count has a hard ceiling that configuration cannot lift.
    private const int DefaultMaxRetries = 2;

    private const int MaxRetriesLimit = 5;

    // D-27: the response cache is on by default in Development and Eval. The path resolves against the content
    // root (the working directory, so <repo>/.cache/llm for every just recipe) and .cache/ is git-ignored at any depth.
    private const string DefaultCacheDirectory = ".cache/llm";

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

        // Registered before the host callback so a test host can add its own exporter to the same provider.
        var tracingLog = RegisterTracing(builder.Services, builder.Configuration);

        builder.Services.AddSingleton(ExtractionContract.Default);
        var extractionSettings = builder.Configuration.GetSection("Extraction").Get<ExtractionSettings>() ?? new ExtractionSettings();
        if (extractionSettings.MaxRepairs is < 0 or > ExtractionSettings.MaxRepairsLimit)
        {
            throw new InvalidOperationException($"Extraction:MaxRepairs must be between 0 and {ExtractionSettings.MaxRepairsLimit}.");
        }

        if (extractionSettings.MaxTokens <= 0)
        {
            throw new InvalidOperationException("Extraction:MaxTokens must be positive.");
        }

        builder.Services.AddSingleton(extractionSettings);

        var agentSettings = builder.Configuration.GetSection("Agent").Get<AgentSettings>() ?? new AgentSettings();

        // The step budget multiplies paid calls, so it has a hard ceiling that configuration cannot lift (phase D-12).
        if (agentSettings.MaxSteps is < 1 or > AgentSettings.MaxStepsLimit)
        {
            throw new InvalidOperationException($"Agent:MaxSteps must be between 1 and {AgentSettings.MaxStepsLimit}.");
        }

        if (agentSettings.MaxTokens <= 0)
        {
            throw new InvalidOperationException("Agent:MaxTokens must be positive.");
        }

        if (agentSettings.ToolCallTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Agent:ToolCallTimeoutSeconds must be positive.");
        }

        if (!IsLoopbackHttpUrl(agentSettings.ToolsUrl))
        {
            throw new InvalidOperationException("Agent:ToolsUrl must be an absolute http or https URL on a loopback host.");
        }

        // The same rule the overrides.model path enforces: an unpriced model would spend with a null cost and slip past
        // the cost cap and the phase accounting.
        var pricingTable = LlmPricingTable.LoadEmbedded();
        if (string.IsNullOrWhiteSpace(agentSettings.Model) || !pricingTable.CanPrice(agentSettings.Model))
        {
            throw new InvalidOperationException(
                $"Agent:Model must be a model id the pricing table can price (pricing version {pricingTable.Version}).");
        }

        builder.Services.AddSingleton(agentSettings);
        builder.Services.AddSingleton(AgentContract.Default);

        var validationOptions = builder.Configuration.GetSection("Validation").Get<ValidationOptions>() ?? new ValidationOptions();
        if (validationOptions.Tolerance <= 0)
        {
            throw new InvalidOperationException("Validation:Tolerance must be positive.");
        }

        if (validationOptions.SumToleranceCap < validationOptions.Tolerance)
        {
            throw new InvalidOperationException("Validation:SumToleranceCap must not be below Validation:Tolerance.");
        }

        builder.Services.AddSingleton(validationOptions);
        builder.Services.AddSingleton(services => new InvoiceValidator(services.GetRequiredService<ValidationOptions>()));

        // The clock behind the default reference date; registered before the host callback so a test host can replace it.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // A factory, not a type registration: a host without a gateway must still start (the eval
        // route simply stays unmapped), and Development's build-time validation cannot see through a factory.
        builder.Services.AddSingleton<IInvoiceExtractor>(services => new InvoiceExtractor(
            services.GetRequiredService<ILlmGateway>(),
            services.GetRequiredService<ExtractionContract>(),
            services.GetRequiredService<ExtractionSettings>(),
            services.GetRequiredService<InvoiceValidator>()));

        // Like the extractor: a factory over the decorated gateway, so a host without a gateway still starts.
        builder.Services.AddSingleton<IDecisionAgent>(services => new AgentRunner(
            services.GetRequiredService<ILlmGateway>(),
            services.GetRequiredService<IAgentToolbox>(),
            services.GetRequiredService<AgentSettings>(),
            services.GetRequiredService<AgentContract>()));
        builder.Services.AddSingleton(pricingTable);
        configureServices?.Invoke(builder.Services);

        // After the host callback, so a test host's own toolbox wins. One MCP session per run, never shared.
        builder.Services.TryAddSingleton<IAgentToolbox>(_ => new McpAgentToolbox(
            new Uri(agentSettings.ToolsUrl),
            callTimeout: TimeSpan.FromSeconds(agentSettings.ToolCallTimeoutSeconds)));
        var gatewayLog = RegisterAnthropicGatewayWhenUnclaimed(builder.Services, builder.Configuration);
        var (cache, cacheLog) = ResolveCache(builder.Configuration, builder.Environment);
        if (cache is not null)
        {
            builder.Services.AddSingleton(cache);
        }

        DecorateGateway(builder.Services, cache);

        var app = builder.Build();
        app.Logger.LogInformation("{GatewayStatus}", gatewayLog);
        app.Logger.LogInformation("{CacheStatus}", cacheLog);
        app.Logger.LogInformation("{TracingStatus}", tracingLog);

        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
        EvalEndpoint.TryMap(app, app.Logger);
        DecisionEndpoint.TryMap(app, app.Logger);

        return app;
    }

    /// <summary>
    /// Registers OpenTelemetry tracing (D-31): the ASP.NET Core server activity, the <c>Carimbo.Llm</c> model-call
    /// source, the <c>Carimbo.Agent</c> agent and tool source, and the OTLP exporter only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so no span leaves the process by default (T-03-18). Returns the one
    /// startup line to log.
    /// </summary>
    private static string RegisterTracing(IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = NullIfBlank(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("carimbo-api"))
            .WithTracing(tracing =>
            {
                // D-31: AlwaysOn set in code records every request whatever the inbound traceparent's sampled flag
                // and makes OTEL_TRACES_SAMPLER ineffective, so a trace id an eval response reports always names a
                // trace holding its model-call spans. Whether spans leave the process is the exporter switch alone.
                tracing.SetSampler(new AlwaysOnSampler());
                tracing.AddSource(LlmTelemetry.SourceName);
                tracing.AddSource(AgentTelemetry.SourceName);
                tracing.AddAspNetCoreInstrumentation();
                if (endpoint is not null)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return endpoint is null
            ? "tracing: no exporter (OTEL_EXPORTER_OTLP_ENDPOINT unset)"
            : $"tracing: OTLP export to {endpoint}";
    }

    /// <summary>
    /// Registers <see cref="AnthropicLlmGateway"/> when the host registered no <see cref="ILlmGateway"/> of its own
    /// (tests and the scripted host win) and a provider key resolves. Returns the one startup line to log; it names
    /// the key's source variable or "missing", never a value (D-10).
    /// </summary>
    private static string RegisterAnthropicGatewayWhenUnclaimed(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(ILlmGateway) && !d.IsKeyedService))
        {
            return "model gateway: host-registered";
        }

        var (key, source) = ProviderKey.Resolve(configuration);
        if (key is null)
        {
            return "model gateway: none (provider key missing)";
        }

        var section = configuration.GetSection("Llm:Anthropic");
        var baseUrl = section["BaseUrl"];
        var timeoutSeconds = section.GetValue<int?>("TimeoutSeconds") ?? DefaultTimeoutSeconds;
        var maxRetries = section.GetValue<int?>("MaxRetries") ?? DefaultMaxRetries;
        if (timeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Llm:Anthropic:TimeoutSeconds must be positive.");
        }

        if (maxRetries is < 0 or > MaxRetriesLimit)
        {
            throw new InvalidOperationException("Llm:Anthropic:MaxRetries must be between 0 and 5.");
        }

        var options = new AnthropicLlmGatewayOptions(
            key,
            string.IsNullOrWhiteSpace(baseUrl) ? null : new Uri(baseUrl, UriKind.Absolute),
            TimeSpan.FromSeconds(timeoutSeconds),
            maxRetries);
        services.AddSingleton<ILlmGateway>(_ => new AnthropicLlmGateway(options));
        return $"model gateway: anthropic (key from {source}), timeout {timeoutSeconds}s per attempt, max retries {maxRetries}";
    }

    /// <summary>
    /// Reads <c>Llm:Cache:Directory</c> and <c>Llm:Cache:Mode</c> (blank means unset) and decides whether the response
    /// cache runs (D-19, D-27). It runs only in Development or Eval (the environments where the eval route exists),
    /// read_write at <c>.cache/llm</c> unless configured; anywhere else it is off and configuring it fails startup.
    /// Returns the settings (null when off) and the one startup line to log.
    /// </summary>
    private static (LlmCacheSettings? Settings, string Log) ResolveCache(IConfiguration configuration, IHostEnvironment environment)
    {
        var directory = NullIfBlank(configuration["Llm:Cache:Directory"]);
        var modeText = NullIfBlank(configuration["Llm:Cache:Mode"]);

        var mode = LlmCacheMode.ReadWrite;
        if (modeText is not null && !LlmCacheModes.TryParse(modeText, out mode))
        {
            throw new InvalidOperationException("Llm:Cache:Mode must be one of off, read_write, read_only, refresh.");
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Eval"))
        {
            if (directory is not null)
            {
                throw new InvalidOperationException(
                    $"Llm:Cache:Directory is set but the environment is {environment.EnvironmentName}: the response cache runs only in Development or Eval (D-19, D-27).");
            }

            if (modeText is not null && mode != LlmCacheMode.Off)
            {
                throw new InvalidOperationException(
                    $"Llm:Cache:Mode is {modeText} but the environment is {environment.EnvironmentName}: the response cache runs only in Development or Eval (D-19, D-27).");
            }

            return (null, $"response cache: off (environment {environment.EnvironmentName})");
        }

        if (mode == LlmCacheMode.Off)
        {
            return (null, "response cache: off (Llm:Cache:Mode off)");
        }

        // The directory is not created here: LlmCacheStore creates it on the first stored answer.
        var path = Path.GetFullPath(directory ?? DefaultCacheDirectory, environment.ContentRootPath);
        return (new LlmCacheSettings(path, mode), $"response cache: {LlmCacheModes.ToWire(mode)} at {path}");
    }

    /// <summary>
    /// True for an absolute http or https URL whose host is "localhost" or a loopback IP address (T-03.1-33): the
    /// agent's tool calls can never be pointed off-host by configuration.
    /// </summary>
    private static bool IsLoopbackHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && (string.Equals(uri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address)));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Wraps whichever <see cref="ILlmGateway"/> the host registered (scripted, test stub or the real adapter) so every
    /// response is priced from the versioned table and, when the cache is on, stored and replayed with that cost. The
    /// cache sits outside cost accounting, so a replayed answer carries its original cost, and tracing is outermost
    /// (D-31) so a cache hit is a span too. A host with no gateway is left alone.
    /// </summary>
    private static void DecorateGateway(IServiceCollection services, LlmCacheSettings? cache)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(ILlmGateway) && !d.IsKeyedService);
        if (original is null)
        {
            return;
        }

        services.Remove(original);
        services.Add(new ServiceDescriptor(
            typeof(ILlmGateway),
            provider =>
            {
                ILlmGateway gateway = new CostAccountingLlmGateway(
                    CreateOriginal(provider, original),
                    provider.GetRequiredService<LlmPricingTable>());
                if (cache is not null)
                {
                    gateway = new CachingLlmGateway(
                        gateway,
                        new LlmCacheStore(cache.Directory),
                        cache.DefaultMode,
                        provider.GetRequiredService<TimeProvider>());
                }

                return new TracingLlmGateway(gateway);
            },
            original.Lifetime));
    }

    private static ILlmGateway CreateOriginal(IServiceProvider provider, ServiceDescriptor original)
    {
        if (original.ImplementationInstance is ILlmGateway instance)
        {
            return instance;
        }

        if (original.ImplementationFactory is { } factory)
        {
            return (ILlmGateway)factory(provider);
        }

        return (ILlmGateway)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
    }
}

/// <summary>
/// Resolves the model provider key (D-10): <c>CARIMBO_ANTHROPIC_API_KEY</c> first, then
/// <c>ANTHROPIC_API_KEY</c>. A blank value counts as unset. The caller passes the result to the client
/// explicitly, so the SDK's own implicit environment default never decides which key is used.
/// </summary>
internal static class ProviderKey
{
    internal const string Primary = "CARIMBO_ANTHROPIC_API_KEY";
    internal const string Fallback = "ANTHROPIC_API_KEY";

    /// <returns>The key and the name of the variable it came from; a null key has source "missing".</returns>
    public static (string? Key, string Source) Resolve(IConfiguration configuration)
    {
        foreach (var name in new[] { Primary, Fallback })
        {
            var value = configuration[name];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return (value, name);
            }
        }

        return (null, "missing");
    }
}

internal static class Program
{
    public static void Main(string[] args) => CarimboApi.CreateApp(args).Run();
}
