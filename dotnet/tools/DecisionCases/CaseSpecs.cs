using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.Validation;

namespace DecisionCases;

/// <summary>
/// One decision case to generate: a skeleton base case, a mutation of its mapped invoice, and optionally a
/// forced extraction status. <see cref="IntendedOutcome"/> and <see cref="IntendedCause"/> are a self-check
/// only: generation fails when <see cref="DecisionRules.Label"/> disagrees, and the intended label is never
/// written anywhere.
/// </summary>
/// <param name="CaseId">Neutral id (dc-NNN) that does not name the label.</param>
/// <param name="Intent">What the case is for; written to manifest.json only.</param>
/// <param name="BaseCase">The skeleton case whose XML is mapped.</param>
/// <param name="Mutation">A one-line description of <paramref name="Apply"/>; written to manifest.json only.</param>
/// <param name="ForcedStatus">A typed extraction status to force (the invoice is then dropped), or null.</param>
/// <param name="IntendedOutcome">The outcome the spec expects the rule to give.</param>
/// <param name="IntendedCause">The cause the spec expects the rule to give.</param>
/// <param name="Apply">The mutation of the mapped invoice.</param>
/// <param name="Failure">The typed failure recorded with a forced status.</param>
internal sealed record CaseSpec(
    string CaseId,
    string Intent,
    string BaseCase,
    string Mutation,
    string? ForcedStatus,
    DecisionOutcome IntendedOutcome,
    DecisionCause IntendedCause,
    Func<Invoice, Invoice> Apply,
    CaseFailure? Failure = null);

/// <summary>The synthetic buyer policy and the case specs, in code (D-34).</summary>
internal static class CaseSpecs
{
    /// <summary>
    /// A synthetic alphanumeric CNPJ: the base CARIMBO00001 is not any skeleton party and not any nfelib
    /// sample, and the check digits are computed, so the validator accepts it (D-14).
    /// </summary>
    public static readonly string BuyerCnpj = "CARIMBO00001" + Identifiers.CnpjCheckDigits("CARIMBO00001");

    /// <summary>Invoices above this total escalate (rule 4); a total equal to it does not.</summary>
    public static readonly Money ApprovalLimit = Money.Parse("200000.00");

    public static IReadOnlyList<CaseSpec> All { get; } =
    [
        new(
            "dc-001",
            "approve-simples",
            "case-001",
            "recipient set to the buyer CNPJ",
            null,
            DecisionOutcome.Approve,
            DecisionCause.None,
            ToBuyer),
    ];

    /// <summary>Addresses the invoice to the buyer: the recipient tax id becomes the buyer CNPJ.</summary>
    private static Invoice ToBuyer(Invoice invoice) =>
        invoice with { Recipient = invoice.Recipient with { TaxId = BuyerCnpj, TaxIdKind = TaxIdKind.Cnpj } };
}
