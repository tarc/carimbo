using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

namespace Carimbo.Tools.Tests;

/// <summary>The Tools host in process, over the committed decision cases, plus helpers to open MCP clients on it.</summary>
internal sealed class ToolsTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ToolsTestHost(WebApplication app)
    {
        _app = app;
        Http = app.GetTestClient();
    }

    public HttpClient Http { get; }

    public static string DecisionDirectory { get; } = Repo.PathOf("data", "decision");

    public static async Task<ToolsTestHost> StartAsync(string? casesDirectory = null)
    {
        var app = CarimboTools.CreateApp(
            [],
            configureServices: null,
            configureBuilder: builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(CarimboTools.CasesDirectoryKey, casesDirectory ?? DecisionDirectory),
                ]);
            });
        await app.StartAsync();
        return new ToolsTestHost(app);
    }

    public Task<McpClient> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            Http,
            ownsHttpClient: false);
        return McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
