using System.Globalization;
using System.Text.Json;

namespace Carimbo.Llm;

/// <summary>
/// USD-per-million-token rates loaded from the embedded, versioned <c>pricing.json</c>. Rates are data,
/// never constants in code, and every calculation uses <see cref="decimal"/>.
/// </summary>
public sealed class LlmPricingTable
{
    private const string ResourceName = "Carimbo.Llm.pricing.json";
    private const decimal TokensPerUnit = 1_000_000m;
    private const int Places = 8;

    private readonly IReadOnlyDictionary<string, Rates> models;
    private readonly IReadOnlyDictionary<string, string> aliases;

    private LlmPricingTable(
        string version,
        IReadOnlyDictionary<string, Rates> models,
        IReadOnlyDictionary<string, string> aliases)
    {
        Version = version;
        this.models = models;
        this.aliases = aliases;
    }

    public string Version { get; }

    /// <summary>Parses the table embedded in this assembly.</summary>
    public static LlmPricingTable LoadEmbedded()
    {
        using var stream = typeof(LlmPricingTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var models = new Dictionary<string, Rates>(StringComparer.Ordinal);
        foreach (var model in root.GetProperty("models").EnumerateObject())
        {
            models[model.Name] = new Rates(
                Rate(model.Value, "input"),
                Rate(model.Value, "cache_write_5m"),
                Rate(model.Value, "cache_write_1h"),
                Rate(model.Value, "cache_read"),
                Rate(model.Value, "output"));
        }

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in root.GetProperty("aliases").EnumerateObject())
        {
            aliases[alias.Name] = alias.Value.GetString()
                ?? throw new InvalidOperationException($"Alias {alias.Name} has no target.");
        }

        return new LlmPricingTable(root.GetProperty("pricing_version").GetString()!, models, aliases);
    }

    /// <summary>True when <paramref name="model"/> (or the base model its alias names) has a rate row.</summary>
    public bool CanPrice(string model) => Resolve(model) is not null;

    /// <summary>
    /// Prices one call. A model missing from the table is reported as an unpriced cost, never as zero.
    /// </summary>
    public LlmCost Price(string model, LlmUsage usage)
    {
        var rates = Resolve(model);
        if (rates is null)
        {
            return new LlmCost(null, Version, $"unpriced_model:{model}");
        }

        var micro =
            (usage.InputTokens * rates.Input)
            + (usage.CacheWrite5mTokens * rates.CacheWrite5m)
            + (usage.CacheWrite1hTokens * rates.CacheWrite1h)
            + (usage.CacheReadTokens * rates.CacheRead)
            + (usage.OutputTokens * rates.Output);

        var amount = decimal.Round(micro / TokensPerUnit, Places, MidpointRounding.AwayFromZero);
        return new LlmCost(amount, Version, null);
    }

    private static decimal Rate(JsonElement row, string name) =>
        decimal.Parse(row.GetProperty(name).GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private Rates? Resolve(string model)
    {
        if (models.TryGetValue(model, out var direct))
        {
            return direct;
        }

        return aliases.TryGetValue(model, out var target) && models.TryGetValue(target, out var aliased)
            ? aliased
            : null;
    }

    private sealed record Rates(decimal Input, decimal CacheWrite5m, decimal CacheWrite1h, decimal CacheRead, decimal Output);
}

/// <summary>
/// Decorates any <see cref="ILlmGateway"/> so every response carries a cost from the versioned table.
/// Exceptions pass through untouched: a call that produced no response has nothing to price.
/// </summary>
public sealed class CostAccountingLlmGateway(ILlmGateway inner, LlmPricingTable table) : ILlmGateway
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var response = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

        // The model the provider actually served is what was billed; fall back to the requested id
        // when the provider returned none or one the table does not know.
        var model = response.ModelReturned is { } returned && table.CanPrice(returned)
            ? returned
            : response.ModelRequested;

        return response with { Cost = table.Price(model, response.Usage) };
    }
}
