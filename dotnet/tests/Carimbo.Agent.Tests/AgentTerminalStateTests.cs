using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Llm;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace Carimbo.Agent.Tests;

/// <summary>Tool failures and the step budget end in an escalation made by code, at once and in order (AGT-03, D-35).</summary>
public class AgentTerminalStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentRunner Runner(ILlmGateway gateway, IAgentToolbox toolbox, int maxSteps = 6, int timeoutSeconds = 30) =>
        new(gateway, toolbox, new AgentSettings { MaxSteps = maxSteps, ToolCallTimeoutSeconds = timeoutSeconds }, AgentContract.Default);

    private static LlmToolCall Call(int number, string name, string caseId) => Scripts.Call(number, name, caseId);

    private static ScriptedGateway EndlessToolUse() =>
        new((_, request) => Scripts.ToolUse(request, Call(1, "get_policy", "dc-001")));

    [Fact]
    public async Task A_tool_use_answer_on_the_last_step_escalates_with_step_budget_exhausted_and_runs_nothing()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var toolbox = new RecordingToolbox(host.Toolbox());
        var model = EndlessToolUse();

        var result = await Runner(model, toolbox, maxSteps: 2).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(2, model.Calls);
        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal(AgentDecidedBy.StepBudget, result.DecidedBy);
        Assert.Equal(DecisionOutcome.Escalate, result.Decision!.Outcome);
        Assert.Equal(DecisionCause.StepBudgetExhausted, result.Decision.Cause);
        Assert.Contains("budget of 2", result.Decision.Reason);
        Assert.Empty(result.Decision.Evidence);
        Assert.Null(result.ModelDecision);
        Assert.Null(result.Guard);
        Assert.Null(result.ToolFailure);
        Assert.Equal(2, result.Steps.Count);
        var skipped = Assert.Single(result.Steps[1].ToolCalls);
        Assert.Equal("skipped", skipped.Outcome);
        Assert.Equal("get_policy", skipped.Name);
        Assert.Null(skipped.Latency);

        // Only step 0's call reached the toolbox: nothing was run on the last step, and the guard never ran.
        Assert.Equal([("get_policy", "{}")], toolbox.Calls.ToArray());
    }

    [Fact]
    public async Task An_end_turn_answer_on_the_last_allowed_step_is_a_decision_not_budget_exhaustion()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var model = new ScriptedGateway((step, request) => step == 0
            ? Scripts.ToolUse(request, [.. Scripts.FourCalls("dc-001")])
            : Scripts.Answer(request, Scripts.Approve()));

        var result = await Runner(model, host.Toolbox(), maxSteps: 2).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal(AgentDecidedBy.Model, result.DecidedBy);
        Assert.Equal(DecisionOutcome.Approve, result.Decision!.Outcome);
        Assert.Equal(2, model.Calls);
    }

    [Fact]
    public async Task A_tool_error_result_ends_the_run_at_once_in_escalate_tool_failed()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request, Call(1, "get_invoice", "dc-999")));

        var result = await Runner(model, host.Toolbox()).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(1, model.Calls);
        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal(DecisionOutcome.Escalate, result.Decision!.Outcome);
        Assert.Equal(DecisionCause.ToolFailed, result.Decision.Cause);
        Assert.Contains("get_invoice", result.Decision.Reason);
        Assert.Empty(result.Decision.Evidence);
        Assert.Null(result.ModelDecision);
        Assert.Null(result.Guard);
        var failure = result.ToolFailure!;
        Assert.Equal("get_invoice", failure.Tool);
        Assert.Equal("model", failure.Source);
        Assert.Equal("is_error", failure.ErrorKind);
        Assert.Contains("unknown case dc-999", failure.ErrorMessage);
        var recorded = Assert.Single(Assert.Single(result.Steps).ToolCalls);
        Assert.Equal("error", recorded.Outcome);
        Assert.Equal("is_error", recorded.ErrorKind);
    }

    [Fact]
    public async Task An_unlisted_tool_name_is_unknown_tool_and_never_reaches_the_toolbox()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var toolbox = new RecordingToolbox(host.Toolbox());
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request, new LlmToolCall("toolu_01", "approve_payment", "{\"case_id\":\"dc-001\"}")));

        var result = await Runner(model, toolbox).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(1, model.Calls);
        Assert.Empty(toolbox.Calls);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal(DecisionCause.ToolFailed, result.Decision!.Cause);
        Assert.Equal("unknown_tool", result.ToolFailure!.ErrorKind);
        Assert.Equal("approve_payment", result.ToolFailure.Tool);
        Assert.Equal("model", result.ToolFailure.Source);
    }

    [Fact]
    public async Task After_a_failing_call_the_remaining_calls_of_that_answer_are_skipped_in_block_order()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var toolbox = new RecordingToolbox(host.Toolbox());
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(
            request,
            Call(1, "get_policy", "dc-001"),
            Call(2, "get_invoice", "dc-999"),
            Call(3, "get_validator_findings", "dc-001")));

        var result = await Runner(model, toolbox).DecideAsync("dc-001", AgentOverrides.None, Ct);

        var calls = Assert.Single(result.Steps).ToolCalls;
        Assert.Equal(["get_policy", "get_invoice", "get_validator_findings"], calls.Select(c => c.Name).ToArray());
        Assert.Equal(["ok", "error", "skipped"], calls.Select(c => c.Outcome).ToArray());
        Assert.Equal(["get_policy", "get_invoice"], toolbox.Calls.Select(c => c.Name).ToArray());
        Assert.Equal("get_invoice", result.ToolFailure!.Tool);
    }

    [Fact]
    public async Task Three_good_calls_return_their_results_in_block_order_in_the_next_request()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var model = new ScriptedGateway((step, request) => step == 0
            ? Scripts.ToolUse(
                request,
                Call(7, "get_policy", "dc-001"),
                Call(3, "get_validator_findings", "dc-001"),
                Call(5, "get_invoice", "dc-001"))
            : Scripts.Answer(request, Scripts.Approve()));

        var result = await Runner(model, host.Toolbox()).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(AgentStatus.Decided, result.Status);
        var results = model.Requests[1].FollowUps[1].ToolResults;
        Assert.Equal(["toolu_07", "toolu_03", "toolu_05"], results.Select(r => r.ToolUseId).ToArray());
        var store = new CaseStore(InProcessToolsHost.DecisionDirectory);
        var @case = store.Load("dc-001");
        Assert.Equal(
            [CaseToolCatalog.PolicyJson(store.LoadPolicy()), CaseToolCatalog.FindingsJson(@case), CaseToolCatalog.InvoiceJson(@case)],
            results.Select(r => r.Content).ToArray());
    }

    [Fact]
    public async Task An_unreachable_tools_host_escalates_before_any_model_call()
    {
        var port = FreePort();
        var toolbox = new McpAgentToolbox(new Uri($"http://127.0.0.1:{port}/mcp"));
        var model = Scripts.Never();

        var result = await Runner(model, toolbox).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(0, model.Calls);
        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal(DecisionCause.ToolFailed, result.Decision!.Cause);
        Assert.Equal("tools/list", result.ToolFailure!.Tool);
        Assert.Equal("list", result.ToolFailure.Source);
        Assert.Equal("transport_error", result.ToolFailure.ErrorKind);
        Assert.Null(result.ToolsSha256);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public async Task A_server_that_lists_a_tool_that_is_not_read_only_is_refused_before_any_model_call()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateless).WithTools<WritingTools>();
        await using var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(Ct);
        using var http = app.GetTestClient();
        var model = Scripts.Never();

        var result = await Runner(model, new McpAgentToolbox(new Uri("http://localhost/mcp"), http)).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(0, model.Calls);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal(DecisionCause.ToolFailed, result.Decision!.Cause);
        Assert.Equal("tools/list", result.ToolFailure!.Tool);
        Assert.Equal("list", result.ToolFailure.Source);
        Assert.Equal("not_read_only", result.ToolFailure.ErrorKind);
        await app.StopAsync(Ct);
    }

    [Fact]
    public async Task A_tool_call_that_never_completes_times_out_as_a_tool_failure()
    {
        var toolbox = new FakeToolbox(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new AgentToolResult(false, "never", null, null);
        });
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request, Call(1, "get_policy", "dc-001")));

        var result = await Runner(model, toolbox, timeoutSeconds: 1).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(1, model.Calls);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal("timeout", result.ToolFailure!.ErrorKind);
        Assert.Equal("get_policy", result.ToolFailure.Tool);
    }

    [Fact]
    public async Task A_timeout_is_reported_even_by_a_tool_that_ignores_cancellation()
    {
        var toolbox = new FakeToolbox((_, _, _) => new TaskCompletionSource<AgentToolResult>().Task);
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request, Call(1, "get_policy", "dc-001")));

        var result = await Runner(model, toolbox, timeoutSeconds: 1).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal("timeout", result.ToolFailure!.ErrorKind);
    }

    [Fact]
    public async Task A_caller_cancellation_during_a_tool_call_propagates()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var started = new TaskCompletionSource();
        var toolbox = new FakeToolbox(async (_, _, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new AgentToolResult(false, "never", null, null);
        });
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request, Call(1, "get_policy", "dc-001")));

        var run = Runner(model, toolbox, timeoutSeconds: 30).DecideAsync("dc-001", AgentOverrides.None, cancel.Token);
        await started.Task;
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Nothing_can_execute_a_decision_and_only_the_four_read_only_tools_are_ever_offered()
    {
        var publicMethods = typeof(AgentRunner).Assembly.GetTypes()
            .Where(type => typeof(IDecisionAgent).IsAssignableFrom(type) && !type.IsInterface)
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();
        Assert.Equal(["DecideAsync"], publicMethods);

        await using var host = await InProcessToolsHost.StartAsync();
        var model = new ScriptedGateway((step, request) => step == 0
            ? Scripts.ToolUse(request, [.. Scripts.FourCalls("dc-001")])
            : Scripts.Answer(request, Scripts.Approve()));
        await Runner(model, host.Toolbox()).DecideAsync("dc-001", AgentOverrides.None, Ct);
        Assert.All(model.Requests, request => Assert.Equal(CaseToolCatalog.Names, request.Tools.Select(t => t.Name).ToArray()));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [McpServerToolType]
    private sealed class WritingTools
    {
        [McpServerTool(Name = "delete_case", ReadOnly = false, Destructive = true), Description("Deletes a case.")]
        public static string DeleteCase([Description("The case id.")] string case_id) => "deleted " + case_id;
    }
}
