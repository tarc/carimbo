using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Carimbo.Domain;

/// <summary>
/// A monetary amount. On the wire it is a JSON string with exactly two fraction digits and an
/// invariant decimal point, for example <c>"1234.50"</c>. A pt-BR formatted value such as
/// <c>12,34</c> is rejected, never read as 1234.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public readonly record struct Money(decimal Amount)
{
    private static readonly Regex WireFormat = new(
        Patterns.Money,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses the wire form. The pattern is checked before <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>
    /// because the default number styles accept "12,34" as 1234 (thousands separator).
    /// </summary>
    /// <exception cref="FormatException">
    /// The text is not an invariant decimal with two fraction digits, or the amount does not fit in a decimal.
    /// </exception>
    public static Money Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // The pattern's trailing anchor also matches before a final newline. The restricted
        // NumberStyles below do not allow trailing white space, so such input still throws.
        if (!WireFormat.IsMatch(text))
        {
            throw new FormatException($"'{text}' is not a monetary amount with two decimal places and a '.' separator.");
        }

        // TryParse, not Parse: the pattern has no length bound, so a model-controlled amount can
        // have more integer digits than a decimal holds, and the throwing parse would raise
        // OverflowException instead of the FormatException callers handle.
        if (!decimal.TryParse(
                text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            throw new FormatException($"'{text}' is not a monetary amount that fits in a decimal.");
        }

        return new Money(amount);
    }

    /// <summary>The wire form: two fraction digits, invariant culture.</summary>
    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Reads and writes <see cref="Money"/> as a two-decimal JSON string and nothing else.</summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Money must be a JSON string, found {reader.TokenType}.");
        }

        try
        {
            return Money.Parse(reader.GetString()!);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
        catch (OverflowException ex)
        {
            // Defence in depth: Money.Parse reports out-of-range amounts as FormatException, but
            // System.Text.Json does not wrap an OverflowException thrown by a converter.
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>JSON wire conventions of the domain.</summary>
public static class Wire
{
    /// <summary>
    /// The single options object for schema export (plan 01-04) and for parsing model output
    /// (plan 01-03), so the schema describes exactly what is accepted. snake_case names, string
    /// enums, money as decimal strings; unknown, missing or null members raise
    /// <see cref="JsonException"/>. Built explicitly: the ASP.NET web defaults preset would turn
    /// every number into a string-or-number union in the exported schema.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.MakeReadOnly();
        return options;
    }
}
