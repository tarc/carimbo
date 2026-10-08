using Carimbo.Domain;

namespace Carimbo.Validation;

/// <summary>
/// Runs the deterministic rule catalogue (D-23) over an invoice and lists every problem as a structured
/// finding. Pure: the reference date is a parameter (D-11), there is no clock, no I/O, no logging and no
/// catch-all, so a rule bug surfaces as a failing test instead of as "no findings". The same input always
/// yields the identical list, in a fixed order: identity, access key, dates, items by ascending index,
/// totals, installments.
/// </summary>
public sealed class InvoiceValidator(ValidationOptions options)
{
    /// <summary>The options this validator runs with.</summary>
    public ValidationOptions Options { get; } = options;

    /// <summary>All findings for <paramref name="invoice"/>; empty when it passes every rule.</summary>
    /// <param name="invoice">The parsed invoice. Any decimal, string or date value is accepted and checked.</param>
    /// <param name="referenceDate">"Today" for the plausibility of the issue date; never read from a clock.</param>
    public IReadOnlyList<ValidationFinding> Validate(Invoice invoice, DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var findings = new List<ValidationFinding>();
        IdentityRules.Append(invoice, findings);
        KeyAndDateRules.AppendKeyCrossChecks(invoice, findings);
        KeyAndDateRules.AppendDates(invoice, referenceDate, findings);
        ArithmeticRules.Append(invoice, Options, findings);
        return findings;
    }
}
