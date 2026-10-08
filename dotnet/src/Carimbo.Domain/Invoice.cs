using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Carimbo.Domain;

/// <summary>
/// Regular expressions shared by the domain attributes and the exported JSON Schema.
/// Every digit class is the explicit <c>[0-9]</c>, never the digit shorthand class,
/// because in .NET the shorthand also matches non-ASCII digits.
/// </summary>
public static class Patterns
{
    /// <summary>44 characters: 6 digits, 12 alphanumeric, 26 digits (admits the alphanumeric CNPJ form).</summary>
    public const string AccessKey = "^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$";

    /// <summary>CNPJ without punctuation: 12 alphanumeric characters plus 2 check digits.</summary>
    public const string Cnpj = "^[A-Z0-9]{12}[0-9]{2}$";

    /// <summary>Invariant decimal string with exactly two fraction digits.</summary>
    public const string Money = "^-?[0-9]+\\.[0-9]{2}$";

    /// <summary>
    /// True when <paramref name="value"/> is matched by <paramref name="pattern"/> over its whole length.
    /// A plain <see cref="Regex.IsMatch(string, string)"/> is not enough: the .NET <c>$</c> anchor also
    /// matches before a final newline, which JSON Schema (ECMA-262) does not, so it would accept
    /// <c>"key\n"</c>. There is no IgnoreCase on purpose: <c>[A-Z]</c> must stay uppercase-only.
    /// </summary>
    public static bool IsFullMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);

        var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant);
        return match.Success && match.Index == 0 && match.Length == value.Length;
    }
}

/// <summary>The Phase 1 subset of an NF-e: identity, issue date, parties and invoice total.</summary>
public sealed record Invoice(
    [property: Description("44-character NF-e access key (chave de acesso), digits and uppercase letters only, no spaces")]
    [property: RegularExpression(Patterns.AccessKey)]
    string AccessKey,
    int Number,
    int Series,
    DateOnly IssueDate,
    Party Issuer,
    Party Recipient,
    Money TotalAmount)
{
    /// <summary>
    /// The dotted snake_case JSON paths, in schema property order, of string members that are not a
    /// full match of the pattern their <see cref="RegularExpressionAttribute"/> declares. The
    /// serializer ignores those attributes (they only feed the exported schema), so this is what makes
    /// a parsed invoice schema-valid. Money is not listed: <see cref="MoneyJsonConverter"/> already
    /// enforces <see cref="Patterns.Money"/> while parsing. This checks the schema's patterns only: per
    /// D-03 there is no check-digit validation here, which belongs to the Phase 2 validators.
    /// A method, not a property, so neither the serializer nor the schema exporter sees it.
    /// </summary>
    public IReadOnlyList<string> PatternViolations()
    {
        var violations = new List<string>();
        if (!Patterns.IsFullMatch(Patterns.AccessKey, AccessKey))
        {
            violations.Add("access_key");
        }

        if (!Patterns.IsFullMatch(Patterns.Cnpj, Issuer.Cnpj))
        {
            violations.Add("issuer.cnpj");
        }

        if (!Patterns.IsFullMatch(Patterns.Cnpj, Recipient.Cnpj))
        {
            violations.Add("recipient.cnpj");
        }

        return violations;
    }
}

/// <summary>A company taking part in the invoice.</summary>
public sealed record Party(
    [property: Description("CNPJ without punctuation: 12 alphanumeric characters plus 2 check digits")]
    [property: RegularExpression(Patterns.Cnpj)]
    string Cnpj,
    [property: Description("Company name exactly as printed")]
    string Name);

/// <summary>The typed outcome of reviewing an invoice. Never reachable from <see cref="Invoice"/>.</summary>
public enum DecisionOutcome
{
    Approve,
    Reject,
    Escalate,
}

/// <summary>A typed approve / reject / escalate decision with the reason behind it.</summary>
public sealed record Decision(DecisionOutcome Outcome, string Reason);
