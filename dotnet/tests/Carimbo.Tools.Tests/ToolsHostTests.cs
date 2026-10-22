using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Decisions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Carimbo.Tools.Tests;

/// <summary>A real MCP client against the in-process Tools host: it lists and calls the four read-only case tools (AGT-01, D-35).</summary>
public class ToolsHostTests
{
    private static readonly string[] ToolNames =
        ["get_extraction_status", "get_invoice", "get_validator_findings", "get_policy"];

    private static readonly CaseStore Store = new(ToolsTestHost.DecisionDirectory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_client_lists_exactly_the_four_read_only_tools_in_catalog_order()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(ToolNames, tools.Select(t => t.Name).ToArray());
        Assert.Equal(CaseToolCatalog.Names, tools.Select(t => t.Name).ToArray());
        foreach (var tool in tools)
        {
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, tool.Name + " must carry ReadOnlyHint true");
        }

        Assert.Equal(CaseToolCatalog.GetPolicyDescription, tools.Single(t => t.Name == "get_policy").Description);
        Assert.Empty(tools.Single(t => t.Name == "get_policy").JsonSchema.GetProperty("properties").EnumerateObject());
        foreach (var name in ToolNames.Take(3))
        {
            var schema = tools.Single(t => t.Name == name).JsonSchema;
            Assert.Equal("string", schema.GetProperty("properties").GetProperty("case_id").GetProperty("type").GetString());
            Assert.Equal("case_id", Assert.Single(schema.GetProperty("required").EnumerateArray()).GetString());
        }
    }

    [Fact]
    public async Task Each_tool_returns_the_catalog_rendering_for_a_committed_case()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);
        var @case = Store.Load("dc-001");

        Assert.Equal(CaseToolCatalog.StatusJson(@case), await CallAsync(client, "get_extraction_status", "dc-001"));
        Assert.Equal(CaseToolCatalog.InvoiceJson(@case), await CallAsync(client, "get_invoice", "dc-001"));
        Assert.Equal(CaseToolCatalog.FindingsJson(@case), await CallAsync(client, "get_validator_findings", "dc-001"));
        Assert.Equal(CaseToolCatalog.PolicyJson(Store.LoadPolicy()), await CallAsync(client, "get_policy", null));
    }

    [Fact]
    public async Task The_host_is_stateless_so_the_client_holds_no_session_id()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        Assert.Null(client.SessionId);
    }

    [Fact]
    public void Every_tool_method_in_the_assembly_is_declared_read_only_idempotent_and_non_destructive()
    {
        var methods = typeof(CarimboTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .ToList();

        Assert.Equal(4, methods.Count);
        foreach (var (method, attribute) in methods)
        {
            Assert.True(attribute!.ReadOnly, method.Name + " must be ReadOnly");
            Assert.False(attribute.Destructive, method.Name + " must not be Destructive");
            Assert.True(attribute.Idempotent, method.Name + " must be Idempotent");
            Assert.False(attribute.OpenWorld, method.Name + " must not be OpenWorld");
        }
    }

    [Fact]
    public async Task Calling_every_tool_for_every_committed_case_changes_no_file_and_leaks_no_label()
    {
        var ids = CommittedCaseIds();
        Assert.Equal(8, ids.Count);
        var before = Snapshot(ToolsTestHost.DecisionDirectory);
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        foreach (var id in ids)
        {
            foreach (var tool in ToolNames.Take(3))
            {
                var text = await CallAsync(client, tool, id);
                AssertNoLabel(text, tool + " " + id);
            }
        }

        AssertNoLabel(await CallAsync(client, "get_policy", null), "get_policy");
        Assert.Equal(before, Snapshot(ToolsTestHost.DecisionDirectory));
    }

    [Fact]
    public async Task A_case_without_an_invoice_answers_normally_with_invoice_null_and_no_findings_is_an_empty_list()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        var noInvoice = JsonNode.Parse(await CallAsync(client, "get_invoice", "dc-005"))!.AsObject();
        Assert.True(noInvoice.ContainsKey("invoice"));
        Assert.Null(noInvoice["invoice"]);

        var findings = JsonNode.Parse(await CallAsync(client, "get_validator_findings", "dc-001"))!;
        Assert.Empty(findings["findings"]!.AsArray());
    }

    [Fact]
    public async Task An_unknown_well_formed_case_id_is_a_tool_error_that_names_the_id_and_no_path()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        var text = await CallErrorAsync(client, "get_invoice", "dc-999");

        Assert.Contains("unknown case dc-999", text);
        AssertNoPath(text);
    }

    public static TheoryData<string> BadCaseIds() =>
        ["../policy", "../../etc/passwd", "DC-001", "a/b", "a\\b", "", "-dc-001", "dc-001\n", new string('a', 65)];

    [Theory]
    [MemberData(nameof(BadCaseIds))]
    public async Task An_id_outside_the_allow_list_is_a_tool_error_that_names_no_path(string caseId)
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        foreach (var tool in ToolNames.Take(3))
        {
            var text = await CallErrorAsync(client, tool, caseId);
            Assert.Contains("invalid case id", text);
            AssertNoPath(text);
        }
    }

    [Fact]
    public async Task An_unknown_tool_name_raises_a_protocol_exception_and_a_missing_case_id_is_a_tool_error()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("nope_tool", null, cancellationToken: Ct));

        var result = await client.CallToolAsync("get_invoice", null, cancellationToken: Ct);
        Assert.True(result.IsError);
        AssertNoPath(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Two_clients_asking_for_the_same_case_at_once_receive_identical_answers()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var first = await host.ConnectAsync(Ct);
        await using var second = await host.ConnectAsync(Ct);

        var answers = await Task.WhenAll(
            CallAsync(first, "get_invoice", "dc-006"),
            CallAsync(second, "get_invoice", "dc-006"));

        Assert.Equal(answers[0], answers[1]);
        Assert.Equal(CaseToolCatalog.InvoiceJson(Store.Load("dc-006")), answers[0]);
    }

    // Recomputed by hand, outside the code under test: for each tool in catalog order,
    // name + "\n" + description + "\n" + input schema JSON, the per-tool renderings joined with "\n"; SHA-256 of the
    // UTF-8 bytes (Python hashlib over the strings of CaseToolCatalog and the SDK schema shape recorded in RESEARCH Pattern 1).
    private const string PinnedToolListSha256 = "8d4fb66f4eeba53d13c5473f5419227b98b9836dd2055fc368cc11da6a41cd68";

    [Fact]
    public async Task The_served_tool_list_is_pinned_so_any_name_description_or_schema_edit_fails()
    {
        await using var host = await ToolsTestHost.StartAsync();
        await using var client = await host.ConnectAsync(Ct);

        var first = Render(await client.ListToolsAsync(cancellationToken: Ct));
        var second = Render(await client.ListToolsAsync(cancellationToken: Ct));

        Assert.Equal(first, second);
        Assert.Equal(PinnedToolListSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(first))));
    }

    [Fact]
    public async Task A_foreign_host_header_gets_400_and_a_loopback_one_gets_200_over_kestrel()
    {
        var app = CarimboTools.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            configureServices: null,
            configureBuilder: builder => builder.Configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(CarimboTools.CasesDirectoryKey, ToolsTestHost.DecisionDirectory),
            ]));
        try
        {
            await app.StartAsync(Ct);
            var baseAddress = Assert.Single(app.Urls);
            using var http = new HttpClient();

            using var loopback = new HttpRequestMessage(HttpMethod.Get, baseAddress + "/healthz");
            loopback.Headers.Host = "127.0.0.1";
            using var ok = await http.SendAsync(loopback, Ct);
            Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);

            using var foreign = new HttpRequestMessage(HttpMethod.Get, baseAddress + "/healthz");
            foreign.Headers.Host = "evil.example";
            using var rejected = await http.SendAsync(foreign, Ct);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void Startup_refuses_a_case_directory_without_cases_and_the_default_url_is_loopback()
    {
        var empty = Path.Combine(Path.GetTempPath(), "carimbo-tools-empty-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(empty);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => CarimboTools.CreateApp(
                [],
                configureServices: null,
                configureBuilder: builder => builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(CarimboTools.CasesDirectoryKey, empty),
                ])));
            Assert.Contains("Tools:CasesDirectory", error.Message);
            Assert.DoesNotContain(empty, error.Message);
        }
        finally
        {
            System.IO.Directory.Delete(empty, recursive: true);
        }

        Assert.StartsWith("http://127.0.0.1:", CarimboTools.DefaultUrl);
        Assert.Equal("data/decision", CarimboTools.DefaultCasesDirectory);
    }

    private static string Render(IEnumerable<McpClientTool> tools) =>
        string.Join("\n", tools.Select(tool => tool.Name + "\n" + tool.Description + "\n" + tool.JsonSchema.GetRawText()));

    private static List<string> CommittedCaseIds()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ToolsTestHost.DecisionDirectory, "manifest.json")));
        return manifest.RootElement.GetProperty("cases").EnumerateArray()
            .Select(entry => entry.GetProperty("case_id").GetString()!)
            .ToList();
    }

    private static SortedDictionary<string, (string Sha256, DateTime LastWriteUtc)> Snapshot(string directory) =>
        new(System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(directory, path),
                path => (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), File.GetLastWriteTimeUtc(path)),
                StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static readonly string[] ForbiddenMembers = ["label", "intent", "decision", "cause", "labelling_version"];

    private static readonly string[] ForbiddenText = ["decision-labels", "approve-", "escalate-", "reject-"];

    // A leak walk over every JSON member at any depth: the expected label, the case intent and the manifest never appear.
    private static void AssertNoLabel(string text, string context)
    {
        foreach (var forbidden in ForbiddenText)
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }

        Walk(JsonNode.Parse(text), context);

        static void Walk(JsonNode? node, string context)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, child) in obj)
                    {
                        Assert.DoesNotContain(name, ForbiddenMembers);
                        Walk(child, context);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        Walk(child, context);
                    }

                    break;
            }
        }
    }

    private static void AssertNoPath(string text)
    {
        Assert.DoesNotContain(Repo.PathOf("data"), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Repo.PathOf(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("policy.json", text, StringComparison.Ordinal);
    }

    private static async Task<string> CallErrorAsync(McpClient client, string tool, string caseId)
    {
        var result = await client.CallToolAsync(tool, new Dictionary<string, object?> { ["case_id"] = caseId }, cancellationToken: Ct);
        Assert.True(result.IsError, tool + " should have failed for " + caseId);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    internal static async Task<string> CallAsync(McpClient client, string tool, string? caseId)
    {
        var arguments = caseId is null ? null : new Dictionary<string, object?> { ["case_id"] = caseId };
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        Assert.NotEqual(true, result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
