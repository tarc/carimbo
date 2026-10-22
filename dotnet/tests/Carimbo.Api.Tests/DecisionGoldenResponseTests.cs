using System.Text;
using System.Text.Json.Nodes;
using Carimbo.Llm;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// Golden decision-1 responses (D-36, mirroring D-32). Each scenario posts a fixed request to
/// <c>/eval/decisions</c> through the real endpoint, with a scripted model and the Tools host in process, and compares
/// the response, byte for byte, with <c>data/contract/decision-responses/&lt;scenario&gt;.json</c>.
/// <c>CARIMBO_UPDATE_GOLDEN=1</c> (<c>just golden</c>) rewrites the files instead; they are generated and never
/// hand-edited. pytest parses the same files with the strict Pydantic <c>DecisionResponse</c>, so a changed response
/// contract fails both stacks.
/// </summary>
public class DecisionGoldenResponseTests
{
    // Not an id any other test class uses: they count the spans exported for theirs, and tests run in parallel.
    private const string TraceParent = "00-7c1d3e5f9a2b4c6d8e0f1a2b3c4d5e6f-a1b2c3d4e5f60718-01";

    private static readonly DateTimeOffset Clock = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The scenario matrix: file name (without extension) to the function that produces its response.</summary>
    private static readonly IReadOnlyDictionary<string, Func<Task<JsonNode>>> Scenarios =
        new Dictionary<string, Func<Task<JsonNode>>>
        {
            ["decided"] = Decided,
            ["guard_override"] = GuardOverride,
            ["tool_failed"] = ToolFailed,
            ["step_budget_exhausted"] = StepBudgetExhausted,
            ["refused"] = Refused,
            ["schema_invalid"] = SchemaInvalid,
            ["cache_hit"] = CacheHit,
            ["cache_miss"] = CacheMiss,
            ["overrides"] = Overrides,
        };

    public static IEnumerable<object[]> ScenarioNames => Scenarios.Keys.Order().Select(name => new object[] { name });

    private static string GoldenDirectory => Path.Combine(FindRepoRoot(), "data", "contract", "decision-responses");

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task The_scenario_response_equals_its_golden_file(string scenario)
    {
        var response = await Scenarios[scenario]();
        var bytes = GoldenResponseTests.Render(response);

        GoldenResponseTests.AssertNoSecretsOrPaths(scenario, Encoding.UTF8.GetString(bytes));
        GoldenResponseTests.AssertGolden(
            Path.Combine(GoldenDirectory, scenario + ".json"), bytes, GoldenResponseTests.UpdateMode);
    }

    [Fact]
    public void The_golden_directory_holds_exactly_the_scenario_files()
    {
        var expected = Scenarios.Keys.Select(name => name + ".json").Order().ToArray();
        if (GoldenResponseTests.UpdateMode)
        {
            Directory.CreateDirectory(GoldenDirectory);
            foreach (var extra in Directory.GetFiles(GoldenDirectory, "*.json").Where(f => !expected.Contains(Path.GetFileName(f))))
            {
                File.Delete(extra);
            }

            return;
        }

        var actual = Directory.Exists(GoldenDirectory)
            ? Directory.GetFiles(GoldenDirectory, "*.json").Select(f => Path.GetFileName(f)!).Order().ToArray()
            : [];
        Assert.True(
            expected.SequenceEqual(actual),
            $"data/contract/decision-responses holds [{string.Join(", ", actual)}] but the scenarios are "
            + $"[{string.Join(", ", expected)}]. Run: just golden");
    }

    [Fact]
    public void A_stale_or_missing_decision_golden_fails_naming_the_file_and_the_command_that_fixes_it()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-golden-");
        try
        {
            var path = Path.Combine(directory.FullName, "decision-responses", "decided.json");
            var fresh = GoldenResponseTests.Render(JsonNode.Parse("""{"contract_version":"decision-1","latency_ms":0}""")!);
            GoldenResponseTests.AssertGolden(path, fresh, update: true);
            GoldenResponseTests.AssertGolden(path, fresh, update: false);

            var edited = (byte[])fresh.Clone();
            edited[5] ^= 0x01;
            File.WriteAllBytes(path, edited);
            var stale = Assert.ThrowsAny<Exception>(() => GoldenResponseTests.AssertGolden(path, fresh, update: false));
            Assert.Contains("data/contract/decision-responses/decided.json", stale.Message);
            Assert.Contains("Run: just golden", stale.Message);

            File.Delete(path);
            var missing = Assert.ThrowsAny<Exception>(() => GoldenResponseTests.AssertGolden(path, fresh, update: false));
            Assert.Contains("decided.json", missing.Message);
            Assert.Contains("Run: just golden", missing.Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- scenario plumbing

    private static Task<TestHost> StartAsync(
        DecisionEndpointTests.InProcessTools tools,
        ILlmGateway gateway,
        IReadOnlyDictionary<string, string?>? config = null) =>
        DecisionEndpointTests.DecisionHost.StartAsync(
            "Development",
            Key,
            gateway,
            tools,
            config,
            services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock)));

    /// <summary>
    /// Returns the parsed response with the wall-clock values normalised. The run's own stopwatch and each tool call's
    /// measured latency are the only values a scripted run does not fix; step latencies come from the scripted model.
    /// </summary>
    private static async Task<JsonNode> NormalisedAsync(HttpResponseMessage response)
    {
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        json["latency_ms"] = 0;
        foreach (var step in json["steps"]!.AsArray())
        {
            foreach (var call in step!["tool_calls"]!.AsArray())
            {
                ZeroLatency(call!);
            }
        }

        if (json["outcome"]!["guard"] is { } guard)
        {
            ZeroLatency(guard["tool_call"]!);
        }

        return json;
    }

    private static void ZeroLatency(JsonNode call)
    {
        if (call["latency_ms"] is not null)
        {
            call["latency_ms"] = 0;
        }
    }

    private static async Task<JsonNode> PostAsync(TestHost host, JsonObject body) =>
        await NormalisedAsync(await host.PostToAsync(DecisionEndpointTests.Route, body, Key, TraceParent));

    private static JsonObject WithCacheMode(JsonObject body, string mode)
    {
        body["overrides"] = new JsonObject { ["cache_mode"] = mode };
        return body;
    }

    // ---------------------------------------------------------------- scenarios

    private static async Task<JsonNode> Decided()
    {
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        await using var host = await StartAsync(tools, DecisionEndpointTests.Dc001Model());
        return await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-001"));
    }

    // dc-005 has no invoice (the extraction failed); the model answers approve anyway and the guard escalates.
    private static async Task<JsonNode> GuardOverride()
    {
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        var gateway = new ScriptedGateway(request => Task.FromResult(
            DecisionEndpointTests.StepOf(request) == 0
                ? DecisionEndpointTests.ToolUse(request, [.. DecisionEndpointTests.FourCalls("dc-005")])
                : DecisionEndpointTests.Answer(request, DecisionEndpointTests.Approve())));
        await using var host = await StartAsync(tools, gateway);
        return await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-005"));
    }

    // dc-999 is a well-formed id the Tools host does not hold, so the tool reports an error and the run escalates.
    private static async Task<JsonNode> ToolFailed()
    {
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        var gateway = new ScriptedGateway(request => Task.FromResult(
            DecisionEndpointTests.ToolUse(request, DecisionEndpointTests.Call(2, "get_invoice", "dc-999"))));
        await using var host = await StartAsync(tools, gateway);
        return await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-999"));
    }

    private static async Task<JsonNode> StepBudgetExhausted()
    {
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        var gateway = new ScriptedGateway(request => Task.FromResult(
            DecisionEndpointTests.ToolUse(request, DecisionEndpointTests.Call(4, "get_policy", "dc-001"))));
        await using var host = await StartAsync(tools, gateway, new Dictionary<string, string?> { ["Agent:MaxSteps"] = "2" });
        return await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-001"));
    }

    private static async Task<JsonNode> Refused()
    {
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        var gateway = new ScriptedGateway(request => Task.FromResult(
            DecisionEndpointTests.Answer(request, string.Empty, LlmStopReason.Refusal, "scripted refusal")));
        await using var host = await StartAsync(tools, gateway);
        return await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-007"));
    }

    private static async Task<JsonNode> SchemaInvalid()
    {
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        var gateway = new ScriptedGateway(request => Task.FromResult(DecisionEndpointTests.Answer(request, "not json")));
        await using var host = await StartAsync(tools, gateway);
        return await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-002"));
    }

    private static async Task<JsonNode> CacheHit()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-golden-cache-");
        try
        {
            await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
            await using var host = await StartAsync(
                tools, DecisionEndpointTests.Dc001Model(), CacheConfig(directory.FullName, "read_write"));
            await PostAsync(host, DecisionEndpointTests.DecisionBody("dc-001"));
            return await PostAsync(host, WithCacheMode(DecisionEndpointTests.DecisionBody("dc-001"), "read_only"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<JsonNode> CacheMiss()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-golden-cache-");
        try
        {
            await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
            await using var host = await StartAsync(
                tools, DecisionEndpointTests.Dc001Model(), CacheConfig(directory.FullName, "read_write"));
            return await PostAsync(host, WithCacheMode(DecisionEndpointTests.DecisionBody("dc-001"), "read_only"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Posts the hand-written <c>data/contract/decision-request.json</c> verbatim.</summary>
    private static async Task<JsonNode> Overrides()
    {
        var request = await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "data", "contract", "decision-request.json"), TestContext.Current.CancellationToken);
        await using var tools = await DecisionEndpointTests.InProcessTools.StartAsync();
        await using var host = await StartAsync(tools, DecisionEndpointTests.Dc001Model());
        var response = await host.PostRawToAsync(
            DecisionEndpointTests.Route, new StringContent(request, Encoding.UTF8, "application/json"), Key, TraceParent);
        return await NormalisedAsync(response);
    }
}
