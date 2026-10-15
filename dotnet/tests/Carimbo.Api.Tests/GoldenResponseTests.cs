using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Llm;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Carimbo.Api.Tests.EvalEndpointTests;

namespace Carimbo.Api.Tests;

/// <summary>
/// Golden eval responses (D-32, API-04). Each scenario posts a fixed request through the real endpoint with a
/// scripted gateway and compares the response, byte for byte, with <c>data/contract/eval-responses/&lt;scenario&gt;.json</c>.
/// <c>CARIMBO_UPDATE_GOLDEN=1</c> (<c>just golden</c>) rewrites the files instead; they are generated and never
/// hand-edited. pytest parses the same files with strict Pydantic models, so a changed response contract fails
/// both stacks.
/// </summary>
public class GoldenResponseTests
{
    private const string UpdateVariable = "CARIMBO_UPDATE_GOLDEN";

    // Not the id other test classes use: they count the spans exported for it, and tests run in parallel.
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string Model = "claude-haiku-4-5";

    private static readonly DateTimeOffset Clock = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The scenario matrix: file name (without extension) to the function that produces its response.</summary>
    private static readonly IReadOnlyDictionary<string, Func<Task<JsonNode>>> Scenarios =
        new Dictionary<string, Func<Task<JsonNode>>>
        {
            ["success"] = Success,
            ["repaired"] = Repaired,
            ["validation_failed"] = ValidationFailed,
            ["refused"] = Refused,
            ["truncated"] = Truncated,
            ["schema_invalid"] = SchemaInvalid,
            ["infrastructure_failure"] = InfrastructureFailure,
            ["unpriced_model"] = UnpricedModel,
            ["cache_hit"] = CacheHit,
            ["cache_miss"] = CacheMiss,
            ["overrides"] = Overrides,
        };

    public static IEnumerable<object[]> ScenarioNames => Scenarios.Keys.Order().Select(name => new object[] { name });

    private static bool UpdateMode => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    private static string GoldenDirectory => Path.Combine(FindRepoRoot(), "data", "contract", "eval-responses");

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task The_scenario_response_equals_its_golden_file(string scenario)
    {
        var response = await Scenarios[scenario]();
        var bytes = Render(response);

        AssertNoSecretsOrPaths(scenario, Encoding.UTF8.GetString(bytes));
        AssertGolden(Path.Combine(GoldenDirectory, scenario + ".json"), bytes, UpdateMode);
    }

    [Fact]
    public void The_golden_directory_holds_exactly_the_scenario_files()
    {
        var expected = Scenarios.Keys.Select(name => name + ".json").Order().ToArray();
        if (UpdateMode)
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
            $"data/contract/eval-responses holds [{string.Join(", ", actual)}] but the scenarios are "
            + $"[{string.Join(", ", expected)}]. Run: just golden");
    }

    [Fact]
    public void A_stale_golden_file_fails_naming_the_file_and_the_command_that_fixes_it()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-golden-");
        try
        {
            var path = Path.Combine(directory.FullName, "success.json");
            var fresh = Render(JsonNode.Parse("""{"contract_version":"3","latency_ms":0}""")!);
            File.WriteAllBytes(path, fresh);
            AssertGolden(path, fresh, update: false);

            var edited = (byte[])fresh.Clone();
            edited[5] ^= 0x01;
            File.WriteAllBytes(path, edited);
            var stale = Assert.ThrowsAny<Exception>(() => AssertGolden(path, fresh, update: false));
            Assert.Contains("success.json", stale.Message);
            Assert.Contains("Run: just golden", stale.Message);

            File.Delete(path);
            var missing = Assert.ThrowsAny<Exception>(() => AssertGolden(path, fresh, update: false));
            Assert.Contains("success.json", missing.Message);
            Assert.Contains("Run: just golden", missing.Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Update_mode_writes_utf8_without_bom_with_lf_only_and_one_trailing_newline()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-golden-");
        try
        {
            var path = Path.Combine(directory.FullName, "sub", "success.json");
            var bytes = Render(JsonNode.Parse("""{"a":{"b":[1,2]},"note":"nota fiscal"}""")!);

            AssertGolden(path, bytes, update: true);
            AssertGolden(path, bytes, update: false);

            var written = File.ReadAllBytes(path);
            Assert.True(bytes.AsSpan().SequenceEqual(written));
            Assert.NotEqual(0xEF, written[0]);
            var text = Encoding.UTF8.GetString(written);
            Assert.DoesNotContain('\r', text);
            Assert.EndsWith("}\n", text, StringComparison.Ordinal);
            Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
            Assert.StartsWith("{\n  \"a\": {\n    \"b\": [", text, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------- comparison

    /// <summary>Indented JSON with two-space indentation, LF line endings, one trailing newline and no BOM.</summary>
    private static byte[] Render(JsonNode response) =>
        new UTF8Encoding(false).GetBytes(response.ToJsonString(Indented) + "\n");

    private static void AssertGolden(string path, byte[] fresh, bool update)
    {
        var relative = "data/contract/eval-responses/" + Path.GetFileName(path);
        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, fresh);
            return;
        }

        Assert.True(File.Exists(path), $"{relative} is missing. Run: just golden");
        Assert.True(
            fresh.AsSpan().SequenceEqual(File.ReadAllBytes(path)),
            $"{relative} is stale. Run: just golden");
    }

    /// <summary>T-03-24: a fixture never holds the eval key, a provider key, an auth header name or a local path.</summary>
    private static void AssertNoSecretsOrPaths(string scenario, string text)
    {
        foreach (var forbidden in new[] { Key, "sk-ant-", "X-Api-Key", Path.GetTempPath(), FindRepoRoot() })
        {
            Assert.False(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{scenario} contains {forbidden}");
        }
    }

    // ---------------------------------------------------------------- scenario plumbing

    private static Task<TestHost> StartAsync(
        ScriptedGateway gateway, IReadOnlyDictionary<string, string?>? config = null) =>
        TestHost.StartAsync(
            "Development",
            Key,
            gateway,
            config,
            services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock)));

    /// <summary>
    /// Posts the body with the fixed traceparent and returns the parsed response. The endpoint's own stopwatch is the
    /// only wall-clock value in a response (attempt latencies come from the scripted gateway), so it is normalised.
    /// </summary>
    private static async Task<JsonNode> PostAsync(TestHost host, JsonObject body)
    {
        var response = await host.PostAsync(body, Key, TraceParent);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        json["latency_ms"] = 0;
        return json;
    }

    /// <summary>The valid fixture with the invoice total raised by 1.00, so the total no longer matches its parts.</summary>
    private static string WrongTotal() =>
        ValidInvoiceJson(invoice => invoice["totals"]!["invoice_total"] = "156.00");

    private static ScriptedGateway Answering(string text, LlmStopReason stop = LlmStopReason.EndTurn, string? detail = null, string model = Model) =>
        new(_ => Task.FromResult(Response(text, stop, detail, model)));

    // ---------------------------------------------------------------- scenarios

    private static async Task<JsonNode> Success()
    {
        await using var host = await StartAsync(Answering(ValidInvoiceJson()));
        return await PostAsync(host, Body(caseId: "golden-success"));
    }

    private static async Task<JsonNode> Repaired()
    {
        var gateway = new ScriptedGateway(request =>
            Task.FromResult(Response(request.FollowUps.Count == 0 ? WrongTotal() : ValidInvoiceJson(), model: Model)));
        await using var host = await StartAsync(gateway);
        return await PostAsync(host, Body(caseId: "golden-repaired"));
    }

    private static async Task<JsonNode> ValidationFailed()
    {
        await using var host = await StartAsync(Answering(WrongTotal()));
        return await PostAsync(host, Body(caseId: "golden-validation-failed"));
    }

    private static async Task<JsonNode> Refused()
    {
        await using var host = await StartAsync(Answering(string.Empty, LlmStopReason.Refusal, "scripted refusal"));
        return await PostAsync(host, Body(caseId: "golden-refused"));
    }

    private static async Task<JsonNode> Truncated()
    {
        var cutOff = ValidInvoiceJson();
        await using var host = await StartAsync(Answering(cutOff[..(cutOff.Length / 2)], LlmStopReason.MaxTokens));
        return await PostAsync(host, Body(caseId: "golden-truncated"));
    }

    private static async Task<JsonNode> SchemaInvalid()
    {
        await using var host = await StartAsync(Answering("not json"));
        return await PostAsync(host, Body(caseId: "golden-schema-invalid"));
    }

    private static async Task<JsonNode> InfrastructureFailure()
    {
        var gateway = new ScriptedGateway(_ => throw new LlmGatewayException(
            LlmFailureKind.Overloaded, "scripted failure", 529));
        await using var host = await StartAsync(gateway);
        return await PostAsync(host, Body(caseId: "golden-infrastructure-failure"));
    }

    private static async Task<JsonNode> UnpricedModel()
    {
        await using var host = await StartAsync(Answering(ValidInvoiceJson(), model: "mystery-model"));
        return await PostAsync(host, Body(caseId: "golden-unpriced-model"));
    }

    private static JsonObject WithCacheMode(JsonObject body, string mode)
    {
        body["overrides"] = new JsonObject { ["cache_mode"] = mode };
        return body;
    }

    private static async Task<JsonNode> CacheHit()
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-golden-cache-");
        try
        {
            await using var host = await StartAsync(
                Answering(ValidInvoiceJson()), CacheConfig(directory.FullName, "read_write"));
            await PostAsync(host, Body(caseId: "golden-cache-hit"));
            return await PostAsync(host, WithCacheMode(Body(caseId: "golden-cache-hit"), "read_only"));
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
            await using var host = await StartAsync(
                Answering(ValidInvoiceJson()), CacheConfig(directory.FullName, "read_write"));
            return await PostAsync(host, WithCacheMode(Body(caseId: "golden-cache-miss"), "read_only"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Posts the hand-written <c>data/contract/eval-request.json</c> verbatim.</summary>
    private static async Task<JsonNode> Overrides()
    {
        var request = await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "data", "contract", "eval-request.json"), TestContext.Current.CancellationToken);
        await using var host = await StartAsync(Answering(ValidInvoiceJson(), model: "claude-sonnet-5-5"));
        var response = await host.PostRawAsync(request, Key, TraceParent);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        json["latency_ms"] = 0;
        return json;
    }
}
