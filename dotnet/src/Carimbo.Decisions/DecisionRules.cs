using Carimbo.Domain;

namespace Carimbo.Decisions;

/// <summary>The label a case gets from <see cref="DecisionRules.Label"/>, and the number of the rule that gave it.</summary>
public sealed record DecisionLabel(DecisionOutcome Outcome, DecisionCause Cause, int Rule);

/// <summary>
/// The labelling rule <c>decision-labels-1</c> (D-34), the single implementation. The generator labels every
/// case with it, the agent prompt states the same list in prose, and the grader reads its output from
/// <c>expected.json</c>.
/// </summary>
public static class DecisionRules
{
    /// <summary>The version string recorded in the manifest and in expected.json.</summary>
    public const string LabellingVersion = "decision-labels-1";

    /// <summary>
    /// Applies the five ordered rules; the first match wins:
    /// 1. a status other than success gives escalate / extraction_not_success;
    /// 2. a recipient tax id other than the buyer CNPJ (ordinal comparison) gives reject;
    /// 3. any warning finding gives escalate / policy_warning;
    /// 4. an invoice total strictly greater than the approval limit gives escalate / over_limit;
    /// 5. otherwise approve. Approve and reject carry cause none.
    /// </summary>
    /// <exception cref="InvalidOperationException">A success case holds no invoice.</exception>
    public static DecisionLabel Label(DecisionCaseFile @case, DecisionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(policy);

        if (!string.Equals(@case.Extraction.Status, "success", StringComparison.Ordinal))
        {
            return new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.ExtractionNotSuccess, 1);
        }

        var invoice = @case.Invoice
            ?? throw new InvalidOperationException("A case with status success must hold an invoice.");

        if (!string.Equals(invoice.Recipient.TaxId, policy.BuyerCnpj, StringComparison.Ordinal))
        {
            return new DecisionLabel(DecisionOutcome.Reject, DecisionCause.None, 2);
        }

        if (@case.Findings.Any(finding => string.Equals(finding.Severity, "warning", StringComparison.Ordinal)))
        {
            return new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.PolicyWarning, 3);
        }

        if (invoice.Totals.InvoiceTotal.Amount > policy.ApprovalLimit.Amount)
        {
            return new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.OverLimit, 4);
        }

        return new DecisionLabel(DecisionOutcome.Approve, DecisionCause.None, 5);
    }
}
