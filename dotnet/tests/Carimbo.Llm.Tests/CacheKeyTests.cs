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

    // llm-cache-key-2 vectors (D-37). Both canonical JSON strings and both hex keys were computed outside the code
    // under test with Python hashlib (see the plan SUMMARY): every member SHA-256 with hashlib.sha256 over the UTF-8
    // text, the object serialised with json.dumps(separators=(",", ":")), the key with hashlib.sha256 over that text.
    // Pin A is the second step of an agent conversation (no document, system prompt, one tool, echoed blocks, one
    // tool result). Pin B has a document, thinking and redacted thinking blocks, and an error tool result.
    private const string PinnedToolJsonA =
        "{\"key_version\":\"llm-cache-key-2\",\"model\":\"claude-haiku-4-5\",\"max_tokens\":4096,"
        + "\"cache_document\":false,\"prompt_version\":\"agent-001\",\"repair_prompt_version\":null,"
        + "\"prompt_sha256\":\"f903e1bf01724d77f43b43cc1f1c014e05421055c59c33bb6cab98d1beecc196\","
        + "\"system_sha256\":\"8ac69a0d83ba3d3629922b925a31f14277b127400c7aaaa621ca48d243774ceb\","
        + "\"schema_sha256\":\"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a\","
        + "\"document_media_type\":null,\"document_sha256\":null,\"tools\":[{\"name\":\"get_policy\","
        + "\"description_sha256\":\"d93b80172be6412c23d0643019eefe826056b0208cca0484879e1f24df987666\","
        + "\"input_schema_sha256\":\"8243f0af367f188a376f2c17b5eabe872a2f7a979813e0d4e2be6d594c2aa259\"}],"
        + "\"follow_ups\":[{\"role\":\"assistant\","
        + "\"text_sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\","
        + "\"blocks\":[{\"kind\":\"text\","
        + "\"text_sha256\":\"1c76a18924bb813ffcaa950814e3ecfd7b79ea772f65db96e89d49a0411272eb\","
        + "\"signature_sha256\":null,\"data_sha256\":null,\"tool_use_id\":null,\"tool_name\":null,"
        + "\"input_sha256\":null},{\"kind\":\"tool_use\",\"text_sha256\":null,\"signature_sha256\":null,"
        + "\"data_sha256\":null,\"tool_use_id\":\"toolu_01\",\"tool_name\":\"get_policy\","
        + "\"input_sha256\":\"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a\"}],"
        + "\"tool_results\":[]},{\"role\":\"user\","
        + "\"text_sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\","
        + "\"blocks\":[],\"tool_results\":[{\"tool_use_id\":\"toolu_01\","
        + "\"content_sha256\":\"471e305bd41f770eb7cd1002cb57f7b334255f9c6cea2fcd38a7e40ce4136810\","
        + "\"is_error\":false}]}],\"replicate_salt\":\"\"}";

    private const string PinnedToolKeyA = "479c4ab785e5e995fba01309a30416c23e4a69a3f796d82890afcb2535532fad";

    private const string PinnedToolJsonB =
        "{\"key_version\":\"llm-cache-key-2\",\"model\":\"claude-sonnet-5-5\",\"max_tokens\":8000,"
        + "\"cache_document\":true,\"prompt_version\":\"agent-001\","
        + "\"repair_prompt_version\":\"repair-001\","
        + "\"prompt_sha256\":\"55f749c89b4597ba5fcf532d1985f09acd0991511704eb48cb8a93a094312ff2\","
        + "\"system_sha256\":null,"
        + "\"schema_sha256\":\"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a\","
        + "\"document_media_type\":\"application/pdf\","
        + "\"document_sha256\":\"56eff5be559c9c48e89e6f7763f8b9f6f08183a7b80c8b87e72b98b0f9dd3629\","
        + "\"tools\":[],\"follow_ups\":[{\"role\":\"assistant\","
        + "\"text_sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\","
        + "\"blocks\":[{\"kind\":\"thinking\","
        + "\"text_sha256\":\"43faeb718cae45588c4d1123daacbe2819f10a185c270e898e75b5a60c8707bc\","
        + "\"signature_sha256\":\"845ed3481ddbfc748428119cd6a2d848cfff097e6aa97a71aaf85a2e3fd63c46\","
        + "\"data_sha256\":null,\"tool_use_id\":null,\"tool_name\":null,\"input_sha256\":null},"
        + "{\"kind\":\"redacted_thinking\",\"text_sha256\":null,\"signature_sha256\":null,"
        + "\"data_sha256\":\"50e7fb402e25ae5774b915c94913706d869c2d752f4d349627fcb318af6b7f34\","
        + "\"tool_use_id\":null,\"tool_name\":null,\"input_sha256\":null},{\"kind\":\"tool_use\","
        + "\"text_sha256\":null,\"signature_sha256\":null,\"data_sha256\":null,"
        + "\"tool_use_id\":\"toolu_09\",\"tool_name\":\"get_invoice\","
        + "\"input_sha256\":\"2fd77bf4a196008901a188df72066bc7ad6b6d6a7c0d40e3ad8c858f2bc96045\"}],"
        + "\"tool_results\":[]},{\"role\":\"user\","
        + "\"text_sha256\":\"f3daf2941865055833e9db4c9bab5d1280281952bb1699514a28ee4c67256573\","
        + "\"blocks\":[],\"tool_results\":[{\"tool_use_id\":\"toolu_09\","
        + "\"content_sha256\":\"81f52337ebb4cb1669bb802c708807dde0519d15cb102a6313d26ad5cd821713\","
        + "\"is_error\":true}]}],\"replicate_salt\":\"r1\"}";

    private const string PinnedToolKeyB = "be5c5e952675e1ed1f6e9cc418118a0291a6c0e419e63112509aa7aa6589d527";

    private static LlmToolDefinition PolicyTool { get; } =
        new("get_policy", "Returns the buyer policy.", "{\"type\":\"object\",\"properties\":{}}");

    private static LlmRequest PinnedToolA() =>
        new(
            "claude-haiku-4-5",
            4096,
            "case_id: dc-001\nDecide this case: approve, reject or escalate.",
            null,
            "{}")
        {
            SystemPrompt = "system text",
            Tools = [PolicyTool],
            PromptVersion = "agent-001",
            FollowUps =
            [
                new LlmTurn(LlmTurnRole.Assistant, string.Empty)
                {
                    Blocks =
                    [
                        LlmContentBlock.OfText("Reading the policy."),
                        LlmContentBlock.OfToolUse(new LlmToolCall("toolu_01", "get_policy", "{}")),
                    ],
                },
                new LlmTurn(LlmTurnRole.User, string.Empty)
                {
                    ToolResults = [new LlmToolResult("toolu_01", "{\"buyer_cnpj\":\"X\"}", false)],
                },
            ],
            CacheMode = LlmCacheMode.ReadOnly,
        };

    private static LlmRequest PinnedToolB() =>
        new(
            "claude-sonnet-5-5",
            8000,
            "Decide.",
            new LlmDocument("application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4\nsynthetic")),
            "{}")
        {
            CacheDocument = true,
            PromptVersion = "agent-001",
            RepairPromptVersion = "repair-001",
            ReplicateSalt = "r1",
            FollowUps =
            [
                new LlmTurn(LlmTurnRole.Assistant, string.Empty)
                {
                    Blocks =
                    [
                        LlmContentBlock.OfThinking("Need the policy.", "sig-abc"),
                        LlmContentBlock.OfRedactedThinking("redacted-data"),
                        LlmContentBlock.OfToolUse(new LlmToolCall("toolu_09", "get_invoice", "{\"case_id\":\"dc-001\"}")),
                    ],
                },
                new LlmTurn(LlmTurnRole.User, "Continue.")
                {
                    ToolResults = [new LlmToolResult("toolu_09", "boom", true)],
                },
            ],
        };

    [Fact]
    public void The_pinned_tool_conversation_requests_have_the_pinned_canonical_json_and_key()
    {
        Assert.Equal(PinnedToolJsonA, LlmCacheKey.CanonicalJson(PinnedToolA()));
        Assert.Equal(PinnedToolKeyA, LlmCacheKey.Compute(PinnedToolA()));
        Assert.Equal(PinnedToolJsonB, LlmCacheKey.CanonicalJson(PinnedToolB()));
        Assert.Equal(PinnedToolKeyB, LlmCacheKey.Compute(PinnedToolB()));
        Assert.Equal("llm-cache-key-2", LlmCacheKey.ToolConversationVersion);
    }

    [Fact]
    public void A_tool_conversation_key_is_independent_of_the_cache_mode()
    {
        foreach (LlmCacheMode? mode in new LlmCacheMode?[] { null, LlmCacheMode.Off, LlmCacheMode.ReadWrite, LlmCacheMode.ReadOnly, LlmCacheMode.Refresh })
        {
            Assert.Equal(PinnedToolKeyA, LlmCacheKey.Compute(PinnedToolA() with { CacheMode = mode }));
        }
    }

    [Fact]
    public void A_system_prompt_alone_moves_an_extraction_request_to_the_v2_recipe()
    {
        var withSystem = Pinned() with { SystemPrompt = "be brief" };

        Assert.StartsWith("{\"key_version\":\"llm-cache-key-2\"", LlmCacheKey.CanonicalJson(withSystem), StringComparison.Ordinal);
        Assert.NotEqual(PinnedKey, LlmCacheKey.Compute(withSystem));
        Assert.StartsWith("{\"key_version\":\"llm-cache-key-1\"", LlmCacheKey.CanonicalJson(Pinned()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_alone_or_a_missing_document_alone_moves_a_request_to_the_v2_recipe()
    {
        Assert.StartsWith(
            "{\"key_version\":\"llm-cache-key-2\"",
            LlmCacheKey.CanonicalJson(Pinned() with { Tools = [PolicyTool] }),
            StringComparison.Ordinal);
        Assert.StartsWith(
            "{\"key_version\":\"llm-cache-key-2\"",
            LlmCacheKey.CanonicalJson(Pinned() with { Document = null }),
            StringComparison.Ordinal);
    }

    private static LlmToolDefinition SecondTool { get; } =
        new("get_invoice", "Returns the extracted invoice.", "{\"type\":\"object\",\"properties\":{\"case_id\":{\"type\":\"string\"}}}");

    /// <summary>Pin B enriched with a system prompt and two tools, so every v2 member has something to change.</summary>
    private static LlmRequest Rich() => PinnedToolB() with { SystemPrompt = "system text", Tools = [PolicyTool, SecondTool] };

    private static LlmRequest WithAssistantBlocks(LlmRequest r, params LlmContentBlock[] blocks) =>
        r with { FollowUps = [r.FollowUps[0] with { Blocks = blocks }, r.FollowUps[1]] };

    private static LlmRequest WithToolResult(LlmRequest r, LlmToolResult result) =>
        r with { FollowUps = [r.FollowUps[0], r.FollowUps[1] with { ToolResults = [result] }] };

    public static TheoryData<string, Func<LlmRequest, LlmRequest>> ToolVariants() => new()
    {
        { "system prompt text", r => r with { SystemPrompt = "system text!" } },
        { "system prompt removed", r => r with { SystemPrompt = null } },
        { "system prompt empty", r => r with { SystemPrompt = string.Empty } },
        { "tool name", r => r with { Tools = [PolicyTool with { Name = "get_policy2" }, SecondTool] } },
        { "tool description", r => r with { Tools = [PolicyTool with { Description = "Returns the policy." }, SecondTool] } },
        { "tool input schema", r => r with { Tools = [PolicyTool with { InputSchemaJson = "{\"type\":\"object\"}" }, SecondTool] } },
        { "tool order", r => r with { Tools = [SecondTool, PolicyTool] } },
        { "tool removed", r => r with { Tools = [PolicyTool] } },
        { "document removed", r => r with { Document = null } },
        {
            "thinking text",
            r => WithAssistantBlocks(r, LlmContentBlock.OfThinking("Need the policy!", "sig-abc"), LlmContentBlock.OfRedactedThinking("redacted-data"), r.FollowUps[0].Blocks[2])
        },
        {
            "thinking signature",
            r => WithAssistantBlocks(r, LlmContentBlock.OfThinking("Need the policy.", "sig-abd"), LlmContentBlock.OfRedactedThinking("redacted-data"), r.FollowUps[0].Blocks[2])
        },
        {
            "redacted thinking data",
            r => WithAssistantBlocks(r, LlmContentBlock.OfThinking("Need the policy.", "sig-abc"), LlmContentBlock.OfRedactedThinking("redacted-datb"), r.FollowUps[0].Blocks[2])
        },
        {
            "block order",
            r => WithAssistantBlocks(r, r.FollowUps[0].Blocks[1], r.FollowUps[0].Blocks[0], r.FollowUps[0].Blocks[2])
        },
        {
            "block kind with the same text",
            r => WithAssistantBlocks(r, LlmContentBlock.OfText("Need the policy."), r.FollowUps[0].Blocks[1], r.FollowUps[0].Blocks[2])
        },
        {
            "tool use id",
            r => WithAssistantBlocks(r, r.FollowUps[0].Blocks[0], r.FollowUps[0].Blocks[1], LlmContentBlock.OfToolUse(new LlmToolCall("toolu_10", "get_invoice", "{\"case_id\":\"dc-001\"}")))
        },
        {
            "tool use name",
            r => WithAssistantBlocks(r, r.FollowUps[0].Blocks[0], r.FollowUps[0].Blocks[1], LlmContentBlock.OfToolUse(new LlmToolCall("toolu_09", "get_policy", "{\"case_id\":\"dc-001\"}")))
        },
        {
            "tool use input",
            r => WithAssistantBlocks(r, r.FollowUps[0].Blocks[0], r.FollowUps[0].Blocks[1], LlmContentBlock.OfToolUse(new LlmToolCall("toolu_09", "get_invoice", "{\"case_id\":\"dc-002\"}")))
        },
        { "tool result id", r => WithToolResult(r, new LlmToolResult("toolu_10", "boom", true)) },
        { "tool result content", r => WithToolResult(r, new LlmToolResult("toolu_09", "boom!", true)) },
        { "tool result is_error", r => WithToolResult(r, new LlmToolResult("toolu_09", "boom", false)) },
        { "tool result removed", r => r with { FollowUps = [r.FollowUps[0], r.FollowUps[1] with { ToolResults = [] }] } },
        { "follow-up text", r => r with { FollowUps = [r.FollowUps[0], r.FollowUps[1] with { Text = "Continue!" }] } },
    };

    [Theory]
    [MemberData(nameof(ToolVariants))]
    public void Changing_any_tool_conversation_member_changes_the_key(string field, Func<LlmRequest, LlmRequest> change)
    {
        var baseline = LlmCacheKey.Compute(Rich());
        var changed = LlmCacheKey.Compute(change(Rich()));

        Assert.True(baseline != changed, $"Changing {field} did not change the cache key.");
        Assert.Matches("^[0-9a-f]{64}$", changed);
    }

    [Fact]
    public void A_tool_use_block_without_a_tool_call_is_refused_rather_than_keyed_as_empty()
    {
        var request = WithAssistantBlocks(Rich(), new LlmContentBlock(LlmContentKind.ToolUse));

        Assert.Throws<ArgumentException>(() => LlmCacheKey.Compute(request));
    }

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
