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
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
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
        };

    public static IEnumerable<object[]> ScenarioNames => Scenarios.Keys.Order().Select(name => new object[] { name });

    private static bool UpdateMode => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    private static string GoldenDirectory => Path.Combine(FindRepoRoot(), "data", "contract", "eval-responses");

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task The_scenario_response_equals_its_golden_file(string scenario)
    {
        var response = await Scenarios[scenario]();

        AssertGolden(Path.Combine(GoldenDirectory, scenario + ".json"), Render(response), UpdateMode);
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

    private static ScriptedGateway Answering(string text, LlmStopReason stop = LlmStopReason.EndTurn, string? detail = null, string model = Model) =>
        new(_ => Task.FromResult(Response(text, stop, detail, model)));

    // ---------------------------------------------------------------- scenarios

    private static async Task<JsonNode> Success()
    {
        await using var host = await StartAsync(Answering(ValidInvoiceJson()));
        return await PostAsync(host, Body(caseId: "golden-success"));
    }
}
