using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Llm;
using ModelContextProtocol.Client;
using Xunit;

namespace Carimbo.Agent.Tests;

/// <summary>The hand-written decision loop end to end over the real Tools host and a scripted model (AGT-02, AGT-06 offline).</summary>
public class AgentRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CaseStore Cases = new(InProcessToolsHost.DecisionDirectory);

    private static ScriptedGateway Dc001Model() => new((step, request) => step == 0
        ? Scripts.ToolUse(request, [.. Scripts.FourCalls("dc-001")])
        : Scripts.Answer(request, Scripts.Approve()));

    private static CachingLlmGateway Stack(ILlmGateway inner, string directory, LlmCacheMode mode) =>
        new(
            new CostAccountingLlmGateway(inner, LlmPricingTable.LoadEmbedded()),
            new LlmCacheStore(directory),
            mode,
            TimeProvider.System);

    private static async Task WithTempDirectory(Func<string, Task> body)
    {
        var directory = Directory.CreateTempSubdirectory("carimbo-agent-test-").FullName;
        try
        {
            await body(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_agent_decides_dc_001_over_the_in_process_tools_host_and_a_read_only_rerun_replays_it_for_free()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        await WithTempDirectory(async directory =>
        {
            var model = Dc001Model();
            var runner = new AgentRunner(
                Stack(model, directory, LlmCacheMode.ReadWrite), host.Toolbox(), new AgentSettings(), AgentContract.Default);

            var result = await runner.DecideAsync("dc-001", AgentOverrides.None, Ct);

            Assert.Equal(AgentStatus.Decided, result.Status);
            Assert.Equal(AgentDecidedBy.Model, result.DecidedBy);
            Assert.Equal(Scripts.Json(Scripts.Approve()), Scripts.Json(result.Decision!));
            Assert.Equal(Scripts.Json(Scripts.Approve()), Scripts.Json(result.ModelDecision!));
            Assert.Null(result.ToolFailure);
            Assert.Null(result.Failure);
            Assert.Equal(2, model.Calls);

            var guard = result.Guard!;
            Assert.Equal("success", guard.ExtractionStatus);
            Assert.False(guard.Overridden);
            Assert.Equal("guard", guard.ToolCall.Source);
            Assert.Equal("ok", guard.ToolCall.Outcome);
            Assert.Equal("get_extraction_status", guard.ToolCall.Name);

            Assert.Equal(2, result.Steps.Count);
            Assert.Equal(
                ["get_extraction_status", "get_invoice", "get_validator_findings", "get_policy"],
                result.Steps[0].ToolCalls.Select(c => c.Name).ToArray());
            Assert.All(result.Steps[0].ToolCalls, c =>
            {
                Assert.Equal("model", c.Source);
                Assert.Equal("ok", c.Outcome);
            });
            Assert.Empty(result.Steps[1].ToolCalls);
            Assert.NotNull(result.ToolsSha256);

            // Step 1 echoes the step 0 blocks unchanged and answers every call in one user turn, in block order.
            var second = model.Requests[1];
            Assert.Equal(2, second.FollowUps.Count);
            Assert.Equal(LlmTurnRole.Assistant, second.FollowUps[0].Role);
            Assert.Equal(result.Steps[0].Response!.Blocks, second.FollowUps[0].Blocks);
            Assert.Equal(LlmTurnRole.User, second.FollowUps[1].Role);
            var @case = Cases.Load("dc-001");
            Assert.Equal(
                [
                    CaseToolCatalog.StatusJson(@case),
                    CaseToolCatalog.InvoiceJson(@case),
                    CaseToolCatalog.FindingsJson(@case),
                    CaseToolCatalog.PolicyJson(Cases.LoadPolicy()),
                ],
                second.FollowUps[1].ToolResults.Select(r => r.Content).ToArray());
            Assert.Equal(
                ["toolu_01", "toolu_02", "toolu_03", "toolu_04"],
                second.FollowUps[1].ToolResults.Select(r => r.ToolUseId).ToArray());
            Assert.All(second.FollowUps[1].ToolResults, r => Assert.False(r.IsError));

            // Every request has the same fixed shape.
            var schema = File.ReadAllText(Repo.PathOf("schema", "decision.model.schema.json"));
            Assert.True(AgentPromptCatalog.TryGet("agent-001", out var systemPrompt));
            Assert.All(model.Requests, request =>
            {
                Assert.Equal(systemPrompt, request.SystemPrompt);
                Assert.Equal(AgentPromptCatalog.UserTurn("dc-001"), request.Prompt);
                Assert.Null(request.Document);
                Assert.Equal(schema, request.OutputSchemaJson);
                Assert.Equal(CaseToolCatalog.Names, request.Tools.Select(t => t.Name).ToArray());
                Assert.Equal("agent-001", request.PromptVersion);
                Assert.Equal("claude-haiku-4-5", request.Model);
            });

            // A rerun in read_only over the same directory makes zero model calls and returns the same run.
            var replayModel = Dc001Model();
            var replayRunner = new AgentRunner(
                Stack(replayModel, directory, LlmCacheMode.ReadOnly), host.Toolbox(), new AgentSettings(), AgentContract.Default);

            var replay = await replayRunner.DecideAsync("dc-001", AgentOverrides.None, Ct);

            Assert.Equal(0, replayModel.Calls);
            Assert.Equal(AgentStatus.Decided, replay.Status);
            Assert.Equal(Scripts.Json(result.Decision!), Scripts.Json(replay.Decision!));
            Assert.Equal(guard.ExtractionStatus, replay.Guard!.ExtractionStatus);
            Assert.Equal(
                result.Steps.SelectMany(s => s.ToolCalls).Select(c => (c.Name, c.ArgumentsJson, c.Outcome)).ToArray(),
                replay.Steps.SelectMany(s => s.ToolCalls).Select(c => (c.Name, c.ArgumentsJson, c.Outcome)).ToArray());
            Assert.Equal(2, replay.Steps.Count);
            Assert.All(replay.Steps, step =>
            {
                Assert.Equal(0, step.Response!.HttpAttempts);
                Assert.Equal(LlmCacheStatus.Hit, step.Response.Cache!.Status);
            });
        });
    }

    [Fact]
    public async Task The_request_overrides_reach_every_model_request()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        var model = Dc001Model();
        var runner = new AgentRunner(model, host.Toolbox(), new AgentSettings(), AgentContract.Default);

        var result = await runner.DecideAsync(
            "dc-001",
            new AgentOverrides { Model = "claude-sonnet-5-5", CacheMode = LlmCacheMode.Off, ReplicateSalt = "r1" },
            Ct);

        Assert.Equal(AgentStatus.Decided, result.Status);
        Assert.Equal("claude-sonnet-5-5", result.Model);
        Assert.Equal(2, model.Requests.Count);
        Assert.All(model.Requests, request =>
        {
            Assert.Equal("claude-sonnet-5-5", request.Model);
            Assert.Equal(LlmCacheMode.Off, request.CacheMode);
            Assert.Equal("r1", request.ReplicateSalt);
        });
    }

    [Fact]
    public async Task The_four_listed_tool_definitions_go_through_a_real_anthropic_gateway_unchanged()
    {
        await using var host = await InProcessToolsHost.StartAsync();
        await using var session = await host.Toolbox().ConnectAsync(Ct);
        const string canned = """
            {"model":"claude-haiku-4-5","id":"msg_1","type":"message","role":"assistant",
             "content":[{"type":"text","text":"{}"}],"stop_reason":"end_turn","stop_sequence":null,
             "usage":{"input_tokens":10,"output_tokens":2,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
            """;
        var handler = new CannedHandler(canned);
        using var gateway = new AnthropicLlmGateway(
            new AnthropicLlmGatewayOptions("test-key-not-real", new Uri("http://anthropic.invalid"), TimeSpan.FromSeconds(30), 0),
            handler);
        var request = new LlmRequest("claude-haiku-4-5", 256, AgentPromptCatalog.UserTurn("dc-001"), null, AgentContract.Default.OutputSchemaJson)
        {
            SystemPrompt = AgentContract.Default.SystemPrompt,
            Tools = session.Tools,
            PromptVersion = "agent-001",
        };

        await gateway.CompleteAsync(request, Ct);

        var body = JsonNode.Parse(Assert.Single(handler.Bodies))!;
        var sent = body["tools"]!.AsArray();
        Assert.Equal(session.Tools.Select(t => t.Name).ToArray(), sent.Select(t => t!["name"]!.GetValue<string>()).ToArray());
        for (var i = 0; i < sent.Count; i++)
        {
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(session.Tools[i].InputSchemaJson), sent[i]!["input_schema"]),
                "the sent input_schema of " + session.Tools[i].Name + " differs from the listed one");
        }
    }

    [Fact]
    public async Task The_sdk_lists_only_type_properties_and_required_in_every_tool_input_schema()
    {
        // The toolbox keeps exactly these three members for the adapter; this test fails if the SDK ever lists
        // another one, so that nothing is dropped silently.
        await using var host = await InProcessToolsHost.StartAsync();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            host.Http,
            ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: Ct);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(4, tools.Count);
        foreach (var tool in tools)
        {
            var members = tool.JsonSchema.EnumerateObject().Select(m => m.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.True(
                members.All(m => m is "type" or "properties" or "required"),
                tool.Name + " lists the schema members: " + string.Join(", ", members));
        }
    }

    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            response.Headers.Add("request-id", "req_synthetic");
            return response;
        }
    }
}
