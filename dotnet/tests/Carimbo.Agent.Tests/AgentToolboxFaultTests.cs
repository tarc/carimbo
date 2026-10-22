using System.ComponentModel;
using Carimbo.Domain;
using Carimbo.Llm;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Xunit;

namespace Carimbo.Agent.Tests;

/// <summary>
/// The failure handling of <see cref="McpAgentToolbox"/> against a real MCP session, and the redaction of model-chosen
/// strings in the response (AGT-03, T-03.1-25, T-03.1-34). These are the places where model-controlled text meets the
/// record and the traces.
/// </summary>
public class AgentToolboxFaultTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task A_tool_that_outlives_the_call_timeout_is_a_timeout_on_the_real_session()
    {
        await using var server = await FaultServer.StartAsync();
        await using var session = await server.Toolbox(ShortTimeout).ConnectAsync(Ct);

        var ex = await Assert.ThrowsAsync<AgentToolException>(() => session.CallAsync("slow_tool", "{}", Ct));

        Assert.Equal("timeout", ex.ErrorKind);
        Assert.DoesNotContain("slow_tool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_caller_cancellation_on_the_real_session_is_not_mapped_to_a_tool_failure()
    {
        await using var server = await FaultServer.StartAsync();
        await using var session = await server.Toolbox(TimeSpan.FromSeconds(30)).ConnectAsync(Ct);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CallAsync("slow_tool", "{}", cancel.Token));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{oops")]
    [InlineData("")]
    public async Task Arguments_that_are_not_a_json_object_are_invalid_arguments_before_any_call(string arguments)
    {
        await using var host = await InProcessToolsHost.StartAsync();
        await using var session = await host.Toolbox().ConnectAsync(Ct);

        var ex = await Assert.ThrowsAsync<AgentToolException>(() => session.CallAsync("get_policy", arguments, Ct));

        Assert.Equal("invalid_arguments", ex.ErrorKind);
        Assert.DoesNotContain("oops", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_error_text_longer_than_the_limit_is_clipped_to_the_limit()
    {
        await using var server = await FaultServer.StartAsync();
        await using var session = await server.Toolbox(TimeSpan.FromSeconds(30)).ConnectAsync(Ct);

        var result = await session.CallAsync("long_error", "{}", Ct);

        Assert.True(result.IsError);
        Assert.Equal("is_error", result.ErrorKind);
        Assert.Equal(McpAgentToolbox.MaxErrorLength, result.ErrorMessage!.Length);
    }

    [Fact]
    public async Task A_model_invented_tool_name_is_clipped_in_the_record_and_never_reaches_the_response_reason()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var name = "invented_" + new string('z', 191);
        Assert.Equal(200, name.Length);
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request, new LlmToolCall("toolu_01", name, "{\"case_id\":\"dc-001\"}")));
        var runner = new AgentRunner(model, host.Toolbox(), new AgentSettings(), AgentContract.Default);

        var result = await runner.DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal("unknown_tool", result.ToolFailure!.ErrorKind);
        Assert.Equal(64, result.ToolFailure.Tool.Length);
        Assert.Equal(name[..64], result.ToolFailure.Tool);
        Assert.DoesNotContain("invented_", result.Decision!.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.ToolFailure.Tool, result.Decision.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(name, result.ToolFailure.ErrorMessage, StringComparison.Ordinal);
    }

    [McpServerToolType]
    private sealed class FaultTools
    {
        [McpServerTool(Name = "slow_tool", ReadOnly = true), Description("Never finishes on its own.")]
        public static async Task<string> Slow(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "never";
        }

        [McpServerTool(Name = "long_error", ReadOnly = true), Description("Fails with a very long message.")]
        public static string LongError() => throw new McpException(new string('x', 500));
    }

    /// <summary>A second in-process MCP server, read-only tools that misbehave.</summary>
    private sealed class FaultServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http;

        private FaultServer(WebApplication app)
        {
            _app = app;
            _http = app.GetTestClient();
        }

        public static async Task<FaultServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddMcpServer()
                .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
                .WithTools<FaultTools>();
            var app = builder.Build();
            app.MapMcp("/mcp");
            await app.StartAsync();
            return new FaultServer(app);
        }

        public McpAgentToolbox Toolbox(TimeSpan callTimeout) => new(new Uri("http://localhost/mcp"), _http, callTimeout);

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
