using System.ComponentModel;
using Carimbo.Decisions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Carimbo.Tools;

/// <summary>
/// The four read-only case tools (AGT-01, D-35). Names, descriptions and results come from
/// <see cref="CaseToolCatalog"/>, which renders only the case file and the policy: no label, no intent, no manifest.
/// Nothing here writes, so every tool declares itself read-only, idempotent and non-destructive.
/// </summary>
[McpServerToolType]
public sealed class CaseTools(CaseStore cases)
{
    /// <summary>The extraction status of a case.</summary>
    [McpServerTool(Name = CaseToolCatalog.GetExtractionStatus, ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(CaseToolCatalog.GetExtractionStatusDescription)]
    public string GetExtractionStatus([Description(CaseToolCatalog.CaseIdParameterDescription)] string case_id) =>
        CaseToolCatalog.StatusJson(Load(case_id));

    /// <summary>The extracted invoice of a case, or invoice null when the extraction produced none.</summary>
    [McpServerTool(Name = CaseToolCatalog.GetInvoice, ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(CaseToolCatalog.GetInvoiceDescription)]
    public string GetInvoice([Description(CaseToolCatalog.CaseIdParameterDescription)] string case_id) =>
        CaseToolCatalog.InvoiceJson(Load(case_id));

    /// <summary>The deterministic validator findings of a case.</summary>
    [McpServerTool(Name = CaseToolCatalog.GetValidatorFindings, ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(CaseToolCatalog.GetValidatorFindingsDescription)]
    public string GetValidatorFindings([Description(CaseToolCatalog.CaseIdParameterDescription)] string case_id) =>
        CaseToolCatalog.FindingsJson(Load(case_id));

    /// <summary>The buyer policy; it takes no argument.</summary>
    [McpServerTool(Name = CaseToolCatalog.GetPolicy, ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(CaseToolCatalog.GetPolicyDescription)]
    public string GetPolicy() => CaseToolCatalog.PolicyJson(cases.LoadPolicy());

    // The id is echoed only after CaseStore accepted it against the allow-list; neither message carries a path.
    private DecisionCaseFile Load(string caseId)
    {
        try
        {
            return cases.Load(caseId);
        }
        catch (InvalidCaseIdException)
        {
            throw new McpException("invalid case id");
        }
        catch (CaseNotFoundException)
        {
            throw new McpException("unknown case " + caseId);
        }
    }
}
