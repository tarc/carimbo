using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Carimbo.Llm;

/// <summary>
/// The identity of a model request for the response cache (LLM-04, D-19): the lowercase SHA-256 hex of a
/// canonical JSON object. The object is written member by member in a fixed order, never by reflection, so the
/// key cannot drift when a property is added or reordered. Large inputs (prompt, schema, document, follow-up
/// turns, system prompt, tool descriptions and schemas, tool results) enter as their own SHA-256 so the key never
/// depends on, and the entry never stores, their content.
/// <para>
/// Two recipes coexist (D-37). A request with a document and nothing a tool conversation adds (no system prompt,
/// no tools, no follow-up carrying blocks or tool results) keeps the exact <see cref="Version"/> object, so every
/// key recorded in Phase 3 still matches. Any other request is keyed by <see cref="ToolConversationVersion"/>.
/// </para>
/// </summary>
public static class LlmCacheKey
{
    /// <summary>
    /// Bumped on purpose when the key recipe changes, so old entries stop matching visibly instead of silently.
    /// </summary>
    public const string Version = "llm-cache-key-1";

    /// <summary>The recipe for requests that carry a system prompt, tools, content blocks or tool results, or no document.</summary>
    public const string ToolConversationVersion = "llm-cache-key-2";

    /// <summary>Every <see cref="LlmRequest"/> property that is part of the key.</summary>
    public static IReadOnlyList<string> KeyedProperties { get; } =
    [
        nameof(LlmRequest.Model),
        nameof(LlmRequest.MaxTokens),
        nameof(LlmRequest.CacheDocument),
        nameof(LlmRequest.PromptVersion),
        nameof(LlmRequest.RepairPromptVersion),
        nameof(LlmRequest.Prompt),
        nameof(LlmRequest.OutputSchemaJson),
        nameof(LlmRequest.Document),
        nameof(LlmRequest.FollowUps),
        nameof(LlmRequest.ReplicateSalt),
        nameof(LlmRequest.SystemPrompt),
        nameof(LlmRequest.Tools),
    ];

    /// <summary>
    /// Every <see cref="LlmRequest"/> property that is deliberately not in the key. The cache mode chooses how a
    /// call is treated; it must never change which entry the call maps to.
    /// </summary>
    public static IReadOnlyList<string> ExcludedProperties { get; } = [nameof(LlmRequest.CacheMode)];

    /// <summary>The canonical JSON the key is the SHA-256 of.</summary>
    public static string CanonicalJson(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            if (UsesLegacyRecipe(request))
            {
                WriteLegacy(writer, request, request.Document!);
            }
            else
            {
                WriteToolConversation(writer, request);
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>True when the request is an extraction-shaped one, keyed by the unchanged Phase 3 recipe.</summary>
    private static bool UsesLegacyRecipe(LlmRequest request) =>
        request.Document is not null
        && request.SystemPrompt is null
        && request.Tools.Count == 0
        && request.FollowUps.All(turn => turn.Blocks.Count == 0 && turn.ToolResults.Count == 0);

    private static void WriteLegacy(Utf8JsonWriter writer, LlmRequest request, LlmDocument document)
    {
        writer.WriteStartObject();
        writer.WriteString("key_version", Version);
        writer.WriteString("model", request.Model);
        writer.WriteNumber("max_tokens", request.MaxTokens);
        writer.WriteBoolean("cache_document", request.CacheDocument);
        WriteNullableString(writer, "prompt_version", request.PromptVersion);
        WriteNullableString(writer, "repair_prompt_version", request.RepairPromptVersion);
        writer.WriteString("prompt_sha256", Sha256Hex(request.Prompt));
        writer.WriteString("schema_sha256", Sha256Hex(request.OutputSchemaJson));
        writer.WriteString("document_media_type", document.MediaType);
        writer.WriteString("document_sha256", Sha256Hex(document.Content.Span));
        writer.WriteStartArray("follow_ups");
        foreach (var turn in request.FollowUps)
        {
            writer.WriteStartObject();
            writer.WriteString("role", RoleName(turn));
            writer.WriteString("text_sha256", Sha256Hex(turn.Text));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("replicate_salt", request.ReplicateSalt ?? string.Empty);
        writer.WriteEndObject();
    }

    private static void WriteToolConversation(Utf8JsonWriter writer, LlmRequest request)
    {
        writer.WriteStartObject();
        writer.WriteString("key_version", ToolConversationVersion);
        writer.WriteString("model", request.Model);
        writer.WriteNumber("max_tokens", request.MaxTokens);
        writer.WriteBoolean("cache_document", request.CacheDocument);
        WriteNullableString(writer, "prompt_version", request.PromptVersion);
        WriteNullableString(writer, "repair_prompt_version", request.RepairPromptVersion);
        writer.WriteString("prompt_sha256", Sha256Hex(request.Prompt));
        WriteNullableHash(writer, "system_sha256", request.SystemPrompt);
        writer.WriteString("schema_sha256", Sha256Hex(request.OutputSchemaJson));
        if (request.Document is { } document)
        {
            writer.WriteString("document_media_type", document.MediaType);
            writer.WriteString("document_sha256", Sha256Hex(document.Content.Span));
        }
        else
        {
            writer.WriteNull("document_media_type");
            writer.WriteNull("document_sha256");
        }

        writer.WriteStartArray("tools");
        foreach (var tool in request.Tools)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("description_sha256", Sha256Hex(tool.Description));
            writer.WriteString("input_schema_sha256", Sha256Hex(tool.InputSchemaJson));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("follow_ups");
        foreach (var turn in request.FollowUps)
        {
            writer.WriteStartObject();
            writer.WriteString("role", RoleName(turn));
            writer.WriteString("text_sha256", Sha256Hex(turn.Text));

            writer.WriteStartArray("blocks");
            foreach (var block in turn.Blocks)
            {
                WriteBlock(writer, block);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("tool_results");
            foreach (var result in turn.ToolResults)
            {
                writer.WriteStartObject();
                writer.WriteString("tool_use_id", result.ToolUseId);
                writer.WriteString("content_sha256", Sha256Hex(result.Content));
                writer.WriteBoolean("is_error", result.IsError);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("replicate_salt", request.ReplicateSalt ?? string.Empty);
        writer.WriteEndObject();
    }

    private static void WriteBlock(Utf8JsonWriter writer, LlmContentBlock block)
    {
        if (block.Kind == LlmContentKind.ToolUse && block.ToolCall is null)
        {
            throw new ArgumentException("A tool_use block carries a tool call.", nameof(block));
        }

        var call = block.ToolCall;
        writer.WriteStartObject();
        writer.WriteString("kind", KindName(block.Kind));
        WriteNullableHash(writer, "text_sha256", block.Kind is LlmContentKind.Text or LlmContentKind.Thinking ? block.Text ?? string.Empty : null);
        WriteNullableHash(writer, "signature_sha256", block.Kind == LlmContentKind.Thinking ? block.Signature ?? string.Empty : null);
        WriteNullableHash(writer, "data_sha256", block.Kind == LlmContentKind.RedactedThinking ? block.Data ?? string.Empty : null);
        WriteNullableString(writer, "tool_use_id", block.Kind == LlmContentKind.ToolUse ? call!.Id : null);
        WriteNullableString(writer, "tool_name", block.Kind == LlmContentKind.ToolUse ? call!.Name : null);
        WriteNullableHash(writer, "input_sha256", block.Kind == LlmContentKind.ToolUse ? call!.InputJson : null);
        writer.WriteEndObject();
    }

    private static string RoleName(LlmTurn turn) => turn.Role == LlmTurnRole.Assistant ? "assistant" : "user";

    internal static string KindName(LlmContentKind kind) => kind switch
    {
        LlmContentKind.Text => "text",
        LlmContentKind.ToolUse => "tool_use",
        LlmContentKind.Thinking => "thinking",
        LlmContentKind.RedactedThinking => "redacted_thinking",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown content kind."),
    };

    /// <summary>The cache key: 64 lowercase hex characters.</summary>
    public static string Compute(LlmRequest request) => Sha256Hex(CanonicalJson(request));

    /// <summary>Lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="text"/>.</summary>
    internal static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    internal static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void WriteNullableHash(Utf8JsonWriter writer, string name, string? content)
    {
        if (content is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, Sha256Hex(content));
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
