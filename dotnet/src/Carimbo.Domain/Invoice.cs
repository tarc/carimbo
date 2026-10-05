using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

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
    Money TotalAmount);

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
