using System.Text.Json;
using Carimbo.Domain;
using Carimbo.Llm;
using Carimbo.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Carimbo.Agent.Tests;

/// <summary>The Tools host in process over the committed decision cases, and a toolbox on it.</summary>
internal sealed class InProcessToolsHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private InProcessToolsHost(WebApplication app)
    {
        _app = app;
        Http = app.GetTestClient();
    }

    public HttpClient Http { get; }

    public static string DecisionDirectory { get; } = Repo.PathOf("data", "decision");

    public static async Task<InProcessToolsHost> StartAsync()
    {
        var app = CarimboTools.CreateApp(
            [],
            configureServices: null,
            configureBuilder: builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(CarimboTools.CasesDirectoryKey, DecisionDirectory),
                ]);
            });
        await app.StartAsync();
        return new InProcessToolsHost(app);
    }

    public McpAgentToolbox Toolbox(TimeSpan? callTimeout = null) =>
        new(new Uri("http://localhost/mcp"), Http, callTimeout);

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// A scripted provider double: it answers by step (the number of follow-up turns divided by two), counts calls and
/// keeps every request it saw.
/// </summary>
internal sealed class ScriptedGateway(Func<int, LlmRequest, LlmResponse> respond) : ILlmGateway
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public List<LlmRequest> Requests { get; } = [];

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(respond(request.FollowUps.Count / 2, request));
    }
}

internal static class Scripts
{
    public static readonly LlmUsage Usage = new(1000, 200, 0, 0, 0);

    public static LlmToolCall Call(int number, string name, string caseId) =>
        new($"toolu_{number:00}", name, name == "get_policy" ? "{}" : "{\"case_id\":\"" + caseId + "\"}");

    /// <summary>The four calls a thorough model makes on its first turn.</summary>
    public static IReadOnlyList<LlmToolCall> FourCalls(string caseId) =>
    [
        Call(1, "get_extraction_status", caseId),
        Call(2, "get_invoice", caseId),
        Call(3, "get_validator_findings", caseId),
        Call(4, "get_policy", caseId),
    ];

    public static LlmResponse ToolUse(LlmRequest request, params LlmToolCall[] calls) =>
        new(
            string.Empty,
            LlmStopReason.ToolUse,
            null,
            Usage,
            request.Model,
            request.Model,
            "msg_tool",
            TimeSpan.FromMilliseconds(40),
            1)
        {
            Blocks = [.. calls.Select(LlmContentBlock.OfToolUse)],
        };

    public static LlmResponse Answer(LlmRequest request, Decision decision) => Answer(request, Json(decision));

    public static LlmResponse Answer(LlmRequest request, string text, LlmStopReason stop = LlmStopReason.EndTurn, string? detail = null) =>
        new(text, stop, detail, Usage, request.Model, request.Model, "msg_answer", TimeSpan.FromMilliseconds(30), 1)
        {
            Blocks = text.Length == 0 ? [] : [LlmContentBlock.OfText(text)],
        };

    /// <summary>A model that must never be called: any request fails the test.</summary>
    public static ScriptedGateway Never() =>
        new((_, _) => throw new InvalidOperationException("The model must not be called."));

    public static Decision Approve() => new(
        [
            new EvidenceItem("get_invoice", "recipient.tax_id", "CARIMBO0000177"),
            new EvidenceItem("get_policy", "buyer_cnpj", "CARIMBO0000177"),
        ],
        "The recipient is the buyer and no rule matches.",
        DecisionOutcome.Approve,
        DecisionCause.None);

    public static Decision Of(DecisionOutcome outcome, DecisionCause cause) =>
        new([], "Scripted decision.", outcome, cause);

    public static string Json(Decision decision) => JsonSerializer.Serialize(decision, Wire.Options);
}

/// <summary>Wraps a toolbox and records every call that reaches a session, so a test can see what was never sent.</summary>
internal sealed class RecordingToolbox(IAgentToolbox inner) : IAgentToolbox
{
    public List<(string Name, string ArgumentsJson)> Calls { get; } = [];

    public int Connects { get; private set; }

    public async Task<IAgentToolSession> ConnectAsync(CancellationToken cancellationToken)
    {
        Connects++;
        return new RecordingSession(await inner.ConnectAsync(cancellationToken), Calls);
    }

    private sealed class RecordingSession(IAgentToolSession inner, List<(string Name, string ArgumentsJson)> calls) : IAgentToolSession
    {
        public IReadOnlyList<LlmToolDefinition> Tools => inner.Tools;

        public Task<AgentToolResult> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            lock (calls)
            {
                calls.Add((name, argumentsJson));
            }

            return inner.CallAsync(name, argumentsJson, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

/// <summary>A toolbox with the four catalog tools whose calls are answered by a function, for faults the real host cannot make.</summary>
internal sealed class FakeToolbox(Func<string, string, CancellationToken, Task<AgentToolResult>> call) : IAgentToolbox
{
    public static IReadOnlyList<LlmToolDefinition> Definitions { get; } =
    [
        new(Decisions.CaseToolCatalog.GetExtractionStatus, Decisions.CaseToolCatalog.GetExtractionStatusDescription, CaseSchema),
        new(Decisions.CaseToolCatalog.GetInvoice, Decisions.CaseToolCatalog.GetInvoiceDescription, CaseSchema),
        new(Decisions.CaseToolCatalog.GetValidatorFindings, Decisions.CaseToolCatalog.GetValidatorFindingsDescription, CaseSchema),
        new(Decisions.CaseToolCatalog.GetPolicy, Decisions.CaseToolCatalog.GetPolicyDescription, "{\"type\":\"object\",\"properties\":{}}"),
    ];

    private const string CaseSchema =
        "{\"type\":\"object\",\"properties\":{\"case_id\":{\"type\":\"string\"}},\"required\":[\"case_id\"]}";

    public Task<IAgentToolSession> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IAgentToolSession>(new Session(call));

    private sealed class Session(Func<string, string, CancellationToken, Task<AgentToolResult>> call) : IAgentToolSession
    {
        public IReadOnlyList<LlmToolDefinition> Tools => Definitions;

        public Task<AgentToolResult> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken) =>
            call(name, argumentsJson, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
