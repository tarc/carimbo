// Phase 3.1 agent probe (plan 03.1-07, phase decision D-13). Reached through `LlmSpike --agent-probe`; never part of
// `dotnet test`.
//
// It sends the exact conversation the agent will send (agent-001 system prompt, the case user turn, the four tool
// definitions, the Decision model-facing schema as output_config.format, tool turns answered from the committed
// cases) through the real AnthropicLlmGateway:
//
//   (claude-haiku-4-5, dc-001) and (claude-haiku-4-5, dc-003): at most 6 steps, MaxTokens 8192
//   (claude-sonnet-5-5, dc-001): the thinking-echo check, probe-only MaxTokens 2048, at most 2 steps
//
// --dry-run answers every request from a recording handler (no key, no network). The live run is reached only
// through `just agent-probe` and is capped by --budget-usd with a pre-flight estimate before every call.
//
// Secrets: the key comes from the environment, is passed explicitly and is never printed. The record holds shapes,
// counts, status codes, tool names and parse outcomes only: no key, prompt, tool result or model text.
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Extraction;
using Carimbo.Llm;

internal static class AgentProbe
{
    internal const string Haiku = "claude-haiku-4-5";
    internal const string Sonnet = "claude-sonnet-5-5";
    internal const string PromptVersion = AgentPromptCatalog.DefaultVersion;
    internal const string SyntheticSignature = "synthetic-signature";

    /// <summary>The three probe runs in order.</summary>
    internal static readonly IReadOnlyList<RunSpec> Runs =
    [
        new(Haiku, "dc-001", 6, 8192, false),
        new(Haiku, "dc-003", 6, 8192, false),
        new(Sonnet, "dc-001", 2, 2048, true),
    ];

    public static async Task<int> RunAsync(string[] args, bool dryRun)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var budgetText = Arg("--budget-usd");
        var cases = Arg("--cases");
        var outPath = Arg("--out");
        if (budgetText is null || cases is null || outPath is null
            || !decimal.TryParse(budgetText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var budget))
        {
            Console.Error.WriteLine("usage: LlmSpike --agent-probe [--dry-run] --budget-usd DECIMAL --cases PATH --out PATH");
            return 64;
        }

        if (!Directory.Exists(cases))
        {
            Console.Error.WriteLine($"{cases} not found");
            return 66;
        }

        if (!AgentPromptCatalog.TryGet(PromptVersion, out var agentPrompt))
        {
            Console.Error.WriteLine("agent prompt missing");
            return 2;
        }

        // T-03.1-24: the probe sends the schema the agent will send, and that is the committed file.
        var schemaJson = ModelFacingSchema.Json(typeof(Decision));
        var committed = FindCommittedSchema();
        if (committed is null || !string.Equals(File.ReadAllText(committed), schemaJson, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("the Decision model-facing schema differs from schema/decision.model.schema.json (or the file is missing)");
            return 2;
        }

        var store = new CaseStore(cases);
        var pricing = LlmPricingTable.LoadEmbedded();
        var setup = new Setup(agentPrompt, schemaJson, store, pricing, ToolDefinitions());

        if (dryRun)
        {
            return await DryRunAsync(setup, budget, outPath);
        }

        // D-10: the dedicated project variable first, then the standard one.
        var key = Environment.GetEnvironmentVariable("CARIMBO_ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("provider key unset");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        using var inner = new AnthropicLlmGateway(new AnthropicLlmGatewayOptions(key, null, TimeSpan.FromSeconds(120), 0));
        var gateway = new CostAccountingLlmGateway(inner, pricing);
        var driver = new Driver(setup, budget, cancellation.Token);
        try
        {
            var records = new List<RunRecord>();
            foreach (var spec in Runs)
            {
                records.Add(await driver.RunAsync(spec, gateway));
            }

            WriteAtomic(outPath, Doc.Render(setup, records, driver.Spent, budget, dryRun: false));
            Console.WriteLine($"PROBE DONE: spend {Doc.Money(driver.Spent)} of {Doc.Money(budget)}; wrote {outPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"INTERRUPTED: spend so far {Doc.Money(driver.Spent)}; nothing was written");
            return 130;
        }
    }

    /// <summary>The four case tools as the agent sends them (names, descriptions and input schemas from the catalog).</summary>
    internal static IReadOnlyList<LlmToolDefinition> ToolDefinitions()
    {
        var caseSchema =
            $$$"""{"type":"object","properties":{"case_id":{"description":{{{JsonSerializer.Serialize(CaseToolCatalog.CaseIdParameterDescription)}}},"type":"string"}},"required":["case_id"]}""";
        const string policySchema = """{"type":"object","properties":{}}""";
        return
        [
            new(CaseToolCatalog.GetExtractionStatus, CaseToolCatalog.GetExtractionStatusDescription, caseSchema),
            new(CaseToolCatalog.GetInvoice, CaseToolCatalog.GetInvoiceDescription, caseSchema),
            new(CaseToolCatalog.GetValidatorFindings, CaseToolCatalog.GetValidatorFindingsDescription, caseSchema),
            new(CaseToolCatalog.GetPolicy, CaseToolCatalog.GetPolicyDescription, policySchema),
        ];
    }

    internal static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string? FindCommittedSchema()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "schema", "decision.model.schema.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void WriteAtomic(string outPath, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath + ".tmp", text, new UTF8Encoding(false));
        File.Move(outPath + ".tmp", outPath, overwrite: true);
    }

    // ---- dry run ---------------------------------------------------------------------------------------

    private static async Task<int> DryRunAsync(Setup setup, decimal budget, string outPath)
    {
        Console.WriteLine($"agent prompt {PromptVersion} sha256 {Sha256(setup.AgentPrompt)}; Decision schema sha256 {Sha256(setup.SchemaJson)}");
        var driver = new Driver(setup, null, CancellationToken.None);
        var records = new List<RunRecord>();
        try
        {
            foreach (var spec in Runs)
            {
                var handler = DryRunHandler(spec);
                using var inner = new AnthropicLlmGateway(
                    new AnthropicLlmGatewayOptions("dry-run-key", new Uri("http://anthropic.invalid"), TimeSpan.FromSeconds(30), 0), handler);
                var gateway = new CostAccountingLlmGateway(inner, setup.Pricing);
                var record = await driver.RunAsync(spec, gateway);
                AssertRecordedBodies(setup, spec, handler, record);
                records.Add(record);
            }
        }
        catch (DryRunAssertion ex)
        {
            Console.Error.WriteLine("dry run assertion failed: " + ex.Message);
            return 1;
        }

        WriteAtomic(outPath, Doc.Render(setup, records, driver.Spent, budget, dryRun: true));
        Console.WriteLine($"DRY RUN OK: {records.Sum(r => r.Calls.Count)} recorded requests checked; wrote {outPath}");
        return 0;
    }

    private static RecordingHandler DryRunHandler(RunSpec spec)
    {
        var model = spec.Model == Haiku ? "claude-haiku-4-5-20251001" : spec.Model;
        var usage = new JsonObject
        {
            ["input_tokens"] = 1200,
            ["output_tokens"] = 150,
            ["cache_creation_input_tokens"] = 0,
            ["cache_read_input_tokens"] = 0,
        };

        var content = new JsonArray();
        if (spec.EchoCheck)
        {
            content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = string.Empty, ["signature"] = SyntheticSignature });
        }

        var n = 0;
        foreach (var name in CaseToolCatalog.Names)
        {
            var input = name == CaseToolCatalog.GetPolicy ? new JsonObject() : new JsonObject { ["case_id"] = spec.CaseId };
            content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = $"toolu_synthetic_{++n}", ["name"] = name, ["input"] = input });
        }

        var toolTurn = new JsonObject
        {
            ["id"] = "msg_synthetic_tools",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = model,
            ["content"] = content,
            ["stop_reason"] = "tool_use",
            ["stop_sequence"] = null,
            ["usage"] = usage.DeepClone(),
        };

        var decision = new Decision(
            [new EvidenceItem(CaseToolCatalog.GetInvoice, "recipient.tax_id", "synthetic")],
            "Synthetic dry-run reason.",
            DecisionOutcome.Approve,
            DecisionCause.None);
        var answer = new JsonObject
        {
            ["id"] = "msg_synthetic_end",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = model,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(decision, Wire.Options) }),
            ["stop_reason"] = "end_turn",
            ["stop_sequence"] = null,
            ["usage"] = usage.DeepClone(),
        };

        return new RecordingHandler((HttpStatusCode.OK, toolTurn.ToJsonString()), (HttpStatusCode.OK, answer.ToJsonString()));
    }

    private static void AssertRecordedBodies(Setup setup, RunSpec spec, RecordingHandler handler, RunRecord record)
    {
        var label = $"{spec.Model} {spec.CaseId}";
        if (handler.RequestBodies.Count != 2)
        {
            throw new DryRunAssertion($"{label}: expected 2 requests, saw {handler.RequestBodies.Count}");
        }

        var expectedSchema = JsonNode.Parse(setup.SchemaJson)!;
        var expectedUser = AgentPromptCatalog.UserTurn(spec.CaseId);
        foreach (var raw in handler.RequestBodies)
        {
            var body = JsonNode.Parse(raw)!.AsObject();
            if (SystemText(body["system"]) != setup.AgentPrompt)
            {
                throw new DryRunAssertion($"{label}: system is not the {PromptVersion} text");
            }

            var tools = body["tools"]?.AsArray();
            if (tools is null || !tools.Select(t => t!["name"]!.GetValue<string>()).SequenceEqual(CaseToolCatalog.Names))
            {
                throw new DryRunAssertion($"{label}: tools are not the four case tools in catalog order");
            }

            var schema = body["output_config"]?["format"]?["schema"];
            if (schema is null || !JsonNode.DeepEquals(schema, expectedSchema))
            {
                throw new DryRunAssertion($"{label}: output_config.format.schema is not the Decision model-facing schema");
            }

            foreach (var forbidden in new[] { "tool_choice", "temperature", "top_p", "top_k" })
            {
                if (body.ContainsKey(forbidden))
                {
                    throw new DryRunAssertion($"{label}: the request carries {forbidden}");
                }
            }

            if (body["max_tokens"]?.GetValue<int>() != spec.MaxTokens)
            {
                throw new DryRunAssertion($"{label}: max_tokens is not {spec.MaxTokens}");
            }

            var first = body["messages"]![0]!["content"]!.AsArray();
            if (first.Count != 1 || first[0]!["text"]?.GetValue<string>() != expectedUser)
            {
                throw new DryRunAssertion($"{label}: the first user turn is not the agent-001 user turn alone");
            }
        }

        var second = JsonNode.Parse(handler.RequestBodies[1])!["messages"]!.AsArray();
        if (second.Count != 3)
        {
            throw new DryRunAssertion($"{label}: the second request should carry 3 messages, it carries {second.Count}");
        }

        var results = second[2]!["content"]!.AsArray();
        if (results.Count != 4 || results.Any(r => r!["type"]!.GetValue<string>() != "tool_result" || r["is_error"]?.GetValue<bool>() == true))
        {
            throw new DryRunAssertion($"{label}: the tool answers are not four successful tool_result blocks");
        }

        var served = CaseToolCatalog.PolicyJson(setup.Store.LoadPolicy());
        if (!results.Any(r => ResultText(r!) == served))
        {
            throw new DryRunAssertion($"{label}: the get_policy answer is not the catalog rendering of policy.json");
        }

        if (spec.EchoCheck)
        {
            var echoed = second[1]!["content"]!.AsArray()[0]!;
            if (echoed["type"]?.GetValue<string>() != "thinking" || echoed["signature"]?.GetValue<string>() != SyntheticSignature)
            {
                throw new DryRunAssertion($"{label}: the second request does not start with the echoed thinking block and its signature");
            }
        }

        if (record is not { Completed: true, Parsed: true, EvidenceOk: true, ToolsBeforeAnswer: true })
        {
            throw new DryRunAssertion($"{label}: the scripted run did not end in a parsed Decision after tool calls");
        }
    }

    private static string? SystemText(JsonNode? system) => system switch
    {
        JsonValue value => value.GetValue<string>(),
        JsonArray array when array.Count > 0 => array[0]!["text"]?.GetValue<string>(),
        _ => null,
    };

    private static string? ResultText(JsonNode result) => result["content"] switch
    {
        JsonValue value => value.GetValue<string>(),
        JsonArray array when array.Count > 0 => array[0]!["text"]?.GetValue<string>(),
        _ => null,
    };
}

internal sealed class DryRunAssertion(string message) : Exception(message);

internal sealed record RunSpec(string Model, string CaseId, int MaxSteps, int MaxTokens, bool EchoCheck);

internal sealed record Setup(
    string AgentPrompt, string SchemaJson, CaseStore Store, LlmPricingTable Pricing, IReadOnlyList<LlmToolDefinition> Tools);

internal sealed record CallRecord(
    string Model,
    string CaseId,
    int Step,
    string Outcome,
    string StopReason,
    string Kinds,
    string Tools,
    LlmUsage? Usage,
    decimal? Cost,
    decimal Estimate);

internal sealed class RunRecord(RunSpec spec)
{
    public RunSpec Spec { get; } = spec;

    public List<CallRecord> Calls { get; } = [];

    /// <summary>The first request returned HTTP 200; null when it was never sent.</summary>
    public bool? FirstOk { get; set; }

    /// <summary>The first request was rejected as a bad request (HTTP 400).</summary>
    public bool FirstBadRequest { get; set; }

    /// <summary>The run reached an end_turn answer.</summary>
    public bool Completed { get; set; }

    public bool? Parsed { get; set; }

    public string ParseNote { get; set; } = "no final answer";

    public bool ToolsBeforeAnswer { get; set; }

    public bool? EvidenceOk { get; set; }

    public string? StopNote { get; set; }

    /// <summary>"verified" or "unverified (reason)"; only meaningful for the echo run.</summary>
    public string EchoStatus { get; set; } = "unverified (not run)";
}

/// <summary>Runs the probe conversations, one call at a time, under an optional budget.</summary>
internal sealed class Driver(Setup setup, decimal? budget, CancellationToken ct)
{
    public decimal Spent { get; private set; }

    private bool budgetStopped;

    public async Task<RunRecord> RunAsync(RunSpec spec, ILlmGateway gateway)
    {
        var record = new RunRecord(spec);
        if (budgetStopped)
        {
            record.StopNote = "stopped: budget";
            record.EchoStatus = "unverified (budget)";
            return record;
        }

        var followUps = new List<LlmTurn>();
        var called = new HashSet<string>(StringComparer.Ordinal);
        LlmResponse? firstResponse = null;
        for (var step = 1; step <= spec.MaxSteps; step++)
        {
            var request = new LlmRequest(spec.Model, spec.MaxTokens, AgentPromptCatalog.UserTurn(spec.CaseId), null, setup.SchemaJson)
            {
                SystemPrompt = setup.AgentPrompt,
                Tools = setup.Tools,
                FollowUps = [.. followUps],
                PromptVersion = AgentProbe.PromptVersion,
            };

            var estimate = Estimate(request);
            if (budget is { } cap && Spent + estimate > cap)
            {
                budgetStopped = true;
                record.StopNote = "stopped: budget";
                record.Calls.Add(new(spec.Model, spec.CaseId, step, "skipped: budget", "-", "-", "-", null, null, estimate));
                Console.WriteLine($"  {Label(spec)} step {step}: skipped (budget)");
                break;
            }

            LlmResponse response;
            try
            {
                response = await gateway.CompleteAsync(request, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LlmGatewayException ex)
            {
                var status = ex.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "none";
                var failure = $"failed: {ex.Kind} HTTP {status}";
                record.Calls.Add(new(spec.Model, spec.CaseId, step, failure, "-", "-", "-", null, null, estimate));
                Console.WriteLine($"  {Label(spec)} step {step}: {failure}");
                if (step == 1)
                {
                    record.FirstOk = false;
                    record.FirstBadRequest = ex.Kind == LlmFailureKind.BadRequest;
                }

                record.StopNote = failure;
                break;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException)
            {
                var failure = $"failed: {ex.GetType().Name}";
                record.Calls.Add(new(spec.Model, spec.CaseId, step, failure, "-", "-", "-", null, null, estimate));
                Console.WriteLine($"  {Label(spec)} step {step}: {failure}");
                if (step == 1)
                {
                    record.FirstOk = false;
                }

                record.StopNote = failure;
                break;
            }

            var cost = response.Cost?.AmountUsd;
            Spent += cost ?? 0m;
            if (step == 1)
            {
                record.FirstOk = true;
                firstResponse = response;
            }

            record.Calls.Add(new(
                spec.Model,
                spec.CaseId,
                step,
                "HTTP 200",
                StopName(response.StopReason),
                Kinds(response.Blocks),
                string.Join(",", response.ToolCalls.Select(c => c.Name)),
                response.Usage,
                cost,
                estimate));
            Console.WriteLine($"  {Label(spec)} step {step}: HTTP 200, {StopName(response.StopReason)}, cost {Doc.Money(cost ?? 0m)}");

            if (response.StopReason == LlmStopReason.ToolUse && response.ToolCalls.Count > 0)
            {
                foreach (var call in response.ToolCalls)
                {
                    called.Add(call.Name);
                }

                if (step == spec.MaxSteps)
                {
                    record.StopNote = "stopped: step cap";
                    break;
                }

                followUps.Add(new LlmTurn(LlmTurnRole.Assistant, string.Empty) { Blocks = response.Blocks });
                followUps.Add(new LlmTurn(LlmTurnRole.User, string.Empty)
                {
                    ToolResults = [.. response.ToolCalls.Select(Answer)],
                });
                continue;
            }

            if (response.StopReason == LlmStopReason.EndTurn)
            {
                record.Completed = true;
                record.ToolsBeforeAnswer = called.Count > 0;
                (record.Parsed, record.EvidenceOk, record.ParseNote) = ParseDecision(response.Text, called);
            }
            else
            {
                record.StopNote = $"stopped: {StopName(response.StopReason)}";
            }

            break;
        }

        if (spec.EchoCheck)
        {
            record.EchoStatus = EchoVerdict(firstResponse, record);
        }

        return record;
    }

    private static string Label(RunSpec spec) => $"{spec.Model} {spec.CaseId}";

    // The echo is verified when the step-2 request, carrying the step-1 blocks unchanged, returned HTTP 200.
    private static string EchoVerdict(LlmResponse? first, RunRecord record)
    {
        var call1 = record.Calls.FirstOrDefault(c => c.Step == 1);
        if (call1 is null || call1.Outcome.StartsWith("skipped", StringComparison.Ordinal))
        {
            return "unverified (budget)";
        }

        if (first is null)
        {
            return $"unverified ({Reason(call1.Outcome)})";
        }

        if (first.StopReason == LlmStopReason.MaxTokens)
        {
            return "unverified (max_tokens)";
        }

        if (!first.Blocks.Any(b => b.Kind is LlmContentKind.Thinking or LlmContentKind.RedactedThinking))
        {
            return "unverified (no_thinking_block)";
        }

        if (first.ToolCalls.Count == 0)
        {
            return "unverified (no_tool_use)";
        }

        var call2 = record.Calls.FirstOrDefault(c => c.Step == 2);
        return call2 switch
        {
            null => "unverified (budget)",
            { Outcome: "HTTP 200" } => "verified",
            _ => $"unverified ({Reason(call2.Outcome)})",
        };
    }

    private static string Reason(string outcome) =>
        outcome.StartsWith("skipped", StringComparison.Ordinal)
            ? "budget"
            : outcome.Replace("failed: ", string.Empty, StringComparison.Ordinal);

    private static string StopName(LlmStopReason reason) => reason switch
    {
        LlmStopReason.EndTurn => "end_turn",
        LlmStopReason.MaxTokens => "max_tokens",
        LlmStopReason.Refusal => "refusal",
        LlmStopReason.StopSequence => "stop_sequence",
        LlmStopReason.ToolUse => "tool_use",
        _ => "other",
    };

    private static string Kinds(IReadOnlyList<LlmContentBlock> blocks) =>
        blocks.Count == 0
            ? "none"
            : string.Join(",", blocks.Select(b => b.Kind switch
            {
                LlmContentKind.Text => "text",
                LlmContentKind.ToolUse => "tool_use",
                LlmContentKind.Thinking => "thinking",
                _ => "redacted_thinking",
            }));

    /// <summary>Pre-flight estimate: input characters / 3 as input tokens plus MaxTokens as output tokens.</summary>
    private decimal Estimate(LlmRequest request)
    {
        long chars = (request.SystemPrompt?.Length ?? 0) + request.Prompt.Length + request.OutputSchemaJson.Length;
        foreach (var tool in request.Tools)
        {
            chars += tool.Name.Length + tool.Description.Length + tool.InputSchemaJson.Length;
        }

        foreach (var turn in request.FollowUps)
        {
            chars += turn.Text.Length;
            foreach (var block in turn.Blocks)
            {
                chars += (block.Text?.Length ?? 0) + (block.Signature?.Length ?? 0) + (block.Data?.Length ?? 0)
                    + (block.ToolCall is { } call ? call.Id.Length + call.Name.Length + call.InputJson.Length : 0);
            }

            foreach (var result in turn.ToolResults)
            {
                chars += result.ToolUseId.Length + result.Content.Length;
            }
        }

        var usage = new LlmUsage((chars / 3) + 1, request.MaxTokens, 0, 0, 0);
        return setup.Pricing.Price(request.Model, usage).AmountUsd ?? 0m;
    }

    /// <summary>Answers one tool call from the committed cases; an unknown tool or case is an is_error result.</summary>
    private LlmToolResult Answer(LlmToolCall call)
    {
        try
        {
            if (call.Name == CaseToolCatalog.GetPolicy)
            {
                return new LlmToolResult(call.Id, CaseToolCatalog.PolicyJson(setup.Store.LoadPolicy()), false);
            }

            using var input = JsonDocument.Parse(call.InputJson);
            var caseId = input.RootElement.ValueKind == JsonValueKind.Object
                && input.RootElement.TryGetProperty("case_id", out var id)
                && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
            var file = setup.Store.Load(caseId!);
            return call.Name switch
            {
                CaseToolCatalog.GetExtractionStatus => new LlmToolResult(call.Id, CaseToolCatalog.StatusJson(file), false),
                CaseToolCatalog.GetInvoice => new LlmToolResult(call.Id, CaseToolCatalog.InvoiceJson(file), false),
                CaseToolCatalog.GetValidatorFindings => new LlmToolResult(call.Id, CaseToolCatalog.FindingsJson(file), false),
                _ => new LlmToolResult(call.Id, "Unknown tool.", true),
            };
        }
        catch (Exception ex) when (ex is InvalidCaseIdException or CaseNotFoundException or JsonException)
        {
            return new LlmToolResult(call.Id, "The case id is not valid or names no case.", true);
        }
    }

    private static (bool Parsed, bool EvidenceOk, string Note) ParseDecision(string text, HashSet<string> called)
    {
        try
        {
            var decision = JsonSerializer.Deserialize<Decision>(text, Wire.Options);
            if (decision is null)
            {
                return (false, false, "parsed: false (null document)");
            }

            var nulls = decision.NullViolations();
            if (nulls.Count > 0)
            {
                return (false, false, $"parsed: false (null at [{string.Join(", ", nulls)}])");
            }

            var evidenceOk = decision.Evidence.All(e => called.Contains(e.Source));
            return (true, evidenceOk, $"parsed: true, outcome {decision.Outcome}, cause {decision.Cause}, {decision.Evidence.Count} citations");
        }
        catch (JsonException ex)
        {
            return (false, false, $"parsed: false (JsonException at `{ex.Path}`)");
        }
    }
}

/// <summary>Renders the spike document. It records shapes, counts and costs only.</summary>
internal static class Doc
{
    public static string Money(decimal value) => "US$" + value.ToString("F4", CultureInfo.InvariantCulture);

    private static string YesNo(bool value) => value ? "yes" : "no";

    public static string Render(Setup setup, IReadOnlyList<RunRecord> records, decimal spent, decimal budget, bool dryRun)
    {
        var haiku = records.Where(r => r.Spec.Model == AgentProbe.Haiku).ToList();
        var echo = records.FirstOrDefault(r => r.Spec.EchoCheck);
        var path = dryRun ? "dry-run (no live call)" : PathOf(haiku);

        var doc = new StringBuilder();
        doc.AppendLine("# Agent probe (phase decision D-13)");
        doc.AppendLine();
        doc.AppendLine("Recorded by `dotnet/tools/LlmSpike --agent-probe` (plan 03.1-07). This record is sanitised: it holds shapes, counts, status codes,");
        doc.AppendLine("tool names and parse outcomes only. It contains no provider key, no prompt, no tool result and no model text.");
        doc.AppendLine();
        doc.AppendLine("## Setup");
        doc.AppendLine();
        doc.AppendLine($"- Run date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        doc.AppendLine($"- Mode: {(dryRun ? "dry run (recording handler, no key, no network)" : "live (real AnthropicLlmGateway, MaxRetries 0, no cache)")}");
        doc.AppendLine("- Runs: " + string.Join("; ", records.Select(r =>
            $"`{r.Spec.Model}` on `{r.Spec.CaseId}` (max {r.Spec.MaxSteps} steps, MaxTokens {r.Spec.MaxTokens}{(r.Spec.EchoCheck ? ", thinking-echo check" : string.Empty)})")));
        doc.AppendLine($"- Agent prompt `{AgentProbe.PromptVersion}` sha256 `{AgentProbe.Sha256(setup.AgentPrompt)}`");
        doc.AppendLine($"- Decision model-facing schema sha256 `{AgentProbe.Sha256(setup.SchemaJson)}` (equals the committed schema/decision.model.schema.json)");
        doc.AppendLine($"- Tools: {string.Join(", ", setup.Tools.Select(t => t.Name))}");
        doc.AppendLine($"- Budget: {Money(budget)}; a call is skipped when the spend so far plus its pre-flight estimate (input characters / 3 as input tokens plus MaxTokens as output tokens, at the embedded pricing table `{setup.Pricing.Version}`) would pass it");
        doc.AppendLine("- Counts toward the US$5.00 Phase 3.1 cap.");
        doc.AppendLine();

        doc.AppendLine("## Calls");
        doc.AppendLine();
        doc.AppendLine("| Model | Case | Step | Outcome | Stop reason | Block kinds | Tools called | Input | Output | Cache read | Cache write | Estimate | Cost |");
        doc.AppendLine("|-------|------|------|---------|-------------|-------------|--------------|-------|--------|------------|-------------|----------|------|");
        foreach (var call in records.SelectMany(r => r.Calls))
        {
            var u = call.Usage;
            doc.AppendLine(
                $"| {call.Model} | {call.CaseId} | {call.Step} | {call.Outcome} | {call.StopReason} | {call.Kinds} | {(call.Tools.Length == 0 ? "-" : call.Tools)} "
                + $"| {u?.InputTokens.ToString(CultureInfo.InvariantCulture) ?? "-"} | {u?.OutputTokens.ToString(CultureInfo.InvariantCulture) ?? "-"} "
                + $"| {u?.CacheReadTokens.ToString(CultureInfo.InvariantCulture) ?? "-"} "
                + $"| {(u is null ? "-" : (u.CacheWrite5mTokens + u.CacheWrite1hTokens).ToString(CultureInfo.InvariantCulture))} "
                + $"| {Money(call.Estimate)} | {(call.Cost is { } c ? Money(c) : "-")} |");
        }

        doc.AppendLine();
        doc.AppendLine("## Result");
        doc.AppendLine();
        doc.AppendLine($"path: {path}");
        doc.AppendLine();
        foreach (var r in records.Where(r => !r.Spec.EchoCheck))
        {
            doc.AppendLine($"- `{r.Spec.Model}` on `{r.Spec.CaseId}`: first request {FirstOutcome(r)}; tools before answering: {YesNo(r.ToolsBeforeAnswer)}; "
                + $"final answer: {(r.Completed ? r.ParseNote : r.StopNote ?? "none")}; every evidence source was a tool it called: {(r.EvidenceOk is { } e ? YesNo(e) : "not determined")}");
        }

        if (echo is not null)
        {
            doc.AppendLine($"- `{echo.Spec.Model}` on `{echo.Spec.CaseId}` (echo check, not part of the path): first request {FirstOutcome(echo)}; "
                + $"final answer: {(echo.Completed ? echo.ParseNote : echo.StopNote ?? "none")}");
        }

        doc.AppendLine();
        doc.AppendLine(dryRun ? "thinking_echo: dry-run" : $"thinking_echo: {echo?.EchoStatus ?? "unverified (not run)"}");
        doc.AppendLine();
        doc.AppendLine($"- Total spend: {Money(spent)} of {Money(budget)}; within budget: {YesNo(spent <= budget)}");
        doc.AppendLine();

        doc.AppendLine("## Recommendation");
        doc.AppendLine();
        doc.AppendLine(dryRun
            ? "Dry run only: no live evidence. Run `just agent-probe` to settle the final-answer path."
            : path switch
            {
                "structured_output" => "Keep the planned loop: tools on every step and the Decision schema as `output_config.format`; an answer without tool calls is the final answer.",
                "submit_decision_fallback" => "The provider rejected tools together with `output_config.format`. Adopt the strict local `submit_decision` tool under `tool_choice` auto (phase decision D-13) and revise plans 03.1-08 to 03.1-10.",
                _ => "The probe did not settle the path. Rerun `just agent-probe` within the phase cap, or decide the path explicitly and record it in D-38.",
            });
        return doc.ToString();
    }

    private static string FirstOutcome(RunRecord r) => r.FirstOk switch
    {
        true => "accepted (HTTP 200)",
        false => r.Calls.FirstOrDefault()?.Outcome.Replace("failed: ", string.Empty, StringComparison.Ordinal) ?? "failed",
        _ => "not sent",
    };

    // Decided from the two Haiku runs only; the Sonnet run is the echo check and may stop at its two-step cap.
    private static string PathOf(IReadOnlyList<RunRecord> haiku)
    {
        if (haiku.Any(r => r.FirstBadRequest))
        {
            return "submit_decision_fallback";
        }

        var completed = haiku.Where(r => r.Completed).ToList();
        var firstsOk = haiku.All(r => r.FirstOk == true);
        return firstsOk && completed.Count > 0 && completed.All(r => r.Parsed == true) ? "structured_output" : "inconclusive";
    }
}
