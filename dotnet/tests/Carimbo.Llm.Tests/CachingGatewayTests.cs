using System.Text;
using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

/// <summary>
/// The response cache (LLM-04, LLM-05, D-19) proven through the real decorator stack: the key, the file store
/// and the mode matrix, with a counting double standing in for the provider.
/// </summary>
public class CachingGatewayTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic cache test bytes");

    private static readonly LlmPricingTable Table = LlmPricingTable.LoadEmbedded();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmRequest Request(string? salt = null) =>
        new("claude-haiku-4-5", 1024, "Extract the invoice.", new LlmDocument("application/pdf", Pdf), "{\"type\":\"object\"}")
        {
            PromptVersion = "extract-003",
            ReplicateSalt = salt,
        };

    private static LlmResponse Answer(string text = "{\"ok\":true}", LlmStopReason stop = LlmStopReason.EndTurn) =>
        new(
            text,
            stop,
            null,
            new LlmUsage(1200, 340, 50, 0, 0),
            "claude-haiku-4-5",
            "claude-haiku-4-5",
            "msg_cache_1",
            TimeSpan.FromMilliseconds(1234),
            1);

    private static string[] EntryFiles(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : [];

    private static async Task WithTempDirectory(Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("carimbo-cache-test-").FullName;
        try
        {
            await body(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static CachingLlmGateway Gateway(
        string dir, ILlmGateway inner, LlmCacheMode mode = LlmCacheMode.ReadOnly, bool priced = false) =>
        new(
            priced ? new CostAccountingLlmGateway(inner, Table) : inner,
            new LlmCacheStore(dir),
            mode,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero)));

    [Fact]
    public async Task A_read_write_answer_is_replayed_by_a_fresh_read_only_gateway_with_zero_model_calls()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var request = Request();

            var writer = Gateway(dir, inner, LlmCacheMode.ReadWrite, priced: true);
            var fresh = await writer.CompleteAsync(request, Ct);

            var reader = Gateway(dir, inner, priced: true);
            var replay = await reader.CompleteAsync(request, Ct);

            Assert.Equal(1, inner.Calls);
            var key = LlmCacheKey.Compute(request);

            Assert.Equal(1, fresh.HttpAttempts);
            Assert.Equal(new LlmCacheInfo(LlmCacheMode.ReadWrite, LlmCacheStatus.Miss, key, true), fresh.Cache);

            Assert.Equal(0, replay.HttpAttempts);
            Assert.Equal(TimeSpan.FromMilliseconds(1234), replay.Latency);
            Assert.Equal(fresh.Usage, replay.Usage);
            Assert.NotNull(fresh.Cost);
            Assert.NotNull(replay.Cost);
            Assert.Equal(fresh.Cost.AmountUsd, replay.Cost.AmountUsd);
            Assert.Equal(fresh.Cost.PricingVersion, replay.Cost.PricingVersion);
            Assert.Equal(fresh.Text, replay.Text);
            Assert.Equal(fresh.StopReason, replay.StopReason);
            Assert.Equal(fresh.ModelRequested, replay.ModelRequested);
            Assert.Equal(fresh.ModelReturned, replay.ModelReturned);
            Assert.Equal(fresh.ProviderMessageId, replay.ProviderMessageId);
            Assert.Equal(new LlmCacheInfo(LlmCacheMode.ReadOnly, LlmCacheStatus.Hit, key, false), replay.Cache);

            var file = Assert.Single(EntryFiles(dir));
            Assert.Equal(key + ".json", Path.GetFileName(file));
            Assert.Matches("^[0-9a-f]{64}$", key);
        });
    }

    // ---------------------------------------------------------------- tool conversations (AGT-06, D-37)

    private const string PolicyToolSchema = "{\"type\":\"object\",\"properties\":{}}";

    private static LlmRequest ToolStepOne() =>
        new("claude-haiku-4-5", 4096, "case_id: dc-001\nDecide this case: approve, reject or escalate.", null, "{}")
        {
            SystemPrompt = "system text",
            Tools = [new LlmToolDefinition("get_policy", "Returns the buyer policy.", PolicyToolSchema)],
            PromptVersion = "agent-001",
        };

    private static readonly LlmContentBlock[] ToolStepOneBlocks =
    [
        LlmContentBlock.OfText("Reading the policy."),
        LlmContentBlock.OfToolUse(new LlmToolCall("toolu_01", "get_policy", "{}")),
    ];

    private static LlmRequest ToolStepTwo() =>
        ToolStepOne() with
        {
            FollowUps =
            [
                new LlmTurn(LlmTurnRole.Assistant, string.Empty) { Blocks = ToolStepOneBlocks },
                new LlmTurn(LlmTurnRole.User, string.Empty)
                {
                    ToolResults = [new LlmToolResult("toolu_01", "{\"buyer_cnpj\":\"X\"}", false)],
                },
            ],
        };

    private static LlmResponse ToolAnswer(LlmRequest request) =>
        request.FollowUps.Count == 0
            ? new LlmResponse(
                "Reading the policy.",
                LlmStopReason.ToolUse,
                null,
                new LlmUsage(900, 120, 0, 0, 0),
                "claude-haiku-4-5",
                "claude-haiku-4-5",
                "msg_tool_1",
                TimeSpan.FromMilliseconds(900),
                1)
            {
                Blocks = ToolStepOneBlocks,
            }
            : new LlmResponse(
                "{\"outcome\":\"approve\"}",
                LlmStopReason.EndTurn,
                null,
                new LlmUsage(1100, 80, 0, 0, 0),
                "claude-haiku-4-5",
                "claude-haiku-4-5",
                "msg_tool_2",
                TimeSpan.FromMilliseconds(700),
                1)
            {
                Blocks = [LlmContentBlock.OfText("{\"outcome\":\"approve\"}")],
            };

    [Fact]
    public async Task A_two_step_tool_conversation_is_replayed_by_a_fresh_read_only_gateway_with_zero_model_calls()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(ToolAnswer);

            var writer = Gateway(dir, inner, LlmCacheMode.ReadWrite, priced: true);
            var freshOne = await writer.CompleteAsync(ToolStepOne(), Ct);
            var freshTwo = await writer.CompleteAsync(ToolStepTwo(), Ct);

            var reader = Gateway(dir, inner, priced: true);
            var replayOne = await reader.CompleteAsync(ToolStepOne(), Ct);
            var replayTwo = await reader.CompleteAsync(ToolStepTwo(), Ct);

            Assert.Equal(2, inner.Calls);
            Assert.Equal(1, freshOne.HttpAttempts);
            Assert.Equal(1, freshTwo.HttpAttempts);
            Assert.True(freshOne.Cache!.Stored);
            Assert.True(freshTwo.Cache!.Stored);

            Assert.Equal(LlmStopReason.ToolUse, replayOne.StopReason);
            Assert.Equal(ToolStepOneBlocks, replayOne.Blocks);
            Assert.Equal([new LlmToolCall("toolu_01", "get_policy", "{}")], replayOne.ToolCalls);
            Assert.Equal(0, replayOne.HttpAttempts);
            Assert.Equal(LlmCacheStatus.Hit, replayOne.Cache!.Status);
            Assert.Equal(TimeSpan.FromMilliseconds(900), replayOne.Latency);
            Assert.NotNull(freshOne.Cost);
            Assert.NotNull(replayOne.Cost);
            Assert.Equal(freshOne.Cost.AmountUsd, replayOne.Cost.AmountUsd);

            Assert.Equal(LlmStopReason.EndTurn, replayTwo.StopReason);
            Assert.Equal(freshTwo.Text, replayTwo.Text);
            Assert.Equal(freshTwo.Blocks, replayTwo.Blocks);
            Assert.Equal(0, replayTwo.HttpAttempts);
            Assert.Equal(LlmCacheStatus.Hit, replayTwo.Cache!.Status);

            var files = EntryFiles(dir);
            Assert.Equal(2, files.Length);
            foreach (var file in files)
            {
                using var parsed = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
                Assert.Equal("llm-cache-entry-2", parsed.RootElement.GetProperty("entry_version").GetString());
                Assert.Equal(
                    System.Text.Json.JsonValueKind.Array,
                    parsed.RootElement.GetProperty("response").GetProperty("blocks").ValueKind);
            }
        });
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>An entry file exactly as Phase 3 wrote it: entry_version llm-cache-entry-1, no blocks, no tool_count.</summary>
    private static string LegacyEntryJson(string key) =>
        $$"""
        {
          "entry_version": "llm-cache-entry-1",
          "key": "{{key}}",
          "key_fields": {
            "model": "claude-haiku-4-5",
            "prompt_version": "extract-003",
            "repair_prompt_version": null,
            "schema_sha256": "{{Hex(Encoding.UTF8.GetBytes("{\"type\":\"object\"}"))}}",
            "document_sha256": "{{Hex(Pdf)}}",
            "replicate_salt": "",
            "follow_up_count": 0
          },
          "recorded_at": "2026-10-15T09:30:00.000Z",
          "response": {
            "text": "{\"ok\":true}",
            "stop_reason": "end_turn",
            "stop_detail": null,
            "usage": {
              "input_tokens": 1200,
              "output_tokens": 340,
              "cache_read_tokens": 50,
              "cache_write_5m_tokens": 0,
              "cache_write_1h_tokens": 0
            },
            "model_requested": "claude-haiku-4-5",
            "model_returned": "claude-haiku-4-5",
            "provider_message_id": "msg_phase3",
            "latency_ms": 1234,
            "original_http_attempts": 1,
            "cost": {
              "amount_usd": "0.00250000",
              "pricing_version": "2026-10-01",
              "warning": null
            }
          }
        }
        """;

    [Fact]
    public async Task An_entry_written_in_the_phase_3_llm_cache_entry_1_format_is_still_a_hit_with_no_blocks()
    {
        await WithTempDirectory(async dir =>
        {
            var request = Request();
            var key = LlmCacheKey.Compute(request);
            Directory.CreateDirectory(dir);
            File.WriteAllText(new LlmCacheStore(dir).PathFor(key), LegacyEntryJson(key));
            var inner = new CountingGateway(_ => Answer("must not be called"));

            var replay = await Gateway(dir, inner).CompleteAsync(request, Ct);

            Assert.Equal(0, inner.Calls);
            Assert.Equal(LlmCacheStatus.Hit, replay.Cache!.Status);
            Assert.Equal("{\"ok\":true}", replay.Text);
            Assert.Equal(LlmStopReason.EndTurn, replay.StopReason);
            Assert.Empty(replay.Blocks);
            Assert.Empty(replay.ToolCalls);
            Assert.Equal(0, replay.HttpAttempts);
            Assert.Equal("msg_phase3", replay.ProviderMessageId);
            Assert.Equal(0.0025m, replay.Cost!.AmountUsd);
        });
    }

    public static TheoryData<string> MalformedBlocks() => new()
    {
        "missing", "object", "string", "element_not_object", "unknown_kind", "tool_use_without_call", "tool_call_without_id",
    };

    [Theory]
    [MemberData(nameof(MalformedBlocks))]
    public async Task A_v2_entry_with_malformed_blocks_is_a_miss(string shape)
    {
        await WithTempDirectory(async dir =>
        {
            var request = ToolStepOne();
            await Gateway(dir, new CountingGateway(ToolAnswer), LlmCacheMode.ReadWrite).CompleteAsync(request, Ct);
            var path = Assert.Single(EntryFiles(dir));
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var response = root["response"]!.AsObject();
            switch (shape)
            {
                case "missing":
                    response.Remove("blocks");
                    break;
                case "object":
                    response["blocks"] = new System.Text.Json.Nodes.JsonObject();
                    break;
                case "string":
                    response["blocks"] = "text";
                    break;
                case "element_not_object":
                    response["blocks"] = new System.Text.Json.Nodes.JsonArray(7);
                    break;
                case "unknown_kind":
                    response["blocks"] = new System.Text.Json.Nodes.JsonArray(
                        new System.Text.Json.Nodes.JsonObject { ["kind"] = "image" });
                    break;
                case "tool_use_without_call":
                    response["blocks"] = new System.Text.Json.Nodes.JsonArray(
                        new System.Text.Json.Nodes.JsonObject { ["kind"] = "tool_use", ["tool_call"] = null });
                    break;
                case "tool_call_without_id":
                    response["blocks"] = new System.Text.Json.Nodes.JsonArray(
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["kind"] = "tool_use",
                            ["tool_call"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "get_policy", ["input_json"] = "{}" },
                        });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
            }

            File.WriteAllText(path, root.ToJsonString());
            var inner = new CountingGateway(ToolAnswer);

            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => Gateway(dir, inner).CompleteAsync(request, Ct));

            Assert.Equal(LlmFailureKind.CacheMiss, thrown.Kind);
            Assert.Equal(0, inner.Calls);
        });
    }

    [Fact]
    public async Task A_tool_conversation_entry_never_contains_the_system_prompt_a_tool_description_or_a_tool_result()
    {
        await WithTempDirectory(async dir =>
        {
            const string systemSentinel = "SYSTEM-SENTINEL-4b21d9";
            const string descriptionSentinel = "DESCRIPTION-SENTINEL-93c07a";
            const string resultSentinel = "RESULT-SENTINEL-1e55f0";
            const string promptSentinel = "PROMPT-SENTINEL-a60d3b";
            var request = ToolStepTwo() with
            {
                Prompt = promptSentinel,
                SystemPrompt = systemSentinel,
                Tools = [new LlmToolDefinition("get_policy", descriptionSentinel, PolicyToolSchema)],
                FollowUps =
                [
                    ToolStepTwo().FollowUps[0],
                    new LlmTurn(LlmTurnRole.User, string.Empty)
                    {
                        ToolResults = [new LlmToolResult("toolu_01", resultSentinel, false)],
                    },
                ],
            };
            var inner = new CountingGateway(ToolAnswer);

            await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(request, Ct);

            var text = File.ReadAllText(Assert.Single(EntryFiles(dir)));
            Assert.DoesNotContain(systemSentinel, text, StringComparison.Ordinal);
            Assert.DoesNotContain(descriptionSentinel, text, StringComparison.Ordinal);
            Assert.DoesNotContain(resultSentinel, text, StringComparison.Ordinal);
            Assert.DoesNotContain(promptSentinel, text, StringComparison.Ordinal);
            Assert.DoesNotContain(PolicyToolSchema, text, StringComparison.Ordinal);

            using var parsed = System.Text.Json.JsonDocument.Parse(text);
            var fields = parsed.RootElement.GetProperty("key_fields");
            Assert.Equal(System.Text.Json.JsonValueKind.Null, fields.GetProperty("document_sha256").ValueKind);
            Assert.Equal(1, fields.GetProperty("tool_count").GetInt32());
            Assert.Equal(2, fields.GetProperty("follow_up_count").GetInt32());
        });
    }

    [Fact]
    public async Task Tool_conversation_steps_with_different_tool_results_do_not_share_an_entry()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(ToolAnswer);
            var gateway = Gateway(dir, inner, LlmCacheMode.ReadWrite);
            var other = ToolStepTwo() with
            {
                FollowUps =
                [
                    ToolStepTwo().FollowUps[0],
                    new LlmTurn(LlmTurnRole.User, string.Empty)
                    {
                        ToolResults = [new LlmToolResult("toolu_01", "{\"buyer_cnpj\":\"Y\"}", false)],
                    },
                ],
            };

            await gateway.CompleteAsync(ToolStepTwo(), Ct);
            await gateway.CompleteAsync(other, Ct);
            await gateway.CompleteAsync(ToolStepTwo(), Ct);

            Assert.Equal(2, inner.Calls);
            Assert.Equal(2, EntryFiles(dir).Length);
        });
    }

    [Fact]
    public async Task A_read_only_gateway_over_an_empty_directory_raises_cache_miss_without_calling_the_provider()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var reader = Gateway(dir, inner);

            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => reader.CompleteAsync(Request(), Ct));

            Assert.Equal(LlmFailureKind.CacheMiss, thrown.Kind);
            Assert.Equal(0, inner.Calls);
        });
    }

    // ---------------------------------------------------------------- read_write

    [Fact]
    public async Task Read_write_misses_then_hits_and_the_hit_reports_replay_not_spend()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var gateway = Gateway(dir, inner, LlmCacheMode.ReadWrite, priced: true);

            var miss = await gateway.CompleteAsync(Request(), Ct);
            var hit = await gateway.CompleteAsync(Request(), Ct);

            Assert.Equal(1, inner.Calls);
            Assert.Equal(LlmCacheStatus.Miss, miss.Cache!.Status);
            Assert.True(miss.Cache.Stored);
            Assert.Equal(1, miss.HttpAttempts);

            Assert.Equal(LlmCacheStatus.Hit, hit.Cache!.Status);
            Assert.False(hit.Cache.Stored);
            Assert.Equal(0, hit.HttpAttempts);
            Assert.Equal(TimeSpan.FromMilliseconds(1234), hit.Latency);
            Assert.Equal(miss.Usage, hit.Usage);
            Assert.NotNull(hit.Cost!.AmountUsd);
            Assert.Equal(miss.Cost!.AmountUsd, hit.Cost.AmountUsd);
        });
    }

    [Fact]
    public async Task A_stored_unpriced_cost_and_a_stop_detail_survive_the_round_trip()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer() with { ModelRequested = "mystery-model", ModelReturned = null, ProviderMessageId = null });
            var writer = Gateway(dir, inner, LlmCacheMode.ReadWrite, priced: true);
            var fresh = await writer.CompleteAsync(Request(), Ct);
            var replay = await Gateway(dir, inner, priced: true).CompleteAsync(Request(), Ct);

            Assert.Null(fresh.Cost!.AmountUsd);
            Assert.Null(replay.Cost!.AmountUsd);
            Assert.Equal(fresh.Cost.Warning, replay.Cost.Warning);
            Assert.Null(replay.ModelReturned);
            Assert.Null(replay.ProviderMessageId);
            Assert.Equal(1, inner.Calls);
        });
    }

    [Theory]
    [InlineData(LlmStopReason.EndTurn)]
    [InlineData(LlmStopReason.ToolUse)]
    public async Task An_end_turn_or_tool_use_answer_is_stored_and_replayed(LlmStopReason stop)
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer(stop: stop));

            var response = await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(Request(), Ct);
            var replay = await Gateway(dir, inner).CompleteAsync(Request(), Ct);

            Assert.True(response.Cache!.Stored);
            Assert.Single(EntryFiles(dir));
            Assert.Equal(stop, replay.StopReason);
            Assert.Equal(1, inner.Calls);
        });
    }

    [Theory]
    [InlineData(LlmStopReason.MaxTokens)]
    [InlineData(LlmStopReason.Refusal)]
    [InlineData(LlmStopReason.StopSequence)]
    [InlineData(LlmStopReason.Other)]
    public async Task Only_an_end_turn_or_tool_use_answer_is_cached_so_these_are_not(LlmStopReason stop)
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer(stop: stop));
            var writer = Gateway(dir, inner, LlmCacheMode.ReadWrite);

            var response = await writer.CompleteAsync(Request(), Ct);

            Assert.False(response.Cache!.Stored);
            Assert.Equal(stop, response.StopReason);
            Assert.Empty(EntryFiles(dir));

            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(
                () => Gateway(dir, inner).CompleteAsync(Request(), Ct));
            Assert.Equal(LlmFailureKind.CacheMiss, thrown.Kind);
            Assert.Equal(1, inner.Calls);
        });
    }

    [Fact]
    public async Task A_gateway_exception_propagates_and_writes_nothing()
    {
        await WithTempDirectory(async dir =>
        {
            var failure = new LlmGatewayException(LlmFailureKind.Overloaded, "busy", 529, "req_1");
            var inner = new CountingGateway(_ => throw failure);
            var writer = Gateway(dir, inner, LlmCacheMode.ReadWrite);

            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => writer.CompleteAsync(Request(), Ct));

            Assert.Same(failure, thrown);
            Assert.Empty(EntryFiles(dir));
        });
    }

    // ---------------------------------------------------------------- read_only, refresh, off

    [Fact]
    public async Task Read_only_makes_no_provider_call_on_a_miss_and_names_the_key()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => Gateway(dir, inner).CompleteAsync(Request(), Ct));

            Assert.Equal(LlmFailureKind.CacheMiss, thrown.Kind);
            Assert.Contains(LlmCacheKey.Compute(Request()), thrown.Message, StringComparison.Ordinal);
            Assert.Equal(0, inner.Calls);
            Assert.Empty(EntryFiles(dir));
        });
    }

    [Fact]
    public async Task Refresh_calls_the_provider_overwrites_the_entry_and_reports_refresh()
    {
        await WithTempDirectory(async dir =>
        {
            var answers = new Queue<LlmResponse>([Answer("{\"v\":1}"), Answer("{\"v\":2}")]);
            var inner = new CountingGateway(_ => answers.Dequeue());

            await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(Request(), Ct);
            var refreshed = await Gateway(dir, inner, LlmCacheMode.Refresh).CompleteAsync(Request(), Ct);
            var replay = await Gateway(dir, inner).CompleteAsync(Request(), Ct);

            Assert.Equal(2, inner.Calls);
            Assert.Equal(LlmCacheStatus.Refresh, refreshed.Cache!.Status);
            Assert.True(refreshed.Cache.Stored);
            Assert.Equal(1, refreshed.HttpAttempts);
            Assert.Equal("{\"v\":2}", replay.Text);
            Assert.Equal(LlmCacheStatus.Hit, replay.Cache!.Status);
            Assert.Single(EntryFiles(dir));
        });
    }

    [Fact]
    public async Task A_refresh_that_does_not_end_the_turn_keeps_the_previous_entry()
    {
        await WithTempDirectory(async dir =>
        {
            var answers = new Queue<LlmResponse>([Answer("{\"v\":1}"), Answer("truncated", LlmStopReason.MaxTokens)]);
            var inner = new CountingGateway(_ => answers.Dequeue());

            await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(Request(), Ct);
            var refreshed = await Gateway(dir, inner, LlmCacheMode.Refresh).CompleteAsync(Request(), Ct);
            var replay = await Gateway(dir, inner).CompleteAsync(Request(), Ct);

            Assert.Equal(LlmStopReason.MaxTokens, refreshed.StopReason);
            Assert.False(refreshed.Cache!.Stored);
            Assert.Equal("{\"v\":1}", replay.Text);
            Assert.Equal(LlmStopReason.EndTurn, replay.StopReason);
        });
    }

    [Fact]
    public async Task A_refresh_that_fails_keeps_the_previous_entry()
    {
        await WithTempDirectory(async dir =>
        {
            var calls = 0;
            var inner = new CountingGateway(_ => ++calls == 1
                ? Answer("{\"v\":1}")
                : throw new LlmGatewayException(LlmFailureKind.Network, "down"));

            await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(Request(), Ct);
            await Assert.ThrowsAsync<LlmGatewayException>(
                () => Gateway(dir, inner, LlmCacheMode.Refresh).CompleteAsync(Request(), Ct));
            var replay = await Gateway(dir, inner).CompleteAsync(Request(), Ct);

            Assert.Equal("{\"v\":1}", replay.Text);
        });
    }

    [Fact]
    public async Task Off_bypasses_the_cache_entirely()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var gateway = Gateway(dir, inner, LlmCacheMode.Off);

            var first = await gateway.CompleteAsync(Request(), Ct);
            var second = await gateway.CompleteAsync(Request(), Ct);

            Assert.Equal(2, inner.Calls);
            Assert.Null(first.Cache);
            Assert.Null(second.Cache);
            Assert.Equal(1, second.HttpAttempts);
            Assert.Empty(EntryFiles(dir));
        });
    }

    [Fact]
    public async Task The_request_cache_mode_overrides_the_default_mode_in_each_direction()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());

            // Default read_only, request read_write: the miss reaches the provider and is stored.
            var stored = await Gateway(dir, inner).CompleteAsync(Request() with { CacheMode = LlmCacheMode.ReadWrite }, Ct);
            Assert.True(stored.Cache!.Stored);
            Assert.Equal(LlmCacheMode.ReadWrite, stored.Cache.Mode);

            // Default read_write, request off: bypass, so the stored entry is not consulted.
            var bypass = await Gateway(dir, inner, LlmCacheMode.ReadWrite)
                .CompleteAsync(Request() with { CacheMode = LlmCacheMode.Off }, Ct);
            Assert.Null(bypass.Cache);
            Assert.Equal(2, inner.Calls);

            // Default off, request read_only: the entry written above is served.
            var replay = await Gateway(dir, inner, LlmCacheMode.Off)
                .CompleteAsync(Request() with { CacheMode = LlmCacheMode.ReadOnly }, Ct);
            Assert.Equal(LlmCacheStatus.Hit, replay.Cache!.Status);
            Assert.Equal(2, inner.Calls);
        });
    }

    [Fact]
    public async Task Different_replicate_salts_do_not_share_an_entry()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var gateway = Gateway(dir, inner, LlmCacheMode.ReadWrite);

            await gateway.CompleteAsync(Request("r1"), Ct);
            await gateway.CompleteAsync(Request("r2"), Ct);
            await gateway.CompleteAsync(Request("r1"), Ct);

            Assert.Equal(2, inner.Calls);
            Assert.Equal(2, EntryFiles(dir).Length);
        });
    }

    // ---------------------------------------------------------------- malformed entries (T-03-01)

    public static TheoryData<string> MalformedEntries() => new() { "garbage", "wrong_version", "other_key" };

    private static void Corrupt(string dir, string shape, LlmRequest request)
    {
        var path = new LlmCacheStore(dir).PathFor(LlmCacheKey.Compute(request));
        switch (shape)
        {
            case "garbage":
                File.WriteAllBytes(path, [0x00, 0xFF, 0x7B, 0x13, 0x37]);
                break;
            case "wrong_version":
                File.WriteAllText(path, File.ReadAllText(path).Replace("llm-cache-entry-2", "llm-cache-entry-0", StringComparison.Ordinal));
                break;
            case "other_key":
                var other = Request("another-replicate");
                var otherKey = LlmCacheKey.Compute(other);
                var otherPath = new LlmCacheStore(dir).PathFor(otherKey);
                File.Delete(path);
                File.WriteAllText(path, File.ReadAllText(otherPath));
                File.Delete(otherPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    private static async Task SeedAsync(string dir, LlmRequest request, string shape)
    {
        var seeder = Gateway(dir, new CountingGateway(_ => Answer("seed")), LlmCacheMode.ReadWrite);
        await seeder.CompleteAsync(request, Ct);
        if (shape == "other_key")
        {
            await seeder.CompleteAsync(Request("another-replicate"), Ct);
        }

        Corrupt(dir, shape, request);
    }

    [Theory]
    [MemberData(nameof(MalformedEntries))]
    public async Task A_malformed_entry_is_a_miss_for_read_only(string shape)
    {
        await WithTempDirectory(async dir =>
        {
            var request = Request();
            await SeedAsync(dir, request, shape);
            var inner = new CountingGateway(_ => Answer());

            var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => Gateway(dir, inner).CompleteAsync(request, Ct));

            Assert.Equal(LlmFailureKind.CacheMiss, thrown.Kind);
            Assert.Equal(0, inner.Calls);
        });
    }

    [Theory]
    [MemberData(nameof(MalformedEntries))]
    public async Task A_malformed_entry_is_replaced_by_a_valid_one_on_read_write(string shape)
    {
        await WithTempDirectory(async dir =>
        {
            var request = Request();
            await SeedAsync(dir, request, shape);
            var inner = new CountingGateway(_ => Answer("fresh"));

            var response = await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(request, Ct);
            var replay = await Gateway(dir, inner).CompleteAsync(request, Ct);

            Assert.Equal(1, inner.Calls);
            Assert.Equal(LlmCacheStatus.Miss, response.Cache!.Status);
            Assert.True(response.Cache.Stored);
            Assert.Equal("fresh", replay.Text);
            Assert.Equal(1, EntryFiles(dir).Count(f => Path.GetFileName(f) == LlmCacheKey.Compute(request) + ".json"));
        });
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("0123456789abcdef")]
    public void A_key_that_is_not_64_lowercase_hex_never_reaches_a_path(string key)
    {
        var store = new LlmCacheStore(Path.Combine(Path.GetTempPath(), "carimbo-never-created"));

        Assert.Throws<ArgumentException>(() => store.PathFor(key));
        Assert.Throws<ArgumentException>(() => store.TryRead(key, out _));
        Assert.False(Directory.Exists(store.Directory));
    }

    // ---------------------------------------------------------------- atomic writes (LLM-04)

    [Fact]
    public async Task Concurrent_identical_misses_leave_one_complete_entry_and_no_temporary_file()
    {
        await WithTempDirectory(async dir =>
        {
            var inner = new CountingGateway(_ => Answer());
            var gateway = Gateway(dir, inner, LlmCacheMode.ReadWrite);

            var responses = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Task.Run(() => gateway.CompleteAsync(Request(), Ct), Ct)));

            Assert.Equal(8, responses.Length);
            Assert.InRange(inner.Calls, 1, 8);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            var file = Assert.Single(Directory.GetFiles(dir));
            Assert.Equal(LlmCacheKey.Compute(Request()) + ".json", Path.GetFileName(file));

            using var parsed = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
            Assert.Equal("llm-cache-entry-2", parsed.RootElement.GetProperty("entry_version").GetString());

            var replay = await Gateway(dir, inner).CompleteAsync(Request(), Ct);
            Assert.Equal(LlmCacheStatus.Hit, replay.Cache!.Status);
        });
    }

    // ---------------------------------------------------------------- what is on disk (T-03-04)

    [Fact]
    public async Task An_entry_never_contains_the_prompt_the_schema_or_the_document()
    {
        await WithTempDirectory(async dir =>
        {
            const string promptSentinel = "PROMPT-SENTINEL-8f3a1c";
            const string schemaSentinel = "SCHEMA-SENTINEL-27d0be";
            var request = new LlmRequest(
                "claude-haiku-4-5",
                1024,
                promptSentinel,
                new LlmDocument("application/pdf", Pdf),
                "{\"title\":\"" + schemaSentinel + "\"}")
            {
                FollowUps = [new LlmTurn(LlmTurnRole.Assistant, "FOLLOW-UP-SENTINEL-55e9"), new LlmTurn(LlmTurnRole.User, "Fix it.")],
            };
            var inner = new CountingGateway(_ => Answer());

            await Gateway(dir, inner, LlmCacheMode.ReadWrite).CompleteAsync(request, Ct);

            var text = File.ReadAllText(Assert.Single(EntryFiles(dir)));
            Assert.DoesNotContain(promptSentinel, text, StringComparison.Ordinal);
            Assert.DoesNotContain(schemaSentinel, text, StringComparison.Ordinal);
            Assert.DoesNotContain("FOLLOW-UP-SENTINEL-55e9", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(Pdf), text, StringComparison.Ordinal);
            Assert.DoesNotContain(Encoding.ASCII.GetString(Pdf), text, StringComparison.Ordinal);
            Assert.Contains("{\"ok\":true}", text.Replace("\\u0022", "\"", StringComparison.Ordinal), StringComparison.Ordinal);
        });
    }
}

/// <summary>A provider double that counts calls (thread-safe) and answers from a function or throws.</summary>
internal sealed class CountingGateway(Func<LlmRequest, LlmResponse> respond) : ILlmGateway
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult(respond(request));
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
