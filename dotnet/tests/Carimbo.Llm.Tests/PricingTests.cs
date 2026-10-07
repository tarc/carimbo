using Carimbo.Llm;
using Xunit;

namespace Carimbo.Llm.Tests;

public class PricingTests
{
    private static readonly LlmPricingTable Table = LlmPricingTable.LoadEmbedded();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Haiku_uncached_input_and_output_are_priced_exactly()
    {
        var cost = Table.Price("claude-haiku-4-5", new LlmUsage(1000, 200, 0, 0, 0));

        Assert.Equal(0.00200000m, cost.AmountUsd);
        Assert.Null(cost.Warning);
        Assert.Equal(Table.Version, cost.PricingVersion);
    }

    [Fact]
    public void Sonnet_prices_all_five_token_classes_together()
    {
        // 12 in, 5000 cache-write-5m, 3000 cache read, 300 out:
        // (12*2.00 + 5000*2.50 + 3000*0.20 + 300*10.00) / 1e6 = 16124 / 1e6.
        var cost = Table.Price("claude-sonnet-5-5", new LlmUsage(12, 300, 3000, 5000, 0));

        Assert.Equal(0.01612400m, cost.AmountUsd);
        Assert.Null(cost.Warning);
    }

    [Fact]
    public void Cache_write_1h_is_priced_at_its_own_rate()
    {
        var cost = Table.Price("claude-haiku-4-5", new LlmUsage(0, 0, 0, 0, 1000));

        Assert.Equal(0.00200000m, cost.AmountUsd);
    }

    [Fact]
    public void Cache_read_is_priced_at_its_own_rate()
    {
        var cost = Table.Price("claude-haiku-4-5", new LlmUsage(0, 0, 10000, 0, 0));

        Assert.Equal(0.00100000m, cost.AmountUsd);
    }

    [Fact]
    public void A_dated_snapshot_id_is_priced_as_its_base_model()
    {
        var cost = Table.Price("claude-haiku-4-5-20251001", new LlmUsage(1000, 200, 0, 0, 0));

        Assert.Equal(0.00200000m, cost.AmountUsd);
        Assert.Null(cost.Warning);
    }

    [Fact]
    public void An_unknown_model_is_unpriced_with_a_warning_never_zero()
    {
        var cost = Table.Price("claude-unknown-9", new LlmUsage(1000, 200, 0, 0, 0));

        Assert.Null(cost.AmountUsd);
        Assert.Equal("unpriced_model:claude-unknown-9", cost.Warning);
        Assert.Equal(Table.Version, cost.PricingVersion);
    }

    [Fact]
    public void Zero_usage_on_a_known_model_is_an_honest_zero()
    {
        var cost = Table.Price("claude-haiku-4-5", LlmUsage.Zero);

        Assert.Equal(0m, cost.AmountUsd);
        Assert.Equal("0.00000000", cost.AmountUsd!.Value.ToString("F8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(cost.Warning);
    }

    [Fact]
    public void Sub_cent_amounts_round_half_away_from_zero_at_eight_places()
    {
        // 1 cache-read token on Haiku is 0.10 / 1e6 = 0.0000001 exactly; 5 output tokens is 0.000025.
        Assert.Equal(0.00000010m, Table.Price("claude-haiku-4-5", new LlmUsage(0, 0, 1, 0, 0)).AmountUsd);
        Assert.Equal(0.00002500m, Table.Price("claude-haiku-4-5", new LlmUsage(0, 5, 0, 0, 0)).AmountUsd);
    }

    [Fact]
    public void Loading_the_embedded_table_twice_gives_the_same_version_and_resource_is_found_by_logical_name()
    {
        var again = LlmPricingTable.LoadEmbedded();

        Assert.Equal(Table.Version, again.Version);
        Assert.Equal("anthropic-2026-10-04", Table.Version);
        Assert.NotNull(typeof(LlmPricingTable).Assembly.GetManifestResourceStream("Carimbo.Llm.pricing.json"));
    }

    [Fact]
    public async Task The_decorator_sets_the_cost_by_the_returned_model_when_it_resolves()
    {
        var inner = new FixedGateway(Response("claude-sonnet-5-5", "claude-haiku-4-5-20251001", new LlmUsage(1000, 200, 0, 0, 0)));
        var gateway = new CostAccountingLlmGateway(inner, Table);

        var response = await gateway.CompleteAsync(Request("claude-sonnet-5-5"), Ct);

        Assert.Equal(0.00200000m, response.Cost!.AmountUsd);
        Assert.Equal("text", response.Text);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task The_decorator_falls_back_to_the_requested_model_when_the_returned_one_is_unknown_or_absent()
    {
        var unknownReturned = new CostAccountingLlmGateway(
            new FixedGateway(Response("claude-haiku-4-5", "claude-mystery-1", new LlmUsage(1000, 200, 0, 0, 0))),
            Table);
        var noneReturned = new CostAccountingLlmGateway(
            new FixedGateway(Response("claude-haiku-4-5", null, new LlmUsage(1000, 200, 0, 0, 0))),
            Table);

        Assert.Equal(0.00200000m, (await unknownReturned.CompleteAsync(Request("claude-haiku-4-5"), Ct)).Cost!.AmountUsd);
        Assert.Equal(0.00200000m, (await noneReturned.CompleteAsync(Request("claude-haiku-4-5"), Ct)).Cost!.AmountUsd);
    }

    [Fact]
    public async Task The_decorator_reports_unpriced_when_neither_model_resolves()
    {
        var gateway = new CostAccountingLlmGateway(
            new FixedGateway(Response("mystery-model", "mystery-model", new LlmUsage(1, 1, 0, 0, 0))),
            Table);

        var response = await gateway.CompleteAsync(Request("mystery-model"), Ct);

        Assert.Null(response.Cost!.AmountUsd);
        Assert.Equal("unpriced_model:mystery-model", response.Cost.Warning);
    }

    [Fact]
    public async Task The_decorator_passes_gateway_exceptions_through_unchanged()
    {
        var failure = new LlmGatewayException(LlmFailureKind.Overloaded, "busy", 529, "req_1");
        var gateway = new CostAccountingLlmGateway(new FixedGateway(failure), Table);

        var thrown = await Assert.ThrowsAsync<LlmGatewayException>(() => gateway.CompleteAsync(Request("claude-haiku-4-5"), Ct));

        Assert.Same(failure, thrown);
    }

    private static LlmRequest Request(string model) =>
        new(model, 1024, "prompt", new LlmDocument("application/pdf", new byte[] { 1, 2, 3 }), "{}");

    private static LlmResponse Response(string requested, string? returned, LlmUsage usage) =>
        new("text", LlmStopReason.EndTurn, null, usage, requested, returned, "msg_1", TimeSpan.FromMilliseconds(5), 1);

    private sealed class FixedGateway : ILlmGateway
    {
        private readonly LlmResponse? response;
        private readonly Exception? failure;

        public FixedGateway(LlmResponse response) => this.response = response;

        public FixedGateway(Exception failure) => this.failure = failure;

        public int Calls { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return failure is not null ? throw failure : Task.FromResult(response!);
        }
    }
}
