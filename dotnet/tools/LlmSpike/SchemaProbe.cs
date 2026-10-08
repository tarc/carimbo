// Phase 2 schema probe (plan 02-03). Reached through `LlmSpike --schema-probe`; never part of `dotnet test`.
//
//   (a) grammar acceptance: one text-only request carrying the committed v2 model-facing schema
//   (b) a two-turn conversation over case-001.pdf through AnthropicLlmGateway, with the document block cached
//
// Secrets: the key comes from the environment, is passed explicitly and is never printed. The record holds
// shapes, counts, status codes and parse outcomes only: no key, header, request body, PDF bytes or model text.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Carimbo.Domain;
using Carimbo.Extraction;
using Carimbo.Llm;

internal static class SchemaProbe
{
    internal const string Haiku = "claude-haiku-4-5";
    private const int GatewayMaxTokens = 16000;
    private const int TextProbeMaxTokens = 4096;

    private const string TextProbePrompt =
        "Return a JSON object describing one made-up invoice with exactly one line item and no installments.";

    private const string RepairFeedback =
        "Check the previous answer against the document once more and return the complete invoice again. "
        + "Keep every value exactly as printed.";

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
            Console.Error.WriteLine("usage: LlmSpike --schema-probe [--dry-run] --budget-usd DECIMAL --cases PATH --out PATH");
            return 64;
        }

        var pdfPath = Path.Combine(cases, "case-001.pdf");
        if (!File.Exists(pdfPath) && !dryRun)
        {
            Console.Error.WriteLine($"{pdfPath} not found");
            return 66;
        }

        var pdf = File.Exists(pdfPath) ? File.ReadAllBytes(pdfPath) : Encoding.ASCII.GetBytes("%PDF-1.4 dry-run stand-in");

        if (dryRun)
        {
            return await DryRunAsync(pdf);
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

        var run = new ProbeRun(key, budget, pdf, cancellation.Token);
        try
        {
            var text = await run.ExecuteAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath + ".tmp", text, new UTF8Encoding(false));
            File.Move(outPath + ".tmp", outPath, overwrite: true);
            Console.WriteLine($"PROBE DONE: spend {ProbeRun.Money(run.Spent)} of {ProbeRun.Money(budget)}; wrote {outPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"INTERRUPTED: spend so far {ProbeRun.Money(run.Spent)}; nothing was written");
            return 130;
        }
    }

    /// <summary>The three conversation requests, built the same way for the dry run and the live run.</summary>
    public static (LlmRequest First, LlmRequest Repair) GatewayRequests(byte[] pdf, string firstResponseText)
    {
        var contract = ExtractionContract.Default;
        var first = new LlmRequest(
            Haiku, GatewayMaxTokens, contract.Prompt, new LlmDocument("application/pdf", pdf), contract.OutputSchemaJson)
        {
            CacheDocument = true,
        };
        var repair = first with
        {
            FollowUps =
            [
                new LlmTurn(LlmTurnRole.Assistant, firstResponseText),
                new LlmTurn(LlmTurnRole.User, RepairFeedback),
            ],
        };
        return (first, repair);
    }

    public static MessageCreateParams TextProbeRequest() =>
        new()
        {
            Model = Haiku,
            MaxTokens = TextProbeMaxTokens,
            OutputConfig = Shapes.Output(ExtractionContract.Default.OutputSchemaJson, null),
            Messages = [new MessageParam { Role = Role.User, Content = TextProbePrompt }],
        };

    /// <summary>No key, no network: the roles and block types the three requests would carry on the wire.</summary>
    private static async Task<int> DryRunAsync(byte[] pdf)
    {
        var contract = ExtractionContract.Default;
        Console.WriteLine($"model-facing schema sha256 {contract.OutputSchemaSha256}, {Encoding.UTF8.GetByteCount(contract.OutputSchemaJson)} bytes");
        Console.WriteLine($"prompt {contract.PromptVersion}, {contract.Prompt.Length} chars");

        var textHandler = new RecordingHandler((HttpStatusCode.BadRequest, "{}"));
        using (var client = new AnthropicClient
        {
            ApiKey = "dry-run-key",
            BaseUrl = "http://anthropic.invalid",
            HttpClient = new HttpClient(textHandler),
            MaxRetries = 0,
        })
        {
            try
            {
                await client.Messages.Create(TextProbeRequest());
            }
            catch (AnthropicApiException)
            {
                // The canned answer is a 400 on purpose: only the recorded request matters here.
            }
        }

        Console.WriteLine($"(a) text probe: {Shape(textHandler.RequestBodies[0])}, max_tokens {TextProbeMaxTokens}");

        var (first, repair) = GatewayRequests(pdf, "<first response>");
        foreach (var (label, request) in new[] { ("(b1) first call", first), ("(b2) repair call", repair) })
        {
            var handler = new RecordingHandler((HttpStatusCode.BadRequest, "{}"));
            using var gateway = new AnthropicLlmGateway(
                new AnthropicLlmGatewayOptions("dry-run-key", new Uri("http://anthropic.invalid"), TimeSpan.FromSeconds(30), 0), handler);
            try
            {
                await gateway.CompleteAsync(request, CancellationToken.None);
            }
            catch (LlmGatewayException)
            {
                // Same canned 400.
            }

            Console.WriteLine($"{label}: {Shape(handler.RequestBodies[0])}, max_tokens {request.MaxTokens}, CacheDocument {request.CacheDocument}");
        }

        return 0;
    }

    /// <summary>Message roles and block types, with cache_control marked: for example user[document+cache,text].</summary>
    public static string Shape(string requestBody)
    {
        var body = JsonNode.Parse(requestBody)!;
        var messages = body["messages"]!.AsArray().Select(message =>
        {
            var role = message!["role"]!.GetValue<string>();
            var blocks = message["content"] switch
            {
                JsonArray array => array.Select(block =>
                    block!["type"]!.GetValue<string>() + (block["cache_control"] is null ? string.Empty : "+cache")),
                _ => ["text"],
            };
            return $"{role}[{string.Join(",", blocks)}]";
        });
        return $"roles/blocks {string.Join(" ", messages)}";
    }
}

internal sealed class ProbeRun(string key, decimal budget, byte[] pdf, CancellationToken ct)
{
    private readonly StringBuilder doc = new();
    private readonly List<(string Label, decimal Estimate, decimal? Actual, string Note)> calls = [];
    private readonly LlmPricingTable pricing = LlmPricingTable.LoadEmbedded();
    private readonly ExtractionContract contract = ExtractionContract.Default;

    private bool? schemaAccepted;
    private bool? firstValid;
    private bool? secondValid;
    private bool? nonStreamingAccepted;
    private bool? repairTurnAccepted;

    public decimal Spent { get; private set; }

    public static string Money(decimal value) => "US$" + value.ToString("F4", CultureInfo.InvariantCulture);

    public async Task<string> ExecuteAsync()
    {
        doc.AppendLine("# Phase 2 schema probe: v2 model-facing schema and a cached two-turn conversation");
        doc.AppendLine();
        doc.AppendLine("Recorded by `dotnet/tools/LlmSpike --schema-probe` against the live Anthropic API (plan 02-03).");
        doc.AppendLine("This record is sanitised: it holds shapes, counts, status codes and parse outcomes only. It contains no provider key,");
        doc.AppendLine("no request header, no request body, no PDF bytes and no model text (model text is reduced to a parse outcome and a character count).");
        doc.AppendLine();
        doc.AppendLine($"- Run date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        doc.AppendLine($"- Model: `{SchemaProbe.Haiku}`");
        doc.AppendLine($"- Contract: prompt `{contract.PromptVersion}`, model-facing schema sha256 `{contract.OutputSchemaSha256}` ({Encoding.UTF8.GetByteCount(contract.OutputSchemaJson)} bytes)");
        doc.AppendLine($"- Document: case-001.pdf, {pdf.Length} bytes, one page");
        doc.AppendLine($"- Probe budget: {Money(budget)}; the call is skipped when its worst case would pass the budget");
        doc.AppendLine("- Counts toward the US$5 Phase 2 cap (D-19); Phase 2 live spend before this probe: US$0.");
        doc.AppendLine();

        var result = new StringBuilder();
        await CheckAAsync(result);
        await CheckBAsync(result);

        doc.AppendLine("## Request shapes");
        doc.AppendLine();
        AppendShapes();
        doc.AppendLine("## Result");
        doc.AppendLine();
        doc.Append(result);
        WriteSpend();
        WriteAssumptions();
        return doc.ToString();
    }

    private void AppendShapes()
    {
        var (first, repair) = SchemaProbe.GatewayRequests(pdf, "<first response>");
        doc.AppendLine("What each request carries, derived from the same builders the run uses (`+cache` marks an ephemeral `cache_control` breakpoint):");
        doc.AppendLine();
        doc.AppendLine($"- (a) text probe: one user message with a text block; `output_config.format` = the committed model-facing schema; max_tokens 4096; no sampling, tool, thinking or system fields");
        doc.AppendLine($"- (b1) first call: {ShapeOf(first)}; max_tokens {first.MaxTokens}; CacheDocument {first.CacheDocument}");
        doc.AppendLine($"- (b2) repair call: {ShapeOf(repair)}; max_tokens {repair.MaxTokens}; CacheDocument {repair.CacheDocument}");
        doc.AppendLine();
    }

    private static string ShapeOf(LlmRequest request)
    {
        var parts = new List<string> { "user[document" + (request.CacheDocument ? "+cache" : string.Empty) + ",text]" };
        parts.AddRange(request.FollowUps.Select(t => $"{t.Role.ToString().ToLowerInvariant()}[text]"));
        return string.Join(" ", parts);
    }

    private decimal Estimate(long extraInputTokens, long maxTokens)
    {
        // Upper bound on tokens: schema and prompt at two characters per token, one page at 3000 tokens.
        var inputTokens = 3000 + (contract.OutputSchemaJson.Length / 2) + (contract.Prompt.Length / 2) + extraInputTokens;
        var rates = pricing.Price(SchemaProbe.Haiku, new LlmUsage(inputTokens, maxTokens, 0, 0, 0)).AmountUsd ?? 0m;

        // Cache writes cost 1.25x input, so a worst case counts the whole input at the 5-minute write rate.
        var writeRates = pricing.Price(SchemaProbe.Haiku, new LlmUsage(0, maxTokens, 0, inputTokens, 0)).AmountUsd ?? 0m;
        return Math.Max(rates, writeRates);
    }

    private bool Reserve(string label, decimal estimate)
    {
        if (Spent + estimate <= budget)
        {
            return true;
        }

        calls.Add((label, estimate, null, "skipped: budget"));
        Console.WriteLine($"  skipped {label}");
        return false;
    }

    private static LlmUsage UsageOf(Usage usage)
    {
        var snap = UsageSnap.Of(usage);
        var w5 = snap.W5m + snap.W1h == 0 ? snap.Creation : snap.W5m;
        return new LlmUsage(snap.Input, snap.Output, snap.Read, w5, snap.W1h);
    }

    private static string UsageLine(LlmUsage u) =>
        $"input={u.InputTokens} output={u.OutputTokens} cache_read={u.CacheReadTokens} cache_write_5m={u.CacheWrite5mTokens} cache_write_1h={u.CacheWrite1hTokens}";

    private static (bool Valid, string Line) ParseOutcome(string text)
    {
        try
        {
            var invoice = JsonSerializer.Deserialize<Invoice>(text, Wire.Options);
            if (invoice is null)
            {
                return (false, $"parsed: false (null document), model text {text.Length} chars (not recorded)");
            }

            var violations = invoice.PatternViolations();
            var line = violations.Count == 0
                ? "parsed: true, pattern violations: 0"
                : $"parsed: true, pattern violations: {violations.Count} at [{string.Join(", ", violations)}]";
            return (violations.Count == 0, $"{line}, items {invoice.Items.Count}, model text {text.Length} chars (not recorded)");
        }
        catch (JsonException ex)
        {
            return (false, $"parsed: false (JsonException at `{ex.Path}`), model text {text.Length} chars (not recorded)");
        }
    }

    private static string ErrorType(string? body)
    {
        try
        {
            return JsonNode.Parse(body ?? string.Empty)?["error"]?["type"]?.GetValue<string>() ?? "unknown";
        }
        catch (JsonException)
        {
            return "unparseable";
        }
    }

    // The provider message is recorded only when it speaks about the schema, and never longer than 300 characters.
    private static string SchemaMessage(string? body)
    {
        try
        {
            var message = JsonNode.Parse(body ?? string.Empty)?["error"]?["message"]?.GetValue<string>();
            if (message is not null && message.Contains("schema", StringComparison.OrdinalIgnoreCase))
            {
                return $"; provider message: \"{(message.Length <= 300 ? message : message[..300] + "...")}\"";
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private AnthropicClient NewClient(HttpMessageHandler handler) =>
        new()
        {
            ApiKey = key,
            BaseUrl = "https://api.anthropic.com",
            HttpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            MaxRetries = 0,
            Timeout = TimeSpan.FromSeconds(300),
        };

    // ---- (a) grammar acceptance ----------------------------------------------------------------------

    private async Task CheckAAsync(StringBuilder result)
    {
        Console.WriteLine("check (a)");
        result.AppendLine("### (a) Grammar acceptance of the v2 model-facing schema (text only)");
        result.AppendLine();
        var estimate = Estimate(0, 4096);
        if (!Reserve("(a) text probe", estimate))
        {
            result.AppendLine($"- skipped: worst case {Money(estimate)} would pass the budget");
            result.AppendLine();
            return;
        }

        var handler = new CapturingHandler(new HttpClientHandler());
        using var client = NewClient(handler);
        var watch = Stopwatch.StartNew();
        try
        {
            var message = await client.Messages.Create(SchemaProbe.TextProbeRequest(), ct);
            watch.Stop();
            var usage = UsageOf(message.Usage);
            var cost = pricing.Price(SchemaProbe.Haiku, usage).AmountUsd ?? 0m;
            Spent += cost;
            calls.Add(("(a) text probe", estimate, cost, string.Empty));

            var text = Shapes.TextOf(message);
            var (valid, parse) = ParseOutcome(text);
            schemaAccepted = true;
            result.AppendLine("- v2 schema accepted: **yes** (HTTP 200)");
            result.AppendLine($"- stop_reason `{message.StopReason?.Raw()}`, returned model `{message.Model.Raw()}`");
            result.AppendLine($"- usage: {UsageLine(usage)}");
            result.AppendLine($"- latency {watch.Elapsed.TotalSeconds:F1}s, HTTP attempts {handler.Attempts}, cost {Money(cost)}");
            result.AppendLine($"- {parse}; schema-valid invoice: {Yes(valid)}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AnthropicApiException api)
        {
            watch.Stop();
            calls.Add(("(a) text probe", estimate, 0m, "rejected"));
            schemaAccepted = false;
            result.AppendLine("- v2 schema accepted: **no**");
            result.AppendLine($"- HTTP {(int)api.StatusCode}, error type `{ErrorType(api.ResponseBody)}`{SchemaMessage(api.ResponseBody)}");
            result.AppendLine($"- latency {watch.Elapsed.TotalSeconds:F1}s, HTTP attempts {handler.Attempts}");
        }
        catch (Exception ex)
        {
            watch.Stop();
            calls.Add(("(a) text probe", estimate, 0m, "failed"));
            result.AppendLine($"- not determined: {ex.GetType().Name} after {watch.Elapsed.TotalSeconds:F1}s, HTTP attempts {handler.Attempts}");
        }

        result.AppendLine();
    }

    // ---- (b) two-turn cached conversation -------------------------------------------------------------

    private async Task CheckBAsync(StringBuilder result)
    {
        Console.WriteLine("check (b)");
        result.AppendLine("### (b) Two-turn conversation over case-001.pdf with a cached document block");
        result.AppendLine();

        using var gateway = new AnthropicLlmGateway(
            new AnthropicLlmGatewayOptions(key, null, TimeSpan.FromSeconds(300), 0));
        var (first, _) = SchemaProbe.GatewayRequests(pdf, string.Empty);

        var firstEstimate = Estimate(0, first.MaxTokens);
        if (!Reserve("(b1) first call", firstEstimate))
        {
            result.AppendLine($"- first call skipped: worst case {Money(firstEstimate)} would pass the budget");
            result.AppendLine();
            return;
        }

        var firstResponse = await CallAsync(gateway, "(b1) first call", first, firstEstimate, result);
        nonStreamingAccepted = firstResponse is not null;
        if (firstResponse is null)
        {
            result.AppendLine("- repair call not attempted: the first call failed");
            result.AppendLine();
            return;
        }

        firstValid = ParseOutcome(firstResponse.Text).Valid;

        // The previous answer travels back as the assistant turn, so the follow-up input includes its tokens.
        var (_, repair) = SchemaProbe.GatewayRequests(pdf, firstResponse.Text);
        var repairEstimate = Estimate(firstResponse.Usage.OutputTokens + 200, repair.MaxTokens);
        if (!Reserve("(b2) repair call", repairEstimate))
        {
            result.AppendLine($"- repair call skipped: worst case {Money(repairEstimate)} would pass the budget");
            result.AppendLine();
            return;
        }

        var secondResponse = await CallAsync(gateway, "(b2) repair call", repair, repairEstimate, result);
        repairTurnAccepted = secondResponse is not null;
        if (secondResponse is not null)
        {
            secondValid = ParseOutcome(secondResponse.Text).Valid;
            result.AppendLine($"- second text equals the first: {Yes(string.Equals(firstResponse.Text, secondResponse.Text, StringComparison.Ordinal))}");
        }

        result.AppendLine();
    }

    private async Task<LlmResponse?> CallAsync(
        AnthropicLlmGateway gateway, string label, LlmRequest request, decimal estimate, StringBuilder result)
    {
        try
        {
            var response = await gateway.CompleteAsync(request, ct);
            var cost = pricing.Price(SchemaProbe.Haiku, response.Usage);
            Spent += cost.AmountUsd ?? 0m;
            calls.Add((label, estimate, cost.AmountUsd, cost.Warning ?? string.Empty));
            var (valid, parse) = ParseOutcome(response.Text);
            result.AppendLine($"- {label}: accepted (HTTP 200), stop_reason `{response.StopReason}`, returned model `{response.ModelReturned}`");
            result.AppendLine($"  - usage: {UsageLine(response.Usage)}");
            result.AppendLine($"  - latency {response.Latency.TotalSeconds:F1}s, HTTP attempts {response.HttpAttempts}, cost {Money(cost.AmountUsd ?? 0m)}");
            result.AppendLine($"  - {parse}; schema-valid invoice: {Yes(valid)}");
            return response;
        }
        catch (LlmGatewayException ex)
        {
            calls.Add((label, estimate, 0m, "failed"));
            result.AppendLine($"- {label}: failed, kind `{ex.Kind}`, HTTP {ex.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "none"}, request id present: {Yes(ex.RequestId is not null)}");
            return null;
        }
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string Describe(bool? value) => value is null ? "not determined" : value.Value ? "yes" : "no";

    private void WriteSpend()
    {
        doc.AppendLine("## Spend");
        doc.AppendLine();
        doc.AppendLine("Actual spend is computed from the returned usage with the embedded pricing table "
            + $"(version `{pricing.Version}`). The estimate is the pre-call guard: every input token counted at the 5-minute cache-write rate, "
            + "tokens estimated at two characters each, and max_tokens counted in full at the output rate.");
        doc.AppendLine();
        doc.AppendLine("| Call | Worst-case estimate | Actual | Note |");
        doc.AppendLine("|------|---------------------|--------|------|");
        foreach (var (label, estimate, actual, note) in calls)
        {
            doc.AppendLine($"| {label} | {Money(estimate)} | {(actual is { } a ? Money(a) : "n/a")} | {(note.Length == 0 ? "-" : note)} |");
        }

        doc.AppendLine();
        doc.AppendLine($"- Total: {Money(Spent)}");
        doc.AppendLine($"- Budget: {Money(budget)}; within budget: {Spent <= budget}");
        doc.AppendLine($"- Phase 2 cumulative live spend after this probe: {Money(Spent)} of US$5.0000 (D-19).");
        doc.AppendLine();
    }

    private void WriteAssumptions()
    {
        doc.AppendLine("## Assumptions settled");
        doc.AppendLine();
        doc.AppendLine($"- A1 (enum without `type`, and the `tax_id` alternation pattern, accepted by the structured-output grammar): {Describe(schemaAccepted)}. Check (a) sends the committed schema, which carries both.");
        doc.AppendLine($"- A2 (14-field item array with about nine patterned strings per row stays under the grammar-complexity limit): {Describe(schemaAccepted)}. A 400 here would name the schema; see check (a).");
        doc.AppendLine($"- A4 (no SDK pre-flight guard against a non-streaming request with max_tokens 16000): {Describe(nonStreamingAccepted)}. The first gateway call was sent once, non-streaming.");
        doc.AppendLine($"- A5 (`Role.Assistant` exists and a text-block list is accepted as an assistant turn): {Describe(repairTurnAccepted)}. It compiled, and the repair call carried an assistant turn.");
        doc.AppendLine($"- Both gateway turns returned a schema-valid invoice: first {Describe(firstValid)}, second {Describe(secondValid)}.");
        doc.AppendLine("- Cache: a one-page Haiku 4.5 request is below the 4096-token prefix needed to write a cache entry, so zero cache read tokens is the expected result, not a failure (RESEARCH Pitfall 8).");
    }
}
