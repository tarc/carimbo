using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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

    [Theory]
    [InlineData("json")]
    [InlineData("invalid_operation")]
    [InlineData("disposed")]
    public async Task A_fault_the_mcp_client_surfaces_outside_its_own_types_is_a_protocol_error_tool_failure_not_an_exception(string fault)
    {
        using var http = new HttpClient(new ThrowingHandler(fault));
        var model = Scripts.Never();

        var result = await Runner(model, new McpAgentToolbox(new Uri("http://localhost/mcp"), http)).DecideAsync("dc-001", AgentOverrides.None, Ct);

        Assert.Equal(0, model.Calls);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal(DecisionCause.ToolFailed, result.Decision!.Cause);
        Assert.Equal("tools/list", result.ToolFailure!.Tool);
        Assert.Equal("protocol_error", result.ToolFailure.ErrorKind);
    }

    private sealed class ThrowingHandler(string fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw fault switch
            {
                "json" => new JsonException("malformed JSON-RPC body"),
                "disposed" => new ObjectDisposedException("session"),
                _ => new InvalidOperationException("the session is closed"),
            };
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

    // ---------------------------------------------------------------- the status guard (D-10, user decision 2)

    private static ScriptedGateway Answering(Decision decision) => new((_, request) => Scripts.Answer(request, decision));

    private static ScriptedGateway AnsweringText(string text, LlmStopReason stop = LlmStopReason.EndTurn, string? detail = null) =>
        new((_, request) => Scripts.Answer(request, text, stop, detail));

    private static Task<AgentResult> Decide(ILlmGateway model, IAgentToolbox toolbox, string caseId = "dc-001", int maxSteps = 6) =>
        Runner(model, toolbox, maxSteps).DecideAsync(caseId, AgentOverrides.None, Ct);

    private static IAgentToolbox SuccessToolbox() => new FakeToolbox((name, _, _) => Task.FromResult(
        new AgentToolResult(false, name == "get_extraction_status" ? "{\"case_id\":\"dc-001\",\"status\":\"success\",\"failure\":null}" : "{}", null, null)));

    [Theory]
    [InlineData("dc-005", "refused", DecisionOutcome.Approve)]
    [InlineData("dc-008", "validation_failed", DecisionOutcome.Reject)]
    public async Task A_model_approve_or_reject_on_a_case_whose_extraction_did_not_succeed_is_overridden_and_both_decisions_are_kept(
        string caseId, string status, DecisionOutcome modelOutcome)
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var modelDecision = Scripts.Of(modelOutcome, DecisionCause.None);

        var result = await Decide(Answering(modelDecision), host.Toolbox(), caseId);

        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal(AgentDecidedBy.Guard, result.DecidedBy);
        Assert.Equal(DecisionOutcome.Escalate, result.Decision!.Outcome);
        Assert.Equal(DecisionCause.ExtractionNotSuccess, result.Decision.Cause);
        Assert.Empty(result.Decision.Evidence);
        Assert.Contains("extraction", result.Decision.Reason);
        Assert.Equal(Scripts.Json(modelDecision), Scripts.Json(result.ModelDecision!));
        var guard = result.Guard!;
        Assert.Equal(status, guard.ExtractionStatus);
        Assert.True(guard.Overridden);
        Assert.Equal("guard", guard.ToolCall.Source);
        Assert.Equal("get_extraction_status", guard.ToolCall.Name);
        Assert.Equal("ok", guard.ToolCall.Outcome);

        // The guard's call is not a step and is not listed among any step's tool calls.
        var step = Assert.Single(result.Steps);
        Assert.Empty(step.ToolCalls);
    }

    [Fact]
    public async Task A_model_escalation_with_another_cause_on_a_failed_extraction_is_normalised_without_an_override()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var modelDecision = Scripts.Of(DecisionOutcome.Escalate, DecisionCause.PolicyWarning);

        var result = await Decide(Answering(modelDecision), host.Toolbox(), "dc-005");

        Assert.Equal(AgentDecidedBy.Guard, result.DecidedBy);
        Assert.Equal(DecisionOutcome.Escalate, result.Decision!.Outcome);
        Assert.Equal(DecisionCause.ExtractionNotSuccess, result.Decision.Cause);
        Assert.False(result.Guard!.Overridden);
        Assert.Equal(DecisionCause.PolicyWarning, result.ModelDecision!.Cause);
    }

    [Fact]
    public async Task A_failing_guard_call_escalates_with_tool_failed_and_keeps_the_model_decision()
    {
        var toolbox = new FakeToolbox((name, _, _) => Task.FromResult(name == "get_extraction_status"
            ? new AgentToolResult(true, string.Empty, "is_error", "unknown case dc-001")
            : new AgentToolResult(false, "{}", null, null)));
        var modelDecision = Scripts.Approve();

        var result = await Decide(Answering(modelDecision), toolbox);

        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal(DecisionOutcome.Escalate, result.Decision!.Outcome);
        Assert.Equal(DecisionCause.ToolFailed, result.Decision.Cause);
        Assert.Equal("guard", result.ToolFailure!.Source);
        Assert.Equal("get_extraction_status", result.ToolFailure.Tool);
        Assert.Equal(Scripts.Json(modelDecision), Scripts.Json(result.ModelDecision!));
        Assert.Null(result.Guard);
    }

    [Fact]
    public async Task A_guard_answer_without_a_status_is_a_guard_tool_failure()
    {
        var toolbox = new FakeToolbox((_, _, _) => Task.FromResult(new AgentToolResult(false, "{\"case_id\":\"dc-001\"}", null, null)));

        var result = await Decide(Answering(Scripts.Approve()), toolbox);

        Assert.Equal(AgentDecidedBy.ToolFailure, result.DecidedBy);
        Assert.Equal("guard", result.ToolFailure!.Source);
        Assert.Equal("invalid_result", result.ToolFailure.ErrorKind);
    }

    // ---------------------------------------------------------------- typed failures (user decision 1)

    [Fact]
    public async Task A_refusal_is_a_typed_failure_with_no_decision_and_no_guard_call()
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());

        var result = await Decide(AnsweringText(string.Empty, LlmStopReason.Refusal, "policy"), toolbox);

        Assert.Equal(AgentStatus.Refused, result.Status);
        Assert.Equal(new AgentFailure("refused", null, null, "policy"), result.Failure);
        Assert.Null(result.Decision);
        Assert.Null(result.ModelDecision);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.Guard);
        Assert.Empty(toolbox.Calls);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task A_refusal_without_detail_has_an_empty_message()
    {
        var result = await Decide(AnsweringText(string.Empty, LlmStopReason.Refusal), SuccessToolbox());

        Assert.Equal(string.Empty, result.Failure!.Message);
    }

    [Fact]
    public async Task A_max_tokens_answer_is_truncated_and_never_parsed()
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());

        var result = await Decide(AnsweringText(Scripts.Json(Scripts.Approve()), LlmStopReason.MaxTokens), toolbox);

        Assert.Equal(AgentStatus.Truncated, result.Status);
        Assert.Equal("truncated", result.Failure!.Kind);
        Assert.Null(result.Decision);
        Assert.Empty(toolbox.Calls);
    }

    [Theory]
    [InlineData(LlmStopReason.StopSequence)]
    [InlineData(LlmStopReason.Other)]
    public async Task An_answer_with_any_stop_reason_but_end_turn_is_truncated_and_never_parsed_even_when_it_is_a_valid_decision(LlmStopReason stop)
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());

        var result = await Decide(AnsweringText(Scripts.Json(Scripts.Approve()), stop), toolbox);

        Assert.Equal(AgentStatus.Truncated, result.Status);
        Assert.Equal("unexpected_stop", result.Failure!.Kind);
        Assert.Null(result.Decision);
        Assert.Null(result.ModelDecision);
        Assert.Null(result.Guard);
        Assert.Empty(toolbox.Calls);
        Assert.Single(result.Steps);
    }

    public static TheoryData<string, string> BrokenDecisions() => new()
    {
        { "not_json", "not json" },
        { "extra_member", "{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\",\"extra\":1}" },
        { "enum_case", "{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"APPROVE-SENTINEL-5f3a\",\"cause\":\"none\"}" },
        { "null_element", "{\"evidence\":[null],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\"}" },
        { "missing_cause", "{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"approve\"}" },
    };

    [Theory]
    [MemberData(nameof(BrokenDecisions))]
    public async Task An_unparseable_or_schema_violating_decision_is_schema_invalid_with_paths_only(string shape, string text)
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());

        var result = await Decide(AnsweringText(text), toolbox);

        Assert.Equal(AgentStatus.SchemaInvalid, result.Status);
        Assert.Equal("schema_invalid", result.Failure!.Kind);
        Assert.Null(result.Decision);
        Assert.Null(result.ModelDecision);
        Assert.Equal(text, result.RawOutput);
        Assert.Empty(toolbox.Calls);
        Assert.DoesNotContain("SENTINEL-5f3a", result.Failure.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Failure.Message), shape);
    }

    [Fact]
    public async Task A_cache_miss_is_an_infrastructure_failure_that_records_the_failing_step()
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());
        var model = new ScriptedGateway((_, _) => throw new LlmGatewayException(LlmFailureKind.CacheMiss, "No cached answer.", null, "req_1"));

        var result = await Decide(model, toolbox);

        Assert.Equal(AgentStatus.InfrastructureFailure, result.Status);
        Assert.Equal(new AgentFailure("cache_miss", null, "req_1", "No cached answer."), result.Failure);
        Assert.Null(result.Decision);
        Assert.Empty(toolbox.Calls);
        var step = Assert.Single(result.Steps);
        Assert.Equal(0, step.Index);
        Assert.Null(step.Response);
        Assert.Equal(result.Failure, step.Failure);
    }

    [Fact]
    public async Task A_gateway_failure_after_a_tool_round_keeps_the_earlier_step_and_snake_cases_the_kind()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var model = new ScriptedGateway((step, request) => step == 0
            ? Scripts.ToolUse(request, Call(1, "get_policy", "dc-001"))
            : throw new LlmGatewayException(LlmFailureKind.RateLimit, "Too many requests.", 429));

        var result = await Decide(model, host.Toolbox());

        Assert.Equal(AgentStatus.InfrastructureFailure, result.Status);
        Assert.Equal("rate_limit", result.Failure!.Kind);
        Assert.Equal(429, result.Failure.HttpStatus);
        Assert.Equal(2, result.Steps.Count);
        Assert.NotNull(result.Steps[0].Response);
        Assert.Null(result.Steps[1].Response);
    }

    [Fact]
    public async Task Tool_use_answer_without_a_tool_call_is_schema_invalid()
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());
        var model = new ScriptedGateway((_, request) => Scripts.ToolUse(request));

        var result = await Decide(model, toolbox);

        Assert.Equal(AgentStatus.SchemaInvalid, result.Status);
        Assert.Equal(new AgentFailure("schema_invalid", null, null, "tool_use answer without a tool call"), result.Failure);
        Assert.Null(result.Decision);
        Assert.Empty(toolbox.Calls);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task An_empty_tool_use_answer_on_the_last_allowed_step_is_still_schema_invalid_never_budget_exhaustion()
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());
        var model = new ScriptedGateway((step, request) => step == 0
            ? Scripts.ToolUse(request, Call(1, "get_policy", "dc-001"))
            : Scripts.ToolUse(request));

        var result = await Decide(model, toolbox, maxSteps: 2);

        Assert.Equal(AgentStatus.SchemaInvalid, result.Status);
        Assert.Equal("tool_use answer without a tool call", result.Failure!.Message);
        Assert.Null(result.DecidedBy);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal([("get_policy", "{}")], toolbox.Calls.ToArray());
    }

    private static readonly DecisionOutcome[] Outcomes = [DecisionOutcome.Approve, DecisionOutcome.Reject, DecisionOutcome.Escalate];

    private static readonly DecisionCause[] Causes =
    [
        DecisionCause.None,
        DecisionCause.ExtractionNotSuccess,
        DecisionCause.PolicyWarning,
        DecisionCause.OverLimit,
        DecisionCause.ToolFailed,
        DecisionCause.StepBudgetExhausted,
    ];

    public static TheoryData<DecisionOutcome, DecisionCause, bool> OutcomeCausePairs()
    {
        var data = new TheoryData<DecisionOutcome, DecisionCause, bool>();
        foreach (var outcome in Outcomes)
        {
            foreach (var cause in Causes)
            {
                var allowed = outcome == DecisionOutcome.Escalate
                    ? cause is DecisionCause.ExtractionNotSuccess or DecisionCause.PolicyWarning or DecisionCause.OverLimit
                    : cause == DecisionCause.None;
                data.Add(outcome, cause, allowed);
            }
        }

        return data;
    }

    private static string WireName<T>(T value) => JsonSerializer.Serialize(value, Wire.Options).Trim('"');

    [Theory]
    [MemberData(nameof(OutcomeCausePairs))]
    public async Task Model_decision_with_a_cause_its_outcome_does_not_allow_is_schema_invalid(
        DecisionOutcome outcome, DecisionCause cause, bool allowed)
    {
        var toolbox = new RecordingToolbox(SuccessToolbox());
        var text = Scripts.Json(Scripts.Of(outcome, cause));

        var result = await Decide(AnsweringText(text), toolbox);

        Assert.Equal(allowed, AgentContract.ModelDecisionViolation(Scripts.Of(outcome, cause)) is null);
        if (allowed)
        {
            Assert.Equal(AgentStatus.Decided, result.Status);
            Assert.NotNull(result.Guard);
            Assert.Equal(outcome, result.ModelDecision!.Outcome);
            Assert.Equal(cause, result.ModelDecision.Cause);
            return;
        }

        Assert.Equal(AgentStatus.SchemaInvalid, result.Status);
        Assert.Null(result.Decision);
        Assert.Null(result.ModelDecision);
        Assert.Null(result.Guard);
        Assert.Equal(text, result.RawOutput);
        Assert.Empty(toolbox.Calls);
        Assert.Contains(WireName(outcome), result.Failure!.Message);
        Assert.Contains(WireName(cause), result.Failure.Message);
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
