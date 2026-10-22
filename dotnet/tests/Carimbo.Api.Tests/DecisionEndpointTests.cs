using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Agent;
using Carimbo.Domain;
using Carimbo.Llm;
using Carimbo.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// POST /eval/decisions (AGT-03, AGT-04, D-16, D-36): the decision-1 record over the real Tools host in process and a
/// scripted model, behind the same decorator stack as extraction.
/// </summary>
public class DecisionEndpointTests
{
    internal const string Route = "/eval/decisions";
    private const string Traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4a01-00f067aa0ba902b7-01";
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4a01";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- tracer

    [Fact]
    public async Task A_case_posted_to_the_decision_route_comes_back_decided_with_steps_tool_calls_cost_and_trace_id()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", Key, Dc001Model(), tools);

        var response = await host.PostToAsync(Route, DecisionBody("dc-001"), Key, Traceparent);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                "contract_version", "case_id", "trace_id", "effective", "outcome", "steps", "usage", "cost_usd",
                "cost_warning", "incurred_cost_usd", "latency_ms",
            ],
            json.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal("decision-1", (string?)json["contract_version"]);
        Assert.Equal("dc-001", (string?)json["case_id"]);
        Assert.Equal(TraceId, (string?)json["trace_id"]);

        var effective = json["effective"]!;
        Assert.Equal(
            ["model", "prompt_version", "max_steps", "schema_sha256", "tools_sha256", "pricing_version", "cache_mode", "replicate_salt"],
            effective.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal("claude-haiku-4-5", (string?)effective["model"]);
        Assert.Equal("agent-001", (string?)effective["prompt_version"]);
        Assert.Equal(6, (int?)effective["max_steps"]);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(FindRepoRoot(), "schema", "decision.model.schema.json")))),
            (string?)effective["schema_sha256"]);
        Assert.False(string.IsNullOrEmpty((string?)effective["tools_sha256"]));
        Assert.Equal(LlmPricingTable.LoadEmbedded().Version, (string?)effective["pricing_version"]);
        Assert.Equal("off", (string?)effective["cache_mode"]);
        Assert.Null(effective["replicate_salt"]);

        var outcome = json["outcome"]!;
        Assert.Equal(
            [
                "status", "decision", "model_decision", "decided_by", "guard", "tool_failure", "failure", "raw_output",
            ],
            outcome.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal("decided", (string?)outcome["status"]);
        Assert.Equal("model", (string?)outcome["decided_by"]);
        Assert.Equal("approve", (string?)outcome["decision"]!["outcome"]);
        Assert.Equal("approve", (string?)outcome["model_decision"]!["outcome"]);
        Assert.Equal("success", (string?)outcome["guard"]!["extraction_status"]);
        Assert.False((bool)outcome["guard"]!["overridden"]!);
        Assert.Equal("guard", (string?)outcome["guard"]!["tool_call"]!["source"]);
        Assert.Null(outcome["tool_failure"]);
        Assert.Null(outcome["failure"]);

        var steps = json["steps"]!.AsArray();
        Assert.Equal(2, steps.Count);
        Assert.Equal("tool_use", (string?)steps[0]!["stop_reason"]);
        var calls = steps[0]!["tool_calls"]!.AsArray();
        Assert.Equal(4, calls.Count);
        Assert.All(calls, call =>
        {
            Assert.Equal("model", (string?)call!["source"]);
            Assert.Equal("ok", (string?)call["outcome"]);
            Assert.IsType<JsonObject>(call["arguments"]);
        });
        Assert.Equal("end_turn", (string?)steps[1]!["stop_reason"]);
        Assert.Empty(steps[1]!["tool_calls"]!.AsArray());

        var stepCosts = steps.Select(s => decimal.Parse((string)s!["cost_usd"]!, CultureInfo.InvariantCulture)).ToArray();
        var stepIncurred = steps.Select(s => decimal.Parse((string)s!["incurred_cost_usd"]!, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(stepCosts.Sum().ToString("F8", CultureInfo.InvariantCulture), (string?)json["cost_usd"]);
        Assert.Equal(stepIncurred.Sum().ToString("F8", CultureInfo.InvariantCulture), (string?)json["incurred_cost_usd"]);
        Assert.Equal(2000L, (long?)json["usage"]!["input_tokens"]);
        Assert.Equal(400L, (long?)json["usage"]!["output_tokens"]);
    }

    // ---------------------------------------------------------------- support

    internal static JsonObject DecisionBody(string caseId, Action<JsonObject>? mutate = null)
    {
        var body = new JsonObject { ["contract_version"] = "decision-1", ["case_id"] = caseId };
        mutate?.Invoke(body);
        return body;
    }

    internal static EvalEndpointTests.ScriptedGateway Dc001Model() => new(request => Task.FromResult(
        StepOf(request) == 0
            ? ToolUse(request, [.. FourCalls("dc-001")])
            : Answer(request, Approve())));

    internal static int StepOf(LlmRequest request) => request.FollowUps.Count / 2;

    internal static IReadOnlyList<LlmToolCall> FourCalls(string caseId) =>
    [
        Call(1, "get_extraction_status", caseId),
        Call(2, "get_invoice", caseId),
        Call(3, "get_validator_findings", caseId),
        Call(4, "get_policy", caseId),
    ];

    internal static LlmToolCall Call(int number, string name, string caseId) =>
        new($"toolu_{number:00}", name, name == "get_policy" ? "{}" : "{\"case_id\":\"" + caseId + "\"}");

    internal static LlmResponse ToolUse(LlmRequest request, params LlmToolCall[] calls) =>
        new(
            string.Empty,
            LlmStopReason.ToolUse,
            null,
            new LlmUsage(1000, 200, 0, 0, 0),
            request.Model,
            request.Model,
            "msg_tool",
            TimeSpan.FromMilliseconds(40),
            1)
        {
            Blocks = [.. calls.Select(LlmContentBlock.OfToolUse)],
        };

    internal static LlmResponse Answer(LlmRequest request, Decision decision) =>
        Answer(request, JsonSerializer.Serialize(decision, Wire.Options));

    internal static LlmResponse Answer(LlmRequest request, string text, LlmStopReason stop = LlmStopReason.EndTurn, string? detail = null) =>
        new(text, stop, detail, new LlmUsage(1000, 200, 0, 0, 0), request.Model, request.Model, "msg_answer", TimeSpan.FromMilliseconds(30), 1)
        {
            Blocks = text.Length == 0 ? [] : [LlmContentBlock.OfText(text)],
        };

    internal static Decision Approve() => new(
        [
            new EvidenceItem("get_invoice", "recipient.tax_id", "CARIMBO0000177"),
            new EvidenceItem("get_policy", "buyer_cnpj", "CARIMBO0000177"),
        ],
        "The recipient is the buyer and no rule matches.",
        DecisionOutcome.Approve,
        DecisionCause.None);

    /// <summary>The Tools host in process over the committed decision cases.</summary>
    internal sealed class InProcessTools : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private InProcessTools(WebApplication app)
        {
            _app = app;
            Http = app.GetTestClient();
        }

        public HttpClient Http { get; }

        public static async Task<InProcessTools> StartAsync()
        {
            var app = CarimboTools.CreateApp(
                [],
                configureServices: null,
                configureBuilder: builder =>
                {
                    builder.WebHost.UseTestServer();
                    builder.Configuration.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>(
                            CarimboTools.CasesDirectoryKey, Path.Combine(FindRepoRoot(), "data", "decision")),
                    ]);
                });
            await app.StartAsync(Ct);
            return new InProcessTools(app);
        }

        public McpAgentToolbox Toolbox() => new(new Uri("http://localhost/mcp"), Http);

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _app.StopAsync(Ct);
            await _app.DisposeAsync();
        }
    }

    /// <summary>An Api test host whose agent reaches the Tools host in process.</summary>
    internal static class DecisionHost
    {
        public static Task<TestHost> StartAsync(
            string environment,
            string? key,
            ILlmGateway? gateway,
            InProcessTools tools,
            IReadOnlyDictionary<string, string?>? config = null,
            Action<IServiceCollection>? configureServices = null) =>
            TestHost.StartAsync(
                environment,
                key,
                gateway,
                config,
                services =>
                {
                    services.AddSingleton<IAgentToolbox>(tools.Toolbox());
                    configureServices?.Invoke(services);
                });
    }
}
