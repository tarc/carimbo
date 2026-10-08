using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

/// <summary>
/// The Anthropic adapter, proven offline: a recording handler serves the sanitised live bodies from plan
/// 01-10 and canned failures, so wire shape, usage decomposition, stop reasons, error classes and attempt
/// counting are all checked without a network or a real key.
/// </summary>
public class AnthropicGatewayTests
{
    internal const string DummyKey = "test-key-not-real";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic gateway test bytes");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string CommittedModelSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carimbo.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.Parent!.FullName, "schema", "invoice.model.schema.json"));
    }

    private static LlmRequest Request(string? schema = null, string model = "claude-haiku-4-5") =>
        new(model, 1024, "Extract the invoice.", new LlmDocument("application/pdf", Pdf), schema ?? CommittedModelSchema());

    private static AnthropicLlmGateway Gateway(
        RecordingHandler handler, int maxRetries = 0, TimeSpan? timeout = null) =>
        new(new AnthropicLlmGatewayOptions(DummyKey, new Uri("http://anthropic.invalid"), timeout ?? TimeSpan.FromSeconds(30), maxRetries), handler);

    // ---- wire ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_request_body_carries_the_committed_schema_the_pdf_and_the_prompt()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        var schema = CommittedModelSchema();

        await gateway.CompleteAsync(Request(schema), Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        Assert.Equal("claude-haiku-4-5", body["model"]!.GetValue<string>());
        Assert.Equal(1024, body["max_tokens"]!.GetValue<int>());

        var format = body["output_config"]!["format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema), format["schema"]), "the sent schema differs from invoice.model.schema.json");

        var content = Assert.IsType<JsonArray>(body["messages"]![0]!["content"]);
        Assert.Equal(2, content.Count);
        var document = Assert.Single(content, b => b!["type"]!.GetValue<string>() == "document")!;
        Assert.Equal("base64", document["source"]!["type"]!.GetValue<string>());
        Assert.Equal("application/pdf", document["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(Pdf, Convert.FromBase64String(document["source"]!["data"]!.GetValue<string>()));
        var text = Assert.Single(content, b => b!["type"]!.GetValue<string>() == "text")!;
        Assert.Equal("Extract the invoice.", text["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_request_body_has_no_sampling_tool_thinking_system_or_cache_control_fields()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);

        await gateway.CompleteAsync(Request(), Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        // The schema subtree is data the model reads; everything else is the request envelope.
        body["output_config"]!["format"]!.AsObject().Remove("schema");
        var keys = new HashSet<string>();
        CollectKeys(body, keys);

        foreach (var forbidden in new[] { "temperature", "top_p", "top_k", "tool_choice", "tools", "thinking", "system", "cache_control" })
        {
            Assert.DoesNotContain(forbidden, keys);
        }
    }

    [Fact]
    public async Task Follow_up_turns_become_assistant_then_user_messages_after_the_unchanged_first_message()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        var request = Request() with
        {
            FollowUps = [new LlmTurn(LlmTurnRole.Assistant, "{\"a\":1}"), new LlmTurn(LlmTurnRole.User, "fix it")],
        };

        await gateway.CompleteAsync(request, Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        var messages = Assert.IsType<JsonArray>(body["messages"]);
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => m!["role"]!.GetValue<string>()).ToArray());

        var first = messages[0]!["content"]!.AsArray();
        Assert.Equal(["document", "text"], first.Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("Extract the invoice.", first[1]!["text"]!.GetValue<string>());

        var assistant = messages[1]!["content"]!.AsArray();
        Assert.Equal("text", assistant[0]!["type"]!.GetValue<string>());
        Assert.Equal("{\"a\":1}", assistant[0]!["text"]!.GetValue<string>());
        var user = messages[2]!["content"]!.AsArray();
        Assert.Equal("text", user[0]!["type"]!.GetValue<string>());
        Assert.Equal("fix it", user[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Caching_the_document_puts_one_ephemeral_breakpoint_on_the_document_block_only()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        var request = Request() with
        {
            CacheDocument = true,
            FollowUps = [new LlmTurn(LlmTurnRole.Assistant, "{}"), new LlmTurn(LlmTurnRole.User, "again")],
        };

        await gateway.CompleteAsync(request, Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        var document = body["messages"]![0]!["content"]![0]!;
        Assert.Equal("document", document["type"]!.GetValue<string>());
        Assert.Equal("ephemeral", document["cache_control"]!["type"]!.GetValue<string>());

        document.AsObject().Remove("cache_control");
        body["output_config"]!["format"]!.AsObject().Remove("schema");
        var keys = new HashSet<string>();
        CollectKeys(body, keys);
        Assert.DoesNotContain("cache_control", keys);
    }

    [Fact]
    public async Task Without_follow_ups_or_caching_the_body_is_the_phase_one_shape()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);

        await gateway.CompleteAsync(Request(), Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        Assert.Single(body["messages"]!.AsArray());
        Assert.Null(body["messages"]![0]!["content"]![0]!["cache_control"]);
    }

    [Theory]
    [InlineData("user-only")]
    [InlineData("assistant-final")]
    [InlineData("assistant-assistant")]
    [InlineData("user-assistant-user")]
    public async Task A_conversation_that_does_not_alternate_or_ends_on_the_assistant_is_refused_before_any_request(string shape)
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        LlmTurn[] turns = shape switch
        {
            "user-only" => [new(LlmTurnRole.User, "x")],
            "assistant-final" => [new(LlmTurnRole.Assistant, "x")],
            "assistant-assistant" => [new(LlmTurnRole.Assistant, "a"), new(LlmTurnRole.Assistant, "b")],
            _ => [new(LlmTurnRole.User, "a"), new(LlmTurnRole.Assistant, "b"), new(LlmTurnRole.User, "c")],
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => gateway.CompleteAsync(Request() with { FollowUps = turns }, Ct));

        Assert.Contains("FollowUps", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task A_sixteen_thousand_token_cap_is_sent_once_as_a_non_streaming_request()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);

        await gateway.CompleteAsync(Request() with { MaxTokens = 16000 }, Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        Assert.Equal(16000, body["max_tokens"]!.GetValue<int>());
        Assert.Null(body["stream"]);
    }

    private static void CollectKeys(JsonNode? node, HashSet<string> keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    keys.Add(key);
                    CollectKeys(child, keys);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    CollectKeys(child, keys);
                }

                break;
        }
    }

    [Fact]
    public async Task A_non_pdf_document_is_refused_before_anything_is_sent()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        var request = Request() with { Document = new LlmDocument("image/png", Pdf) };

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.CompleteAsync(request, Ct));
        Assert.Empty(handler.RequestBodies);
    }

    // ---- recorded live bodies ------------------------------------------------------------------------

    [Fact]
    public async Task The_haiku_end_turn_body_maps_to_a_typed_response()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        var fixture = JsonNode.Parse(Fixture("messages-haiku-end-turn.json"))!;

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(LlmStopReason.EndTurn, response.StopReason);
        Assert.Equal(fixture["content"]![0]!["text"]!.GetValue<string>(), response.Text);
        Assert.Equal(fixture["usage"]!["input_tokens"]!.GetValue<long>(), response.Usage.InputTokens);
        Assert.Equal(fixture["usage"]!["output_tokens"]!.GetValue<long>(), response.Usage.OutputTokens);
        Assert.Equal(0, response.Usage.CacheReadTokens);
        Assert.Equal(0, response.Usage.CacheWrite5mTokens);
        Assert.Equal(0, response.Usage.CacheWrite1hTokens);
        Assert.Equal("claude-haiku-4-5", response.ModelRequested);
        Assert.Equal(fixture["model"]!.GetValue<string>(), response.ModelReturned);
        Assert.Equal(fixture["id"]!.GetValue<string>(), response.ProviderMessageId);
        Assert.Equal(1, response.HttpAttempts);
        Assert.True(response.Latency >= TimeSpan.Zero);
        Assert.Null(response.Cost);
    }

    [Fact]
    public async Task The_cache_usage_body_keeps_every_token_class_separate_and_prices_exactly()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-cache-usage.json")));
        using var gateway = Gateway(handler);

        var response = await gateway.CompleteAsync(Request(), Ct);

        // Live body: input 201 uncached, 10310 written to the 5m cache, nothing read, 116 out.
        Assert.Equal(new LlmUsage(201, 116, 0, 10310, 0), response.Usage);

        // 201*1.00 + 10310*1.25 + 116*5.00 = 13668.5 micro-dollars.
        var cost = LlmPricingTable.LoadEmbedded().Price(response.ModelReturned!, response.Usage);
        Assert.Equal(0.0136685m, cost.AmountUsd);
        Assert.Null(cost.Warning);
    }

    [Fact]
    public async Task Cache_read_tokens_are_not_part_of_the_uncached_input_count()
    {
        const string body = """
            {"id":"msg_synthetic_read","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[{"type":"text","text":"{}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":201,"cache_creation_input_tokens":0,"cache_read_input_tokens":10291,"output_tokens":116}}
            """;
        using var gateway = Gateway(new RecordingHandler((HttpStatusCode.OK, body)));

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(new LlmUsage(201, 116, 10291, 0, 0), response.Usage);
        var cost = LlmPricingTable.LoadEmbedded().Price(response.ModelReturned!, response.Usage);
        Assert.Equal((201m * 1.00m + 10291m * 0.10m + 116m * 5.00m) / 1_000_000m, cost.AmountUsd);
    }

    [Fact]
    public async Task The_five_minute_and_one_hour_cache_writes_come_from_the_breakdown()
    {
        const string body = """
            {"id":"msg_synthetic_split","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[{"type":"text","text":"{}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"cache_creation_input_tokens":5000,"cache_read_input_tokens":0,"cache_creation":{"ephemeral_5m_input_tokens":3000,"ephemeral_1h_input_tokens":2000},"output_tokens":7}}
            """;
        using var gateway = Gateway(new RecordingHandler((HttpStatusCode.OK, body)));

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(new LlmUsage(12, 7, 0, 3000, 2000), response.Usage);
    }

    [Fact]
    public async Task Without_a_breakdown_all_cache_creation_tokens_count_as_five_minute_writes()
    {
        const string body = """
            {"id":"msg_synthetic_nobreakdown","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[{"type":"text","text":"{}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"cache_creation_input_tokens":5000,"cache_read_input_tokens":0,"output_tokens":7}}
            """;
        using var gateway = Gateway(new RecordingHandler((HttpStatusCode.OK, body)));

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(new LlmUsage(12, 7, 0, 5000, 0), response.Usage);
    }

    [Fact]
    public async Task The_max_tokens_body_maps_to_a_max_tokens_stop()
    {
        using var gateway = Gateway(new RecordingHandler((HttpStatusCode.OK, Fixture("messages-max-tokens.json"))));

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(LlmStopReason.MaxTokens, response.StopReason);
        Assert.Equal(50, response.Usage.OutputTokens);
    }

    [Fact]
    public async Task A_synthetic_refusal_body_maps_to_a_refusal_with_its_stop_details()
    {
        // SYNTHETIC: a refusal cannot be forced live, so this body is hand-built from the documented shape.
        const string body = """
            {"id":"msg_synthetic_refusal","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[],"stop_reason":"refusal","stop_sequence":null,"stop_details":{"type":"refusal","category":"cyber","explanation":"synthetic refusal explanation"},"usage":{"input_tokens":12,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
            """;
        using var gateway = Gateway(new RecordingHandler((HttpStatusCode.OK, body)));

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(LlmStopReason.Refusal, response.StopReason);
        Assert.Equal(string.Empty, response.Text);
        Assert.NotNull(response.StopDetail);
        Assert.Contains("cyber", response.StopDetail);
        Assert.Contains("synthetic refusal explanation", response.StopDetail);
    }

    [Fact]
    public async Task An_unknown_stop_reason_maps_to_other()
    {
        const string body = """
            {"id":"msg_synthetic_pause","type":"message","role":"assistant","model":"claude-haiku-4-5-20251001","content":[{"type":"text","text":"x"}],"stop_reason":"pause_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
            """;
        using var gateway = Gateway(new RecordingHandler((HttpStatusCode.OK, body)));

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(LlmStopReason.Other, response.StopReason);
    }

    // ---- failures ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(401, LlmFailureKind.Auth)]
    [InlineData(403, LlmFailureKind.Auth)]
    [InlineData(400, LlmFailureKind.BadRequest)]
    [InlineData(404, LlmFailureKind.BadRequest)]
    [InlineData(413, LlmFailureKind.BadRequest)]
    [InlineData(422, LlmFailureKind.BadRequest)]
    [InlineData(429, LlmFailureKind.RateLimit)]
    [InlineData(529, LlmFailureKind.Overloaded)]
    [InlineData(500, LlmFailureKind.ServerError)]
    [InlineData(503, LlmFailureKind.ServerError)]
    public async Task Each_http_failure_class_maps_to_its_own_kind_without_leaking_the_key(int status, LlmFailureKind expected)
    {
        // The body deliberately echoes the key and a secret-looking string: neither may reach the message.
        var body = """{"type":"error","error":{"type":"x_error","message":"echo """ + DummyKey + """ sk-ant-synthetic"}}""";
        var handler = new RecordingHandler((HttpStatusCode)status, body);
        using var gateway = Gateway(handler);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(expected, ex.Kind);
        Assert.Equal(status, ex.HttpStatus);
        Assert.Equal("req_synthetic", ex.RequestId);
        Assert.DoesNotContain(DummyKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant", ex.ToString(), StringComparison.Ordinal);
        Assert.Contains($"status {status}", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task A_connection_failure_maps_to_network()
    {
        var handler = new RecordingHandler(onSend: _ => throw new HttpRequestException($"connect failed for {DummyKey}"));
        using var gateway = Gateway(handler);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(LlmFailureKind.Network, ex.Kind);
        Assert.Null(ex.HttpStatus);
        Assert.DoesNotContain(DummyKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_that_never_answers_maps_to_timeout()
    {
        var handler = new RecordingHandler(onSend: async ct => await Task.Delay(Timeout.Infinite, ct));
        using var gateway = Gateway(handler, timeout: TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(LlmFailureKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_a_gateway_failure()
    {
        var handler = new RecordingHandler(onSend: async ct => await Task.Delay(Timeout.Infinite, ct));
        using var gateway = Gateway(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.CompleteAsync(Request(), cts.Token));
    }

    // ---- attempts ------------------------------------------------------------------------------------

    [Fact]
    public async Task With_no_retries_a_529_is_attempted_exactly_once()
    {
        var handler = new RecordingHandler(
            ((HttpStatusCode)529, """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""),
            (HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler, maxRetries: 0);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(LlmFailureKind.Overloaded, ex.Kind);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Attempts_are_counted_per_call_when_retries_are_enabled()
    {
        const string overloaded = """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";
        var handler = new RecordingHandler(
            ((HttpStatusCode)529, overloaded), ((HttpStatusCode)529, overloaded), (HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler, maxRetries: 2);

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(3, response.HttpAttempts);
        Assert.Equal(3, handler.Attempts);

        // A second call on the same gateway starts from one, not from the previous total.
        var next = await gateway.CompleteAsync(Request(), Ct);
        Assert.Equal(1, next.HttpAttempts);
    }

    [Fact]
    public async Task Concurrent_calls_do_not_share_an_attempt_counter()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => gateway.CompleteAsync(Request(), Ct), Ct)));

        Assert.All(responses, r => Assert.Equal(1, r.HttpAttempts));
    }
}

/// <summary>
/// Serves a script of canned responses (the last one repeats), or runs a custom action per request.
/// Records every request body. Responses carry a synthetic <c>request-id</c> header.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> queue = new();
    private readonly (HttpStatusCode Status, string Body)? last;
    private readonly Func<CancellationToken, Task>? onSend;
    private int attempts;

    public RecordingHandler(params (HttpStatusCode Status, string Body)[] script)
    {
        foreach (var step in script)
        {
            queue.Enqueue(step);
        }

        last = script.Length > 0 ? script[^1] : null;
    }

    public RecordingHandler(HttpStatusCode status, string body)
        : this((status, body))
    {
    }

    public RecordingHandler(Func<CancellationToken, Task> onSend)
        : this()
    {
        this.onSend = onSend;
    }

    public List<string> RequestBodies { get; } = [];

    public int Attempts => Volatile.Read(ref attempts);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref attempts);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (RequestBodies)
        {
            RequestBodies.Add(body);
        }

        if (onSend is not null)
        {
            await onSend(cancellationToken);
        }

        (HttpStatusCode Status, string Body) step;
        lock (queue)
        {
            step = queue.Count > 0 ? queue.Dequeue() : last ?? throw new InvalidOperationException("no scripted response");
        }

        var response = new HttpResponseMessage(step.Status)
        {
            Content = new StringContent(step.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        response.Headers.Add("request-id", "req_synthetic");
        return response;
    }
}
