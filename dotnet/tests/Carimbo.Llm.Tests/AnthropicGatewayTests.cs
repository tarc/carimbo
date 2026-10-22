using System.Diagnostics;
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

    // ---- retry policy (D-28): the SDK is the one owner -------------------------------------------------

    private const string ErrorBody = """{"type":"error","error":{"type":"x_error","message":"synthetic"}}""";

    private static readonly IReadOnlyDictionary<string, string> Instant = new Dictionary<string, string> { ["retry-after-ms"] = "1" };

    [Theory]
    [InlineData(529, 2, 3, LlmFailureKind.Overloaded)]
    [InlineData(500, 2, 3, LlmFailureKind.ServerError)]
    [InlineData(503, 2, 3, LlmFailureKind.ServerError)]
    [InlineData(429, 1, 2, LlmFailureKind.RateLimit)]
    [InlineData(408, 1, 2, LlmFailureKind.BadRequest)]
    [InlineData(409, 1, 2, LlmFailureKind.BadRequest)]
    [InlineData(400, 2, 1, LlmFailureKind.BadRequest)]
    [InlineData(401, 2, 1, LlmFailureKind.Auth)]
    [InlineData(404, 2, 1, LlmFailureKind.BadRequest)]
    public async Task The_sdk_retries_transient_statuses_up_to_max_retries_and_never_retries_the_others(
        int status, int maxRetries, int expectedAttempts, LlmFailureKind expectedKind)
    {
        var handler = new RecordingHandler(new ScriptStep((HttpStatusCode)status, ErrorBody, Instant));
        using var gateway = Gateway(handler, maxRetries);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(expectedKind, ex.Kind);
        Assert.Equal(status, ex.HttpStatus);
        Assert.Equal(expectedAttempts, handler.Attempts);
    }

    [Fact]
    public async Task Without_retry_headers_the_second_attempt_starts_at_least_100_ms_after_the_first()
    {
        var handler = new RecordingHandler(((HttpStatusCode)529, ErrorBody));
        using var gateway = Gateway(handler, maxRetries: 1);

        await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(2, handler.SentAt.Count);
        Assert.True(
            Stopwatch.GetElapsedTime(handler.SentAt[0], handler.SentAt[1]) >= TimeSpan.FromMilliseconds(100),
            "the SDK must back off before it retries");
    }

    [Fact]
    public async Task Retry_after_ms_is_honoured_between_attempts()
    {
        var headers = new Dictionary<string, string> { ["retry-after-ms"] = "300" };
        var handler = new RecordingHandler(new ScriptStep((HttpStatusCode)429, ErrorBody, headers));
        using var gateway = Gateway(handler, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(LlmFailureKind.RateLimit, ex.Kind);
        Assert.Equal(2, handler.SentAt.Count);
        Assert.True(Stopwatch.GetElapsedTime(handler.SentAt[0], handler.SentAt[1]) >= TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task X_should_retry_false_stops_retrying_a_500()
    {
        var headers = new Dictionary<string, string> { ["x-should-retry"] = "false" };
        var handler = new RecordingHandler(new ScriptStep(HttpStatusCode.InternalServerError, ErrorBody, headers));
        using var gateway = Gateway(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(LlmFailureKind.ServerError, ex.Kind);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task With_zero_retries_even_x_should_retry_true_gives_one_attempt()
    {
        var headers = new Dictionary<string, string> { ["x-should-retry"] = "true", ["retry-after-ms"] = "1" };
        var handler = new RecordingHandler(new ScriptStep(HttpStatusCode.InternalServerError, ErrorBody, headers));
        using var gateway = Gateway(handler, maxRetries: 0);

        await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task A_connection_failure_is_retried_and_still_maps_to_network()
    {
        var handler = new RecordingHandler(onSend: _ => throw new HttpRequestException($"connect failed for {DummyKey}"));
        using var gateway = Gateway(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Equal(LlmFailureKind.Network, ex.Kind);
        Assert.Equal(3, handler.Attempts);
        Assert.DoesNotContain(DummyKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_retry_of_one_call_sends_a_byte_identical_body_and_attempts_run_strictly_in_order()
    {
        var handler = new RecordingHandler(
            new ScriptStep((HttpStatusCode)529, ErrorBody, Instant),
            new ScriptStep((HttpStatusCode)529, ErrorBody, Instant),
            new ScriptStep(HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler, maxRetries: 2);

        var response = await gateway.CompleteAsync(Request(), Ct);

        Assert.Equal(3, response.HttpAttempts);
        Assert.Equal(handler.Attempts, response.HttpAttempts);
        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.All(handler.RequestBodies, body => Assert.Equal(handler.RequestBodies[0], body));
        for (var i = 1; i < handler.SentAt.Count; i++)
        {
            Assert.True(handler.SentAt[i] > handler.SentAt[i - 1], $"attempt {i + 1} must be sent after attempt {i}");
        }
    }

    [Fact]
    public async Task A_cancellation_during_backoff_propagates_and_starts_no_further_attempt()
    {
        var handler = new RecordingHandler(((HttpStatusCode)529, ErrorBody));
        using var gateway = Gateway(handler, maxRetries: 2);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // No retry headers, so the SDK now sleeps at least 100 ms; the cancel lands inside that wait.
        handler.OnAttempt = _ => cts.CancelAfter(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.CompleteAsync(Request(), cts.Token));

        await Task.Delay(TimeSpan.FromMilliseconds(400), Ct);
        Assert.Equal(1, handler.Attempts);
    }

    // ---- tool turns (agent steps) --------------------------------------------------------------------

    private static string CommittedDecisionSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Carimbo.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.Parent!.FullName, "schema", "decision.model.schema.json"));
    }

    private const string GetInvoiceSchema =
        "{\"type\":\"object\",\"properties\":{\"case_id\":{\"description\":\"The case id\",\"type\":\"string\"}},\"required\":[\"case_id\"]}";

    private const string GetPolicySchema = "{\"type\":\"object\",\"properties\":{}}";

    private static LlmToolDefinition[] AgentTools() =>
    [
        new("get_invoice", "Read the extracted invoice of a case.", GetInvoiceSchema),
        new("get_policy", "Read the approval policy.", GetPolicySchema),
    ];

    private static LlmRequest AgentRequest(IReadOnlyList<LlmTurn>? followUps = null) =>
        new("claude-haiku-4-5", 2048, "case_id: dc-001\nDecide this case: approve, reject or escalate.", null, CommittedDecisionSchema())
        {
            SystemPrompt = "You review invoices.",
            Tools = AgentTools(),
            FollowUps = followUps ?? [],
        };

    private static LlmTurn[] AgentConversation() =>
    [
        new(LlmTurnRole.Assistant, string.Empty)
        {
            Blocks =
            [
                LlmContentBlock.OfThinking(string.Empty, "sig-1"),
                LlmContentBlock.OfText("Reading."),
                LlmContentBlock.OfToolUse(new LlmToolCall("toolu_a", "get_invoice", "{\"case_id\":\"dc-001\"}")),
                LlmContentBlock.OfToolUse(new LlmToolCall("toolu_b", "get_policy", "{}")),
            ],
        },
        new(LlmTurnRole.User, string.Empty)
        {
            ToolResults = [new("toolu_a", "{\"invoice\":null}", false), new("toolu_b", "boom", true)],
        },
    ];

    [Fact]
    public async Task An_agent_step_goes_out_as_one_body_with_system_tools_schema_and_echoed_tool_turns()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-tool-use.json")));
        using var gateway = Gateway(handler);

        await gateway.CompleteAsync(AgentRequest(AgentConversation()), Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        Assert.Equal("You review invoices.", body["system"]!.GetValue<string>());

        var tools = body["tools"]!.AsArray();
        Assert.Equal(["get_invoice", "get_policy"], tools.Select(t => t!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal("Read the extracted invoice of a case.", tools[0]!["description"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GetInvoiceSchema), tools[0]!["input_schema"]), "get_invoice input_schema differs");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GetPolicySchema), tools[1]!["input_schema"]), "get_policy input_schema differs");
        Assert.Null(tools[1]!["input_schema"]!["required"]);

        var format = body["output_config"]!["format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(CommittedDecisionSchema()), format["schema"]), "the sent schema differs from decision.model.schema.json");

        var messages = body["messages"]!.AsArray();
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => m!["role"]!.GetValue<string>()).ToArray());

        var first = messages[0]!["content"]!.AsArray();
        var prompt = Assert.Single(first)!;
        Assert.Equal("text", prompt["type"]!.GetValue<string>());
        Assert.Equal("case_id: dc-001\nDecide this case: approve, reject or escalate.", prompt["text"]!.GetValue<string>());

        var assistant = messages[1]!["content"]!.AsArray();
        Assert.Equal(["thinking", "text", "tool_use", "tool_use"], assistant.Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("sig-1", assistant[0]!["signature"]!.GetValue<string>());
        Assert.Equal("Reading.", assistant[1]!["text"]!.GetValue<string>());
        Assert.Equal("toolu_a", assistant[2]!["id"]!.GetValue<string>());
        Assert.Equal("get_invoice", assistant[2]!["name"]!.GetValue<string>());
        Assert.Equal("dc-001", assistant[2]!["input"]!["case_id"]!.GetValue<string>());
        Assert.Equal("toolu_b", assistant[3]!["id"]!.GetValue<string>());
        Assert.Equal("get_policy", assistant[3]!["name"]!.GetValue<string>());
        Assert.Empty(assistant[3]!["input"]!.AsObject());

        var results = messages[2]!["content"]!.AsArray();
        Assert.Equal(["tool_result", "tool_result"], results.Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("toolu_a", results[0]!["tool_use_id"]!.GetValue<string>());
        Assert.Equal("{\"invoice\":null}", results[0]!["content"]!.GetValue<string>());
        Assert.False(results[0]!.AsObject().ContainsKey("is_error"), "is_error must be absent unless true");
        Assert.Equal("toolu_b", results[1]!["tool_use_id"]!.GetValue<string>());
        Assert.Equal("boom", results[1]!["content"]!.GetValue<string>());
        Assert.True(results[1]!["is_error"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_agent_step_sets_no_sampling_tool_choice_thinking_or_cache_control_parameter()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-tool-use.json")));
        using var gateway = Gateway(handler);

        await gateway.CompleteAsync(AgentRequest(AgentConversation()), Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        var top = body.AsObject().Select(kv => kv.Key).ToHashSet();
        foreach (var forbidden in new[] { "temperature", "top_p", "top_k", "tool_choice", "thinking", "stream" })
        {
            Assert.DoesNotContain(forbidden, top);
        }

        // Everything but the schema subtrees (data the model reads) is the envelope: no cache_control anywhere in it.
        body["output_config"]!["format"]!.AsObject().Remove("schema");
        foreach (var tool in body["tools"]!.AsArray())
        {
            tool!.AsObject().Remove("input_schema");
        }

        var keys = new HashSet<string>();
        CollectKeys(body, keys);
        foreach (var forbidden in new[] { "temperature", "top_p", "top_k", "tool_choice", "cache_control" })
        {
            Assert.DoesNotContain(forbidden, keys);
        }
    }

    [Fact]
    public async Task A_document_less_request_without_system_or_tools_sends_only_the_prompt_text()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);
        var request = new LlmRequest("claude-haiku-4-5", 512, "Say hi.", null, CommittedDecisionSchema());

        await gateway.CompleteAsync(request, Ct);

        var body = JsonNode.Parse(Assert.Single(handler.RequestBodies))!;
        Assert.Null(body["system"]);
        Assert.Null(body["tools"]);
        var content = Assert.Single(body["messages"]!.AsArray())!["content"]!.AsArray();
        Assert.Equal("Say hi.", Assert.Single(content)!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_empty_text_block_of_an_assistant_turn_is_omitted_and_a_user_turn_with_text_follows_its_tool_results()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-tool-use.json")));
        using var gateway = Gateway(handler);
        LlmTurn[] turns =
        [
            new(LlmTurnRole.Assistant, string.Empty)
            {
                Blocks =
                [
                    LlmContentBlock.OfText(string.Empty),
                    LlmContentBlock.OfToolUse(new LlmToolCall("toolu_a", "get_invoice", "{\"case_id\":\"dc-001\"}")),
                ],
            },
            new(LlmTurnRole.User, "Now decide.") { ToolResults = [new("toolu_a", "{}", false)] },
        ];

        await gateway.CompleteAsync(AgentRequest(turns), Ct);

        var messages = JsonNode.Parse(Assert.Single(handler.RequestBodies))!["messages"]!.AsArray();
        Assert.Equal(["tool_use"], messages[1]!["content"]!.AsArray().Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal(
            ["tool_result", "text"],
            messages[2]!["content"]!.AsArray().Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("Now decide.", messages[2]!["content"]![1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_tool_use_answer_maps_to_ordered_blocks_and_calls()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-tool-use.json")));
        using var gateway = Gateway(handler);

        var response = await gateway.CompleteAsync(AgentRequest(), Ct);

        Assert.Equal(LlmStopReason.ToolUse, response.StopReason);
        Assert.Equal("I will read the invoice and the policy.", response.Text);
        Assert.Equal(
            [LlmContentKind.Text, LlmContentKind.ToolUse, LlmContentKind.ToolUse],
            response.Blocks.Select(b => b.Kind).ToArray());
        Assert.Equal("I will read the invoice and the policy.", response.Blocks[0].Text);
        Assert.Equal(["toolu_synthetic_a", "toolu_synthetic_b"], response.ToolCalls.Select(c => c.Id).ToArray());
        Assert.Equal(["get_invoice", "get_policy"], response.ToolCalls.Select(c => c.Name).ToArray());
        Assert.Equal("dc-001", JsonNode.Parse(response.ToolCalls[0].InputJson)!["case_id"]!.GetValue<string>());
        Assert.Empty(JsonNode.Parse(response.ToolCalls[1].InputJson)!.AsObject());
        Assert.Equal(812, response.Usage.InputTokens);
        Assert.Equal(64, response.Usage.OutputTokens);
    }

    [Fact]
    public async Task A_thinking_tool_use_answer_keeps_the_signature_and_redacted_data_byte_for_byte()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Fixture("messages-thinking-tool-use.json")));
        using var gateway = Gateway(handler);

        var response = await gateway.CompleteAsync(AgentRequest(), Ct);

        Assert.Equal(LlmStopReason.ToolUse, response.StopReason);
        Assert.Equal(string.Empty, response.Text);
        Assert.Equal(
            [LlmContentKind.Thinking, LlmContentKind.RedactedThinking, LlmContentKind.ToolUse],
            response.Blocks.Select(b => b.Kind).ToArray());
        Assert.Equal("Synthetic reasoning text.", response.Blocks[0].Text);
        Assert.Equal("synthetic-signature-AAAA+/bbbb==", response.Blocks[0].Signature);
        Assert.Equal("synthetic-redacted-data-CCCC+/dddd==", response.Blocks[1].Data);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("toolu_synthetic_c", call.Id);
        Assert.Equal("get_invoice", call.Name);
    }

    [Fact]
    public async Task A_thinking_answer_echoed_back_sends_the_same_signature_and_redacted_data()
    {
        var handler = new RecordingHandler(
            (HttpStatusCode.OK, Fixture("messages-thinking-tool-use.json")),
            (HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
        using var gateway = Gateway(handler);

        var first = await gateway.CompleteAsync(AgentRequest(), Ct);
        var next = AgentRequest(
        [
            new LlmTurn(LlmTurnRole.Assistant, first.Text) { Blocks = first.Blocks },
            new LlmTurn(LlmTurnRole.User, string.Empty) { ToolResults = [new(first.ToolCalls[0].Id, "{}", false)] },
        ]);
        await gateway.CompleteAsync(next, Ct);

        var assistant = JsonNode.Parse(handler.RequestBodies[1])!["messages"]![1]!["content"]!.AsArray();
        Assert.Equal(["thinking", "redacted_thinking", "tool_use"], assistant.Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("synthetic-signature-AAAA+/bbbb==", assistant[0]!["signature"]!.GetValue<string>());
        Assert.Equal("Synthetic reasoning text.", assistant[0]!["thinking"]!.GetValue<string>());
        Assert.Equal("synthetic-redacted-data-CCCC+/dddd==", assistant[1]!["data"]!.GetValue<string>());
    }
}

/// <summary>One scripted response: a status, a body and optional extra response headers (for example retry-after-ms).</summary>
internal sealed record ScriptStep(HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// Serves a script of canned responses (the last one repeats), or runs a custom action per request.
/// Records every request body and a monotonic send time per attempt. Responses carry a synthetic
/// <c>request-id</c> header plus whatever headers the step names.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<ScriptStep> queue = new();
    private readonly ScriptStep? last;
    private readonly Func<CancellationToken, Task>? onSend;
    private int attempts;

    public RecordingHandler(params (HttpStatusCode Status, string Body)[] script)
        : this(script.Select(step => new ScriptStep(step.Status, step.Body)).ToArray())
    {
    }

    public RecordingHandler(HttpStatusCode status, string body)
        : this((status, body))
    {
    }

    public RecordingHandler(Func<CancellationToken, Task> onSend)
        : this(Array.Empty<ScriptStep>())
    {
        this.onSend = onSend;
    }

    public RecordingHandler(params ScriptStep[] script)
    {
        foreach (var step in script)
        {
            queue.Enqueue(step);
        }

        last = script.Length > 0 ? script[^1] : null;
    }

    public List<string> RequestBodies { get; } = [];

    /// <summary>Stopwatch timestamps, one per attempt, in the order the attempts reached the transport.</summary>
    public List<long> SentAt { get; } = [];

    /// <summary>Runs right after an attempt is recorded, with its 1-based number, before the response is built.</summary>
    public Action<int>? OnAttempt { get; set; }

    public int Attempts => Volatile.Read(ref attempts);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var number = Interlocked.Increment(ref attempts);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (RequestBodies)
        {
            RequestBodies.Add(body);
            SentAt.Add(Stopwatch.GetTimestamp());
        }

        OnAttempt?.Invoke(number);

        if (onSend is not null)
        {
            await onSend(cancellationToken);
        }

        ScriptStep step;
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
        foreach (var (name, value) in step.Headers ?? new Dictionary<string, string>())
        {
            response.Headers.Add(name, value);
        }

        return response;
    }
}
