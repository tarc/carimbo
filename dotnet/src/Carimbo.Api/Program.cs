using System.Text.Json;
using System.Text.Json.Serialization;
using Carimbo.Extraction;
using Carimbo.Llm;

namespace Carimbo.Api;

/// <summary>Composition root. Tests and hosts register their own <see cref="ILlmGateway"/> through the callbacks.</summary>
public static class CarimboApi
{
    private const string DefaultUrl = "http://127.0.0.1:5080";

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

        // A factory, not a type registration: a host without a gateway must still start (the eval
        // route simply stays unmapped), and Development's build-time validation cannot see through a factory.
        builder.Services.AddSingleton<IInvoiceExtractor>(services => new InvoiceExtractor(
            services.GetRequiredService<ILlmGateway>(),
            services.GetRequiredService<ExtractionContract>(),
            services.GetRequiredService<ExtractionSettings>()));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
        EvalEndpoint.TryMap(app, app.Logger);

        return app;
    }
}

internal static class Program
{
    public static void Main(string[] args) => CarimboApi.CreateApp(args).Run();
}
