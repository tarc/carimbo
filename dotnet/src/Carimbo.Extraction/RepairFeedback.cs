using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Carimbo.Validation;

namespace Carimbo.Extraction;

/// <summary>
/// Builds the user turn of a repair attempt (D-12, D-13). The disclosure policy is the point: feedback must
/// help the model re-read the document without ever handing it an answer to copy.
/// <list type="bullet">
/// <item>Derived arithmetic and date rules reveal the computed value and the printed value, because the
/// model cannot fix a sum without knowing which side disagrees.</item>
/// <item>Identifier, check-digit and access-key rules get a fixed sentence without values: stating the
/// expected check digit or key component would invite the model to fabricate it.</item>
/// <item>Every detail is built here from the rule id, the field path and (reveal set only) the validator's own
/// amount or date strings. No text copied from the document can enter the turn.</item>
/// </list>
/// </summary>
public static partial class RepairFeedback
{
    private const string SchemaMismatchLead = "The previous answer did not match the required schema.";
    private const string UnknownRuleSentence = "This value fails an automatic consistency check. Re-read it on the document.";
    private const int MaxRevealedLength = 64;

    // The rules whose Expected and Actual are numbers or dates built by the validator (RESEARCH Pattern 2).
    private static readonly HashSet<string> RevealSet = new(StringComparer.Ordinal)
    {
        RuleIds.DATE_PLAUSIBLE,
        RuleIds.DUE_DATE_ORDER,
        RuleIds.ITEMS_EMPTY,
        RuleIds.ITEM_ARITH,
        RuleIds.TAX_ARITH_ICMS,
        RuleIds.TAX_NOT_TAXED_AMOUNT,
        RuleIds.TAX_ARITH_IPI,
        RuleIds.TOTAL_SUM_PRODUCTS,
        RuleIds.TOTAL_SUM_ICMS_BASE,
        RuleIds.TOTAL_SUM_ICMS,
        RuleIds.TOTAL_SUM_IPI,
        RuleIds.TOTAL_VNF_FORMULA,
        RuleIds.DUP_SUM,
    };

    private static readonly Dictionary<string, string> FixedSentences = new(StringComparer.Ordinal)
    {
        [RuleIds.CNPJ_FORMAT] = "The CNPJ is not 14 characters of the expected form. Re-read all 14 characters on the document.",
        [RuleIds.CNPJ_CHECK_DIGIT] = "The check digits of the CNPJ do not match its other characters. Re-read all 14 characters on the document.",
        [RuleIds.CPF_FORMAT] = "The CPF is not 11 digits. Re-read all 11 digits on the document.",
        [RuleIds.CPF_CHECK_DIGIT] = "The check digits of the CPF do not match its other digits. Re-read all 11 digits on the document.",
        [RuleIds.TAX_ID_KIND_MISMATCH] = "The tax identifier kind does not match the length of the identifier. Re-read the identifier on the document.",
        [RuleIds.UF_UNKNOWN] = "This state code is not a valid Brazilian state code. Re-read it on the document.",
        [RuleIds.KEY_FORMAT] = "The access key is not 44 characters of the expected form. Re-read all 44 characters on the document.",
        [RuleIds.KEY_CHECK_DIGIT] = "The check digit of the access key does not match its other 43 characters. Re-read all 44 characters on the document.",
        [RuleIds.KEY_UF_MISMATCH] = "The state segment of the access key does not match the issuer state. Re-read the access key and the issuer state on the document.",
        [RuleIds.KEY_ISSUER_CNPJ_MISMATCH] = "The issuer CNPJ segment of the access key does not match the issuer CNPJ. Re-read the access key and the issuer CNPJ on the document.",
        [RuleIds.KEY_YEAR_MONTH_MISMATCH] = "The year and month segment of the access key does not match the issue date. Re-read the access key and the issue date on the document.",
        [RuleIds.KEY_MODEL_MISMATCH] = "The model segment of the access key is not the one expected for this document. Re-read the access key on the document.",
        [RuleIds.KEY_SERIES_MISMATCH] = "The series segment of the access key does not match the invoice series. Re-read the access key and the series on the document.",
        [RuleIds.KEY_NUMBER_MISMATCH] = "The number segment of the access key does not match the invoice number. Re-read the access key and the number on the document.",
        [RuleIds.REGIME_CODE_MISMATCH] = "The CST or CSOSN codes of the items mix code lengths. Re-read the CST column on the document.",
        [RuleIds.ARITH_OVERFLOW] = "A value is too large to be an amount on this document. Re-read it on the document.",
    };

    // Only validator-built dates, amounts and fixed phrases may be echoed: letters, digits, spaces, '.', '-' and ':'.
    [GeneratedRegex(@"^[A-Za-z0-9 .:\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeValue();

    /// <summary>True when feedback for <paramref name="ruleId"/> states the computed and the printed value.</summary>
    public static bool RevealsValues(string ruleId) => RevealSet.Contains(ruleId);

    /// <summary>
    /// The repair user turn: the repair prompt, then the error findings as a compact JSON array of
    /// <c>{rule_id, field, detail}</c>. Warning findings are dropped. When <paramref name="previousAnswerSchemaInvalid"/>
    /// is true the turn starts by saying the previous answer did not match the schema.
    /// </summary>
    public static string Build(
        string repairPrompt,
        IReadOnlyList<ValidationFinding> errorFindings,
        bool previousAnswerSchemaInvalid)
    {
        ArgumentNullException.ThrowIfNull(repairPrompt);
        ArgumentNullException.ThrowIfNull(errorFindings);

        var entries = new JsonArray();
        foreach (var finding in errorFindings.Where(f => f.Severity == FindingSeverity.Error))
        {
            entries.Add(new JsonObject
            {
                ["rule_id"] = finding.RuleId,
                ["field"] = finding.Field,
                ["detail"] = Detail(finding),
            });
        }

        var lead = previousAnswerSchemaInvalid ? SchemaMismatchLead + "\n" : string.Empty;
        return lead + repairPrompt + "\n\nFindings (JSON):\n" + entries.ToJsonString(new JsonSerializerOptions());
    }

    private static string Detail(ValidationFinding finding)
    {
        if (RevealsValues(finding.RuleId))
        {
            if (IsSafe(finding.Expected) && IsSafe(finding.Actual))
            {
                return $"Expected {finding.Expected}, but the answer has {finding.Actual}. Re-read both on the document.";
            }

            return UnknownRuleSentence;
        }

        return FixedSentences.GetValueOrDefault(finding.RuleId, UnknownRuleSentence);
    }

    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= MaxRevealedLength && SafeValue().IsMatch(value);
}
