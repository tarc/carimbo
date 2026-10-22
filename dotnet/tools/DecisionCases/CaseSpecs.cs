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
        new(
            "dc-002",
            "reject-ordering-trap",
            "case-003",
            "none (CPF recipient and a total over the limit: the recipient rule comes first)",
            null,
            DecisionOutcome.Reject,
            DecisionCause.None,
            Unchanged),
        new(
            "dc-003",
            "escalate-warning",
            "case-002",
            "recipient set to the buyer CNPJ; items[0].cst_csosn set to its origin digit and 10 (a CST with no tax rule)",
            null,
            DecisionOutcome.Escalate,
            DecisionCause.PolicyWarning,
            invoice => WithFirstItem(ToBuyer(invoice), item => item with { CstCsosn = item.CstCsosn[..1] + "10" })),
        new(
            "dc-004",
            "reject-not-us",
            "case-001",
            "none (the recipient is another company)",
            null,
            DecisionOutcome.Reject,
            DecisionCause.None,
            Unchanged),
        new(
            "dc-005",
            "escalate-typed-failure",
            "case-002",
            "extraction status forced to refused; invoice and findings dropped",
            "refused",
            DecisionOutcome.Escalate,
            DecisionCause.ExtractionNotSuccess,
            Unchanged,
            new CaseFailure("refused", "The model declined to extract the document.")),
        new(
            "dc-006",
            "escalate-over-limit",
            "case-003",
            "recipient set to the buyer CNPJ (kind cnpj)",
            null,
            DecisionOutcome.Escalate,
            DecisionCause.OverLimit,
            ToBuyer),
        new(
            "dc-007",
            "approve-normal",
            "case-002",
            "recipient set to the buyer CNPJ",
            null,
            DecisionOutcome.Approve,
            DecisionCause.None,
            ToBuyer),
        new(
            "dc-008",
            "escalate-validation-failed",
            "case-001",
            "recipient set to the buyer CNPJ; items[0].total raised by 1.00 so ITEM_ARITH reports an error",
            null,
            DecisionOutcome.Escalate,
            DecisionCause.ExtractionNotSuccess,
            invoice => WithFirstItem(ToBuyer(invoice), item => item with { Total = new Money(item.Total.Amount + 1.00m) })),
    ];

    /// <summary>Addresses the invoice to the buyer: the recipient tax id becomes the buyer CNPJ.</summary>
    private static Invoice ToBuyer(Invoice invoice) =>
        invoice with { Recipient = invoice.Recipient with { TaxId = BuyerCnpj, TaxIdKind = TaxIdKind.Cnpj } };

    private static Invoice Unchanged(Invoice invoice) => invoice;

    /// <summary>Replaces the first line item and keeps the list order.</summary>
    private static Invoice WithFirstItem(Invoice invoice, Func<LineItem, LineItem> change) =>
        invoice with { Items = [change(invoice.Items[0]), .. invoice.Items.Skip(1)] };
}
