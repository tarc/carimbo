using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Llm;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Carimbo.Agent;

/// <summary>Opens a tool session per run. A session is never reused across runs: the Tools host is stateless.</summary>
public interface IAgentToolbox
{
    /// <exception cref="AgentToolException">The host is unreachable, answers badly or serves a tool that is not read-only.</exception>
    Task<IAgentToolSession> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>The tools of one run and the way to call them.</summary>
public interface IAgentToolSession : IAsyncDisposable
{
    /// <summary>The listed tools, in the order the host served them.</summary>
    IReadOnlyList<LlmToolDefinition> Tools { get; }

    /// <summary>Calls one tool. A tool that ran and reported an error is a result with <see cref="AgentToolResult.IsError"/>.</summary>
    /// <exception cref="AgentToolException">The call could not be made or did not complete.</exception>
    Task<AgentToolResult> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken);
}

/// <summary>What a tool call returned. <see cref="ErrorKind"/> and <see cref="ErrorMessage"/> are set only for an error.</summary>
public sealed record AgentToolResult(bool IsError, string Content, string? ErrorKind, string? ErrorMessage);

/// <summary>
/// A tool call or listing that failed before it returned a result. <see cref="ErrorKind"/> is one of
/// <c>protocol_error</c>, <c>transport_error</c>, <c>timeout</c>, <c>not_read_only</c>, <c>invalid_arguments</c>
/// or <c>unknown_tool</c>. The message never carries a path, a key or a response body.
/// </summary>
public sealed class AgentToolException(string errorKind, string message) : Exception(message)
{
    public string ErrorKind { get; } = errorKind;
}

/// <summary>
/// The MCP client side of the Tools host (AGT-01, D-35): one <see cref="McpClient"/> per run over Streamable HTTP,
/// listing the tools into Carimbo records. It refuses a server that lists any tool whose ReadOnlyHint is not true,
/// so the agent can only ever call read-only tools (D-09, T-03.1-25).
/// </summary>
public sealed class McpAgentToolbox(Uri endpoint, HttpClient? httpClient = null, TimeSpan? callTimeout = null) : IAgentToolbox
{
    /// <summary>The longest tool error text kept on a result, in characters.</summary>
    public const int MaxErrorLength = 300;

    private static readonly string[] SchemaMembers = ["type", "properties", "required"];

    private readonly TimeSpan _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);

    public async Task<IAgentToolSession> ConnectAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_callTimeout);
        McpClient? client = null;
        try
        {
            var options = new HttpClientTransportOptions { Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp };
            var transport = httpClient is null
                ? new HttpClientTransport(options)
                : new HttpClientTransport(options, httpClient, ownsHttpClient: false);
            client = await McpClient.CreateAsync(transport, cancellationToken: linked.Token).ConfigureAwait(false);
            var listed = await client.ListToolsAsync(cancellationToken: linked.Token).ConfigureAwait(false);
            var definitions = new List<LlmToolDefinition>(listed.Count);
            foreach (var tool in listed)
            {
                if (tool.ProtocolTool.Annotations?.ReadOnlyHint != true)
                {
                    throw new AgentToolException("not_read_only", $"The tool {tool.Name} is not declared read-only.");
                }

                definitions.Add(new LlmToolDefinition(tool.Name, tool.Description ?? string.Empty, AdapterSchema(tool.JsonSchema)));
            }

            return new Session(client, definitions, _callTimeout);
        }
        catch (Exception ex)
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            var mapped = Map(ex, cancellationToken, "listing the tools");
            if (mapped is null)
            {
                throw;
            }

            throw mapped;
        }
    }

    /// <summary>
    /// Maps what an MCP call can throw to an <see cref="AgentToolException"/> with a fixed message (kinds, never
    /// bodies or paths). A cancellation requested by the caller maps to null, so it propagates.
    /// </summary>
    internal static AgentToolException? Map(Exception exception, CancellationToken caller, string action) => exception switch
    {
        AgentToolException known => known,
        OperationCanceledException when caller.IsCancellationRequested => null,
        OperationCanceledException => new AgentToolException("timeout", $"The Tools host did not finish {action} in time."),
        McpException => new AgentToolException("protocol_error", $"The Tools host answered with a protocol error while {action}."),
        HttpRequestException or IOException => new AgentToolException("transport_error", $"The Tools host could not be reached while {action}."),
        // A malformed JSON-RPC body or a session the client already closed: the host did not answer usably, which D-35 treats
        // like any other tool failure. ObjectDisposedException is an InvalidOperationException.
        JsonException or InvalidOperationException => new AgentToolException("protocol_error", $"The Tools host answered unusably while {action}."),
        _ => null,
    };

    // The Anthropic adapter models only type, properties and required in a tool input schema and refuses anything
    // else, so the definition carries exactly those three members of the listed schema.
    private static string AdapterSchema(JsonElement listed)
    {
        var schema = new JsonObject();
        foreach (var member in SchemaMembers)
        {
            if (listed.TryGetProperty(member, out var value))
            {
                schema[member] = JsonNode.Parse(value.GetRawText());
            }
        }

        return schema.ToJsonString();
    }

    private sealed class Session(McpClient client, IReadOnlyList<LlmToolDefinition> tools, TimeSpan callTimeout) : IAgentToolSession
    {
        public IReadOnlyList<LlmToolDefinition> Tools { get; } = tools;

        public async Task<AgentToolResult> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            var arguments = ParseArguments(argumentsJson);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(callTimeout);
            CallToolResult result;
            try
            {
                result = await client.CallToolAsync(name, arguments, cancellationToken: linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (Map(ex, cancellationToken, "calling a tool") is { } mapped)
            {
                throw mapped;
            }

            var text = new StringBuilder();
            foreach (var block in result.Content.OfType<TextContentBlock>())
            {
                text.Append(block.Text);
            }

            return result.IsError == true
                ? new AgentToolResult(true, string.Empty, "is_error", Truncate(text.ToString()))
                : new AgentToolResult(false, text.ToString(), null, null);
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();

        private static Dictionary<string, object?> ParseArguments(string argumentsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new AgentToolException("invalid_arguments", "The tool arguments are not a JSON object.");
                }

                var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.Clone();
                }

                return arguments;
            }
            catch (JsonException)
            {
                throw new AgentToolException("invalid_arguments", "The tool arguments are not valid JSON.");
            }
        }

        private static string Truncate(string text) =>
            text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}
