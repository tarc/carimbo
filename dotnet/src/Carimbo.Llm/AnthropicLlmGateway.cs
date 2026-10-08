using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Carimbo.Llm;

/// <param name="ApiKey">The provider key, passed explicitly. The SDK's own environment default is never used.</param>
/// <param name="BaseUrl">Optional endpoint override (a proxy or a test double); null means the SDK default.</param>
/// <param name="Timeout">Per-attempt SDK timeout.</param>
/// <param name="MaxRetries">SDK retries. Phase 1 runs with 0 (D-21); the policy is owned in one place in Phase 3.</param>
public sealed record AnthropicLlmGatewayOptions(string ApiKey, Uri? BaseUrl, TimeSpan Timeout, int MaxRetries)
{
    // The record's generated ToString would print the key.
    public override string ToString() => $"AnthropicLlmGatewayOptions {{ BaseUrl = {BaseUrl}, Timeout = {Timeout}, MaxRetries = {MaxRetries} }}";
}

/// <summary>
/// <see cref="ILlmGateway"/> over the official Anthropic SDK, used directly (D-21). It sends the committed
/// model-facing schema verbatim as <c>output_config.format</c>, the PDF as a base64 document block and the
/// prompt as a text block, plus optional follow-up turns, and nothing else: no sampling, tool-choice or thinking
/// fields. Cache-control is set only on the document block, and only when the request asks for it.
/// Provider types stay inside this class; callers see only the Carimbo records.
/// </summary>
public sealed class AnthropicLlmGateway : ILlmGateway, IDisposable
{
    private readonly AsyncLocal<CallState?> current = new();
    private readonly AnthropicClient client;
    private readonly HttpClient httpClient;
    private readonly TimeSpan timeout;

    /// <param name="options">Key, endpoint, timeout and retry count.</param>
    /// <param name="innerHandler">
    /// The transport under the attempt counter. Null means a plain <see cref="HttpClientHandler"/>; tests pass a
    /// recording handler here.
    /// </param>
    public AnthropicLlmGateway(AnthropicLlmGatewayOptions options, HttpMessageHandler? innerHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey);

        timeout = options.Timeout;
        // The SDK owns the timeout; the HttpClient must not add a second, shorter-lived one.
        httpClient = new HttpClient(new AttemptCountingHandler(innerHandler ?? new HttpClientHandler(), current))
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        client = options.BaseUrl is null
            ? new AnthropicClient
            {
                ApiKey = options.ApiKey,
                HttpClient = httpClient,
                MaxRetries = options.MaxRetries,
                Timeout = options.Timeout,
            }
            : new AnthropicClient
            {
                ApiKey = options.ApiKey,
                BaseUrl = options.BaseUrl.ToString(),
                HttpClient = httpClient,
                MaxRetries = options.MaxRetries,
                Timeout = options.Timeout,
            };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = BuildParameters(request);

        var state = new CallState();
        current.Value = state;
        var watch = Stopwatch.StartNew();
        try
        {
            var message = await client.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
            watch.Stop();
            return Map(request, message, watch.Elapsed, state.Attempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (Classify(ex, state) is { } failure)
        {
            throw failure;
        }
        finally
        {
            current.Value = null;
        }
    }

    public void Dispose()
    {
        client.Dispose();
        httpClient.Dispose();
    }

    private static MessageCreateParams BuildParameters(LlmRequest request)
    {
        if (request.Document.MediaType != "application/pdf")
        {
            throw new ArgumentException(
                $"Only application/pdf documents are supported, not '{request.Document.MediaType}'.",
                nameof(request));
        }

        ValidateFollowUps(request);

        // The schema string is parsed, not rewritten: the SDK serialises the same members and values back out.
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.OutputSchemaJson)
            ?? throw new ArgumentException("The output schema is not a JSON object.", nameof(request));
        var source = new Base64PdfSource { Data = Convert.ToBase64String(request.Document.Content.Span) };
        // The property is left unset, not set to null, when caching is off: the SDK would serialise a null member
        // as "cache_control": null, which is not the Phase 1 body.
        var pdf = request.CacheDocument
            ? new DocumentBlockParam { Source = source, CacheControl = new CacheControlEphemeral() }
            : new DocumentBlockParam { Source = source };

        var messages = new List<MessageParam>
        {
            new()
            {
                Role = Role.User,
                Content = new List<ContentBlockParam> { pdf, new TextBlockParam { Text = request.Prompt } },
            },
        };
        foreach (var turn in request.FollowUps)
        {
            messages.Add(new MessageParam
            {
                Role = turn.Role == LlmTurnRole.Assistant ? Role.Assistant : Role.User,
                Content = new List<ContentBlockParam> { new TextBlockParam { Text = turn.Text } },
            });
        }

        return new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Messages = messages,
        };
    }

    // The first message is a user turn, so follow-ups run assistant, user, assistant, user, ... and must end on a
    // user turn: a trailing assistant turn is prefill, which is a 400 on current models.
    private static void ValidateFollowUps(LlmRequest request)
    {
        var turns = request.FollowUps;
        for (var i = 0; i < turns.Count; i++)
        {
            var expected = i % 2 == 0 ? LlmTurnRole.Assistant : LlmTurnRole.User;
            if (turns[i].Role != expected)
            {
                throw new ArgumentException(
                    $"FollowUps must alternate assistant then user; turn {i} is {turns[i].Role}.",
                    nameof(request));
            }
        }

        if (turns.Count % 2 != 0)
        {
            throw new ArgumentException(
                "FollowUps must end on a user turn: an assistant-final conversation is prefill, a 400 on current models.",
                nameof(request));
        }
    }

    private static LlmResponse Map(LlmRequest request, Message message, TimeSpan latency, int attempts)
    {
        var text = new StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text.Append(textBlock.Text);
            }
        }

        var stopReason = message.StopReason?.Raw() switch
        {
            "end_turn" => LlmStopReason.EndTurn,
            "max_tokens" => LlmStopReason.MaxTokens,
            "refusal" => LlmStopReason.Refusal,
            "stop_sequence" => LlmStopReason.StopSequence,
            _ => LlmStopReason.Other,
        };

        return new LlmResponse(
            text.ToString(),
            stopReason,
            StopDetail(message),
            MapUsage(message.Usage),
            request.Model,
            message.Model.Raw(),
            message.ID,
            latency,
            attempts);
    }

    private static string? StopDetail(Message message)
    {
        var details = message.StopDetails;
        if (details is null)
        {
            return null;
        }

        var category = details.Category?.Raw();
        var explanation = details.Explanation;
        return (category, explanation) switch
        {
            (not null, not null) => $"{category}: {explanation}",
            (not null, null) => category,
            (null, not null) => explanation,
            _ => null,
        };
    }

    // input_tokens is already the uncached remainder; cache tokens are separate classes, never folded in.
    private static LlmUsage MapUsage(Usage usage)
    {
        var created = usage.CacheCreationInputTokens ?? 0;
        var breakdown = usage.CacheCreation;
        var write5m = breakdown?.Ephemeral5mInputTokens;
        var write1h = breakdown?.Ephemeral1hInputTokens;
        if (write5m is null && write1h is null)
        {
            // No breakdown: everything written to the cache is a 5m write.
            (write5m, write1h) = (created, 0);
        }

        return new LlmUsage(
            usage.InputTokens,
            usage.OutputTokens,
            usage.CacheReadInputTokens ?? 0,
            write5m ?? 0,
            write1h ?? 0);
    }

    /// <summary>
    /// Maps a provider failure to a typed <see cref="LlmGatewayException"/>. The message is built from the
    /// exception type name and status only: never the response body, never the key. Returns null for
    /// exceptions that are not provider failures, which then propagate unchanged.
    /// </summary>
    private LlmGatewayException? Classify(Exception ex, CallState state)
    {
        switch (ex)
        {
            case AnthropicApiException api:
                var status = (int)api.StatusCode;
                return new LlmGatewayException(
                    KindOf(status),
                    $"{api.GetType().Name} status {status}",
                    status,
                    state.RequestId);
            case AnthropicIOException io when IsTimeout(io.InnerException):
            case OperationCanceledException or TimeoutException:
                return new LlmGatewayException(
                    LlmFailureKind.Timeout,
                    $"{ex.GetType().Name} after {timeout.TotalSeconds:0.##}s",
                    requestId: state.RequestId);
            case AnthropicIOException or HttpRequestException:
                return new LlmGatewayException(LlmFailureKind.Network, $"{ex.GetType().Name} network failure", requestId: state.RequestId);
            case AnthropicException:
                // The provider answered with something this SDK cannot use (for example an undecodable body).
                return new LlmGatewayException(LlmFailureKind.ServerError, $"{ex.GetType().Name} unusable response", requestId: state.RequestId);
            default:
                return null;
        }
    }

    private static bool IsTimeout(Exception? ex) => ex is OperationCanceledException or TimeoutException;

    private static LlmFailureKind KindOf(int status) => status switch
    {
        401 or 403 => LlmFailureKind.Auth,
        400 or 404 or 413 or 422 => LlmFailureKind.BadRequest,
        429 => LlmFailureKind.RateLimit,
        529 => LlmFailureKind.Overloaded,
        >= 500 => LlmFailureKind.ServerError,
        _ => LlmFailureKind.BadRequest,
    };

    /// <summary>Per-call state shared with the handler through an <see cref="AsyncLocal{T}"/>.</summary>
    private sealed class CallState
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public string? RequestId { get; set; }

        public void CountAttempt() => Interlocked.Increment(ref attempts);
    }

    /// <summary>
    /// Counts every HTTP attempt of the current call, including SDK retries the caller never sees, and keeps
    /// the last <c>request-id</c> response header. No Polly: this is visibility only.
    /// </summary>
    private sealed class AttemptCountingHandler(HttpMessageHandler inner, AsyncLocal<CallState?> current) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var state = current.Value;
            state?.CountAttempt();
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (state is not null && response.Headers.TryGetValues("request-id", out var values))
            {
                state.RequestId = values.FirstOrDefault();
            }

            return response;
        }
    }
}
