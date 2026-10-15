using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

/// <summary>
/// The model-call span (LLM-03, D-31, T-03-17): one per call, metadata from the allowlist only, never content,
/// through cache hits, failures and SDK retries. Spans are captured with a BCL <see cref="ActivityListener"/>; the
/// listener is process-wide and test classes run in parallel, so every test starts its own parent activity and
/// keeps only the spans on that trace.
/// </summary>
public class TracingGatewayTests
{
    private const string Overloaded = """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";

    private static readonly LlmPricingTable Table = LlmPricingTable.LoadEmbedded();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static LlmRequest Request(string model = "claude-haiku-4-5", string? salt = null) =>
        new(
            model,
            1024,
            "Extract the invoice.",
            new LlmDocument("application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 synthetic tracing test bytes")),
            "{\"type\":\"object\"}")
        {
            PromptVersion = "extract-003",
            ReplicateSalt = salt,
        };

    private static LlmResponse Answer(
        LlmUsage? usage = null, string model = "claude-haiku-4-5", string text = "{\"ok\":true}") =>
        new(
            text,
            LlmStopReason.EndTurn,
            null,
            usage ?? new LlmUsage(1200, 340, 0, 0, 0),
            model,
            model,
            "msg_trace_1",
            TimeSpan.FromMilliseconds(1234),
            1);

    private static async Task WithTempDirectory(Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("carimbo-tracing-test-").FullName;
        try
        {
            await body(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static CachingLlmGateway Caching(string dir, ILlmGateway inner, LlmCacheMode mode) =>
        new(inner, new LlmCacheStore(dir), mode, TimeProvider.System);

    private static string Tag(Activity span, string key) =>
        Convert.ToString(span.GetTagItem(key), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static void AssertAllowlisted(Activity span) =>
        Assert.All(
            span.TagObjects.Select(t => t.Key),
            key => Assert.True(LlmTelemetry.AllowedTags.Contains(key), $"tag {key} is not in LlmTelemetry.AllowedTags"));

    /// <summary>Listens to the model-call source and a test-local parent source; keeps the spans of one trace.</summary>
    private sealed class SpanScope : IDisposable
    {
        private const string ParentSourceName = "Carimbo.Llm.Tests.Parent";

        private static readonly ActivitySource ParentSource = new(ParentSourceName);

        private readonly ActivityListener listener;
        private readonly ConcurrentQueue<Activity> stopped = new();

        public SpanScope()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is LlmTelemetry.SourceName or ParentSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
            Parent = ParentSource.StartActivity("test-parent")!;
        }

        public Activity Parent { get; }

        /// <summary>The model-call spans on this scope's trace, in start order.</summary>
        public Activity[] Spans =>
            stopped
                .Where(a => a.Source.Name == LlmTelemetry.SourceName && a.TraceId == Parent.TraceId)
                .OrderBy(a => a.StartTimeUtc)
                .ToArray();

        public void Dispose()
        {
            Parent.Stop();
            listener.Dispose();
        }
    }

    [Fact]
    public async Task A_priced_end_turn_span_carries_exactly_allowlisted_metadata()
    {
        using var scope = new SpanScope();
        var inner = new CountingGateway(_ => Answer(new LlmUsage(1000, 200, 300, 40, 60)));
        var gateway = new TracingLlmGateway(new CostAccountingLlmGateway(inner, Table));

        var response = await gateway.CompleteAsync(Request(salt: "rep-1"), Ct);

        var span = Assert.Single(scope.Spans);
        var keys = span.TagObjects.Select(t => t.Key).ToArray();
        AssertAllowlisted(span);
        string[] required =
        [
            LlmTelemetry.OperationName, LlmTelemetry.ProviderName, LlmTelemetry.RequestModel,
            LlmTelemetry.RequestMaxTokens, LlmTelemetry.ResponseModel, LlmTelemetry.ResponseId,
            LlmTelemetry.ResponseFinishReasons, LlmTelemetry.UsageInputTokens, LlmTelemetry.UsageOutputTokens,
            LlmTelemetry.UsageCacheReadInputTokens, LlmTelemetry.UsageCacheWriteInputTokens, LlmTelemetry.CostUsd,
            LlmTelemetry.PricingVersion, LlmTelemetry.CacheStatus, LlmTelemetry.HttpAttempts,
        ];
        Assert.All(required, key => Assert.Contains(key, keys));

        Assert.Equal("chat claude-haiku-4-5", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("chat", Tag(span, LlmTelemetry.OperationName));
        Assert.Equal("anthropic", Tag(span, LlmTelemetry.ProviderName));
        Assert.Equal("1400", Tag(span, LlmTelemetry.UsageInputTokens));
        Assert.Equal("200", Tag(span, LlmTelemetry.UsageOutputTokens));
        Assert.Equal("300", Tag(span, LlmTelemetry.UsageCacheReadInputTokens));
        Assert.Equal("100", Tag(span, LlmTelemetry.UsageCacheWriteInputTokens));
        Assert.Equal(["end_turn"], Assert.IsType<string[]>(span.GetTagItem(LlmTelemetry.ResponseFinishReasons)));
        Assert.Equal("off", Tag(span, LlmTelemetry.CacheStatus));
        Assert.Equal("extract-003", Tag(span, LlmTelemetry.PromptVersion));
        Assert.Equal("rep-1", Tag(span, LlmTelemetry.ReplicateSalt));
        Assert.Equal(response.Cost!.AmountUsd!.Value.ToString("F8", System.Globalization.CultureInfo.InvariantCulture), Tag(span, LlmTelemetry.CostUsd));
        Assert.Matches(@"^\d+\.\d{8}$", Tag(span, LlmTelemetry.CostUsd));
        Assert.Equal(Table.Version, Tag(span, LlmTelemetry.PricingVersion));
    }

    [Fact]
    public async Task An_unpriced_model_with_zero_usage_has_zero_tokens_a_cost_warning_and_no_cost_attribute()
    {
        using var scope = new SpanScope();
        var inner = new CountingGateway(_ => Answer(LlmUsage.Zero, model: "claude-unpriced-9"));
        var gateway = new TracingLlmGateway(new CostAccountingLlmGateway(inner, Table));

        await gateway.CompleteAsync(Request("claude-unpriced-9"), Ct);

        var span = Assert.Single(scope.Spans);
        Assert.Equal("0", Tag(span, LlmTelemetry.UsageInputTokens));
        Assert.Equal("0", Tag(span, LlmTelemetry.UsageOutputTokens));
        Assert.Equal("0", Tag(span, LlmTelemetry.UsageCacheReadInputTokens));
        Assert.Equal("0", Tag(span, LlmTelemetry.UsageCacheWriteInputTokens));
        Assert.NotEmpty(Tag(span, LlmTelemetry.CostWarning));
        Assert.DoesNotContain(span.TagObjects, t => t.Key == LlmTelemetry.CostUsd);
    }

    [Fact]
    public async Task No_prompt_answer_schema_document_or_base64_reaches_a_name_a_tag_or_an_event()
    {
        const string promptSentinel = "SENTINEL-PROMPT-7731";
        const string answerSentinel = "SENTINEL-ANSWER-7731";
        const string schemaSentinel = "SENTINEL-SCHEMA-7731";
        const string followUpSentinel = "SENTINEL-FOLLOWUP-7731";
        const string detailSentinel = "SENTINEL-DETAIL-7731";
        var documentBytes = Encoding.ASCII.GetBytes("%PDF-1.4 SENTINEL-DOCUMENT-7731 body");
        var base64 = Convert.ToBase64String(documentBytes);

        using var scope = new SpanScope();
        var inner = new CountingGateway(_ => Answer(text: answerSentinel) with { StopDetail = detailSentinel });
        var gateway = new TracingLlmGateway(new CostAccountingLlmGateway(inner, Table));
        var request = new LlmRequest(
            "claude-haiku-4-5",
            1024,
            promptSentinel,
            new LlmDocument("application/pdf", documentBytes),
            "{\"title\":\"" + schemaSentinel + "\"}")
        {
            FollowUps = [new LlmTurn(LlmTurnRole.Assistant, answerSentinel), new LlmTurn(LlmTurnRole.User, followUpSentinel)],
        };

        await gateway.CompleteAsync(request, Ct);

        var span = Assert.Single(scope.Spans);
        var everything = new StringBuilder(span.DisplayName).Append(span.OperationName);
        foreach (var tag in span.TagObjects)
        {
            everything.Append(tag.Key).Append('=');
            everything.Append(tag.Value is string[] array ? string.Join(',', array) : Convert.ToString(tag.Value));
        }

        var text = everything.ToString();
        foreach (var sentinel in new[] { "SENTINEL-", promptSentinel, answerSentinel, schemaSentinel, followUpSentinel, detailSentinel, base64 })
        {
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
        }

        Assert.Empty(span.Events);
        Assert.Null(span.StatusDescription);
    }

    [Fact]
    public async Task A_gateway_failure_is_one_error_span_with_the_snake_case_kind_and_the_same_exception_is_rethrown()
    {
        using var scope = new SpanScope();
        var thrown = new LlmGatewayException(LlmFailureKind.Overloaded, "provider overloaded SENTINEL-MESSAGE", 529, "req_1");
        var gateway = new TracingLlmGateway(new ThrowingGateway(thrown));

        var caught = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

        Assert.Same(thrown, caught);
        var span = Assert.Single(scope.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("overloaded", Tag(span, LlmTelemetry.ErrorType));
        Assert.Empty(span.Events);
        Assert.DoesNotContain("SENTINEL-MESSAGE", span.StatusDescription ?? string.Empty, StringComparison.Ordinal);
        AssertAllowlisted(span);
    }

    [Fact]
    public async Task A_cache_hit_and_the_miss_that_stored_it_are_two_separate_spans()
    {
        await WithTempDirectory(async dir =>
        {
            using var scope = new SpanScope();
            var inner = new CountingGateway(_ => Answer());
            var gateway = new TracingLlmGateway(Caching(dir, new CostAccountingLlmGateway(inner, Table), LlmCacheMode.ReadWrite));

            await gateway.CompleteAsync(Request(), Ct);
            await gateway.CompleteAsync(Request(), Ct);

            Assert.Equal(1, inner.Calls);
            var spans = scope.Spans;
            Assert.Equal(2, spans.Length);
            Assert.Equal("miss", Tag(spans[0], LlmTelemetry.CacheStatus));
            Assert.Equal("1", Tag(spans[0], LlmTelemetry.HttpAttempts));
            Assert.Equal("hit", Tag(spans[1], LlmTelemetry.CacheStatus));
            Assert.Equal("0", Tag(spans[1], LlmTelemetry.HttpAttempts));
            Assert.Equal("read_write", Tag(spans[1], LlmTelemetry.CacheMode));
            Assert.All(spans, AssertAllowlisted);
        });
    }

    [Fact]
    public async Task A_read_only_miss_is_an_error_span_with_the_cache_miss_kind()
    {
        await WithTempDirectory(async dir =>
        {
            using var scope = new SpanScope();
            var inner = new CountingGateway(_ => Answer());
            var gateway = new TracingLlmGateway(Caching(dir, inner, LlmCacheMode.ReadOnly));

            var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request(), Ct));

            Assert.Equal(LlmFailureKind.CacheMiss, ex.Kind);
            Assert.Equal(0, inner.Calls);
            var span = Assert.Single(scope.Spans);
            Assert.Equal(ActivityStatusCode.Error, span.Status);
            Assert.Equal("cache_miss", Tag(span, LlmTelemetry.ErrorType));
        });
    }

    [Fact]
    public async Task Two_529s_then_a_200_through_the_full_chain_is_one_span_with_three_http_attempts()
    {
        await WithTempDirectory(async dir =>
        {
            using var scope = new SpanScope();
            var handler = new RecordingHandler(
                ((HttpStatusCode)529, Overloaded),
                ((HttpStatusCode)529, Overloaded),
                (HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
            using var anthropic = new AnthropicLlmGateway(
                new AnthropicLlmGatewayOptions(
                    AnthropicGatewayTests.DummyKey, new Uri("http://anthropic.invalid"), TimeSpan.FromSeconds(30), 2),
                handler);
            var gateway = new TracingLlmGateway(
                Caching(dir, new CostAccountingLlmGateway(anthropic, Table), LlmCacheMode.ReadWrite));

            await gateway.CompleteAsync(Request(), Ct);

            Assert.Equal(3, handler.Attempts);
            var span = Assert.Single(scope.Spans);
            Assert.Equal("3", Tag(span, LlmTelemetry.HttpAttempts));
            Assert.Equal("miss", Tag(span, LlmTelemetry.CacheStatus));
            Assert.Equal(ActivityStatusCode.Unset, span.Status);
        });
    }

    [Fact]
    public async Task Sequential_calls_under_one_parent_share_its_trace_and_span_ids_and_are_ordered_by_start()
    {
        using var scope = new SpanScope();
        var gateway = new TracingLlmGateway(new CostAccountingLlmGateway(new CountingGateway(_ => Answer()), Table));

        foreach (var salt in new[] { "a", "b", "c" })
        {
            await gateway.CompleteAsync(Request(salt: salt), Ct);
            await Task.Delay(2, Ct);
        }

        var spans = scope.Spans;
        Assert.Equal(3, spans.Length);
        Assert.All(spans, span => Assert.Equal(scope.Parent.TraceId, span.TraceId));
        Assert.All(spans, span => Assert.Equal(scope.Parent.SpanId, span.ParentSpanId));
        Assert.Equal(["a", "b", "c"], spans.Select(s => Tag(s, LlmTelemetry.ReplicateSalt)).ToArray());
    }

    private sealed class ThrowingGateway(Exception exception) : ILlmGateway
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
