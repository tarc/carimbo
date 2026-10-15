using System.Net;
using System.Text;
using System.Text.Json;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

/// <summary>
/// One retry owner (D-28, LLM-01, success criterion 3): through the composed chain Caching over CostAccounting
/// over the Anthropic adapter, the SDK's MaxRetries is the only retry policy, so a transient provider failure is
/// attempted 1 + MaxRetries times and never (1 + N) x (1 + M). A failing fake transport stands in for the provider.
/// </summary>
public class RetryOwnershipTests
{
    private const string Overloaded = """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";

    private const string BadRequest = """{"type":"error","error":{"type":"invalid_request_error","message":"bad"}}""";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic retry ownership bytes");

    private static readonly LlmPricingTable Table = LlmPricingTable.LoadEmbedded();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static LlmRequest Request() =>
        new("claude-haiku-4-5", 1024, "Extract the invoice.", new LlmDocument("application/pdf", Pdf), "{\"type\":\"object\"}");

    private static CachingLlmGateway Chain(RecordingHandler handler, string dir, int maxRetries, out AnthropicLlmGateway anthropic)
    {
        anthropic = new AnthropicLlmGateway(
            new AnthropicLlmGatewayOptions(
                AnthropicGatewayTests.DummyKey, new Uri("http://anthropic.invalid"), TimeSpan.FromSeconds(30), maxRetries),
            handler);
        return new CachingLlmGateway(
            new CostAccountingLlmGateway(anthropic, Table),
            new LlmCacheStore(dir),
            LlmCacheMode.ReadWrite,
            TimeProvider.System);
    }

    private static async Task WithTempDirectory(Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("carimbo-retry-test-").FullName;
        try
        {
            await body(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_persistent_529_is_attempted_one_plus_max_retries_times_through_the_chain_and_caches_nothing()
    {
        await WithTempDirectory(async dir =>
        {
            var handler = new RecordingHandler(((HttpStatusCode)529, Overloaded));
            var chain = Chain(handler, dir, maxRetries: 2, out var anthropic);
            using var _ = anthropic;

            var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => chain.CompleteAsync(Request(), Ct));

            Assert.Equal(LlmFailureKind.Overloaded, ex.Kind);
            Assert.Equal(3, handler.Attempts);
            Assert.Empty(Directory.GetFiles(dir, "*.json"));
        });
    }

    [Fact]
    public async Task Two_529s_then_a_200_is_one_cached_response_with_three_attempts_and_its_replay_makes_no_attempt()
    {
        await WithTempDirectory(async dir =>
        {
            var handler = new RecordingHandler(
                ((HttpStatusCode)529, Overloaded),
                ((HttpStatusCode)529, Overloaded),
                (HttpStatusCode.OK, Fixture("messages-haiku-end-turn.json")));
            var chain = Chain(handler, dir, maxRetries: 2, out var anthropic);
            using var _ = anthropic;

            var fresh = await chain.CompleteAsync(Request(), Ct);

            Assert.Equal(3, fresh.HttpAttempts);
            Assert.NotNull(fresh.Cache);
            Assert.True(fresh.Cache.Stored);
            Assert.Equal(3, handler.Attempts);

            var entry = Assert.Single(Directory.GetFiles(dir, "*.json"));
            using (var document = JsonDocument.Parse(File.ReadAllBytes(entry)))
            {
                Assert.Equal(3, document.RootElement.GetProperty("response").GetProperty("original_http_attempts").GetInt32());
            }

            var replay = await chain.CompleteAsync(Request(), Ct);

            Assert.Equal(0, replay.HttpAttempts);
            Assert.Equal(LlmCacheStatus.Hit, replay.Cache!.Status);
            Assert.Equal(3, handler.Attempts);
        });
    }

    [Fact]
    public async Task A_400_through_the_chain_is_attempted_once_and_maps_to_bad_request()
    {
        await WithTempDirectory(async dir =>
        {
            var handler = new RecordingHandler((HttpStatusCode.BadRequest, BadRequest));
            var chain = Chain(handler, dir, maxRetries: 2, out var anthropic);
            using var _ = anthropic;

            var ex = await Assert.ThrowsAsync<LlmGatewayException>(() => chain.CompleteAsync(Request(), Ct));

            Assert.Equal(LlmFailureKind.BadRequest, ex.Kind);
            Assert.Equal(1, handler.Attempts);
            Assert.Empty(Directory.GetFiles(dir, "*.json"));
        });
    }
}
