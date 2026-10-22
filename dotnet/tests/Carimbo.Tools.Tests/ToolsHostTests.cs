using Carimbo.Decisions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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

    internal static async Task<string> CallAsync(McpClient client, string tool, string? caseId)
    {
        var arguments = caseId is null ? null : new Dictionary<string, object?> { ["case_id"] = caseId };
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        Assert.NotEqual(true, result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
