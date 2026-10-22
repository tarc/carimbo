using System.Text.Json;
using Carimbo.Domain;

namespace Carimbo.Decisions;

/// <summary>
/// What the four read-only case tools are called, how they are described and the JSON each one returns (AGT-01,
/// D-35). The Tools host returns exactly these strings and the live probe reuses them, so a description or a
/// rendering change is a deliberate change of every agent cache key (D-37, Pitfall 8) and of the live evidence.
/// Every rendering is built from a <see cref="DecisionCaseFile"/> or a <see cref="DecisionPolicy"/> only: never
/// from the manifest or expected.json, so no label can reach the agent (AGT-04, D-34).
/// </summary>
public static class CaseToolCatalog
{
    /// <summary>The tool that returns the extraction status of a case.</summary>
    public const string GetExtractionStatus = "get_extraction_status";

    /// <summary>The tool that returns the extracted invoice of a case.</summary>
    public const string GetInvoice = "get_invoice";

    /// <summary>The tool that returns the validator findings of a case.</summary>
    public const string GetValidatorFindings = "get_validator_findings";

    /// <summary>The tool that returns the buyer policy; it takes no argument.</summary>
    public const string GetPolicy = "get_policy";

    /// <summary>The description of <see cref="GetExtractionStatus"/>.</summary>
    public const string GetExtractionStatusDescription =
        "Returns the extraction status of a case (success, validation_failed, refused, truncated, schema_invalid or infrastructure_failure) and the failure, if any.";

    /// <summary>The description of <see cref="GetInvoice"/>.</summary>
    public const string GetInvoiceDescription =
        "Returns the invoice extracted for a case as JSON, or invoice null with a note when the extraction produced none.";

    /// <summary>The description of <see cref="GetValidatorFindings"/>.</summary>
    public const string GetValidatorFindingsDescription =
        "Returns the deterministic validator findings for a case: field, rule_id, expected, actual and severity (error or warning).";

    /// <summary>The description of <see cref="GetPolicy"/>.</summary>
    public const string GetPolicyDescription =
        "Returns the buyer policy: the buyer CNPJ and the approval limit in BRL.";

    /// <summary>The description of the <c>case_id</c> parameter of the three case tools.</summary>
    public const string CaseIdParameterDescription = "The case id, for example dc-001.";

    /// <summary>The tool names in their fixed order.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [GetExtractionStatus, GetInvoice, GetValidatorFindings, GetPolicy];

    /// <summary>Renders the status result: case id, status and the failure (null when there is none).</summary>
    public static string StatusJson(DecisionCaseFile @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Serialize(new StatusResult(@case.CaseId, @case.Extraction.Status, @case.Extraction.Failure));
    }

    /// <summary>
    /// Renders the invoice result. A case without an invoice is an ordinary answer: invoice null plus a note
    /// naming the extraction status, never an error.
    /// </summary>
    public static string InvoiceJson(DecisionCaseFile @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Invoice is null
            ? Serialize(new NoInvoiceResult(
                @case.CaseId,
                null,
                $"No invoice: the extraction status is {@case.Extraction.Status}."))
            : Serialize(new InvoiceResult(@case.CaseId, @case.Invoice));
    }

    /// <summary>Renders the findings in the order the validator stored them; an empty list when there are none.</summary>
    public static string FindingsJson(DecisionCaseFile @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Serialize(new FindingsResult(@case.CaseId, @case.Findings));
    }

    /// <summary>Renders the buyer policy: the CNPJ and the approval limit as a two-decimal money string.</summary>
    public static string PolicyJson(DecisionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return Serialize(policy);
    }

    // Wire.Options: compact (no indentation), snake_case, money as strings, members in declaration order.
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Wire.Options);

    private sealed record StatusResult(string CaseId, string Status, CaseFailure? Failure);

    private sealed record InvoiceResult(string CaseId, Invoice Invoice);

    private sealed record NoInvoiceResult(string CaseId, Invoice? Invoice, string Note);

    private sealed record FindingsResult(string CaseId, IReadOnlyList<CaseFinding> Findings);
}
