using Carimbo.Decisions;
using ModelContextProtocol.AspNetCore;

namespace Carimbo.Tools;

/// <summary>
/// Composition root of the Tools host (AGT-01, D-35): a separate, stateless MCP server over Streamable HTTP that
/// serves the committed decision cases read-only. It binds loopback and answers only loopback host names.
/// </summary>
public static class CarimboTools
{
    /// <summary>The default bind address: loopback only.</summary>
    public const string DefaultUrl = "http://127.0.0.1:5090";

    /// <summary>The default decision case directory, resolved against the content root.</summary>
    public const string DefaultCasesDirectory = "data/decision";

    /// <summary>The host names a request may carry (ASP.NET Core host filtering answers 400 to any other).</summary>
    public const string DefaultAllowedHosts = "127.0.0.1;localhost;[::1]";

    /// <summary>The configuration key of the case directory.</summary>
    public const string CasesDirectoryKey = "Tools:CasesDirectory";

    /// <summary>Builds the host. Tests pass callbacks to swap the server and add configuration.</summary>
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

        // Host filtering is the DNS-rebinding control for an unauthenticated endpoint (T-03.1-18).
        if (string.IsNullOrEmpty(builder.Configuration["AllowedHosts"]))
        {
            builder.Configuration["AllowedHosts"] = DefaultAllowedHosts;
        }

        var configured = builder.Configuration[CasesDirectoryKey];
        var casesDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configured) ? DefaultCasesDirectory : configured,
            builder.Environment.ContentRootPath);
        if (!Directory.Exists(Path.Combine(casesDirectory, "cases"))
            || !File.Exists(Path.Combine(casesDirectory, "policy.json")))
        {
            throw new InvalidOperationException(
                $"{CasesDirectoryKey} must name a directory with a cases/ folder and a policy.json.");
        }

        builder.Services.AddSingleton(new CaseStore(casesDirectory));
        builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "carimbo-tools", Version = "0.1.0" })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.Logger.LogInformation("Tools host serves cases from {CasesDirectory}", casesDirectory);

        app.MapMcp("/mcp");
        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
        return app;
    }
}

internal static class Program
{
    public static void Main(string[] args) => CarimboTools.CreateApp(args).Run();
}
