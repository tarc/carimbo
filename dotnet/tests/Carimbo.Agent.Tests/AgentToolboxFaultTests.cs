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

    // What the other real-session tests here connect with: long enough that a cold start never trips it.
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(30);

    // Deliberately shorter than GenerousTimeout.
    private static readonly TimeSpan OverrunGuard = TimeSpan.FromSeconds(10);

    // A budget that never fires leaves this guard to fire. A caller cancellation propagates as OperationCanceledException
    // rather than AgentToolException, so the test fails in 10 s instead of passing slowly on the wrong budget.
    private static CancellationTokenSource Guard()
    {
        var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(OverrunGuard);
        return guard;
    }

    [Fact]
    public async Task A_tool_that_outlives_the_call_timeout_is_a_timeout_on_the_real_session()
    {
        await using var server = await FaultServer.StartAsync();
        await using var session = await server.Toolbox(ShortTimeout, GenerousTimeout).ConnectAsync(Ct);
        using var guard = Guard();

        var ex = await Assert.ThrowsAsync<AgentToolException>(() => session.CallAsync("slow_tool", "{}", guard.Token));

        Assert.Equal("timeout", ex.ErrorKind);
        Assert.DoesNotContain("slow_tool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_outlives_the_connect_timeout_is_a_timeout_even_with_a_longer_call_timeout()
    {
        using var http = new HttpClient(new SilentHandler());
        var toolbox = new McpAgentToolbox(new Uri("http://localhost/mcp"), http, GenerousTimeout, ShortTimeout);
        using var guard = Guard();

        var ex = await Assert.ThrowsAsync<AgentToolException>(() => toolbox.ConnectAsync(guard.Token));

        Assert.Equal("timeout", ex.ErrorKind);
    }

    [Fact]
    public async Task Without_a_connect_timeout_the_call_timeout_bounds_the_connect()
    {
        using var http = new HttpClient(new SilentHandler());
        var toolbox = new McpAgentToolbox(new Uri("http://localhost/mcp"), http, ShortTimeout);
        using var guard = Guard();

        var ex = await Assert.ThrowsAsync<AgentToolException>(() => toolbox.ConnectAsync(guard.Token));

        Assert.Equal("timeout", ex.ErrorKind);
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

    /// <summary>A host that accepts the request and never answers, so a connect cannot succeed however fast the machine is.</summary>
    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage();
        }
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

        public McpAgentToolbox Toolbox(TimeSpan callTimeout, TimeSpan? connectTimeout = null) =>
            new(new Uri("http://localhost/mcp"), _http, callTimeout, connectTimeout);

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
