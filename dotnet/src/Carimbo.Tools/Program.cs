using System.Reflection;
using Carimbo.Decisions;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
        var tools = CreateTools();
        builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "carimbo-tools", Version = "0.1.0" })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = tools.Select(tool => tool.ProtocolTool).ToList() }))
            .WithCallToolHandler((request, cancellationToken) => CallToolAsync(tools, request, cancellationToken));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.Logger.LogInformation("Tools host serves cases from {CasesDirectory}", casesDirectory);

        app.MapMcp("/mcp");
        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
        return app;
    }

    // The SDK keeps registered tools in a hash-ordered collection, so the served order would be an accident of
    // hashing. The order is part of every agent cache key (D-37), so the four tools are built here in catalog order
    // and served by explicit list and call handlers instead of the collection. The tools are still the SDK's own
    // McpServerTool instances, so argument binding and the error mapping (McpException -> IsError) are unchanged.
    private static IReadOnlyList<McpServerTool> CreateTools()
    {
        var methods = typeof(CaseTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .ToDictionary(pair => pair.Attribute!.Name!, pair => pair.Method);
        return CaseToolCatalog.Names
            .Select(name => McpServerTool.Create(
                methods[name],
                context => new CaseTools(context.Services!.GetRequiredService<CaseStore>())))
            .ToList();
    }

    private static ValueTask<CallToolResult> CallToolAsync(
        IReadOnlyList<McpServerTool> tools,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var name = request.Params?.Name;
        var tool = tools.FirstOrDefault(candidate => candidate.ProtocolTool.Name == name);
        return tool is null
            ? throw new McpProtocolException($"Unknown tool: '{name}'", McpErrorCode.InvalidParams)
            : tool.InvokeAsync(request, cancellationToken);
    }
}

internal static class Program
{
    public static void Main(string[] args) => CarimboTools.CreateApp(args).Run();
}
