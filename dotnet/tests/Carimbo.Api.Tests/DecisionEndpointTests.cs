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

    [Fact]
    public async Task Every_money_member_is_an_f8_string_or_null_never_a_number()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", Key, Dc001Model(), tools);

        var json = await ReadJsonAsync(await host.PostToAsync(Route, DecisionBody("dc-001"), Key));

        var money = MoneyMembers(json).ToArray();
        Assert.True(money.Length >= 6, "expected the run and step cost members");
        Assert.All(money, member =>
        {
            Assert.Equal(JsonValueKind.String, member.Kind);
            Assert.Matches("^[0-9]+\\.[0-9]{8}$", member.Text);
        });
    }

    // An unpriced model can no longer reach a run: Agent:Model is checked against the pricing table at startup
    // (see An_agent_model_the_pricing_table_cannot_price_fails_startup_naming_the_setting) and overrides.model per request.
    // The unknown-cost rendering itself stays pinned by the gateway tests and the extraction goldens.

    // ---------------------------------------------------------------- availability and authentication

    [Fact]
    public async Task Production_with_key_and_gateway_has_no_decision_route_and_the_log_names_the_unmet_condition()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Production", Key, Dc001Model(), tools);

        var response = await host.PostToAsync(Route, DecisionBody("dc-001"), Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(host.Logs.Messages, m => m.Contains("Decision endpoint /eval/decisions is disabled") && m.Contains("environment is not Development or Eval"));
        Assert.DoesNotContain(host.Logs.Messages, m => m.Contains(Key));
    }

    [Fact]
    public async Task Without_an_eval_key_the_decision_route_is_a_404_and_the_log_names_the_key_setting()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", key: null, Dc001Model(), tools);

        var response = await host.PostToAsync(Route, DecisionBody("dc-001"), Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(host.Logs.Messages, m => m.Contains("Decision endpoint /eval/decisions is disabled") && m.Contains("CARIMBO_EVAL_API_KEY is not set"));
    }

    [Fact]
    public async Task Without_a_gateway_the_decision_route_is_a_404_and_the_log_names_the_gateway()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", Key, gateway: null, tools);

        var response = await host.PostToAsync(Route, DecisionBody("dc-001"), Key);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(host.Logs.Messages, m => m.Contains("Decision endpoint /eval/decisions is disabled") && m.Contains("no ILlmGateway is registered"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Eval")]
    public async Task The_route_needs_the_static_key_in_both_allowed_environments(string environment)
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync(environment, Key, Dc001Model(), tools);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostToAsync(Route, DecisionBody("dc-001"), key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.PostToAsync(Route, DecisionBody("dc-001"), "wrong-key")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostToAsync(Route, DecisionBody("dc-001"), Key)).StatusCode);
    }

    [Fact]
    public async Task Authentication_is_checked_before_the_body_is_read()
    {
        await using var tools = await InProcessTools.StartAsync();
        var gateway = Counting(out var calls);
        await using var host = await DecisionHost.StartAsync("Development", Key, gateway, tools);

        var malformed = new StringContent("{ not json", Encoding.UTF8, "application/json");
        var response = await host.PostRawToAsync(Route, malformed, "wrong-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, calls.Value);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_415()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", Key, Dc001Model(), tools);

        var response = await host.PostRawToAsync(
            Route, new StringContent("case_id=dc-001", Encoding.UTF8, "text/plain"), Key);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_body_declaring_more_than_sixty_four_kilobytes_is_a_413()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", Key, Dc001Model(), tools);

        var oversized = "{\"contract_version\":\"decision-1\",\"case_id\":\"" + new string('a', 70_000) + "\"}";
        var response = await host.PostRawToAsync(
            Route, new StringContent(oversized, Encoding.UTF8, "application/json"), Key);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task The_decision_route_carries_a_sixty_four_kilobyte_server_size_limit()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync("Development", Key, Dc001Model(), tools);

        var endpoint = host.App.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == Route);

        var limit = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>();
        Assert.NotNull(limit);
        Assert.Equal(64 * 1024, limit.MaxRequestBodySize);
    }

    // ---------------------------------------------------------------- validation

    public static TheoryData<string, string, string?> BadRequests() => new()
    {
        { "contract_version", "{\"contract_version\":\"3\",\"case_id\":\"dc-001\"}", "3" },
        { "case_id", "{\"contract_version\":\"decision-1\",\"case_id\":\"../policy\"}", "../policy" },
        { "case_id", "{\"contract_version\":\"decision-1\",\"case_id\":\"DC-001\"}", "DC-001" },
        { "case_id", "{\"contract_version\":\"decision-1\",\"case_id\":\"\"}", null },
        { "case_id", "{\"contract_version\":\"decision-1\",\"case_id\":\"" + new string('a', 65) + "\"}", new string('a', 65) },
        { "overrides.model", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"model\":\"gpt-x\"}}", "gpt-x" },
        { "overrides.model", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"model\":5}}", null },
        { "overrides.cache_mode", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"cache_mode\":\"sometimes\"}}", "sometimes" },
        { "overrides.cache_mode", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"cache_mode\":\"read_only\"}}", null },
        { "overrides.replicate_salt", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"replicate_salt\":\"a b\"}}", "a b" },
        { "body", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"prompt_version\":\"agent-001\"}}", "agent-001" },
        { "body", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"overrides\":{\"max_steps\":3}}", null },
        { "body", "{\"contract_version\":\"decision-1\",\"case_id\":\"dc-001\",\"surprise\":\"leaked-value\"}", "leaked-value" },
        { "body", "{\"contract_version\":\"decision-1\"}", null },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task Bad_requests_are_400s_keyed_by_field_that_never_echo_the_value_and_make_no_model_call(
        string field, string body, string? submitted)
    {
        await using var tools = await InProcessTools.StartAsync();
        var gateway = Counting(out var calls);
        await using var host = await DecisionHost.StartAsync("Development", Key, gateway, tools);

        var response = await host.PostRawToAsync(
            Route, new StringContent(body, Encoding.UTF8, "application/json"), Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonNode.Parse(text)!["errors"]!.AsObject();
        Assert.Equal(field, Assert.Single(errors).Key);
        if (submitted is not null)
        {
            Assert.DoesNotContain(submitted, text, StringComparison.Ordinal);
        }

        Assert.Equal(0, calls.Value);
    }

    [Fact]
    public async Task A_cache_mode_override_is_accepted_on_a_host_that_has_a_cache_and_echoed_as_effective()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-decision-");
        try
        {
            await using var tools = await InProcessTools.StartAsync();
            await using var host = await DecisionHost.StartAsync(
                "Development",
                Key,
                Dc001Model(),
                tools,
                new Dictionary<string, string?> { ["Llm:Cache:Directory"] = directory.FullName });

            var response = await host.PostToAsync(
                Route, DecisionBody("dc-001", o => o["overrides"] = new JsonObject { ["cache_mode"] = "off" }), Key);
            var json = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("off", (string?)json["effective"]!["cache_mode"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Overrides_reach_the_model_request_and_the_effective_block()
    {
        await using var tools = await InProcessTools.StartAsync();
        var seen = new List<LlmRequest>();
        var gateway = new EvalEndpointTests.ScriptedGateway(request =>
        {
            lock (seen)
            {
                seen.Add(request);
            }

            return Task.FromResult(
                StepOf(request) == 0 ? ToolUse(request, [.. FourCalls("dc-001")]) : Answer(request, Approve()));
        });
        await using var host = await DecisionHost.StartAsync("Development", Key, gateway, tools);

        var json = await ReadJsonAsync(await host.PostToAsync(
            Route,
            DecisionBody(
                "dc-001",
                o => o["overrides"] = new JsonObject { ["model"] = "claude-sonnet-5-5", ["replicate_salt"] = "run-2" }),
            Key));

        Assert.Equal("claude-sonnet-5-5", (string?)json["effective"]!["model"]);
        Assert.Equal("run-2", (string?)json["effective"]!["replicate_salt"]);
        Assert.All(seen, request =>
        {
            Assert.Equal("claude-sonnet-5-5", request.Model);
            Assert.Equal("run-2", request.ReplicateSalt);
        });
    }

    // ---------------------------------------------------------------- typed outcomes are HTTP 200

    [Fact]
    public async Task A_scripted_refusal_is_status_refused_with_no_decision()
    {
        await using var tools = await InProcessTools.StartAsync();
        var gateway = new EvalEndpointTests.ScriptedGateway(request => Task.FromResult(
            Answer(request, string.Empty, LlmStopReason.Refusal, "policy")));
        await using var host = await DecisionHost.StartAsync("Development", Key, gateway, tools);

        var response = await host.PostToAsync(Route, DecisionBody("dc-001"), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var outcome = json["outcome"]!;
        Assert.Equal("refused", (string?)outcome["status"]);
        Assert.Null(outcome["decision"]);
        Assert.Null(outcome["model_decision"]);
        Assert.Null(outcome["decided_by"]);
        Assert.Null(outcome["guard"]);
        Assert.Equal("refused", (string?)outcome["failure"]!["kind"]);
        Assert.Single(json["steps"]!.AsArray());
    }

    [Fact]
    public async Task A_read_only_request_on_an_empty_cache_is_a_cache_miss_that_cost_nothing()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-decision-");
        try
        {
            await using var tools = await InProcessTools.StartAsync();
            var gateway = Counting(out var calls);
            await using var host = await DecisionHost.StartAsync(
                "Development",
                Key,
                gateway,
                tools,
                new Dictionary<string, string?> { ["Llm:Cache:Directory"] = directory.FullName });

            var response = await host.PostToAsync(
                Route, DecisionBody("dc-001", o => o["overrides"] = new JsonObject { ["cache_mode"] = "read_only" }), Key);
            var json = await ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("infrastructure_failure", (string?)json["outcome"]!["status"]);
            Assert.Null(json["outcome"]!["decision"]);
            Assert.Equal("cache_miss", (string?)json["outcome"]!["failure"]!["kind"]);
            var step = Assert.Single(json["steps"]!.AsArray())!;
            Assert.Equal("cache_miss", (string?)step["failure"]!["kind"]);
            Assert.Null(step["usage"]);
            Assert.Equal("0.00000000", (string?)step["incurred_cost_usd"]);
            Assert.Equal("0.00000000", (string?)json["incurred_cost_usd"]);
            Assert.Null(json["cost_usd"]);
            Assert.Equal(0, calls.Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_well_formed_unknown_case_id_is_a_decided_escalation_with_no_further_model_call()
    {
        await using var tools = await InProcessTools.StartAsync();
        var calls = 0;
        var gateway = new EvalEndpointTests.ScriptedGateway(request =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(
                StepOf(request) == 0
                    ? ToolUse(request, Call(1, "get_invoice", "dc-999"))
                    : throw new InvalidOperationException("The model must not be called again."));
        });
        await using var host = await DecisionHost.StartAsync("Development", Key, gateway, tools);

        var response = await host.PostToAsync(Route, DecisionBody("dc-999"), Key);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var outcome = json["outcome"]!;
        Assert.Equal("decided", (string?)outcome["status"]);
        Assert.Equal("tool_failure", (string?)outcome["decided_by"]);
        Assert.Equal("escalate", (string?)outcome["decision"]!["outcome"]);
        Assert.Equal("tool_failed", (string?)outcome["decision"]!["cause"]);
        Assert.Null(outcome["model_decision"]);
        Assert.Null(outcome["guard"]);
        Assert.Equal("get_invoice", (string?)outcome["tool_failure"]!["tool"]);
        Assert.Equal("model", (string?)outcome["tool_failure"]!["source"]);
        var step = Assert.Single(json["steps"]!.AsArray())!;
        Assert.Equal("error", (string?)step["tool_calls"]![0]!["outcome"]);
        Assert.Equal(1, calls);
    }

    // ---------------------------------------------------------------- bounded agent configuration

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("-1")]
    public async Task A_step_budget_outside_one_to_ten_fails_startup_naming_the_setting(string value)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestHost.StartAsync(
                "Development", Key, gateway: null, new Dictionary<string, string?> { ["Agent:MaxSteps"] = value }));

        Assert.Contains("Agent:MaxSteps", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    public async Task A_step_budget_at_either_bound_starts(string value)
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, new Dictionary<string, string?> { ["Agent:MaxSteps"] = value });

        Assert.Equal(int.Parse(value, CultureInfo.InvariantCulture), host.App.Services.GetRequiredService<AgentSettings>().MaxSteps);
    }

    [Fact]
    public async Task The_configured_step_budget_is_echoed_as_the_effective_max_steps()
    {
        await using var tools = await InProcessTools.StartAsync();
        await using var host = await DecisionHost.StartAsync(
            "Development", Key, Dc001Model(), tools, new Dictionary<string, string?> { ["Agent:MaxSteps"] = "3" });

        var json = await ReadJsonAsync(await host.PostToAsync(Route, DecisionBody("dc-001"), Key));

        Assert.Equal(3, (int?)json["effective"]!["max_steps"]);
    }

    [Theory]
    [InlineData("Agent:MaxTokens", "0")]
    [InlineData("Agent:ToolCallTimeoutSeconds", "0")]
    public async Task A_non_positive_token_cap_or_tool_timeout_fails_startup(string setting, string value)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestHost.StartAsync(
                "Development", Key, gateway: null, new Dictionary<string, string?> { [setting] = value }));

        Assert.Contains(setting, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("claude-typo-9")]
    public async Task An_agent_model_the_pricing_table_cannot_price_fails_startup_naming_the_setting(string model)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestHost.StartAsync(
                "Development", Key, gateway: null, new Dictionary<string, string?> { ["Agent:Model"] = model }));

        Assert.Contains("Agent:Model", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_priced_agent_model_starts()
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, new Dictionary<string, string?> { ["Agent:Model"] = "claude-sonnet-5-5" });

        Assert.Equal("claude-sonnet-5-5", host.App.Services.GetRequiredService<AgentSettings>().Model);
    }

    [Theory]
    [InlineData("http://example.com/mcp")]
    [InlineData("ftp://127.0.0.1/mcp")]
    [InlineData("relative/mcp")]
    [InlineData("http://10.0.0.5:5090/mcp")]
    public async Task A_tools_url_that_is_not_http_on_a_loopback_host_fails_startup(string url)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestHost.StartAsync(
                "Development", Key, gateway: null, new Dictionary<string, string?> { ["Agent:ToolsUrl"] = url }));

        Assert.Contains("Agent:ToolsUrl", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:5090/mcp")]
    [InlineData("http://[::1]:5090/mcp")]
    [InlineData("http://127.0.0.1:5090/mcp")]
    public async Task A_loopback_tools_url_starts(string url)
    {
        await using var host = await TestHost.StartAsync(
            "Development", Key, gateway: null, new Dictionary<string, string?> { ["Agent:ToolsUrl"] = url });

        Assert.Equal(url, host.App.Services.GetRequiredService<AgentSettings>().ToolsUrl);
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

    /// <summary>A gateway that counts its calls and always fails the test, for requests that must never reach a model.</summary>
    internal static EvalEndpointTests.ScriptedGateway Counting(out Counter calls)
    {
        var counter = new Counter();
        calls = counter;
        return new EvalEndpointTests.ScriptedGateway(_ =>
        {
            Interlocked.Increment(ref counter.Value);
            throw new InvalidOperationException("The model must not be called.");
        });
    }

    internal sealed class Counter
    {
        public int Value;
    }

    /// <summary>Every member whose name ends in cost_usd, anywhere in the response.</summary>
    internal static IEnumerable<(string Path, JsonValueKind Kind, string Text)> MoneyMembers(JsonNode? node, string path = "$")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    if (name.EndsWith("cost_usd", StringComparison.Ordinal) && value is not null)
                    {
                        yield return ($"{path}.{name}", value.GetValueKind(), value.ToJsonString().Trim('"'));
                    }

                    foreach (var inner in MoneyMembers(value, $"{path}.{name}"))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var inner in MoneyMembers(array[i], $"{path}[{i}]"))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }

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
