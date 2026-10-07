namespace Carimbo.Llm;

public sealed record AnthropicLlmGatewayOptions(string ApiKey, Uri? BaseUrl, TimeSpan Timeout, int MaxRetries);

// RED stub: replaced by the real adapter in the next commit.
public sealed class AnthropicLlmGateway(AnthropicLlmGatewayOptions options, HttpMessageHandler? innerHandler = null) : ILlmGateway, IDisposable
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{options.MaxRetries}{innerHandler}");

    public void Dispose()
    {
    }
}
