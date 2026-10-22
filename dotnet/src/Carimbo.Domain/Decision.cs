using System.ComponentModel;
using System.Reflection;

namespace Carimbo.Domain;

/// <summary>The typed outcome of reviewing an invoice. Never reachable from <see cref="Invoice"/>.</summary>
public enum DecisionOutcome
{
    Approve,
    Reject,
    Escalate,
}

/// <summary>
/// Why a decision is an escalation. Required on every <see cref="Decision"/> so the exported schema keeps a
/// typed enum: a nullable enum would export as the literal <c>true</c>. <see cref="None"/> goes with approve
/// and reject. <see cref="ToolFailed"/> and <see cref="StepBudgetExhausted"/> are assigned only by code, never
/// by the model: the agent's parse boundary types a model answer whose cause its outcome does not allow as
/// schema_invalid, so the schema stays one type while the model-allowed pairs are enforced in code.
/// </summary>
public enum DecisionCause
{
    None,
    ExtractionNotSuccess,
    PolicyWarning,
    OverLimit,
    ToolFailed,
    StepBudgetExhausted,
}

/// <summary>One citation of a tool result that supports a decision.</summary>
public sealed record EvidenceItem(
    [property: Description("Name of the tool whose result supports this citation, for example get_invoice")]
    string Source,
    [property: Description("Field path or rule id inside that result, for example recipient.tax_id or TAX_CODE_UNSUPPORTED")]
    string Ref,
    [property: Description("The cited value exactly as the tool returned it, as a string")]
    string Value);

/// <summary>
/// A typed approve / reject / escalate decision with the evidence and reason behind it. The property order
/// puts the evidence and the reason before the verdict because structured output is generated in schema
/// order. Never reachable from <see cref="Invoice"/>.
/// </summary>
public sealed record Decision(
    [property: Description("Citations of tool results that support the decision, in the order they were used")]
    IReadOnlyList<EvidenceItem> Evidence,
    [property: Description("One or two sentences explaining the decision")]
    string Reason,
    [property: Description("approve, reject or escalate")]
    DecisionOutcome Outcome,
    [property: Description("none for approve and reject; for escalate, extraction_not_success, policy_warning or over_limit")]
    DecisionCause Cause)
{
    /// <summary>
    /// The dotted snake_case JSON paths, in schema property order, where the decision holds null although the
    /// schema requires a value: a null evidence list, a null evidence element (<c>evidence[0]</c>), a null
    /// citation member (<c>evidence[0].ref</c>) or a null reason. The serializer rejects a null member under
    /// <c>RespectNullableAnnotations</c> but accepts a null collection element, which is the hole this closes.
    /// Paths only, never values. A method, not a property, so neither the serializer nor the schema exporter
    /// sees it.
    /// </summary>
    public IReadOnlyList<string> NullViolations()
    {
        var violations = new List<string>();
        DomainReflection.CollectNulls(this, string.Empty, new NullabilityInfoContext(), violations);
        return violations;
    }
}
