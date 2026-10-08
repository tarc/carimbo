// LLM-06 spike tool (plan 01-10). It is NOT part of the solution or of `dotnet test`.
//
//   --offline-selftest   no network, no key: reproduces the research evidence about both SDK paths
//   --live               paid calls under a budget: records sanitised evidence for docs/spikes/01-llm-gateway.md
//
// Secrets: the provider key is read from the environment, passed explicitly to the client and never
// printed. Request bodies, headers and PDF bytes are never written anywhere; only shapes, counts and
// parse outcomes are recorded, and model text is replaced by a character count.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Carimbo.Domain;
using Carimbo.Extraction;
using Microsoft.Extensions.AI;

return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--offline-selftest"))
        {
            return await SelfTest.RunAsync();
        }

        if (args.Contains("--live"))
        {
            return await Live.RunAsync(args);
        }

        Console.Error.WriteLine("usage: LlmSpike --offline-selftest");
        Console.Error.WriteLine("       LlmSpike --live --budget-usd DECIMAL --cases PATH --out PATH --fixtures PATH");
        return 64;
    }
}

/// <summary>Request builders shared by the self-test and the live run, so both exercise identical shapes.</summary>
internal static class Shapes
{
    public static Dictionary<string, JsonElement> SchemaDictionary(string schemaJson) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schemaJson)!;

    public static OutputConfig Output(string schemaJson, Effort? effort)
    {
        var format = new JsonOutputFormat { Schema = SchemaDictionary(schemaJson) };
        return effort is { } e
            ? new OutputConfig { Format = format, Effort = e }
            : new OutputConfig { Format = format };
    }

    /// <summary>The direct-SDK request: document block (optionally cached) plus the instruction text.</summary>
    public static MessageCreateParams Direct(
        string model,
        long maxTokens,
        byte[] pdf,
        string prompt,
        string schemaJson,
        bool cache,
        Effort? effort = null,
        string? documentTitle = null) =>
        new()
        {
            Model = model,
            MaxTokens = maxTokens,
            OutputConfig = Output(schemaJson, effort),
            Messages =
            [
                new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        Document(pdf, cache, documentTitle),
                        new TextBlockParam { Text = prompt },
                    },
                },
            ],
        };

    // Optional members are set only when used: assigning null would put an explicit null member on the wire.
    private static DocumentBlockParam Document(byte[] pdf, bool cache, string? title)
    {
        var source = new Base64PdfSource { Data = Convert.ToBase64String(pdf) };
        return (cache, title) switch
        {
            (true, not null) => new DocumentBlockParam { Source = source, CacheControl = new CacheControlEphemeral(), Title = title },
            (true, null) => new DocumentBlockParam { Source = source, CacheControl = new CacheControlEphemeral() },
            (false, not null) => new DocumentBlockParam { Source = source, Title = title },
            _ => new DocumentBlockParam { Source = source },
        };
    }

    /// <summary>The same request through IChatClient with the raw factory carrying the committed schema.</summary>
    public static (IChatClient Client, ChatOptions Options, ChatMessage Message) ChatRaw(
        IAnthropicClient client,
        string model,
        long maxTokens,
        byte[] pdf,
        string prompt,
        string schemaJson,
        bool cache,
        Effort? effort = null)
    {
        var chat = client.AsIChatClient(model);
        var options = new ChatOptions
        {
            MaxOutputTokens = (int)maxTokens,
            RawRepresentationFactory = _ => new MessageCreateParams
            {
                Model = model,
                MaxTokens = maxTokens,
                Messages = [],
                OutputConfig = Output(schemaJson, effort),
            },
        };
        var document = new DataContent(pdf, "application/pdf");
        if (cache)
        {
            document = document.WithCacheControl(new CacheControlEphemeral());
        }

        return (chat, options, new ChatMessage(ChatRole.User, [document, new TextContent(prompt)]));
    }

    public static string TextOf(Message message)
    {
        var text = new StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var t))
            {
                text.Append(t.Text);
            }
        }

        return text.ToString();
    }

    public static int BlockCount(Message message, bool thinking)
    {
        var count = 0;
        foreach (var block in message.Content)
        {
            if (thinking ? block.TryPickThinking(out _) : block.TryPickText(out _))
            {
                count++;
            }
        }

        return count;
    }

    public static long? SafeLong(Func<long?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Names(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.OrderBy(v => v, StringComparer.Ordinal)) + "]";

    public static Dictionary<string, string> Flatten(JsonNode? node, string path = "")
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject obj)
        {
            foreach (var (name, child) in obj)
            {
                foreach (var (k, v) in Flatten(child, path + "/" + name))
                {
                    map[k] = v;
                }
            }
        }
        else
        {
            map[path] = node?.ToJsonString() ?? "null";
        }

        return map;
    }

    public static string Last(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Like <see cref="Flatten"/> but also descends into arrays, indexing each element.</summary>
    public static Dictionary<string, string> FlattenDeep(JsonNode? node, string path = "")
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    foreach (var (k, v) in FlattenDeep(child, path + "/" + name))
                    {
                        map[k] = v;
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var (k, v) in FlattenDeep(array[i], path + "/" + i))
                    {
                        map[k] = v;
                    }
                }

                break;
            default:
                map[path] = node?.ToJsonString() ?? "null";
                break;
        }

        return map;
    }
}

/// <summary>Canned HTTP transport for the offline self-test. Its bodies are synthetic, never recorded API output.</summary>
internal sealed class RecordingHandler(params (HttpStatusCode Status, string Body)[] script) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> queue = new(script);
    private readonly (HttpStatusCode Status, string Body) last = script[^1];

    public List<string> RequestBodies { get; } = [];

    public int Attempts => RequestBodies.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        var (status, body) = queue.Count > 0 ? queue.Dequeue() : last;
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        response.Headers.Add("request-id", "req_synthetic");
        return response;
    }
}

internal sealed class SelfTestFailure(string message) : Exception(message);

internal sealed record StopFacts(
    string StopReason,
    string? Category,
    bool HasExplanation,
    string? FinishReason,
    bool ChatDetailsInProperties,
    bool ChatRawHasDetails);

internal static class SelfTest
{
    // All bodies below are SYNTHETIC canned Messages-API payloads written for this self-test.
    private const string EndTurnBody = """
        {"id":"msg_synthetic_end_turn","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[{"type":"text","text":"{\"synthetic\":true}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":7,"cache_creation_input_tokens":5000,"cache_read_input_tokens":0,"cache_creation":{"ephemeral_5m_input_tokens":3000,"ephemeral_1h_input_tokens":2000}}}
        """;

    private const string MaxTokensBody = """
        {"id":"msg_synthetic_max_tokens","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[{"type":"text","text":"{\"access_"}],"stop_reason":"max_tokens","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":4,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
        """;

    private const string RefusalBody = """
        {"id":"msg_synthetic_refusal","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[],"stop_reason":"refusal","stop_sequence":null,"stop_details":{"type":"refusal","category":"cyber","explanation":"synthetic refusal explanation"},"usage":{"input_tokens":12,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
        """;

    private const string OverloadedBody = """
        {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}
        """;

    private const string Model = "claude-haiku-4-5";
    private const string Prompt = "Extract the invoice.";

    private static readonly byte[] SyntheticPdf = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic offline self-test bytes");

    public static async Task<int> RunAsync()
    {
        try
        {
            await ChecksAsync();
            Console.WriteLine("SELFTEST PASS");
            return 0;
        }
        catch (SelfTestFailure ex)
        {
            Console.WriteLine($"SELFTEST FAIL: {ex.Message}");
            return 1;
        }
    }

    /// <summary>The refusal mapping through both paths, reused by the live record (canned, no network).</summary>
    public static Task<StopFacts> StopFactsAsync(string body) => StopFactsCoreAsync(body);

    public static string RefusalBodyForLive => RefusalBody;

    /// <summary>How the adapter maps the canned cache-creation body: is the count summed, is the 5m/1h split exposed.</summary>
    public static async Task<(bool InputIsSum, bool SplitReachable)> AdapterUsageFactsAsync()
    {
        var contract = ExtractionContract.Default;
        var handler = new RecordingHandler((HttpStatusCode.OK, EndTurnBody));
        var (chat, options, message) = Shapes.ChatRaw(
            Client(handler, 0), Model, 256, SyntheticPdf, contract.Prompt, contract.OutputSchemaJson, cache: false);
        var response = await chat.GetResponseAsync([message], options);
        var usage = response.Usage ?? throw new SelfTestFailure("adapter returned no usage");
        var values = usage.AdditionalCounts?.Values.ToHashSet() ?? [];
        return (usage.InputTokenCount == 12 + 5000, values.Contains(3000) && values.Contains(2000));
    }

    private static void Check(bool condition, string assertion)
    {
        if (!condition)
        {
            throw new SelfTestFailure(assertion);
        }
    }

    private static AnthropicClient Client(HttpMessageHandler handler, int maxRetries) =>
        new()
        {
            ApiKey = "offline-selftest",
            BaseUrl = "http://offline.invalid",
            HttpClient = new HttpClient(handler),
            MaxRetries = maxRetries,
        };

    private static JsonNode SentSchema(RecordingHandler handler) =>
        JsonNode.Parse(handler.RequestBodies[^1])?["output_config"]?["format"]?["schema"]
        ?? throw new SelfTestFailure("no output_config.format.schema in the captured request");

    private static JsonNode? DocumentBlock(RecordingHandler handler)
    {
        var content = JsonNode.Parse(handler.RequestBodies[^1])?["messages"]?[0]?["content"] as JsonArray;
        return content?.FirstOrDefault(b => b?["type"]?.GetValue<string>() == "document");
    }

    private static async Task ChecksAsync()
    {
        var contract = ExtractionContract.Default;
        var contractSchema = JsonNode.Parse(contract.OutputSchemaJson)!;
        Console.WriteLine($"selftest: contract {contract.PromptVersion} schema sha256 {contract.OutputSchemaSha256[..12]}");

        // (a) direct SDK path: the committed schema is sent verbatim.
        var directHandler = new RecordingHandler((HttpStatusCode.OK, EndTurnBody));
        var direct = Client(directHandler, 0);
        var directMessage = await direct.Messages.Create(
            Shapes.Direct(Model, 256, SyntheticPdf, contract.Prompt, contract.OutputSchemaJson, cache: true));
        var directVerbatim = JsonNode.DeepEquals(SentSchema(directHandler), contractSchema);
        var directDocumentCached = DocumentBlock(directHandler)?["cache_control"]?["type"]?.GetValue<string>() == "ephemeral";
        Check(directVerbatim, "(a) direct path did not send the contract schema verbatim");
        Check(directDocumentCached, "(a) direct path lost cache_control on the document block");
        Console.WriteLine(
            $"(a) direct SDK path: schema verbatim={directVerbatim}; cache_control on document={directDocumentCached}; attempts={directHandler.Attempts}");

        // (b) IChatClient with the raw factory and WithCacheControl.
        var rawHandler = new RecordingHandler((HttpStatusCode.OK, EndTurnBody));
        var (rawChat, rawOptions, rawMessage) = Shapes.ChatRaw(
            Client(rawHandler, 0), Model, 256, SyntheticPdf, contract.Prompt, contract.OutputSchemaJson, cache: true);
        var rawResponse = await rawChat.GetResponseAsync([rawMessage], rawOptions);
        var rawVerbatim = JsonNode.DeepEquals(SentSchema(rawHandler), contractSchema);
        var rawDocumentCached = DocumentBlock(rawHandler)?["cache_control"]?["type"]?.GetValue<string>() == "ephemeral";
        Check(rawVerbatim, "(b) IChatClient raw-factory path did not send the contract schema verbatim");
        Check(rawDocumentCached, "(b) IChatClient raw-factory path lost cache_control on the document block");
        Console.WriteLine(
            $"(b) IChatClient raw-factory path: schema verbatim={rawVerbatim}; cache_control on document={rawDocumentCached}; attempts={rawHandler.Attempts}");

        // (c) default ChatResponseFormat.ForJsonSchema path rewrites the schema.
        var defaultHandler = new RecordingHandler((HttpStatusCode.OK, EndTurnBody));
        var defaultChat = Client(defaultHandler, 0).AsIChatClient(Model);
        var schemaElement = JsonDocument.Parse(contract.OutputSchemaJson).RootElement.Clone();
        var defaultOptions = new ChatOptions
        {
            MaxOutputTokens = 256,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaElement, "invoice"),
        };
        await defaultChat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, [new DataContent(SyntheticPdf, "application/pdf"), new TextContent(contract.Prompt)])],
            defaultOptions);
        var sentDefault = SentSchema(defaultHandler);
        var before = Shapes.Flatten(contractSchema);
        var after = Shapes.Flatten(sentDefault);
        var removed = before.Keys.Except(after.Keys).Select(Shapes.Last).Distinct().ToList();
        var added = after.Keys.Except(before.Keys).Select(Shapes.Last).Distinct().ToList();
        var changed = before.Keys.Intersect(after.Keys).Where(k => before[k] != after[k]).Select(Shapes.Last).Distinct().ToList();
        var defaultChangedSchema = !JsonNode.DeepEquals(sentDefault, contractSchema);
        Check(defaultChangedSchema, "(c) default JSON-schema path unexpectedly sent the schema unchanged");
        Console.WriteLine(
            $"(c) default ChatResponseFormat.ForJsonSchema path changed the schema: removed={Shapes.Names(removed)}; added={Shapes.Names(added)}; value changed={Shapes.Names(changed)}");

        // (d) usage decomposition: input 12, cache_creation 5000 split 3000 (5m) and 2000 (1h), cache_read 0.
        var usage = directMessage.Usage;
        var split5m = usage.CacheCreation?.Ephemeral5mInputTokens;
        var split1h = usage.CacheCreation?.Ephemeral1hInputTokens;
        Check(usage.InputTokens == 12 && usage.CacheCreationInputTokens == 5000 && usage.CacheReadInputTokens == 0,
            "(d) direct usage fields do not match the canned body");
        Check(split5m == 3000 && split1h == 2000, "(d) direct usage lost the 5m/1h cache-creation split");
        var chatUsage = rawResponse.Usage ?? throw new SelfTestFailure("(d) adapter returned no usage");
        var counts = chatUsage.AdditionalCounts is null
            ? "none"
            : string.Join(
                ";",
                chatUsage.AdditionalCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        Check(chatUsage.InputTokenCount == 5012, "(d) adapter input count is not uncached plus cache creation");
        Console.WriteLine(
            $"(d) usage: direct input={usage.InputTokens} creation={usage.CacheCreationInputTokens} (5m={split5m}, 1h={split1h}) read={usage.CacheReadInputTokens} output={usage.OutputTokens}; "
            + $"IChatClient InputTokenCount={chatUsage.InputTokenCount} CachedInputTokenCount={chatUsage.CachedInputTokenCount} OutputTokenCount={chatUsage.OutputTokenCount} AdditionalCounts=[{counts}]");

        // (e) stop reasons and stop details through each path.
        var parts = new List<string>();
        foreach (var (label, body, expectedStop, expectedFinish) in new[]
                 {
                     ("end_turn", EndTurnBody, "end_turn", "stop"),
                     ("max_tokens", MaxTokensBody, "max_tokens", "length"),
                     ("refusal", RefusalBody, "refusal", "content_filter"),
                 })
        {
            var facts = await StopFactsCoreAsync(body);
            Check(facts.StopReason == expectedStop, $"(e) direct stop_reason for {label} was {facts.StopReason}");
            Check(facts.FinishReason == expectedFinish, $"(e) adapter FinishReason for {label} was {facts.FinishReason}");
            var details = facts.Category is null ? "none" : $"{facts.Category}/explanation={facts.HasExplanation}";
            parts.Add(
                $"{label}: direct stop_reason={facts.StopReason} stop_details={details}; adapter FinishReason={facts.FinishReason} "
                + $"stop_details in response properties={facts.ChatDetailsInProperties}, in raw representation={facts.ChatRawHasDetails}");
        }

        Console.WriteLine("(e) stop reasons: " + string.Join(" | ", parts));

        // (f) retry visibility: 529, 529, 200.
        var retryHandler = new RecordingHandler(
            ((HttpStatusCode)529, OverloadedBody), ((HttpStatusCode)529, OverloadedBody), (HttpStatusCode.OK, EndTurnBody));
        await Client(retryHandler, 2).Messages.Create(MinimalRequest());
        var retriedAttempts = retryHandler.Attempts;
        Check(retriedAttempts == 3, $"(f) MaxRetries 2 made {retriedAttempts} attempts instead of 3");

        var noRetryHandler = new RecordingHandler(
            ((HttpStatusCode)529, OverloadedBody), ((HttpStatusCode)529, OverloadedBody), (HttpStatusCode.OK, EndTurnBody));
        string thrown;
        try
        {
            await Client(noRetryHandler, 0).Messages.Create(MinimalRequest());
            thrown = "no exception";
        }
        catch (AnthropicApiException ex)
        {
            thrown = ex.GetType().Name;
        }

        Check(noRetryHandler.Attempts == 1, $"(f) MaxRetries 0 made {noRetryHandler.Attempts} attempts instead of 1");
        Check(thrown != "no exception", "(f) MaxRetries 0 did not throw on a 529");
        Console.WriteLine(
            $"(f) retries for 529,529,200: MaxRetries=2 attempts={retriedAttempts} then success; MaxRetries=0 attempts={noRetryHandler.Attempts} then {thrown}");
    }

    private static MessageCreateParams MinimalRequest() =>
        new()
        {
            Model = Model,
            MaxTokens = 16,
            Messages = [new MessageParam { Role = Role.User, Content = Prompt }],
        };

    private static async Task<StopFacts> StopFactsCoreAsync(string body)
    {
        var contract = ExtractionContract.Default;

        var directHandler = new RecordingHandler((HttpStatusCode.OK, body));
        var message = await Client(directHandler, 0).Messages.Create(
            Shapes.Direct(Model, 256, SyntheticPdf, contract.Prompt, contract.OutputSchemaJson, cache: false));
        var stopReason = message.StopReason?.Raw() ?? "(none)";
        var details = message.StopDetails;
        var category = details?.Category?.Raw();
        var hasExplanation = !string.IsNullOrEmpty(details?.Explanation);

        var chatHandler = new RecordingHandler((HttpStatusCode.OK, body));
        var (chat, options, chatMessage) = Shapes.ChatRaw(
            Client(chatHandler, 0), Model, 256, SyntheticPdf, contract.Prompt, contract.OutputSchemaJson, cache: false);
        var response = await chat.GetResponseAsync([chatMessage], options);
        var propertyKeys = response.AdditionalProperties?.Keys ?? [];
        var detailsInProperties = propertyKeys.Any(k => k.Contains("stop_detail", StringComparison.OrdinalIgnoreCase)
            || k.Contains("StopDetail", StringComparison.OrdinalIgnoreCase)
            || k.Contains("refusal", StringComparison.OrdinalIgnoreCase));
        var rawHasDetails = response.RawRepresentation is Message raw && raw.StopDetails is not null;
        return new StopFacts(stopReason, category, hasExplanation, response.FinishReason?.Value, detailsInProperties, rawHasDetails);
    }
}

/// <summary>Wraps the real handler: counts attempts and keeps the last response body in memory only.</summary>
internal sealed class CapturingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public List<int> Statuses { get; } = [];

    public string? LastResponseBody { get; private set; }

    public bool LastRequestIdHeader { get; private set; }

    public string? LastRequestHash { get; private set; }

    /// <summary>Held in memory only, to diff request shapes. It is never written anywhere.</summary>
    public string? LastRequestJson { get; private set; }

    public int Attempts => Statuses.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            LastRequestHash = Convert.ToHexString(SHA256.HashData(bytes));
            LastRequestJson = Encoding.UTF8.GetString(bytes);
        }

        var response = await base.SendAsync(request, cancellationToken);
        Statuses.Add((int)response.StatusCode);
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        LastResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        LastRequestIdHeader = response.Headers.Contains("request-id");
        return response;
    }
}

internal sealed record UsageSnap(long Input, long Output, long Creation, long Read, long W5m, long W1h, long? Thinking)
{
    public long TotalInput => Input + Creation + Read;

    public override string ToString() =>
        $"input={Input} output={Output} cache_creation={Creation} (5m={W5m}, 1h={W1h}) cache_read={Read} thinking_tokens={(Thinking?.ToString(CultureInfo.InvariantCulture) ?? "n/a")}";

    public static UsageSnap Of(Usage usage)
    {
        var creation = usage.CacheCreationInputTokens ?? 0;
        return new UsageSnap(
            usage.InputTokens,
            usage.OutputTokens,
            creation,
            usage.CacheReadInputTokens ?? 0,
            Shapes.SafeLong(() => usage.CacheCreation?.Ephemeral5mInputTokens) ?? 0,
            Shapes.SafeLong(() => usage.CacheCreation?.Ephemeral1hInputTokens) ?? 0,
            Shapes.SafeLong(() => usage.OutputTokensDetails?.ThinkingTokens));
    }
}

internal sealed record CallResult(
    Message? Message,
    Exception? Error,
    TimeSpan Latency,
    int Attempts,
    IReadOnlyList<int> Statuses,
    string? RawBody,
    bool RequestIdHeader,
    string? RequestHash);

internal sealed record GroundTruth(
    string AccessKey,
    string Number,
    string Series,
    string IssueDate,
    string IssuerCnpj,
    string IssuerName,
    string RecipientTaxId,
    string RecipientName,
    string Total)
{
    public static GroundTruth Load(string xmlPath)
    {
        XNamespace ns = "http://www.portalfiscal.inf.br/nfe";
        var doc = XDocument.Load(xmlPath);
        var inf = doc.Descendants(ns + "infNFe").First();
        string Field(XElement parent, string name) => parent.Element(ns + name)?.Value ?? string.Empty;
        var ide = inf.Element(ns + "ide")!;
        var emit = inf.Element(ns + "emit")!;
        var dest = inf.Element(ns + "dest")!;
        var total = inf.Descendants(ns + "ICMSTot").First();
        return new GroundTruth(
            inf.Attribute("Id")!.Value[3..],
            Field(ide, "nNF"),
            Field(ide, "serie"),
            Field(ide, "dhEmi")[..10],
            Field(emit, "CNPJ"),
            Field(emit, "xNome"),
            Field(dest, "CNPJ") is { Length: > 0 } recipientCnpj ? recipientCnpj : Field(dest, "CPF"),
            Field(dest, "xNome"),
            Field(total, "vNF"));
    }

    /// <summary>Parses the model text with the strict wire options and counts equal fields out of nine.</summary>
    public (bool Parsed, int Matched) Compare(string text)
    {
        Invoice? invoice;
        try
        {
            invoice = JsonSerializer.Deserialize<Invoice>(text, Wire.Options);
        }
        catch (JsonException)
        {
            return (false, 0);
        }

        if (invoice is null)
        {
            return (false, 0);
        }

        var c = CultureInfo.InvariantCulture;
        var checks = new[]
        {
            invoice.AccessKey == AccessKey,
            invoice.Number.ToString(c) == Number,
            invoice.Series.ToString(c) == Series,
            invoice.IssueDate.ToString("yyyy-MM-dd", c) == IssueDate,
            invoice.Issuer.Cnpj == IssuerCnpj,
            invoice.Issuer.Name == IssuerName,
            invoice.Recipient.TaxId == RecipientTaxId,
            invoice.Recipient.Name == RecipientName,
            invoice.Totals.InvoiceTotal.ToString() == Total,
        };
        return (true, checks.Count(x => x));
    }
}

internal static class Live
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var budgetText = Arg("--budget-usd");
        var cases = Arg("--cases");
        var outPath = Arg("--out");
        var fixtures = Arg("--fixtures");
        if (budgetText is null || cases is null || outPath is null || fixtures is null
            || !decimal.TryParse(budgetText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var budget))
        {
            Console.Error.WriteLine("usage: LlmSpike --live --budget-usd DECIMAL --cases PATH --out PATH --fixtures PATH");
            return 64;
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

        var run = new LiveRun(key, budget, cases, outPath, fixtures, cancellation.Token);
        try
        {
            return await run.ExecuteAsync();
        }
        catch (OperationCanceledException)
        {
            run.CleanTemporaryFiles();
            Console.WriteLine(
                $"INTERRUPTED: spend so far US${run.Spent.ToString("F4", CultureInfo.InvariantCulture)}; nothing was written, previously committed files are unchanged");
            return 130;
        }
    }
}

internal sealed class LiveRun(string key, decimal budget, string casesDir, string outPath, string fixturesDir, CancellationToken ct)
{
    // Spike-only constants. The production pricing table arrives in plan 01-11.
    private const decimal WorstCaseInputPerMTok = 4.00m;
    private const decimal WorstCaseOutputPerMTok = 10.00m;
    private const string Haiku = "claude-haiku-4-5";
    private const string Sonnet = "claude-sonnet-5-5";

    private readonly StringBuilder doc = new();
    private readonly Dictionary<string, string> fixtureBodies = new(StringComparer.Ordinal);
    private readonly List<(string Label, string Model, decimal Estimate, decimal Actual)> calls = [];
    private readonly ExtractionContract contract = ExtractionContract.Default;
    private readonly Dictionary<string, int> pages = new(StringComparer.Ordinal);

    // Facts collected for the recommendation.
    private string? aliasSnapshot;
    private string? schemaVerdict;
    private bool? schemaAcceptedAsIs;
    private long? tokensPerPage;
    private bool? haikuCachesMultiPage;
    private bool? sonnetCaches;
    private bool? usageLossless;
    private bool? sonnetAcceptsFormatAndEffort;
    private bool? fourXxNotRetried;
    private StopFacts? refusalFacts;

    public decimal Spent { get; private set; }

    public void CleanTemporaryFiles()
    {
        foreach (var path in new[] { outPath }.Concat(fixtureBodies.Keys.Select(n => Path.Combine(fixturesDir, n))))
        {
            var tmp = path + ".tmp";
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    public async Task<int> ExecuteAsync()
    {
        foreach (var c in JsonNode.Parse(File.ReadAllText(Path.Combine(casesDir, "manifest.json")))!["cases"]!.AsArray())
        {
            pages[c!["case_id"]!.GetValue<string>()] = c["pages"]!.GetValue<int>();
        }

        var pdf1 = File.ReadAllBytes(Path.Combine(casesDir, "case-001.pdf"));
        var pdf3 = File.ReadAllBytes(Path.Combine(casesDir, "case-003.pdf"));
        var truth1 = GroundTruth.Load(Path.Combine(casesDir, "case-001.xml"));
        var truth3 = GroundTruth.Load(Path.Combine(casesDir, "case-003.xml"));

        doc.AppendLine("# LLM-06 spike: gateway shape and retry ownership");
        doc.AppendLine();
        doc.AppendLine("Recorded by `dotnet/tools/LlmSpike --live` against the live Anthropic API (plan 01-10, decision D-11).");
        doc.AppendLine("This record is sanitised: it holds shapes, counts, status codes and parse outcomes only. It contains no provider key,");
        doc.AppendLine("no request header, no request body, no PDF bytes and no model text (model text is reduced to a parse outcome).");
        doc.AppendLine();
        doc.AppendLine($"- Run date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        doc.AppendLine($"- Contract: prompt `{contract.PromptVersion}`, model-facing schema sha256 `{contract.OutputSchemaSha256}`");
        doc.AppendLine($"- Dataset: skeleton cases (case-001: {pages["case-001"]} page, case-003: {pages["case-003"]} pages), ground truth from the case XML");
        doc.AppendLine($"- Spike budget: US${budget.ToString("F2", CultureInfo.InvariantCulture)} (the Phase 1 cap is US$5.00, D-09)");
        doc.AppendLine();

        await Step1Async();
        await Step2Async(pdf1, truth1);
        await Step3Async(pdf1, pdf3);
        await Step4Async(pdf1, truth1);
        await Step5Async(pdf1, truth1);
        await Step6Async(pdf3, truth3);
        await Step7Async();
        Step8();
        WriteSpend();
        WriteRecommendation();

        WriteAllAtomically();
        Console.WriteLine($"LIVE DONE: spend US${Spent.ToString("F4", CultureInfo.InvariantCulture)} of US${budget.ToString("F2", CultureInfo.InvariantCulture)}; wrote {outPath}");
        return 0;
    }

    private static string Money(decimal value) => "US$" + value.ToString("F4", CultureInfo.InvariantCulture);

    private AnthropicClient NewClient(CapturingHandler handler, int maxRetries) =>
        new()
        {
            ApiKey = key,
            BaseUrl = "https://api.anthropic.com",
            HttpClient = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan },
            MaxRetries = maxRetries,
            Timeout = TimeSpan.FromSeconds(180),
        };

    private async Task<CallResult> SendAsync(MessageCreateParams request, int maxRetries = 0)
    {
        var handler = new CapturingHandler(new HttpClientHandler());
        using var client = NewClient(handler, maxRetries);
        var watch = Stopwatch.StartNew();
        try
        {
            var message = await client.Messages.Create(request, ct);
            return new CallResult(message, null, watch.Elapsed, handler.Attempts, handler.Statuses, handler.LastResponseBody, handler.LastRequestIdHeader, handler.LastRequestHash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CallResult(null, ex, watch.Elapsed, handler.Attempts, handler.Statuses, handler.LastResponseBody, handler.LastRequestIdHeader, handler.LastRequestHash);
        }
    }

    private static decimal Estimate(int pageCount, long maxTokens) =>
        (((pageCount * 3000) + 2000) * WorstCaseInputPerMTok / 1_000_000m) + (maxTokens * WorstCaseOutputPerMTok / 1_000_000m);

    private static decimal Cost(string model, UsageSnap u)
    {
        var (input, write5m, write1h, read, output) = model.StartsWith(Sonnet, StringComparison.Ordinal)
            ? (2.00m, 2.50m, 4.00m, 0.20m, 10.00m)
            : (1.00m, 1.25m, 2.00m, 0.10m, 5.00m);
        var w5 = u.W5m;
        if (u.W5m + u.W1h == 0 && u.Creation > 0)
        {
            w5 = u.Creation;
        }

        return ((u.Input * input) + (w5 * write5m) + (u.W1h * write1h) + (u.Read * read) + (u.Output * output)) / 1_000_000m;
    }

    /// <summary>Returns false (and records the skip) when the worst case would push the running total over the budget.</summary>
    private bool Reserve(string label, int pageCount, long maxTokens, out decimal estimate)
    {
        estimate = Estimate(pageCount, maxTokens);
        if (Spent + estimate <= budget)
        {
            return true;
        }

        doc.AppendLine($"- {label}: SKIPPED, worst-case estimate {Money(estimate)} would exceed the budget (spent so far {Money(Spent)}).");
        Console.WriteLine($"  skipped {label}");
        return false;
    }

    private UsageSnap? Account(string label, string model, decimal estimate, CallResult result)
    {
        var snap = result.Message is { } m ? UsageSnap.Of(m.Usage) : null;
        var actual = snap is null ? 0m : Cost(model, snap);
        Spent += actual;
        calls.Add((label, model, estimate, actual));
        Console.WriteLine($"  {label}: spend so far {Money(Spent)}");
        return snap;
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

    private static string DescribeError(Exception ex) =>
        ex is AnthropicApiException api
            ? $"{ex.GetType().Name}, HTTP {(int)api.StatusCode}, error type `{ErrorType(api.ResponseBody)}`"
            : ex.GetType().Name;

    private static string Status(CallResult r) => $"attempts={r.Attempts}, http statuses=[{string.Join(", ", r.Statuses)}]";

    private static string UsageKeys(string? body)
    {
        var keys = new List<string>();
        if (JsonNode.Parse(body ?? "{}")?["usage"] is JsonObject usage)
        {
            foreach (var (name, child) in usage)
            {
                if (child is JsonObject nested)
                {
                    keys.AddRange(nested.Select(kv => $"{name}.{kv.Key}"));
                }
                else
                {
                    keys.Add(name);
                }
            }
        }

        return Shapes.Names(keys);
    }

    private (bool Parsed, int Matched, string Line) Outcome(CallResult r, GroundTruth truth)
    {
        if (r.Message is not { } m)
        {
            return (false, 0, "no response");
        }

        var text = Shapes.TextOf(m);
        var (parsed, matched) = truth.Compare(text);
        return (parsed, matched, $"parsed: {parsed.ToString().ToLowerInvariant()}, fields equal to ground truth: {matched}/9, model text {text.Length} chars (not recorded)");
    }

    private string Line(string tag, string model, CallResult r, UsageSnap? snap, string extra = "")
    {
        if (r.Message is not { } m || snap is null)
        {
            return $"- {tag}: failed, {DescribeError(r.Error!)}; {Status(r)}; latency {r.Latency.TotalSeconds:F1}s";
        }

        return $"- {tag}: requested `{model}`, returned `{m.Model.Raw()}`, stop_reason `{m.StopReason?.Raw()}`, {snap}, "
            + $"text blocks {Shapes.BlockCount(m, thinking: false)}, thinking blocks {Shapes.BlockCount(m, thinking: true)}, "
            + $"latency {r.Latency.TotalSeconds:F1}s, {Status(r)}, cost {Money(Cost(model, snap))}{extra}";
    }

    private MessageCreateParams Tiny(string model, long maxTokens = 16, string? schemaJson = null)
    {
        var request = new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = [new MessageParam { Role = Role.User, Content = "Reply with the single word: ok" }],
        };
        return schemaJson is null
            ? request
            : new MessageCreateParams
            {
                Model = model,
                MaxTokens = maxTokens,
                OutputConfig = Shapes.Output(schemaJson, null),
                Messages = [new MessageParam { Role = Role.User, Content = "Return a JSON object for an invoice with made-up values." }],
            };
    }

    // ---- Step 1 ----
    private async Task Step1Async()
    {
        Console.WriteLine("step 1");
        doc.AppendLine("## Step 1: Reachability, model snapshot, message id and usage keys (A2)");
        doc.AppendLine();
        if (Reserve("step 1", 0, 16, out var estimate))
        {
            var r = await SendAsync(Tiny(Haiku));
            var snap = Account("step 1", Haiku, estimate, r);
            doc.AppendLine(Line("tiny request without document", Haiku, r, snap));
            if (r.Message is { } m)
            {
                aliasSnapshot = m.Model.Raw();
                doc.AppendLine($"- message id shape: `{m.ID[..Math.Min(4, m.ID.Length)]}...` ({m.ID.Length} chars); `request-id` response header present: {r.RequestIdHeader}");
                doc.AppendLine($"- raw `usage` keys returned by the API: {UsageKeys(r.RawBody)}");
                doc.AppendLine($"- alias `{Haiku}` resolved to snapshot `{aliasSnapshot}` (A2 {(aliasSnapshot == Haiku ? "refuted: no dated snapshot returned" : "confirmed: a dated snapshot is returned")}).");
            }
        }

        doc.AppendLine();
    }

    // ---- Step 2 ----
    private async Task Step2Async(byte[] pdf, GroundTruth truth)
    {
        Console.WriteLine("step 2");
        doc.AppendLine("## Step 2: Haiku 4.5 with the model-facing schema and a PDF document block (A1, A5)");
        doc.AppendLine();
        doc.AppendLine("The schema keywords present in the contract: `$defs`/`$ref`, `pattern`, `format: date`, `title`, `description`, `additionalProperties: false`.");
        doc.AppendLine();
        if (!Reserve("step 2", pages["case-001"], 4096, out var estimate))
        {
            doc.AppendLine();
            return;
        }

        var request = Shapes.Direct(Haiku, 4096, pdf, contract.Prompt, contract.OutputSchemaJson, cache: false);
        var r = await SendAsync(request);
        var snap = Account("step 2", Haiku, estimate, r);
        var outcome = Outcome(r, truth);
        doc.AppendLine(Line("case-001 as sent (full contract schema)", Haiku, r, snap));
        if (r.Message is not null)
        {
            schemaAcceptedAsIs = true;
            schemaVerdict = "the API accepted the full model-facing schema unchanged (`$defs`/`$ref`, `pattern`, `format: date`, `title`); no fallback is needed";
            doc.AppendLine($"- schema acceptance: accepted as is. Parse outcome: {outcome.Line}");
            tokensPerPage = snap?.TotalInput;
            doc.AppendLine($"- input tokens for the one-page request (prompt, schema and document): {snap?.TotalInput}");
            fixtureBodies["messages-haiku-end-turn.json"] = r.RawBody ?? "{}";
        }
        else if (r.Error is AnthropicBadRequestException bad)
        {
            schemaAcceptedAsIs = false;
            doc.AppendLine($"- schema acceptance: rejected with HTTP 400, error type `{ErrorType(bad.ResponseBody)}`; API message (validation text, no secrets): {Truncate(ErrorMessage(bad.ResponseBody), 400)}");
            await BisectSchemaAsync(pdf, truth);
        }
        else
        {
            doc.AppendLine("- schema acceptance: not determined, the request failed for a reason other than a 400.");
        }

        doc.AppendLine();
    }

    private async Task BisectSchemaAsync(byte[] pdf, GroundTruth truth)
    {
        doc.AppendLine("- bisecting by dropping keywords cumulatively, using a tiny request without a document:");
        var dropped = new List<string>();
        foreach (var keyword in new[] { "title", "format", "pattern" })
        {
            dropped.Add(keyword);
            var stripped = StripKeywords(JsonNode.Parse(contract.OutputSchemaJson)!, dropped.ToHashSet()).ToJsonString();
            if (!Reserve($"bisect without {Shapes.Names(dropped)}", 0, 32, out var estimate))
            {
                continue;
            }

            var probe = await SendAsync(Tiny(Haiku, 32, stripped));
            Account($"bisect {keyword}", Haiku, estimate, probe);
            doc.AppendLine(probe.Error is null
                ? $"  - without {Shapes.Names(dropped)}: accepted"
                : $"  - without {Shapes.Names(dropped)}: {DescribeError(probe.Error)}");
            if (probe.Error is not null)
            {
                continue;
            }

            schemaVerdict = $"the API rejected the full schema; dropping {Shapes.Names(dropped)} made it acceptable";
            if (Reserve("step 2 rerun with fallback schema", pages["case-001"], 4096, out var rerunEstimate))
            {
                var rerun = await SendAsync(Shapes.Direct(Haiku, 4096, pdf, contract.Prompt, stripped, cache: false));
                var snap = Account("step 2 rerun", Haiku, rerunEstimate, rerun);
                doc.AppendLine(Line("case-001 with the fallback schema", Haiku, rerun, snap, $"; {Outcome(rerun, truth).Line}"));
                if (rerun.Message is not null)
                {
                    fixtureBodies["messages-haiku-end-turn.json"] = rerun.RawBody ?? "{}";
                    tokensPerPage = snap?.TotalInput;
                }
            }

            return;
        }

        schemaVerdict = "the API rejected the full schema and dropping title, format and pattern did not help; escalate before plan 01-12";
    }

    private static JsonNode StripKeywords(JsonNode node, ISet<string> drop)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (name, child) in obj)
            {
                if (!drop.Contains(name) && child is not null)
                {
                    result[name] = StripKeywords(child, drop);
                }
            }

            return result;
        }

        return node.DeepClone();
    }

    private static string ErrorMessage(string? body)
    {
        try
        {
            return JsonNode.Parse(body ?? string.Empty)?["error"]?["message"]?.GetValue<string>() ?? "(none)";
        }
        catch (JsonException)
        {
            return "(unparseable)";
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    // ---- Step 3 ----
    private async Task Step3Async(byte[] pdf1, byte[] pdf3)
    {
        _ = pdf1;
        Console.WriteLine("step 3");
        doc.AppendLine("## Step 3: Cache-token confirmation on the multi-page case (Pitfall 6)");
        doc.AppendLine();
        doc.AppendLine("Two identical requests per model with `cache_control` on the document and a byte-identical schema, on case-003 (2 pages), max_tokens 1024. The document title carries a per-run nonce so the first call is a cold cache write.");
        doc.AppendLine("Haiku 4.5 needs a 4096-token prefix to cache; Sonnet 5.5 needs 512.");
        doc.AppendLine();
        // A per-run nonce in the document title makes the first call of each pair a cold cache write even when a
        // previous run left a warm entry; both calls in a pair carry the same nonce.
        var nonce = "spike-" + Guid.NewGuid().ToString("N")[..12];
        string? creationFixture = null;
        string? readFixture = null;
        foreach (var (model, label, effort) in new[] { (Haiku, "haiku", (Effort?)null), (Sonnet, "sonnet", (Effort?)Effort.Low) })
        {
            var snaps = new List<UsageSnap>();
            for (var i = 1; i <= 2; i++)
            {
                if (!Reserve($"step 3 {label} call {i}", pages["case-003"], 1024, out var estimate))
                {
                    break;
                }

                var r = await SendAsync(Shapes.Direct(model, 1024, pdf3, contract.Prompt, contract.OutputSchemaJson, cache: true, effort, nonce));
                var snap = Account($"step 3 {label} {i}", model, estimate, r);
                doc.AppendLine(Line($"{label} call {i}", model, r, snap));
                if (snap is null)
                {
                    break;
                }

                snaps.Add(snap);
                if (snap.Creation > 0)
                {
                    creationFixture ??= r.RawBody;
                }
                else if (snap.Read > 0)
                {
                    readFixture ??= r.RawBody;
                }
            }

            if (snaps.Count == 2)
            {
                var cached = snaps[0].Creation > 0 && snaps[1].Read > 0;
                doc.AppendLine($"- {label}: first call cache_creation={snaps[0].Creation} (5m={snaps[0].W5m}, 1h={snaps[0].W1h}), second call cache_read={snaps[1].Read}; cache write then read observed: {cached}");
                if (label == "haiku")
                {
                    haikuCachesMultiPage = cached;
                    if (tokensPerPage is { } onePage)
                    {
                        tokensPerPage = snaps[0].TotalInput - onePage;
                        doc.AppendLine($"- approximate input tokens per extra page (case-003 minus case-001 total input): {tokensPerPage} (A5). Case-003 is item-dense, so this is an upper-end figure for the skeleton, not a per-page constant.");
                    }
                }
                else
                {
                    sonnetCaches = cached;
                }
            }
            else if (label == "haiku")
            {
                haikuCachesMultiPage = null;
            }
        }

        var cacheFixture = creationFixture ?? readFixture;
        if (cacheFixture is not null)
        {
            fixtureBodies["messages-cache-usage.json"] = cacheFixture;
        }

        doc.AppendLine();
    }

    // ---- Step 4 ----
    private async Task Step4Async(byte[] pdf, GroundTruth truth)
    {
        Console.WriteLine("step 4");
        doc.AppendLine("## Step 4: Sonnet 5.5 with explicit effort, then two deliberately invalid requests (A3, A4, A6)");
        doc.AppendLine();
        if (Reserve("step 4 sonnet", pages["case-001"], 8192, out var estimate))
        {
            var r = await SendAsync(Shapes.Direct(Sonnet, 8192, pdf, contract.Prompt, contract.OutputSchemaJson, cache: false, Effort.Low));
            var snap = Account("step 4 sonnet", Sonnet, estimate, r);
            doc.AppendLine(Line("case-001, `OutputConfig.Format` plus `Effort.Low`, no thinking parameter, no sampling parameters, max_tokens 8192", Sonnet, r, snap, $"; {Outcome(r, truth).Line}"));
            sonnetAcceptsFormatAndEffort = r.Message is not null;
            if (snap is { Thinking: { } thinking })
            {
                doc.AppendLine($"- A3: the API reports thinking_tokens={thinking} inside output_tokens={snap.Output} (output_tokens includes thinking: {snap.Output >= thinking}).");
            }
            else if (snap is not null)
            {
                doc.AppendLine("- A3: no `output_tokens_details.thinking_tokens` field was returned; thinking tokens could not be separated from output_tokens.");
            }
        }

        foreach (var (label, build) in new (string, Func<MessageCreateParams>)[]
                 {
                     ("invalid request A: `thinking: disabled` on Sonnet 5.5", () => new MessageCreateParams
                     {
                         Model = Sonnet,
                         MaxTokens = 16,
                         Thinking = new ThinkingConfigParam(new ThinkingConfigDisabled()),
                         Messages = [new MessageParam { Role = Role.User, Content = "Reply with the single word: ok" }],
                     }),
                     ("invalid request B: non-default `temperature` (0.5) on Sonnet 5.5", () => new MessageCreateParams
                     {
                         Model = Sonnet,
                         MaxTokens = 16,
#pragma warning disable CS0618 // deliberately sending the deprecated parameter to record the 400
                         Temperature = 0.5,
#pragma warning restore CS0618
                         Messages = [new MessageParam { Role = Role.User, Content = "Reply with the single word: ok" }],
                     }),
                 })
        {
            if (!Reserve(label, 0, 16, out var invalidEstimate))
            {
                continue;
            }

            var r = await SendAsync(build());
            var snap = Account(label, Sonnet, invalidEstimate, r);
            doc.AppendLine(r.Error is not null
                ? $"- {label}: {DescribeError(r.Error)}; {Status(r)}"
                : $"- {label}: ACCEPTED (unexpected), {snap}; {Status(r)}");
        }

        doc.AppendLine();
    }

    // ---- Step 5 ----
    private async Task Step5Async(byte[] pdf, GroundTruth truth)
    {
        Console.WriteLine("step 5");
        doc.AppendLine("## Step 5: Truncation (max_tokens) and refusal paths");
        doc.AppendLine();
        if (Reserve("step 5", pages["case-001"], 50, out var estimate))
        {
            var r = await SendAsync(Shapes.Direct(Haiku, 50, pdf, contract.Prompt, contract.OutputSchemaJson, cache: false));
            var snap = Account("step 5", Haiku, estimate, r);
            doc.AppendLine(Line("case-001 with max_tokens 50", Haiku, r, snap, $"; {Outcome(r, truth).Line}"));
            if (r.Message is { } m)
            {
                doc.AppendLine($"- observed stop_reason `{m.StopReason?.Raw()}`; a truncated answer is not valid JSON, so strict parsing yields a typed schema failure unless the stop reason is checked first.");
                if (m.StopReason?.Raw() == "max_tokens")
                {
                    fixtureBodies["messages-max-tokens.json"] = r.RawBody ?? "{}";
                }
            }
        }

        doc.AppendLine("- refusal: it cannot be forced reliably, so it was not attempted live. The offline self-test line (e) covers the mapping with a canned payload.");
        doc.AppendLine();
    }

    // ---- Step 6 ----
    private async Task Step6Async(byte[] pdf, GroundTruth truth)
    {
        Console.WriteLine("step 6");
        doc.AppendLine("## Step 6: Direct SDK versus IChatClient with the raw factory, same request");
        doc.AppendLine();
        doc.AppendLine("Both calls use Haiku 4.5, case-003, `cache_control` on the document, the contract schema and max_tokens 4096.");
        doc.AppendLine();
        if (!Reserve("step 6 direct", pages["case-003"], 4096, out var directEstimate))
        {
            doc.AppendLine();
            return;
        }

        // Direct path through WithRawResponse, so the request id is reachable.
        var directHandler = new CapturingHandler(new HttpClientHandler());
        UsageSnap? directSnap = null;
        string? directRequestId = null;
        Message? directMessage = null;
        var watch = Stopwatch.StartNew();
        try
        {
            using var directClient = NewClient(directHandler, 0);
            using var raw = await directClient.WithRawResponse.Messages.Create(
                Shapes.Direct(Haiku, 4096, pdf, contract.Prompt, contract.OutputSchemaJson, cache: true), ct);
            directRequestId = raw.RequestID;
            directMessage = await raw.Deserialize(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            doc.AppendLine($"- direct path failed: {DescribeError(ex)}");
        }

        if (directMessage is not null)
        {
            directSnap = UsageSnap.Of(directMessage.Usage);
            Account("step 6 direct", Haiku, directEstimate, new CallResult(directMessage, null, watch.Elapsed, directHandler.Attempts, directHandler.Statuses, directHandler.LastResponseBody, directHandler.LastRequestIdHeader, directHandler.LastRequestHash));
            var directOutcome = Outcome(new CallResult(directMessage, null, watch.Elapsed, 1, [], null, false, null), truth);
            doc.AppendLine($"- direct SDK: stop_reason `{directMessage.StopReason?.Raw()}`, {directSnap}, message id and model reachable ({directMessage.ID.Length} chars, `{directMessage.Model.Raw()}`), request id reachable through `WithRawResponse`: {!string.IsNullOrEmpty(directRequestId)}; {directOutcome.Line}");
        }

        if (!Reserve("step 6 IChatClient", pages["case-003"], 4096, out var chatEstimate))
        {
            doc.AppendLine();
            return;
        }

        var chatHandler = new CapturingHandler(new HttpClientHandler());
        ChatResponse? chatResponse = null;
        watch.Restart();
        try
        {
            using var chatClient = NewClient(chatHandler, 0);
            var (chat, options, message) = Shapes.ChatRaw(chatClient, Haiku, 4096, pdf, contract.Prompt, contract.OutputSchemaJson, cache: true);
            chatResponse = await chat.GetResponseAsync([message], options, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            doc.AppendLine($"- IChatClient path failed: {DescribeError(ex)}");
        }

        if (chatResponse is null)
        {
            doc.AppendLine();
            return;
        }

        var rawUsage = JsonNode.Parse(chatHandler.LastResponseBody ?? "{}")?["usage"];
        long RawLong(params string[] path)
        {
            JsonNode? node = rawUsage;
            foreach (var part in path)
            {
                node = node?[part];
            }

            return node is null ? 0 : node.GetValue<long>();
        }

        var rawInput = RawLong("input_tokens");
        var rawRead = RawLong("cache_read_input_tokens");
        var rawCreation = RawLong("cache_creation_input_tokens");
        var raw5m = RawLong("cache_creation", "ephemeral_5m_input_tokens");
        var raw1h = RawLong("cache_creation", "ephemeral_1h_input_tokens");
        var rawOutput = RawLong("output_tokens");
        var usage = chatResponse.Usage;
        var counts = usage?.AdditionalCounts is null
            ? "none"
            : string.Join(";", usage.AdditionalCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        var chatSnapForCost = new UsageSnap(rawInput, rawOutput, rawCreation, rawRead, raw5m, raw1h, null);
        Account("step 6 IChatClient", Haiku, chatEstimate, new CallResult(null, null, watch.Elapsed, chatHandler.Attempts, chatHandler.Statuses, chatHandler.LastResponseBody, chatHandler.LastRequestIdHeader, chatHandler.LastRequestHash));
        Spent += Cost(Haiku, chatSnapForCost);
        calls[^1] = (calls[^1].Label, calls[^1].Model, calls[^1].Estimate, Cost(Haiku, chatSnapForCost));

        var chatText = chatResponse.Text;
        var chatOutcome = truth.Compare(chatText);
        doc.AppendLine($"- IChatClient raw factory: FinishReason `{chatResponse.FinishReason?.Value}`, ModelId `{chatResponse.ModelId}`, ResponseId reachable ({chatResponse.ResponseId?.Length} chars), "
            + $"Usage.InputTokenCount={usage?.InputTokenCount} CachedInputTokenCount={usage?.CachedInputTokenCount} OutputTokenCount={usage?.OutputTokenCount} AdditionalCounts=[{counts}]; "
            + $"parsed: {chatOutcome.Parsed.ToString().ToLowerInvariant()}, fields equal to ground truth: {chatOutcome.Matched}/9");
        doc.AppendLine($"- raw `usage` from the API for the IChatClient call: input={rawInput} cache_creation={rawCreation} (5m={raw5m}, 1h={raw1h}) cache_read={rawRead} output={rawOutput}");

        var sameBody = directHandler.LastRequestHash is not null && directHandler.LastRequestHash == chatHandler.LastRequestHash;
        doc.AppendLine($"- request bodies byte-identical between the two paths (compared by SHA-256 in memory, not recorded): {sameBody}");
        if (!sameBody && directHandler.LastRequestJson is { } directJson && chatHandler.LastRequestJson is { } chatJson)
        {
            // Only member NAMES are recorded, never values, so no request content reaches the document.
            var directShape = Shapes.FlattenDeep(JsonNode.Parse(directJson));
            var chatShape = Shapes.FlattenDeep(JsonNode.Parse(chatJson));
            var onlyDirect = directShape.Keys.Except(chatShape.Keys).Select(Shapes.Last).Distinct().ToList();
            var onlyChat = chatShape.Keys.Except(directShape.Keys).Select(Shapes.Last).Distinct().ToList();
            var different = directShape.Keys.Intersect(chatShape.Keys).Where(k => directShape[k] != chatShape[k]).Select(Shapes.Last).Distinct().ToList();
            var structurallySame = onlyDirect.Count == 0 && onlyChat.Count == 0 && different.Count == 0;
            doc.AppendLine(structurallySame
                ? "  - structurally identical (same members and same values, compared in memory); the byte difference is serialisation order or whitespace only"
                : $"  - structural difference by member name only: only in the direct request={Shapes.Names(onlyDirect)}; only in the IChatClient request={Shapes.Names(onlyChat)}; same member, different value={Shapes.Names(different)}");
            var schemaSame = JsonNode.DeepEquals(
                JsonNode.Parse(directJson)?["output_config"]?["format"]?["schema"],
                JsonNode.Parse(chatJson)?["output_config"]?["format"]?["schema"]);
            doc.AppendLine($"  - the output schema is identical between the two requests: {schemaSame}");
        }

        var adapterInput = usage?.InputTokenCount ?? 0;
        var adapterCached = usage?.CachedInputTokenCount ?? 0;
        var adapterCreation = usage?.AdditionalCounts is { } counted && counted.TryGetValue("CacheCreationInputTokens", out var created) ? created : 0;
        var inputDerivable = adapterInput - adapterCached - adapterCreation == rawInput;
        var inputSumsCache = adapterInput == rawInput + rawCreation + rawRead;
        var readMatches = adapterCached == rawRead;
        var offline = await SelfTest.AdapterUsageFactsAsync();
        usageLossless = inputDerivable && readMatches && offline.SplitReachable;
        doc.AppendLine($"- usage decomposition from the ChatResponse alone (live): adapter input count equals uncached+creation+read={inputSumsCache}; uncached input recoverable by subtraction={inputDerivable}; cache read matches={readMatches}. "
            + $"Cache creation was {rawCreation} here (the cache entry was already warm), so the creation case and the 5m/1h split come from the canned body of self-test line (d): the adapter sums creation into the input count={offline.InputIsSum} and exposes the 5m/1h split={offline.SplitReachable}.");

        refusalFacts = await SelfTest.StopFactsAsync(SelfTest.RefusalBodyForLive);
        doc.AppendLine($"- refusal mapping (canned payload, offline): direct SDK stop_reason `{refusalFacts.StopReason}` with stop_details category `{refusalFacts.Category}`; "
            + $"adapter FinishReason `{refusalFacts.FinishReason}`, stop_details in the response properties: {refusalFacts.ChatDetailsInProperties}, reachable only through the raw representation: {refusalFacts.ChatRawHasDetails}");
        doc.AppendLine($"- request id: the HTTP `request-id` header was present on the wire: {chatHandler.LastRequestIdHeader}; reachable from the direct path via `WithRawResponse`: {!string.IsNullOrEmpty(directRequestId)}; "
            + $"visible in the ChatResponse: {(chatResponse.AdditionalProperties?.Keys.Any(k => k.Contains("request", StringComparison.OrdinalIgnoreCase)) ?? false)}");
        _ = directSnap;
        doc.AppendLine();
    }

    // ---- Step 7 ----
    private async Task Step7Async()
    {
        Console.WriteLine("step 7");
        doc.AppendLine("## Step 7: Retry ownership");
        doc.AppendLine();
        doc.AppendLine("A counting handler wraps the real handler and the client is configured with `MaxRetries = 2`. Both requests below are invalid and are rejected before inference.");
        doc.AppendLine();
        var allOne = true;
        var ran = false;
        foreach (var (label, build) in new (string, Func<MessageCreateParams>)[]
                 {
                     ("`thinking: disabled` on Sonnet 5.5", () => new MessageCreateParams
                     {
                         Model = Sonnet,
                         MaxTokens = 16,
                         Thinking = new ThinkingConfigParam(new ThinkingConfigDisabled()),
                         Messages = [new MessageParam { Role = Role.User, Content = "Reply with the single word: ok" }],
                     }),
                     ("unknown model id", () => new MessageCreateParams
                     {
                         Model = "claude-model-that-does-not-exist",
                         MaxTokens = 16,
                         Messages = [new MessageParam { Role = Role.User, Content = "Reply with the single word: ok" }],
                     }),
                 })
        {
            if (!Reserve($"step 7 {label}", 0, 16, out var estimate))
            {
                continue;
            }

            var r = await SendAsync(build(), maxRetries: 2);
            Account($"step 7 {label}", Sonnet, estimate, r);
            ran = true;
            allOne &= r.Attempts == 1;
            doc.AppendLine(r.Error is not null
                ? $"- {label}: {DescribeError(r.Error)}; {Status(r)} (not retried: {r.Attempts == 1})"
                : $"- {label}: unexpectedly accepted; {Status(r)}");
        }

        fourXxNotRetried = ran ? allOne : null;
        doc.AppendLine("- 5xx and 529 behaviour is taken from the offline self-test line (f): with `MaxRetries = 2` a 529, 529, 200 sequence makes 3 attempts and succeeds; with `MaxRetries = 0` it makes 1 attempt and throws `Anthropic5xxException`. A live 529 cannot be provoked on demand.");
        doc.AppendLine("- The SDK hides the extra attempts unless a counting handler is installed through `AnthropicClient.HttpClient`; with it, per-attempt visibility needs no Polly.");
        doc.AppendLine();
    }

    // ---- Step 8 ----
    private void Step8()
    {
        Console.WriteLine("step 8");
        doc.AppendLine("## Step 8: Sanitised golden response bodies");
        doc.AppendLine();
        doc.AppendLine("Written under `dotnet/tests/Carimbo.Llm.Tests/Fixtures/` for the pricing and mapping tests in plan 01-12. Every `content[]` text, thinking and signature value is replaced by `<redacted: N chars>`; ids, model, stop_reason and usage are kept as returned.");
        doc.AppendLine();
        foreach (var name in new[] { "messages-haiku-end-turn.json", "messages-max-tokens.json", "messages-cache-usage.json" })
        {
            if (fixtureBodies.TryGetValue(name, out var body))
            {
                fixtureBodies[name] = Sanitise(body);
                doc.AppendLine($"- `{name}`: written");
            }
            else
            {
                doc.AppendLine($"- `{name}`: NOT written, the step that produces it did not run or did not return the expected shape");
            }
        }

        doc.AppendLine();
    }

    private static string Sanitise(string body)
    {
        var node = JsonNode.Parse(body)!.AsObject();
        if (node["content"] is JsonArray content)
        {
            foreach (var item in content.OfType<JsonObject>())
            {
                foreach (var field in new[] { "text", "thinking", "signature", "data" })
                {
                    if (item[field] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        item[field] = $"<redacted: {text.Length} chars>";
                    }
                }
            }
        }

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return node.ToJsonString(options) + "\n";
    }

    private void WriteSpend()
    {
        doc.AppendLine("## Spend");
        doc.AppendLine();
        doc.AppendLine("Actual spend is computed from the returned usage with the research pricing table (US$ per MTok: Haiku 4.5 input 1.00, 5m write 1.25, 1h write 2.00, read 0.10, output 5.00; Sonnet 5.5 input 2.00, 5m write 2.50, 1h write 4.00, read 0.20, output 10.00).");
        doc.AppendLine("The estimate is the conservative pre-call guard: every input token at US$4.00/MTok (pages x 3000 + 2000 tokens) and max_tokens at US$10.00/MTok.");
        doc.AppendLine();
        doc.AppendLine("| Call | Model | Worst-case estimate | Actual |");
        doc.AppendLine("|------|-------|---------------------|--------|");
        foreach (var (label, model, estimate, actual) in calls)
        {
            doc.AppendLine($"| {label} | {model} | {Money(estimate)} | {Money(actual)} |");
        }

        doc.AppendLine();
        doc.AppendLine($"- Estimated (sum of worst cases for the calls made): {Money(calls.Sum(c => c.Estimate))}");
        doc.AppendLine($"- Actual: {Money(Spent)}");
        doc.AppendLine($"- Budget: US${budget.ToString("F2", CultureInfo.InvariantCulture)}; within budget: {Spent <= budget}");
        doc.AppendLine("- This counts toward the US$5.00 Phase 1 cap (D-09).");
        doc.AppendLine();
    }

    private void WriteRecommendation()
    {
        var stopLossless = refusalFacts is { ChatDetailsInProperties: true };
        var adapterLossless = usageLossless == true && stopLossless;
        doc.AppendLine("## Recommendation");
        doc.AppendLine();
        doc.AppendLine("Decision rule from the research: choose the direct SDK unless the IChatClient path is lossless on usage decomposition and on stop details.");
        doc.AppendLine();
        doc.AppendLine($"- Usage decomposition lossless through the adapter (live, Step 6): {Describe(usageLossless)}");
        doc.AppendLine($"- Stop details lossless through the adapter (canned refusal, Step 6): {Describe(stopLossless)}");
        doc.AppendLine("- Default `ChatResponseFormat.ForJsonSchema` path rewrites the committed schema (offline self-test line (c)); the raw factory keeps it verbatim.");
        doc.AppendLine("- \"Lossless\" is judged on the ChatResponse itself. The adapter also exposes the provider `Message` through `RawRepresentation` (self-test line (e)), so the full detail can be recovered there, but that is the direct SDK type again: the adapter then adds a mapping layer and a provider-type cast without adding information.");
        doc.AppendLine();
        if (adapterLossless)
        {
            doc.AppendLine("**Recommended bottom adapter: `ichatclient-raw`.** The adapter was lossless on both measures in this run.");
        }
        else
        {
            doc.AppendLine("**Recommended bottom adapter: `direct-sdk`** behind `ILlmGateway`. The IChatClient path is not lossless on at least one measure, so the rule selects the direct SDK.");
        }

        doc.AppendLine();
        doc.AppendLine("Supporting evidence:");
        doc.AppendLine();
        doc.AppendLine($"- Schema acceptance on Haiku 4.5: {schemaVerdict ?? "not determined"}.");
        doc.AppendLine($"- Sonnet 5.5 accepts `OutputConfig.Format` together with `Effort`: {Describe(sonnetAcceptsFormatAndEffort)}.");
        doc.AppendLine($"- Haiku 4.5 wrote then read cache on the 2-page case: {Describe(haikuCachesMultiPage)}; Sonnet 5.5: {Describe(sonnetCaches)}.");
        doc.AppendLine($"- Approximate input tokens per page: {(tokensPerPage is { } t ? t.ToString(CultureInfo.InvariantCulture) : "not determined")}.");
        doc.AppendLine();
        doc.AppendLine("**Phase 1 retry ownership: `MaxRetries = 0` on the SDK client.** One HTTP attempt per call keeps cost and attempt counts exactly attributable under the US$5 cap, "
            + $"and the SDK does not retry a 4xx (live, Step 7: not retried = {Describe(fourXxNotRetried)}). The offline self-test line (f) shows that a transient 529 would be retried only when `MaxRetries` is raised. "
            + "A counting handler passed through `AnthropicClient.HttpClient` gives per-attempt visibility if retries are switched on later. Phase 3 (LLM-01) should own the single retry policy, not stack a second one on top.");
        doc.AppendLine();
        var snapshot = aliasSnapshot is null
            ? "Step 1 did not run, so the snapshot question is open."
            : aliasSnapshot == Haiku
                ? $"The API returned the alias `{Haiku}` itself, so there is no dated snapshot to pin."
                : $"The alias `{Haiku}` resolved to `{aliasSnapshot}`.";
        doc.AppendLine($"**Model id pinning.** {snapshot} Send the alias during development, always record both `model_requested` and `model_returned` (the gateway response already carries both), and pin the dated snapshot for the published eval runs so a moved alias cannot change results silently.");
        doc.AppendLine();
        doc.AppendLine("Not edited here: `docs/DECISIONS.md`. D-21 is written in plan 01-12 after the developer's decision at the Task 3 checkpoint.");
    }

    private static string Describe(bool? value) => value is null ? "not determined" : value.Value.ToString().ToLowerInvariant();

    private void WriteAllAtomically()
    {
        var files = new List<(string Path, string Content)> { (outPath, doc.ToString()) };
        files.AddRange(fixtureBodies.Select(kv => (Path.Combine(fixturesDir, kv.Key), kv.Value)));
        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", content, new UTF8Encoding(false));
        }

        foreach (var (path, _) in files)
        {
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }
}
