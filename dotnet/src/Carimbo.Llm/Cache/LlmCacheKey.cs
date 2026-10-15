using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Carimbo.Llm;

/// <summary>
/// The identity of a model request for the response cache (LLM-04, D-19): the lowercase SHA-256 hex of a
/// canonical JSON object. The object is written member by member in a fixed order, never by reflection, so the
/// key cannot drift when a property is added or reordered. Large inputs (prompt, schema, document, follow-up
/// turns) enter as their own SHA-256 so the key never depends on, and the entry never stores, their content.
/// </summary>
public static class LlmCacheKey
{
    /// <summary>
    /// Bumped on purpose when the key recipe changes, so old entries stop matching visibly instead of silently.
    /// </summary>
    public const string Version = "llm-cache-key-1";

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
            writer.WriteStartObject();
            writer.WriteString("key_version", Version);
            writer.WriteString("model", request.Model);
            writer.WriteNumber("max_tokens", request.MaxTokens);
            writer.WriteBoolean("cache_document", request.CacheDocument);
            WriteNullableString(writer, "prompt_version", request.PromptVersion);
            WriteNullableString(writer, "repair_prompt_version", request.RepairPromptVersion);
            writer.WriteString("prompt_sha256", Sha256Hex(request.Prompt));
            writer.WriteString("schema_sha256", Sha256Hex(request.OutputSchemaJson));
            writer.WriteString("document_media_type", request.Document.MediaType);
            writer.WriteString("document_sha256", Sha256Hex(request.Document.Content.Span));
            writer.WriteStartArray("follow_ups");
            foreach (var turn in request.FollowUps)
            {
                writer.WriteStartObject();
                writer.WriteString("role", turn.Role == LlmTurnRole.Assistant ? "assistant" : "user");
                writer.WriteString("text_sha256", Sha256Hex(turn.Text));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("replicate_salt", request.ReplicateSalt ?? string.Empty);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The cache key: 64 lowercase hex characters.</summary>
    public static string Compute(LlmRequest request) => Sha256Hex(CanonicalJson(request));

    /// <summary>Lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="text"/>.</summary>
    internal static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    internal static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

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
