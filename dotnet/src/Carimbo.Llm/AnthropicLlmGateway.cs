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
/// <param name="MaxRetries">
/// Retries after the first attempt, so a call makes at most 1 + MaxRetries HTTP attempts. This is the only retry
/// policy there is (D-28): the SDK retries 408, 409, 429, 5xx (529 included) and connection errors with exponential
/// backoff, honouring <c>retry-after</c>, <c>retry-after-ms</c> and <c>x-should-retry</c>; other 4xx are attempted once.
/// 0 disables retries and also ignores <c>x-should-retry: true</c>.
/// </param>
public sealed record AnthropicLlmGatewayOptions(string ApiKey, Uri? BaseUrl, TimeSpan Timeout, int MaxRetries)
{
    // The record's generated ToString would print the key.
    public override string ToString() => $"AnthropicLlmGatewayOptions {{ BaseUrl = {BaseUrl}, Timeout = {Timeout}, MaxRetries = {MaxRetries} }}";
}

/// <summary>
/// <see cref="ILlmGateway"/> over the official Anthropic SDK, used directly (D-21). It sends the committed
/// model-facing schema verbatim as <c>output_config.format</c>, the PDF (when there is one) as a base64 document
/// block and the prompt as a text block, plus optional follow-up turns. An agent step adds a system prompt, tool
/// definitions, the model's own blocks echoed unchanged (thinking signatures included) and tool_result blocks.
/// Nothing else is sent: no sampling, tool-choice or thinking request fields. Cache-control is set only on the
/// document block, and only when the request asks for it.
/// Provider types stay inside this class; callers see only the Carimbo records.
/// <para>
/// Retry ownership (D-21, D-28): the SDK's <see cref="AnthropicLlmGatewayOptions.MaxRetries"/> is the single retry
/// policy. The attempt counter below is visibility only. Never wrap this gateway in another retrying layer (Polly,
/// Microsoft.Extensions.Http.Resilience, a hand-written handler): attempts would multiply to (1 + N) x (1 + M) and
/// each one may be billed. Only transport and provider failures are retried; a refused or truncated answer is a
/// 200 and stays a typed outcome for the repair loop, never a retry.
/// </para>
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
        if (request.Document is { } document && document.MediaType != "application/pdf")
        {
            throw new ArgumentException(
                $"Only application/pdf documents are supported, not '{document.MediaType}'.",
                nameof(request));
        }

        ValidateTools(request);
        ValidateFollowUps(request);

        // The schema string is parsed, not rewritten: the SDK serialises the same members and values back out.
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.OutputSchemaJson)
            ?? throw new ArgumentException("The output schema is not a JSON object.", nameof(request));

        var first = new List<ContentBlockParam>();
        if (request.Document is { } pdfDocument)
        {
            var source = new Base64PdfSource { Data = Convert.ToBase64String(pdfDocument.Content.Span) };
            // The property is left unset, not set to null, when caching is off: the SDK would serialise a null member
            // as "cache_control": null, which is not the Phase 1 body.
            first.Add(request.CacheDocument
                ? new DocumentBlockParam { Source = source, CacheControl = new CacheControlEphemeral() }
                : new DocumentBlockParam { Source = source });
        }

        first.Add(new TextBlockParam { Text = request.Prompt });

        var messages = new List<MessageParam> { new() { Role = Role.User, Content = first } };
        foreach (var turn in request.FollowUps)
        {
            messages.Add(new MessageParam
            {
                Role = turn.Role == LlmTurnRole.Assistant ? Role.Assistant : Role.User,
                Content = MapTurn(turn),
            });
        }

        var outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } };

        // System and Tools are left unset, not set to null or empty, when the request has none: the SDK would
        // serialise a null member as "system": null and an empty list as "tools": [].
        var tools = request.Tools.Select(MapTool).ToList();
        return (request.SystemPrompt, tools.Count) switch
        {
            (null, 0) => new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                OutputConfig = outputConfig,
                Messages = messages,
            },
            (null, _) => new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                OutputConfig = outputConfig,
                Messages = messages,
                Tools = tools,
            },
            (var system, 0) => new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                OutputConfig = outputConfig,
                Messages = messages,
                System = system,
            },
            (var system, _) => new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                OutputConfig = outputConfig,
                Messages = messages,
                System = system,
                Tools = tools,
            },
        };
    }

    // The SDK's InputSchema models only type, properties and required; ValidateTools refuses anything else
    // before this runs, because the SDK would drop it silently.
    private static ToolUnion MapTool(LlmToolDefinition tool)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tool.InputSchemaJson)
            ?? throw new ArgumentException($"The input schema of tool '{tool.Name}' is not a JSON object.");
        var properties = schema.TryGetValue("properties", out var props)
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(props.GetRawText())
            : null;
        var required = schema.TryGetValue("required", out var req)
            ? JsonSerializer.Deserialize<List<string>>(req.GetRawText())
            : null;

        var inputSchema = (properties, required) switch
        {
            (null, null) => new InputSchema(),
            (var p, null) => new InputSchema { Properties = p },
            (null, var r) => new InputSchema { Required = r },
            (var p, var r) => new InputSchema { Properties = p, Required = r },
        };
        return new Tool { Name = tool.Name, Description = tool.Description, InputSchema = inputSchema };
    }

    private static List<ContentBlockParam> MapTurn(LlmTurn turn)
    {
        var blocks = new List<ContentBlockParam>();
        if (turn.Role == LlmTurnRole.Assistant && turn.Blocks.Count > 0)
        {
            foreach (var block in turn.Blocks)
            {
                switch (block.Kind)
                {
                    case LlmContentKind.Thinking:
                        blocks.Add(new ThinkingBlockParam { Thinking = block.Text ?? string.Empty, Signature = block.Signature ?? string.Empty });
                        break;
                    case LlmContentKind.RedactedThinking:
                        blocks.Add(new RedactedThinkingBlockParam { Data = block.Data ?? string.Empty });
                        break;
                    case LlmContentKind.Text:
                        // The API rejects empty text blocks.
                        if (!string.IsNullOrEmpty(block.Text))
                        {
                            blocks.Add(new TextBlockParam { Text = block.Text });
                        }

                        break;
                    case LlmContentKind.ToolUse:
                        var call = block.ToolCall!;
                        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.InputJson)
                            ?? throw new ArgumentException($"The input of tool call '{call.Id}' is not a JSON object.");
                        blocks.Add(new ToolUseBlockParam { ID = call.Id, Name = call.Name, Input = input });
                        break;
                }
            }

            return blocks;
        }

        if (turn.Role == LlmTurnRole.User && turn.ToolResults.Count > 0)
        {
            foreach (var result in turn.ToolResults)
            {
                // is_error is set only when true: the SDK would write "is_error": false for a set property.
                blocks.Add(result.IsError
                    ? new ToolResultBlockParam { ToolUseID = result.ToolUseId, Content = result.Content, IsError = true }
                    : new ToolResultBlockParam { ToolUseID = result.ToolUseId, Content = result.Content });
            }

            if (!string.IsNullOrEmpty(turn.Text))
            {
                blocks.Add(new TextBlockParam { Text = turn.Text });
            }

            return blocks;
        }

        blocks.Add(new TextBlockParam { Text = turn.Text });
        return blocks;
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

        ValidateToolTurns(request);
    }

    // Every tool_use of an assistant turn must be answered exactly once, in order, by the next user turn, and
    // nothing else may carry tool results. Messages name turn indexes only, never content.
    private static void ValidateToolTurns(LlmRequest request)
    {
        var turns = request.FollowUps;
        IReadOnlyList<string> pending = [];
        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            if (turn.Role == LlmTurnRole.Assistant)
            {
                if (turn.ToolResults.Count > 0)
                {
                    throw new ArgumentException($"FollowUps turn {i} is an assistant turn and cannot carry tool results.", nameof(request));
                }

                var sendable = 0;
                var ids = new List<string>();
                foreach (var block in turn.Blocks)
                {
                    switch (block.Kind)
                    {
                        case LlmContentKind.Text:
                            sendable += string.IsNullOrEmpty(block.Text) ? 0 : 1;
                            break;
                        case LlmContentKind.ToolUse:
                            var call = block.ToolCall
                                ?? throw new ArgumentException($"FollowUps turn {i} has a tool_use block without a tool call.", nameof(request));
                            RequireJsonObject(call.InputJson, $"FollowUps turn {i} tool call {ids.Count + 1} input", nameof(request));
                            ids.Add(call.Id);
                            sendable++;
                            break;
                        default:
                            sendable++;
                            break;
                    }
                }

                if (turn.Blocks.Count > 0 && sendable == 0)
                {
                    throw new ArgumentException($"FollowUps turn {i} has blocks but none can be sent (empty text only).", nameof(request));
                }

                pending = ids;
                continue;
            }

            if (turn.Blocks.Count > 0)
            {
                throw new ArgumentException($"FollowUps turn {i} is a user turn and cannot carry assistant blocks.", nameof(request));
            }

            if (!turn.ToolResults.Select(r => r.ToolUseId).SequenceEqual(pending, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"FollowUps turn {i} must answer exactly the {pending.Count} tool_use block(s) of turn {i - 1}, in order; it carries {turn.ToolResults.Count} tool result(s) that do not match.",
                    nameof(request));
            }

            pending = [];
        }
    }

    // The SDK's InputSchema models only type, properties and required and silently drops every other member, so
    // a tool schema with anything else is refused here instead of being sent as a different schema.
    private static void ValidateTools(LlmRequest request)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in request.Tools)
        {
            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException($"Tool '{tool.Name}' is listed more than once.", nameof(request));
            }

            var label = $"The input schema of tool '{tool.Name}'";
            var schema = RequireJsonObject(tool.InputSchemaJson, label, nameof(request));
            foreach (var member in schema.EnumerateObject())
            {
                if (member.Name is not ("type" or "properties" or "required"))
                {
                    throw new ArgumentException(
                        $"{label} has the member '{member.Name}'; only type, properties and required are supported.",
                        nameof(request));
                }
            }

            if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object")
            {
                throw new ArgumentException($"{label} must have type \"object\".", nameof(request));
            }

            if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"{label} has properties that are not an object.", nameof(request));
            }

            if (schema.TryGetProperty("required", out var required)
                && (required.ValueKind != JsonValueKind.Array || required.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.String)))
            {
                throw new ArgumentException($"{label} has required that is not an array of strings.", nameof(request));
            }
        }
    }

    private static JsonElement RequireJsonObject(string json, string label, string paramName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement.Clone();
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"{label} is not a JSON object.", paramName);
            }

            return element;
        }
        catch (JsonException)
        {
            throw new ArgumentException($"{label} is not valid JSON.", paramName);
        }
    }

    private static LlmResponse Map(LlmRequest request, Message message, TimeSpan latency, int attempts)
    {
        var text = new StringBuilder();
        var blocks = new List<LlmContentBlock>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                text.Append(textBlock.Text);
                blocks.Add(LlmContentBlock.OfText(textBlock.Text));
            }
            else if (block.TryPickThinking(out var thinking))
            {
                blocks.Add(LlmContentBlock.OfThinking(thinking.Thinking, thinking.Signature));
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                blocks.Add(LlmContentBlock.OfRedactedThinking(redacted.Data));
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                blocks.Add(LlmContentBlock.OfToolUse(
                    new LlmToolCall(toolUse.ID, toolUse.Name, JsonSerializer.Serialize(toolUse.Input))));
            }
        }

        var stopReason = message.StopReason?.Raw() switch
        {
            "end_turn" => LlmStopReason.EndTurn,
            "max_tokens" => LlmStopReason.MaxTokens,
            "refusal" => LlmStopReason.Refusal,
            "stop_sequence" => LlmStopReason.StopSequence,
            "tool_use" => LlmStopReason.ToolUse,
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
            attempts)
        {
            Blocks = blocks,
        };
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
