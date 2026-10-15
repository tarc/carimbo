namespace Carimbo.Llm;

/// <summary>
/// The request-hash response cache (LLM-04, LLM-05, D-19) as a decorator over any <see cref="ILlmGateway"/>.
/// Compose it outside cost accounting so the stored answer carries its original cost; a hit then replays
/// latency, usage and cost as recorded while reporting zero HTTP attempts, so replay is never mistaken for spend.
/// Only an <see cref="LlmStopReason.EndTurn"/> answer is eligible to be stored: a truncated, refused or otherwise
/// unusual answer must never be replayed as if it were the model's answer.
/// </summary>
public sealed class CachingLlmGateway(
    ILlmGateway inner,
    LlmCacheStore store,
    LlmCacheMode defaultMode,
    TimeProvider timeProvider) : ILlmGateway
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var mode = request.CacheMode ?? defaultMode;
        if (mode == LlmCacheMode.Off)
        {
            return await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var key = LlmCacheKey.Compute(request);

        if (mode != LlmCacheMode.Refresh && store.TryRead(key, out var entry))
        {
            return entry.Response with
            {
                HttpAttempts = 0,
                Cache = new LlmCacheInfo(mode, LlmCacheStatus.Hit, key, Stored: false),
            };
        }

        if (mode == LlmCacheMode.ReadOnly)
        {
            throw new LlmGatewayException(
                LlmFailureKind.CacheMiss,
                $"No cache entry for key {key}; read_only makes no provider call.");
        }

        var response = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        var stored = TryStore(key, request, response);
        var status = mode == LlmCacheMode.Refresh ? LlmCacheStatus.Refresh : LlmCacheStatus.Miss;
        return response with { Cache = new LlmCacheInfo(mode, status, key, stored) };
    }

    private bool TryStore(string key, LlmRequest request, LlmResponse response)
    {
        if (response.StopReason != LlmStopReason.EndTurn)
        {
            return false;
        }

        try
        {
            // The entry holds the response as the provider call produced it; the cache report is per call.
            store.Write(new LlmCacheEntry(key, LlmCacheKeyFields.From(request), timeProvider.GetUtcNow(), response with { Cache = null }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A paid answer is not thrown away because the cache directory misbehaved; Stored false tells the caller.
            return false;
        }
    }
}
