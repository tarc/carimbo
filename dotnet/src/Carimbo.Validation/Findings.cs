namespace Carimbo.Validation;

/// <summary>How serious a finding is. Only errors make an invoice fail validation and drive repair.</summary>
public enum FindingSeverity
{
    Error,
    Warning,
}

/// <summary>
/// One problem found in an invoice. <see cref="Field"/> is the dotted snake_case JSON path with list
/// indices (<c>items[1].icms_amount</c>), <see cref="RuleId"/> a stable id from <see cref="RuleIds"/>.
/// <see cref="Expected"/> and <see cref="Actual"/> are text: amounts as invariant <c>0.00</c>, dates as
/// <c>yyyy-MM-dd</c>, identifiers and segments as printed, and the empty string when not applicable.
/// Serialized with <c>Wire.Options</c> the members are <c>field</c>, <c>rule_id</c>, <c>expected</c>,
/// <c>actual</c> and <c>severity</c> (<c>error</c> or <c>warning</c>).
/// </summary>
public sealed record ValidationFinding(
    string Field,
    string RuleId,
    string Expected,
    string Actual,
    FindingSeverity Severity);

/// <summary>
/// The tunable numbers of the arithmetic rules. The defaults equal the <c>tolerance</c> block of
/// <c>data/vectors/validator-vectors.json</c>, which a test asserts.
/// </summary>
public sealed record ValidationOptions
{
    /// <summary>The earliest plausible issue date: the NF-e model 55 went live on 2006-04-01 (D-11).</summary>
    public static readonly DateOnly EarliestIssueDate = new(2006, 4, 1);

    /// <summary>Allowed difference for a single computed value, inclusive (D-08).</summary>
    public decimal Tolerance { get; init; } = 0.01m;

    /// <summary>Upper bound of the allowed difference for a sum over many items (D-08).</summary>
    public decimal SumToleranceCap { get; init; } = 1.00m;

    /// <summary>
    /// The allowed difference for a sum over <paramref name="itemCount"/> printed values:
    /// <c>Tolerance * max(count, 1)</c>, capped at <see cref="SumToleranceCap"/>. With no items the sum
    /// is compared as if there were one, so an empty table still has to print 0.00 within a cent.
    /// </summary>
    public decimal SumTolerance(int itemCount) =>
        Math.Min(Tolerance * Math.Max(1, itemCount), SumToleranceCap);
}

/// <summary>
/// The stable rule ids (D-10, D-23). They end up in committed reports, so a value is never renamed;
/// each constant's value equals its name.
/// </summary>
public static class RuleIds
{
    public const string CNPJ_FORMAT = "CNPJ_FORMAT";
    public const string CNPJ_CHECK_DIGIT = "CNPJ_CHECK_DIGIT";
    public const string CPF_FORMAT = "CPF_FORMAT";
    public const string CPF_CHECK_DIGIT = "CPF_CHECK_DIGIT";
    public const string TAX_ID_KIND_MISMATCH = "TAX_ID_KIND_MISMATCH";
    public const string UF_UNKNOWN = "UF_UNKNOWN";
    public const string KEY_FORMAT = "KEY_FORMAT";
    public const string KEY_CHECK_DIGIT = "KEY_CHECK_DIGIT";
    public const string KEY_UF_MISMATCH = "KEY_UF_MISMATCH";
    public const string KEY_ISSUER_CNPJ_MISMATCH = "KEY_ISSUER_CNPJ_MISMATCH";
    public const string KEY_YEAR_MONTH_MISMATCH = "KEY_YEAR_MONTH_MISMATCH";
    public const string KEY_MODEL_MISMATCH = "KEY_MODEL_MISMATCH";
    public const string KEY_SERIES_MISMATCH = "KEY_SERIES_MISMATCH";
    public const string KEY_NUMBER_MISMATCH = "KEY_NUMBER_MISMATCH";
    public const string DATE_PLAUSIBLE = "DATE_PLAUSIBLE";
    public const string DUE_DATE_ORDER = "DUE_DATE_ORDER";
    public const string ITEMS_EMPTY = "ITEMS_EMPTY";
    public const string ITEM_ARITH = "ITEM_ARITH";
    public const string REGIME_CODE_MISMATCH = "REGIME_CODE_MISMATCH";
    public const string TAX_CODE_UNSUPPORTED = "TAX_CODE_UNSUPPORTED";
    public const string TAX_ARITH_ICMS = "TAX_ARITH_ICMS";
    public const string TAX_NOT_TAXED_AMOUNT = "TAX_NOT_TAXED_AMOUNT";
    public const string TAX_ARITH_IPI = "TAX_ARITH_IPI";
    public const string TOTAL_SUM_PRODUCTS = "TOTAL_SUM_PRODUCTS";
    public const string TOTAL_SUM_ICMS_BASE = "TOTAL_SUM_ICMS_BASE";
    public const string TOTAL_SUM_ICMS = "TOTAL_SUM_ICMS";
    public const string TOTAL_SUM_IPI = "TOTAL_SUM_IPI";
    public const string TOTAL_VNF_FORMULA = "TOTAL_VNF_FORMULA";
    public const string DUP_SUM = "DUP_SUM";
    public const string ARITH_OVERFLOW = "ARITH_OVERFLOW";
}

/// <summary>Builds findings and renders the values they carry.</summary>
internal static class Finding
{
    public static ValidationFinding Error(string field, string ruleId, string expected, string actual) =>
        new(field, ruleId, expected, actual, FindingSeverity.Error);

    public static ValidationFinding Warning(string field, string ruleId, string expected, string actual) =>
        new(field, ruleId, expected, actual, FindingSeverity.Warning);

    /// <summary>An amount as invariant <c>0.00</c>, the form it has on the wire.</summary>
    public static string Amount(decimal value) =>
        value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A date as <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
