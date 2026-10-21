using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Carimbo.Llm;

/// <summary>
/// The non-content summary of the request an entry answers: enough for a human to see what was cached without
/// the prompt, the system prompt, the schema, the tool definitions, the tool results or the document, none of which
/// is ever stored. <see cref="DocumentSha256"/> is null for a request without a document.
/// </summary>
public sealed record LlmCacheKeyFields(
    string Model,
    string? PromptVersion,
    string? RepairPromptVersion,
    string SchemaSha256,
    string? DocumentSha256,
    string ReplicateSalt,
    int FollowUpCount,
    int ToolCount = 0)
{
    public static LlmCacheKeyFields From(LlmRequest request) => new(
        request.Model,
        request.PromptVersion,
        request.RepairPromptVersion,
        LlmCacheKey.Sha256Hex(request.OutputSchemaJson),
        request.Document is { } document ? LlmCacheKey.Sha256Hex(document.Content.Span) : null,
        request.ReplicateSalt ?? string.Empty,
        request.FollowUps.Count,
        request.Tools.Count);
}

/// <summary>One stored answer. <see cref="Response"/> is as the provider call produced it, including its original attempts.</summary>
public sealed record LlmCacheEntry(string Key, LlmCacheKeyFields KeyFields, DateTimeOffset RecordedAt, LlmResponse Response);

/// <summary>
/// One JSON file per key under a directory. Reads are defensive: anything that is not a valid entry for the key
/// is a miss, never an exception. Writes go to a temporary file in the same directory and are moved into place,
/// so a concurrent reader never sees a torn file. The file name is only the validated digest, never derived from
/// request input. Nothing touches the disk until the first write.
/// </summary>
public sealed class LlmCacheStore(string directory)
{
    /// <summary>
    /// Format version written for every entry: the response carries its ordered content blocks. Any version other
    /// than this and <see cref="LegacyEntryVersion"/> is a miss.
    /// </summary>
    public const string EntryVersion = "llm-cache-entry-2";

    /// <summary>The Phase 3 format (no blocks). Still read, as a response with empty blocks; never written.</summary>
    public const string LegacyEntryVersion = "llm-cache-entry-1";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public string Directory { get; } = directory;

    /// <summary>The path an entry for <paramref name="key"/> lives at. Throws when the key is not 64 lowercase hex characters.</summary>
    public string PathFor(string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException("A cache key is 64 lowercase hex characters.", nameof(key));
        }

        return Path.Combine(Directory, key + ".json");
    }

    public bool TryRead(string key, out LlmCacheEntry entry)
    {
        var path = PathFor(key);
        entry = null!;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var parsed = Parse(document.RootElement);
            if (parsed is null || !string.Equals(parsed.Key, key, StringComparison.Ordinal))
            {
                return false;
            }

            entry = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
            or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException
            or ArgumentException)
        {
            return false;
        }
    }

    public void Write(LlmCacheEntry entry)
    {
        var path = PathFor(entry.Key);
        System.IO.Directory.CreateDirectory(Directory);

        var temporary = Path.Combine(Directory, $"{entry.Key}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");
        try
        {
            File.WriteAllBytes(temporary, Serialize(entry));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    internal static bool IsValidKey(string? key)
    {
        if (key is not { Length: 64 })
        {
            return false;
        }

        foreach (var c in key)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[] Serialize(LlmCacheEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            var response = entry.Response;
            writer.WriteStartObject();
            writer.WriteString("entry_version", EntryVersion);
            writer.WriteString("key", entry.Key);

            writer.WriteStartObject("key_fields");
            writer.WriteString("model", entry.KeyFields.Model);
            WriteNullableString(writer, "prompt_version", entry.KeyFields.PromptVersion);
            WriteNullableString(writer, "repair_prompt_version", entry.KeyFields.RepairPromptVersion);
            writer.WriteString("schema_sha256", entry.KeyFields.SchemaSha256);
            WriteNullableString(writer, "document_sha256", entry.KeyFields.DocumentSha256);
            writer.WriteString("replicate_salt", entry.KeyFields.ReplicateSalt);
            writer.WriteNumber("follow_up_count", entry.KeyFields.FollowUpCount);
            writer.WriteNumber("tool_count", entry.KeyFields.ToolCount);
            writer.WriteEndObject();

            writer.WriteString(
                "recorded_at",
                entry.RecordedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));

            writer.WriteStartObject("response");
            writer.WriteString("text", response.Text);
            writer.WriteString("stop_reason", StopReasonToWire(response.StopReason));
            WriteNullableString(writer, "stop_detail", response.StopDetail);

            writer.WriteStartArray("blocks");
            foreach (var block in response.Blocks)
            {
                WriteBlock(writer, block);
            }

            writer.WriteEndArray();

            writer.WriteStartObject("usage");
            writer.WriteNumber("input_tokens", response.Usage.InputTokens);
            writer.WriteNumber("output_tokens", response.Usage.OutputTokens);
            writer.WriteNumber("cache_read_tokens", response.Usage.CacheReadTokens);
            writer.WriteNumber("cache_write_5m_tokens", response.Usage.CacheWrite5mTokens);
            writer.WriteNumber("cache_write_1h_tokens", response.Usage.CacheWrite1hTokens);
            writer.WriteEndObject();

            writer.WriteString("model_requested", response.ModelRequested);
            WriteNullableString(writer, "model_returned", response.ModelReturned);
            WriteNullableString(writer, "provider_message_id", response.ProviderMessageId);
            writer.WriteNumber("latency_ms", (decimal)response.Latency.Ticks / TimeSpan.TicksPerMillisecond);
            writer.WriteNumber("original_http_attempts", response.HttpAttempts);

            if (response.Cost is { } cost)
            {
                writer.WriteStartObject("cost");
                WriteNullableString(
                    writer,
                    "amount_usd",
                    cost.AmountUsd?.ToString("F8", CultureInfo.InvariantCulture));
                writer.WriteString("pricing_version", cost.PricingVersion);
                WriteNullableString(writer, "warning", cost.Warning);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("cost");
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return [.. stream.ToArray(), (byte)'\n'];
    }

    private static void WriteBlock(Utf8JsonWriter writer, LlmContentBlock block)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", LlmCacheKey.KindName(block.Kind));
        WriteNullableString(writer, "text", block.Text);
        WriteNullableString(writer, "signature", block.Signature);
        WriteNullableString(writer, "data", block.Data);
        if (block.ToolCall is { } call)
        {
            writer.WriteStartObject("tool_call");
            writer.WriteString("id", call.Id);
            writer.WriteString("name", call.Name);
            writer.WriteString("input_json", call.InputJson);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("tool_call");
        }

        writer.WriteEndObject();
    }

    /// <summary>Reads the stored blocks; null when the array or any block is malformed.</summary>
    private static List<LlmContentBlock>? ParseBlocks(JsonElement response)
    {
        if (!response.TryGetProperty("blocks", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var blocks = new List<LlmContentBlock>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var kind = KindFromWire(OptionalString(element, "kind"));
            if (kind is null)
            {
                return null;
            }

            LlmToolCall? call = null;
            if (element.TryGetProperty("tool_call", out var callElement) && callElement.ValueKind != JsonValueKind.Null)
            {
                if (callElement.ValueKind != JsonValueKind.Object
                    || OptionalString(callElement, "id") is not { } id
                    || OptionalString(callElement, "name") is not { } name
                    || OptionalString(callElement, "input_json") is not { } input)
                {
                    return null;
                }

                call = new LlmToolCall(id, name, input);
            }

            if (kind == LlmContentKind.ToolUse && call is null)
            {
                return null;
            }

            blocks.Add(new LlmContentBlock(kind.Value)
            {
                Text = OptionalString(element, "text"),
                Signature = OptionalString(element, "signature"),
                Data = OptionalString(element, "data"),
                ToolCall = call,
            });
        }

        return blocks;
    }

    private static LlmContentKind? KindFromWire(string? wire)
    {
        foreach (var kind in Enum.GetValues<LlmContentKind>())
        {
            if (string.Equals(LlmCacheKey.KindName(kind), wire, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    private static LlmCacheEntry? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var entryVersion = root.GetProperty("entry_version").GetString();
        if (entryVersion != EntryVersion && entryVersion != LegacyEntryVersion)
        {
            return null;
        }

        var key = root.GetProperty("key").GetString();
        if (!IsValidKey(key))
        {
            return null;
        }

        var fields = root.GetProperty("key_fields");
        var keyFields = new LlmCacheKeyFields(
            fields.GetProperty("model").GetString()!,
            OptionalString(fields, "prompt_version"),
            OptionalString(fields, "repair_prompt_version"),
            fields.GetProperty("schema_sha256").GetString()!,
            OptionalString(fields, "document_sha256"),
            fields.GetProperty("replicate_salt").GetString()!,
            fields.GetProperty("follow_up_count").GetInt32(),
            fields.TryGetProperty("tool_count", out var toolCount) ? toolCount.GetInt32() : 0);

        var recordedAt = DateTimeOffset.Parse(
            root.GetProperty("recorded_at").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        var r = root.GetProperty("response");
        var blocks = entryVersion == EntryVersion ? ParseBlocks(r) : [];
        if (blocks is null)
        {
            return null;
        }

        var usage = r.GetProperty("usage");
        var costElement = r.GetProperty("cost");
        LlmCost? cost = null;
        if (costElement.ValueKind == JsonValueKind.Object)
        {
            var amount = OptionalString(costElement, "amount_usd");
            cost = new LlmCost(
                amount is null ? null : decimal.Parse(amount, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                costElement.GetProperty("pricing_version").GetString()!,
                OptionalString(costElement, "warning"));
        }

        var response = new LlmResponse(
            r.GetProperty("text").GetString()!,
            StopReasonFromWire(r.GetProperty("stop_reason").GetString()),
            OptionalString(r, "stop_detail"),
            new LlmUsage(
                usage.GetProperty("input_tokens").GetInt64(),
                usage.GetProperty("output_tokens").GetInt64(),
                usage.GetProperty("cache_read_tokens").GetInt64(),
                usage.GetProperty("cache_write_5m_tokens").GetInt64(),
                usage.GetProperty("cache_write_1h_tokens").GetInt64()),
            r.GetProperty("model_requested").GetString()!,
            OptionalString(r, "model_returned"),
            OptionalString(r, "provider_message_id"),
            TimeSpan.FromTicks(checked((long)(r.GetProperty("latency_ms").GetDecimal() * TimeSpan.TicksPerMillisecond))),
            r.GetProperty("original_http_attempts").GetInt32())
        {
            Blocks = blocks,
            Cost = cost,
        };

        return new LlmCacheEntry(key!, keyFields, recordedAt, response);
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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

    private static string StopReasonToWire(LlmStopReason reason) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(reason.ToString());

    private static LlmStopReason StopReasonFromWire(string? wire)
    {
        foreach (var reason in Enum.GetValues<LlmStopReason>())
        {
            if (string.Equals(StopReasonToWire(reason), wire, StringComparison.Ordinal))
            {
                return reason;
            }
        }

        throw new FormatException("Unknown stop reason in cache entry.");
    }
}
