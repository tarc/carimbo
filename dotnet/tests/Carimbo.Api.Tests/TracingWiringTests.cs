using System.Collections;
using System.Diagnostics;
using System.Net;
using Carimbo.Llm;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Xunit;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// Success criterion 2 and LLM-03 (D-31) through the real OpenTelemetry pipeline: the Api registers tracing, and
/// every model call of one eval request is exported as one span on the trace id the response reports. The
/// in-memory exporter stands in for the OTLP receiver; the sampler guarantee rests on <c>AlwaysOnSampler</c> set
/// in code, not on the SDK defaults.
/// </summary>
public class TracingWiringTests
{
    private const string InboundTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Sampled = "00-" + InboundTraceId + "-00f067aa0ba902b7-01";
    private const string Unsampled = "00-" + InboundTraceId + "-00f067aa0ba902b7-00";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> NoExporter() => new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty };

    private static ScriptedGateway HaikuGateway() =>
        new(_ => Task.FromResult(Response(ValidInvoiceJson(), model: "claude-haiku-4-5")));

    private static Task<TestHost> StartAsync(
        ExportedActivities exported,
        ScriptedGateway gateway,
        Dictionary<string, string?>? config = null) =>
        TestHost.StartAsync(
            "Development",
            Key,
            gateway,
            config ?? NoExporter(),
            services => services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exported)));

    private static Activity[] ModelCalls(ExportedActivities exported, string traceId) =>
        exported.Snapshot()
            .Where(a => a.Source.Name == LlmTelemetry.SourceName && a.TraceId.ToString() == traceId)
            .OrderBy(a => a.StartTimeUtc)
            .ToArray();

    [Fact]
    public async Task One_request_with_no_traceparent_exports_one_chat_span_on_the_response_trace_id()
    {
        var exported = new ExportedActivities();
        await using var host = await StartAsync(exported, HaikuGateway());

        var response = await host.PostAsync(Body(), Key);
        var json = await ReadJsonAsync(response);
        var traceId = (string)json["trace_id"]!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var span = Assert.Single(ModelCalls(exported, traceId));
        Assert.Equal("chat claude-haiku-4-5", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.NotEqual(default, span.ParentSpanId);
        Assert.Equal("claude-haiku-4-5", span.GetTagItem(LlmTelemetry.RequestModel));
        Assert.Equal(20L, Convert.ToInt64(span.GetTagItem(LlmTelemetry.UsageOutputTokens)));
        Assert.Equal(
            (string?)json["attempts"]![0]!["cost_usd"],
            (string?)span.GetTagItem(LlmTelemetry.CostUsd));
    }

    [Fact]
    public async Task An_answer_that_fails_validation_every_time_exports_one_span_per_attempt()
    {
        var wrong = ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");
        var gateway = new ScriptedGateway(_ => Task.FromResult(Response(wrong, model: "claude-haiku-4-5")));
        var exported = new ExportedActivities();
        await using var host = await StartAsync(exported, gateway);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));
        var traceId = (string)json["trace_id"]!;

        Assert.Equal(3, json["attempts"]!.AsArray().Count);
        var spans = ModelCalls(exported, traceId);
        Assert.Equal(3, spans.Length);
        Assert.Single(spans.Select(s => s.ParentSpanId).Distinct());
    }

    [Fact]
    public async Task An_inbound_sampled_traceparent_is_continued_by_the_chat_span()
    {
        var exported = new ExportedActivities();
        await using var host = await StartAsync(exported, HaikuGateway());

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key, Sampled));

        Assert.Equal(InboundTraceId, (string?)json["trace_id"]);
        Assert.Single(ModelCalls(exported, InboundTraceId));
    }

    [Fact]
    public async Task An_inbound_unsampled_traceparent_still_exports_the_chat_span_on_the_inbound_trace_id()
    {
        var exported = new ExportedActivities();
        await using var host = await StartAsync(exported, HaikuGateway());

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key, Unsampled));

        Assert.Equal(InboundTraceId, (string?)json["trace_id"]);
        Assert.Single(ModelCalls(exported, InboundTraceId));
    }

    [Fact]
    public async Task A_configured_ratio_zero_sampler_does_not_drop_the_chat_span()
    {
        var config = NoExporter();
        config["OTEL_TRACES_SAMPLER"] = "traceidratio";
        config["OTEL_TRACES_SAMPLER_ARG"] = "0";
        var exported = new ExportedActivities();
        await using var host = await StartAsync(exported, HaikuGateway(), config);

        var json = await ReadJsonAsync(await host.PostAsync(Body(), Key));

        Assert.Single(ModelCalls(exported, (string)json["trace_id"]!));
    }

    [Fact]
    public async Task The_host_logs_that_no_exporter_is_registered_when_the_endpoint_is_blank()
    {
        var exported = new ExportedActivities();
        await using var host = await StartAsync(exported, HaikuGateway());

        Assert.Contains(host.Logs.Messages, m => m.Contains("tracing: no exporter (OTEL_EXPORTER_OTLP_ENDPOINT unset)"));
    }

    [Fact]
    public async Task The_host_logs_the_endpoint_when_one_is_set()
    {
        var exported = new ExportedActivities();
        var config = new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1" };
        await using var host = await StartAsync(exported, HaikuGateway(), config);

        Assert.Contains(host.Logs.Messages, m => m.Contains("tracing: OTLP export to http://127.0.0.1:1"));
    }

    /// <summary>A lock-protected collection for the in-memory exporter, which adds from the thread that ends a span.</summary>
    private sealed class ExportedActivities : ICollection<Activity>
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
