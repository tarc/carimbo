using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Carimbo.Agent;
using Carimbo.Llm;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Xunit;
using static Carimbo.Api.Tests.DecisionEndpointTests;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// AGT-04 and D-31 through the real OpenTelemetry pipeline: one decision request exports an agent span, a tool span per
/// tool call and a model-call span per step on the trace id the response reports, and no span carries case content
/// (T-03.1-34). The in-memory exporter stands in for the OTLP receiver.
/// </summary>
public class DecisionTracingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Parent(string traceId) => $"00-{traceId}-00f067aa0ba902b7-01";

    private static Dictionary<string, string?> NoExporter() => new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty };

    private static Task<TestHost> StartAsync(
        ExportedSpans exported,
        InProcessTools tools,
        EvalEndpointTests.ScriptedGateway gateway) =>
        DecisionHost.StartAsync(
            "Development",
            Key,
            gateway,
            tools,
            NoExporter(),
            services => services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exported)));

    private static Activity[] OnTrace(ExportedSpans exported, string traceId, params string[] sources) =>
        exported.Snapshot()
            .Where(a => a.TraceId.ToString() == traceId && sources.Contains(a.Source.Name))
            .OrderBy(a => a.StartTimeUtc)
            .ToArray();

    [Fact]
    public async Task One_decision_request_exports_an_agent_span_five_tool_spans_and_two_model_calls_under_the_agent()
    {
        const string traceId = "5bf92f3577b34da6a3ce929d0e0e4b02";
        var exported = new ExportedSpans();
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await StartAsync(exported, tools, Dc001Model());

        var response = await host.PostToAsync(Route, DecisionBody("dc-001"), Key, Parent(traceId));
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(traceId, (string?)json["trace_id"]);

        var spans = OnTrace(exported, traceId, AgentTelemetry.SourceName, LlmTelemetry.SourceName);
        var agent = Assert.Single(spans, a => a.Source.Name == AgentTelemetry.SourceName && a.OperationName.StartsWith("invoke_agent", StringComparison.Ordinal));
        Assert.Equal("invoke_agent carimbo-decision", agent.DisplayName);
        Assert.Equal(ActivityKind.Internal, agent.Kind);
        Assert.Equal("invoke_agent", agent.GetTagItem(AgentTelemetry.OperationName));
        Assert.Equal("carimbo-decision", agent.GetTagItem(AgentTelemetry.AgentNameTag));
        Assert.Equal(6, Convert.ToInt32(agent.GetTagItem(AgentTelemetry.MaxSteps)));
        Assert.Equal(2, Convert.ToInt32(agent.GetTagItem(AgentTelemetry.Steps)));
        Assert.Equal("decided", agent.GetTagItem(AgentTelemetry.Status));
        Assert.Equal("approve", agent.GetTagItem(AgentTelemetry.Outcome));
        Assert.Equal("none", agent.GetTagItem(AgentTelemetry.Cause));
        Assert.Equal("model", agent.GetTagItem(AgentTelemetry.DecidedBy));
        Assert.Equal(false, agent.GetTagItem(AgentTelemetry.GuardOverridden));

        var toolSpans = spans.Where(a => a.DisplayName.StartsWith("execute_tool ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, toolSpans.Length);
        Assert.All(toolSpans, span =>
        {
            Assert.Equal(ActivityKind.Client, span.Kind);
            Assert.Equal(agent.SpanId, span.ParentSpanId);
            Assert.Equal("execute_tool", span.GetTagItem(AgentTelemetry.OperationName));
            Assert.Equal("ok", span.GetTagItem(AgentTelemetry.ToolOutcome));
        });
        Assert.Equal(4, toolSpans.Count(s => (string?)s.GetTagItem(AgentTelemetry.ToolSource) == "model"));
        var guard = Assert.Single(toolSpans, s => (string?)s.GetTagItem(AgentTelemetry.ToolSource) == "guard");
        Assert.Equal("execute_tool get_extraction_status", guard.DisplayName);
        Assert.Equal("get_extraction_status", guard.GetTagItem(AgentTelemetry.ToolName));

        var chats = spans.Where(a => a.Source.Name == LlmTelemetry.SourceName).ToArray();
        Assert.Equal(2, chats.Length);
        Assert.All(chats, chat =>
        {
            Assert.Equal("chat claude-haiku-4-5", chat.DisplayName);
            Assert.Equal(agent.SpanId, chat.ParentSpanId);
        });
    }

    [Fact]
    public async Task A_failing_tool_call_is_an_error_span_and_the_agent_span_names_the_cause()
    {
        const string traceId = "6bf92f3577b34da6a3ce929d0e0e4b03";
        var exported = new ExportedSpans();
        await using var tools = await InProcessTools.StartAsync();
        var gateway = new EvalEndpointTests.ScriptedGateway(request => Task.FromResult(
            StepOf(request) == 0
                ? ToolUse(request, Call(1, "get_invoice", "dc-999"))
                : throw new InvalidOperationException("The model must not be called again.")));
        await using var host = await StartAsync(exported, tools, gateway);

        var response = await host.PostToAsync(Route, DecisionBody("dc-999"), Key, Parent(traceId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spans = OnTrace(exported, traceId, AgentTelemetry.SourceName);
        var tool = Assert.Single(spans, s => s.DisplayName.StartsWith("execute_tool ", StringComparison.Ordinal));
        Assert.Equal("execute_tool get_invoice", tool.DisplayName);
        Assert.Equal("error", tool.GetTagItem(AgentTelemetry.ToolOutcome));
        Assert.Equal("is_error", tool.GetTagItem(AgentTelemetry.ErrorType));
        Assert.Equal(ActivityStatusCode.Error, tool.Status);
        var agent = Assert.Single(spans, s => s.DisplayName.StartsWith("invoke_agent ", StringComparison.Ordinal));
        Assert.Equal("tool_failed", agent.GetTagItem(AgentTelemetry.Cause));
        Assert.Equal("escalate", agent.GetTagItem(AgentTelemetry.Outcome));
        Assert.Equal("tool_failure", agent.GetTagItem(AgentTelemetry.DecidedBy));
    }

    [Fact]
    public async Task A_tool_name_the_model_invented_never_reaches_a_span_name_or_a_tag()
    {
        const string traceId = "7bf92f3577b34da6a3ce929d0e0e4b04";
        const string invented = "exfiltrate_the_secret_notes";
        var exported = new ExportedSpans();
        await using var tools = await InProcessTools.StartAsync();
        var gateway = new EvalEndpointTests.ScriptedGateway(request => Task.FromResult(
            StepOf(request) == 0
                ? ToolUse(request, Call(1, invented, "dc-001"))
                : throw new InvalidOperationException("The model must not be called again.")));
        await using var host = await StartAsync(exported, tools, gateway);

        await host.PostToAsync(Route, DecisionBody("dc-001"), Key, Parent(traceId));

        var spans = OnTrace(exported, traceId, AgentTelemetry.SourceName);
        var tool = Assert.Single(spans, s => s.DisplayName.StartsWith("execute_tool ", StringComparison.Ordinal));
        Assert.Equal("execute_tool unknown", tool.DisplayName);
        Assert.Equal("unknown_tool", tool.GetTagItem(AgentTelemetry.ErrorType));
        Assert.DoesNotContain(spans, s => s.DisplayName.Contains(invented, StringComparison.Ordinal));
        Assert.DoesNotContain(
            spans.SelectMany(s => s.TagObjects),
            tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture)!.Contains(invented, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_tag_key_is_allowlisted_and_no_tag_value_or_span_name_carries_case_content()
    {
        const string traceId = "8bf92f3577b34da6a3ce929d0e0e4b05";
        var exported = new ExportedSpans();
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await StartAsync(exported, tools, Dc001Model());

        await host.PostToAsync(Route, DecisionBody("dc-001"), Key, Parent(traceId));

        var spans = OnTrace(exported, traceId, AgentTelemetry.SourceName, LlmTelemetry.SourceName);
        Assert.NotEmpty(spans);
        var allowed = AgentTelemetry.AllowedTags.Union(LlmTelemetry.AllowedTags).ToHashSet(StringComparer.Ordinal);
        Assert.All(spans, span => Assert.All(span.TagObjects, tag => Assert.Contains(tag.Key, allowed)));

        // The ASP.NET Core server span has its own vocabulary, but it too must hold none of the case's content.
        var every = exported.Snapshot().Where(a => a.TraceId.ToString() == traceId).ToArray();
        string[] forbidden =
        [
            "CARIMBO0000177", "SINTETICA", "toolu_", Approve().Reason, "case_id", "dc-001", "da Rocha", "31260454324739000188550010004568011198220840",
        ];
        foreach (var span in every)
        {
            Assert.All(forbidden, text => Assert.DoesNotContain(text, span.DisplayName, StringComparison.OrdinalIgnoreCase));
            foreach (var tag in span.TagObjects)
            {
                var value = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                Assert.All(forbidden, text => Assert.DoesNotContain(text, value, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void The_agent_allowlist_holds_no_content_bearing_attribute()
    {
        string[] content =
        [
            "gen_ai.input.messages", "gen_ai.output.messages", "gen_ai.system_instructions", "gen_ai.tool.call.arguments",
            "gen_ai.tool.call.result", "gen_ai.tool.call.id",
        ];

        var allowed = AgentTelemetry.AllowedTags.ToHashSet(StringComparer.Ordinal);
        Assert.All(content, key => Assert.DoesNotContain(key, allowed));
        Assert.Contains(AgentTelemetry.ToolSource, allowed);
        Assert.Equal("Carimbo.Agent", AgentTelemetry.SourceName);
    }

    private sealed class ExportedSpans : ICollection<Activity>
    {
        private readonly List<Activity> items = [];

        public int Count
        {
            get
            {
                lock (items)
                {
                    return items.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public Activity[] Snapshot()
        {
            lock (items)
            {
                return [.. items];
            }
        }

        public void Add(Activity item)
        {
            lock (items)
            {
                items.Add(item);
            }
        }

        public void Clear()
        {
            lock (items)
            {
                items.Clear();
            }
        }

        public bool Contains(Activity item)
        {
            lock (items)
            {
                return items.Contains(item);
            }
        }

        public void CopyTo(Activity[] array, int arrayIndex)
        {
            lock (items)
            {
                items.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(Activity item)
        {
            lock (items)
            {
                return items.Remove(item);
            }
        }

        public IEnumerator<Activity> GetEnumerator() => ((IEnumerable<Activity>)Snapshot()).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
