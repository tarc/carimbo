using Carimbo.Domain;

namespace Carimbo.Decisions;

/// <summary>
/// One committed decision case as the Tools host serves it: a case id, the extraction outcome the agent
/// reviews, the extracted invoice (null when the extraction did not succeed) and the validator findings.
/// Nothing else: the expected label and the case intent live in other files (D-34).
/// </summary>
public sealed record DecisionCaseFile(
    string CaseId,
    CaseExtraction Extraction,
    Invoice? Invoice,
    IReadOnlyList<CaseFinding> Findings);

/// <summary>
/// The extraction outcome of a case. <paramref name="Status"/> uses the extraction wire names: success,
/// validation_failed, refused, truncated, schema_invalid, infrastructure_failure.
/// </summary>
public sealed record CaseExtraction(string Status, CaseFailure? Failure);

/// <summary>The typed failure of an extraction that did not succeed.</summary>
public sealed record CaseFailure(string Kind, string Message);

/// <summary>One validator finding in the eval-contract shape: severity is "error" or "warning".</summary>
public sealed record CaseFinding(string Field, string RuleId, string Expected, string Actual, string Severity);

/// <summary>The buyer policy: the CNPJ that invoices must be addressed to and the approval limit.</summary>
public sealed record DecisionPolicy(string BuyerCnpj, Money ApprovalLimit);

/// <summary>Raised when a case id is not of the allowed form. The message never carries the raw id or a path.</summary>
public sealed class InvalidCaseIdException() : Exception("The case id is not valid.");

/// <summary>Raised when a valid case id names no committed case. The message never carries a path.</summary>
public sealed class CaseNotFoundException() : Exception("No such case.");

/// <summary>The allowed form of a case id, checked before any file access.</summary>
public static class CaseIds
{
    /// <summary>
    /// True for an id matching <c>^[a-z0-9][a-z0-9-]{0,63}$</c>. An ordinal character check, not a
    /// regular expression: the .NET <c>$</c> anchor also matches before a trailing newline.
    /// </summary>
    public static bool IsValid(string? id) => throw new NotImplementedException();
}

/// <summary>
/// A read-only view of a decision case directory (<c>cases/&lt;id&gt;.json</c> and <c>policy.json</c>).
/// It never creates, writes, moves or deletes anything.
/// </summary>
public sealed class CaseStore(string directory)
{
    /// <summary>Reads one case.</summary>
    /// <exception cref="InvalidCaseIdException">The id is not of the allowed form; no file was touched.</exception>
    /// <exception cref="CaseNotFoundException">The id is valid but names no case.</exception>
    public DecisionCaseFile Load(string caseId) => throw new NotImplementedException(directory);

    /// <summary>True when the id is valid and names a committed case.</summary>
    public bool Exists(string caseId) => throw new NotImplementedException();

    /// <summary>Reads <c>policy.json</c>.</summary>
    public DecisionPolicy LoadPolicy() => throw new NotImplementedException();
}
