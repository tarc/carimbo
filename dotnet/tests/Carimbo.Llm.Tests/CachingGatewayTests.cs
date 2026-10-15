using System.Text;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

/// <summary>
/// The response cache (LLM-04, LLM-05, D-19) proven through the real decorator stack: the key, the file store
/// and the mode matrix, with a counting double standing in for the provider.
/// </summary>
public class CachingGatewayTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic cache test bytes");

    private static readonly LlmPricingTable Table = LlmPricingTable.LoadEmbedded();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmRequest Request(string? salt = null) =>
        new("claude-haiku-4-5", 1024, "Extract the invoice.", new LlmDocument("application/pdf", Pdf), "{\"type\":\"object\"}")
        {
            PromptVersion = "extract-003",
            ReplicateSalt = salt,
        };

    private static LlmResponse Answer(string text = "{\"ok\":true}", LlmStopReason stop = LlmStopReason.EndTurn) =>
        new(
            text,
            stop,
            null,
            new LlmUsage(1200, 340, 50, 0, 0),
            "claude-haiku-4-5",
            "claude-haiku-4-5",
            "msg_cache_1",
            TimeSpan.FromMilliseconds(1234),
            1);

    private static string[] EntryFiles(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : [];

    private static async Task WithTempDirectory(Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("carimbo-cache-test-").FullName;
        try
        {
            await body(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static CachingLlmGateway Gateway(
        string dir, ILlmGateway inner, LlmCacheMode mode = LlmCacheMode.ReadOnly, bool priced = false) =>
        new(
            priced ? new CostAccountingLlmGateway(inner, Table) : inner,
            new LlmCacheStore(dir),
            mode,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero)));

    [Fact]
    public async Task A_read_write_answer_is_replayed_by_a_fresh_read_only_gateway_with_zero_model_calls()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var request = Request();

            var writer = Gateway(dir, inner, LlmCacheMode.ReadWrite, priced: true);
            var fresh = await writer.CompleteAsync(request, Ct);

            var reader = Gateway(dir, inner, priced: true);
            var replay = await reader.CompleteAsync(request, Ct);

            Assert.Equal(1, inner.Calls);
            var key = LlmCacheKey.Compute(request);

            Assert.Equal(1, fresh.HttpAttempts);
            Assert.Equal(new LlmCacheInfo(LlmCacheMode.ReadWrite, LlmCacheStatus.Miss, key, true), fresh.Cache);

            Assert.Equal(0, replay.HttpAttempts);
            Assert.Equal(TimeSpan.FromMilliseconds(1234), replay.Latency);
            Assert.Equal(fresh.Usage, replay.Usage);
            Assert.NotNull(fresh.Cost);
            Assert.NotNull(replay.Cost);
            Assert.Equal(fresh.Cost.AmountUsd, replay.Cost.AmountUsd);
            Assert.Equal(fresh.Cost.PricingVersion, replay.Cost.PricingVersion);
            Assert.Equal(fresh.Text, replay.Text);
            Assert.Equal(fresh.StopReason, replay.StopReason);
            Assert.Equal(fresh.ModelRequested, replay.ModelRequested);
            Assert.Equal(fresh.ModelReturned, replay.ModelReturned);
            Assert.Equal(fresh.ProviderMessageId, replay.ProviderMessageId);
            Assert.Equal(new LlmCacheInfo(LlmCacheMode.ReadOnly, LlmCacheStatus.Hit, key, false), replay.Cache);

            var file = Assert.Single(EntryFiles(dir));
            Assert.Equal(key + ".json", Path.GetFileName(file));
            Assert.Matches("^[0-9a-f]{64}$", key);
        });
    }

    [Fact]
    public async Task A_read_only_gateway_over_an_empty_directory_raises_cache_miss_without_calling_the_provider()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var reader = Gateway(dir, inner);

            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => reader.CompleteAsync(Request(), Ct));

            Assert.Equal(LlmFailureKind.CacheMiss, thrown.Kind);
            Assert.Equal(0, inner.Calls);
        });
    }
}

/// <summary>A provider double that counts calls (thread-safe) and answers from a function or throws.</summary>
internal sealed class CountingGateway(Func<LlmRequest, LlmResponse> respond) : ILlmGateway
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult(respond(request));
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
