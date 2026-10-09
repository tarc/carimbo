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

    /// <summary>
    /// Rounds to two decimals half away from zero, the fiscal rule (2.345 gives 2.35, -2.345 gives
    /// -2.35), which equals Python's <c>ROUND_HALF_UP</c>. The default <see cref="decimal.Round(decimal, int)"/>
    /// is half-even and would give 2.34. A result of zero is returned as a plain <c>0.00m</c> so a
    /// negative zero (for example from -0.004) never reaches the wire.
    /// </summary>
    public static decimal RoundHalfUp(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0m ? 0.00m : rounded;
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

/// <summary>
/// A quantity or unit price. On the wire it is a non-negative JSON string with exactly four fraction
/// digits and an invariant decimal point, for example <c>"2.0000"</c>, as printed in the QTD. and
/// V.UNIT. columns. Fraction digits are never dropped or padded: <c>"2.00"</c> is rejected.
/// </summary>
[JsonConverter(typeof(Decimal4JsonConverter))]
public readonly record struct Decimal4(decimal Amount)
{
    private static readonly Regex WireFormat = new(
        Patterns.Decimal4,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses the wire form: the pattern first, then an invariant decimal parse without sign.</summary>
    /// <exception cref="FormatException">The text is not a four-decimal invariant number, or does not fit in a decimal.</exception>
    public static Decimal4 Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!WireFormat.IsMatch(text))
        {
            throw new FormatException($"'{text}' is not a decimal with four decimal places and a '.' separator.");
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            throw new FormatException($"'{text}' is not a decimal that fits in a decimal.");
        }

        return new Decimal4(amount);
    }

    /// <summary>The wire form: four fraction digits, invariant culture.</summary>
    public override string ToString() => Amount.ToString("0.0000", CultureInfo.InvariantCulture);
}

/// <summary>Reads and writes <see cref="Decimal4"/> as a four-decimal JSON string and nothing else.</summary>
public sealed class Decimal4JsonConverter : JsonConverter<Decimal4>
{
    public override Decimal4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A four-decimal number must be a JSON string, found {reader.TokenType}.");
        }

        try
        {
            return Decimal4.Parse(reader.GetString()!);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
        catch (OverflowException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Decimal4 value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// A tax rate in percent. On the wire it is a non-negative JSON string with exactly two fraction
/// digits and an invariant decimal point, without the percent sign, for example <c>"18.00"</c>.
/// </summary>
[JsonConverter(typeof(RateJsonConverter))]
public readonly record struct Rate(decimal Amount)
{
    private static readonly Regex WireFormat = new(
        Patterns.Rate,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses the wire form: the pattern first, then an invariant decimal parse without sign.</summary>
    /// <exception cref="FormatException">The text is not a two-decimal invariant number, or does not fit in a decimal.</exception>
    public static Rate Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!WireFormat.IsMatch(text))
        {
            throw new FormatException($"'{text}' is not a rate with two decimal places and a '.' separator.");
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            throw new FormatException($"'{text}' is not a rate that fits in a decimal.");
        }

        return new Rate(amount);
    }

    /// <summary>The wire form: two fraction digits, invariant culture.</summary>
    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Reads and writes <see cref="Rate"/> as a two-decimal JSON string and nothing else.</summary>
public sealed class RateJsonConverter : JsonConverter<Rate>
{
    public override Rate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A rate must be a JSON string, found {reader.TokenType}.");
        }

        try
        {
            return Rate.Parse(reader.GetString()!);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
        catch (OverflowException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Rate value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Reads and writes every enum as its snake_case wire name and nothing else. A value is accepted only
/// from a JSON string that equals a wire name ordinally after JSON unescaping, which is the equality a
/// JSON Schema validator applies to an <c>enum</c>. Integers, numeric strings, other casings, padded
/// names and comma lists are rejected, because the committed schema rejects them. A failure carries no
/// message, so the model's value is never echoed: System.Text.Json adds the target type and JSON path.
/// </summary>
public sealed class StrictEnumJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        var converterType = typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class StrictEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private readonly Dictionary<string, TEnum> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<TEnum, string> _byValue = [];

        public StrictEnumConverter()
        {
            var values = Enum.GetValues<TEnum>();
            var names = Wire.EnumNames(typeof(TEnum));
            for (var i = 0; i < values.Length; i++)
            {
                _byName[names[i]] = values[i];
                _byValue[values[i]] = names[i];
            }
        }

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String
                && reader.GetString() is { } text
                && _byName.TryGetValue(text, out var value))
            {
                return value;
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (!_byValue.TryGetValue(value, out var name))
            {
                throw new JsonException();
            }

            writer.WriteStringValue(name);
        }
    }
}

/// <summary>JSON wire conventions of the domain.</summary>
public static class Wire
{
    /// <summary>
    /// The single options object for schema export (plan 01-04) and for parsing model output
    /// (plan 01-03), so the schema describes exactly what is accepted. snake_case names, string
    /// enums, money, quantities and rates as decimal strings; unknown, missing or null members raise
    /// <see cref="JsonException"/>. The serializer enforces names, types, required and non-null
    /// members and the decimal patterns (via the Money, Decimal4 and Rate converters), and reads an
    /// enum only by its exact wire name (<see cref="StrictEnumJsonConverter"/>). It ignores the
    /// <c>[RegularExpression]</c> patterns on string members and does not reject a null list
    /// element; <see cref="Invoice.PatternViolations"/> and <see cref="Invoice.NullViolations"/>
    /// check those, and the extractor applies both before reporting success.
    /// Built explicitly: the ASP.NET web defaults preset would turn every number into a
    /// string-or-number union in the exported schema.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// The wire names of the defined members of an enum: snake_case of each member name, in ascending
    /// value order. The strict enum converter and the exported schema both use this table, so the
    /// parser and the schema cannot drift apart.
    /// </summary>
    public static IReadOnlyList<string> EnumNames(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        if (!enumType.IsEnum)
        {
            throw new ArgumentException($"{enumType} is not an enum.", nameof(enumType));
        }

        return Enum.GetValues(enumType)
            .Cast<object>()
            .Select(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(Enum.GetName(enumType, value)!))
            .ToArray();
    }

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
        options.Converters.Add(new StrictEnumJsonConverter());
        options.MakeReadOnly();
        return options;
    }
}
