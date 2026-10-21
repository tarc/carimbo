using System.Reflection;
using System.Text;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

/// <summary>
/// The cache key is the identity of every replay fixture Phase 6 commits, so it is pinned: a known request must
/// produce a known canonical JSON and a known hex, both computed outside the code under test. A change here is a
/// deliberate key version bump, never an accident.
/// </summary>
public class CacheKeyTests
{
    private const string PinnedCanonicalJson =
        "{\"key_version\":\"llm-cache-key-1\",\"model\":\"claude-haiku-4-5\",\"max_tokens\":16000,\"cache_document\":true,"
        + "\"prompt_version\":\"extract-003\",\"repair_prompt_version\":\"repair-001\","
        + "\"prompt_sha256\":\"7132e04d9584e6c56b6eea50f6e79a64800a4cd9e4f4fb7317ce5a00d4cc2cb0\","
        + "\"schema_sha256\":\"a2c799262a3ce3c19ef5cdd983bf3d12b43ab3c426227091b909dcb7054738c0\","
        + "\"document_media_type\":\"application/pdf\","
        + "\"document_sha256\":\"56eff5be559c9c48e89e6f7763f8b9f6f08183a7b80c8b87e72b98b0f9dd3629\","
        + "\"follow_ups\":[{\"role\":\"assistant\",\"text_sha256\":\"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a\"},"
        + "{\"role\":\"user\",\"text_sha256\":\"690cc521261686d9e468ac60d31aefbfa60ecc5774647f8ff7a65a9d4ed3162c\"}],"
        + "\"replicate_salt\":\"r1\"}";

    private const string PinnedKey = "e9ed4fd7f475483da7417e82b5a8de4fbce1f80dacb5071652b9b4480e4cb41b";

    private static LlmRequest Pinned() =>
        new(
            "claude-haiku-4-5",
            16000,
            "Extract the invoice.",
            new LlmDocument("application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4\nsynthetic")),
            "{\"type\":\"object\"}")
        {
            FollowUps = [new LlmTurn(LlmTurnRole.Assistant, "{}"), new LlmTurn(LlmTurnRole.User, "Fix it.")],
            CacheDocument = true,
            PromptVersion = "extract-003",
            RepairPromptVersion = "repair-001",
            ReplicateSalt = "r1",
            CacheMode = LlmCacheMode.ReadOnly,
        };

    [Fact]
    public void The_pinned_request_has_the_pinned_canonical_json()
    {
        Assert.Equal(PinnedCanonicalJson, LlmCacheKey.CanonicalJson(Pinned()));
    }

    [Fact]
    public void The_pinned_request_has_the_pinned_key()
    {
        Assert.Equal(PinnedKey, LlmCacheKey.Compute(Pinned()));
        Assert.Equal("llm-cache-key-1", LlmCacheKey.Version);
    }

    [Fact]
    public void Every_public_request_property_is_either_keyed_or_explicitly_excluded()
    {
        var actual = typeof(LlmRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(LlmCacheKey.KeyedProperties.Intersect(LlmCacheKey.ExcludedProperties));
        Assert.Equal(
            actual,
            LlmCacheKey.KeyedProperties.Concat(LlmCacheKey.ExcludedProperties).Order(StringComparer.Ordinal).ToArray());
    }

    public static TheoryData<string, Func<LlmRequest, LlmRequest>> Variants() => new()
    {
        { "model", r => r with { Model = "claude-sonnet-5-5" } },
        { "max_tokens", r => r with { MaxTokens = 8000 } },
        { "cache_document", r => r with { CacheDocument = false } },
        { "prompt_version", r => r with { PromptVersion = "extract-004" } },
        { "prompt_version null", r => r with { PromptVersion = null } },
        { "repair_prompt_version", r => r with { RepairPromptVersion = "repair-002" } },
        { "repair_prompt_version null", r => r with { RepairPromptVersion = null } },
        { "prompt text", r => r with { Prompt = "Extract the invoice!" } },
        { "schema text", r => r with { OutputSchemaJson = "{\"type\":\"array\"}" } },
        {
            "document bytes",
            r => r with { Document = new LlmDocument("application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4\nsyntheti!")) }
        },
        {
            "document media type",
            r => r with { Document = new LlmDocument("image/png", r.Document!.Content) }
        },
        {
            "follow-up text",
            r => r with { FollowUps = [r.FollowUps[0], new LlmTurn(LlmTurnRole.User, "Fix it now.")] }
        },
        {
            "follow-up role order",
            r => r with { FollowUps = [new LlmTurn(LlmTurnRole.User, "{}"), new LlmTurn(LlmTurnRole.Assistant, "Fix it.")] }
        },
        { "follow-up count", r => r with { FollowUps = [r.FollowUps[0]] } },
        { "replicate salt r2", r => r with { ReplicateSalt = "r2" } },
        { "replicate salt null", r => r with { ReplicateSalt = null } },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void Changing_any_keyed_field_changes_the_key(string field, Func<LlmRequest, LlmRequest> change)
    {
        var baseline = LlmCacheKey.Compute(Pinned());
        var changed = LlmCacheKey.Compute(change(Pinned()));

        Assert.True(baseline != changed, $"Changing {field} did not change the cache key.");
        Assert.Matches("^[0-9a-f]{64}$", changed);
    }

    [Fact]
    public void A_null_salt_and_an_empty_salt_are_the_same_replicate()
    {
        Assert.Equal(
            LlmCacheKey.Compute(Pinned() with { ReplicateSalt = null }),
            LlmCacheKey.Compute(Pinned() with { ReplicateSalt = string.Empty }));
    }

    [Theory]
    [InlineData(LlmCacheMode.Off)]
    [InlineData(LlmCacheMode.ReadWrite)]
    [InlineData(LlmCacheMode.ReadOnly)]
    [InlineData(LlmCacheMode.Refresh)]
    [InlineData(null)]
    public void The_cache_mode_never_changes_the_key(LlmCacheMode? mode)
    {
        Assert.Equal(PinnedKey, LlmCacheKey.Compute(Pinned() with { CacheMode = mode }));
    }

    [Theory]
    [InlineData("off", LlmCacheMode.Off)]
    [InlineData("read_write", LlmCacheMode.ReadWrite)]
    [InlineData("read_only", LlmCacheMode.ReadOnly)]
    [InlineData("refresh", LlmCacheMode.Refresh)]
    public void Cache_modes_round_trip_through_their_exact_wire_names(string wire, LlmCacheMode mode)
    {
        Assert.True(LlmCacheModes.TryParse(wire, out var parsed));
        Assert.Equal(mode, parsed);
        Assert.Equal(wire, LlmCacheModes.ToWire(mode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OFF")]
    [InlineData("readonly")]
    [InlineData("read-only")]
    [InlineData(" refresh")]
    public void Any_other_spelling_of_a_cache_mode_is_rejected(string? wire)
    {
        Assert.False(LlmCacheModes.TryParse(wire, out _));
    }
}
