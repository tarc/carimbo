using System.Globalization;
using Carimbo.Domain;

namespace Carimbo.Validation;

/// <summary>
/// The access-key cross-checks and the date rules. Expected is what the extracted fields imply, Actual is
/// what the key or the date says. Dates are compared as day numbers so the extremes of
/// <see cref="DateOnly"/> cannot overflow, and "today" is only ever the injected reference date.
/// </summary>
internal static class KeyAndDateRules
{
    private const string KeyField = "access_key";

    // The access key is cUF(2) AAMM(4) CNPJ(14) mod(2) serie(3) nNF(9) tpEmis(1) cNF(8) cDV(1): fixed slices.
    private static readonly Range UfSegment = 0..2;
    private static readonly Range YearMonthSegment = 2..6;
    private static readonly Range CnpjSegment = 6..20;
    private static readonly Range ModelSegment = 20..22;
    private static readonly Range SeriesSegment = 22..25;
    private static readonly Range NumberSegment = 25..34;

    private const string ModelFiscalDocument = "55";

    /// <summary>
    /// Compares the key's segments with the extracted fields. Runs only when the key has the right format,
    /// so the slices are known to be digits (or, for the CNPJ part, uppercase letters and digits).
    /// </summary>
    public static void AppendKeyCrossChecks(Invoice invoice, List<ValidationFinding> findings)
    {
        var key = invoice.AccessKey;
        if (!Patterns.IsFullMatch(Patterns.AccessKey, key))
        {
            return;
        }

        // An unknown issuer UF has its own finding (UF_UNKNOWN); there is no code to compare with.
        if (Identifiers.UfCodes.TryGetValue(invoice.Issuer.Uf, out var ufCode) && key[UfSegment] != ufCode)
        {
            findings.Add(Finding.Error(KeyField, RuleIds.KEY_UF_MISMATCH, ufCode, key[UfSegment]));
        }

        if (key[CnpjSegment] != invoice.Issuer.Cnpj)
        {
            findings.Add(Finding.Error(KeyField, RuleIds.KEY_ISSUER_CNPJ_MISMATCH, invoice.Issuer.Cnpj, key[CnpjSegment]));
        }

        // AAMM comes from the printed local issue date, never from a UTC conversion: 2026-03-31 is 2603.
        var yearMonth = YearMonthText(invoice.IssueDate);
        if (key[YearMonthSegment] != yearMonth)
        {
            findings.Add(Finding.Error(KeyField, RuleIds.KEY_YEAR_MONTH_MISMATCH, yearMonth, key[YearMonthSegment]));
        }

        if (key[ModelSegment] != ModelFiscalDocument)
        {
            findings.Add(Finding.Error(KeyField, RuleIds.KEY_MODEL_MISMATCH, ModelFiscalDocument, key[ModelSegment]));
        }

        // The slices hold at most 3 and 9 digits (pattern-checked), so the integer parses cannot overflow.
        if (int.Parse(key[SeriesSegment], NumberStyles.None, CultureInfo.InvariantCulture) != invoice.Series)
        {
            findings.Add(Finding.Error(KeyField, RuleIds.KEY_SERIES_MISMATCH, Invariant(invoice.Series), key[SeriesSegment]));
        }

        if (int.Parse(key[NumberSegment], NumberStyles.None, CultureInfo.InvariantCulture) != invoice.Number)
        {
            findings.Add(Finding.Error(KeyField, RuleIds.KEY_NUMBER_MISMATCH, Invariant(invoice.Number), key[NumberSegment]));
        }
    }

    /// <summary>DATE_PLAUSIBLE for the issue date, then DUE_DATE_ORDER for each installment in index order.</summary>
    public static void AppendDates(Invoice invoice, DateOnly referenceDate, List<ValidationFinding> findings)
    {
        var issueDate = invoice.IssueDate;

        // One day of slack after the reference date (D-11) covers a printed local date ahead of the reference.
        // Compared as day numbers: reference + 1 day would overflow at DateOnly.MaxValue.
        var latestDayNumber = Math.Min(referenceDate.DayNumber + 1, DateOnly.MaxValue.DayNumber);
        if (issueDate < ValidationOptions.EarliestIssueDate || issueDate.DayNumber > referenceDate.DayNumber + 1)
        {
            findings.Add(Finding.Error(
                "issue_date",
                RuleIds.DATE_PLAUSIBLE,
                Finding.Date(ValidationOptions.EarliestIssueDate) + " to " + Finding.Date(DateOnly.FromDayNumber(latestDayNumber)),
                Finding.Date(issueDate)));
        }

        for (var i = 0; i < invoice.Installments.Count; i++)
        {
            var dueDate = invoice.Installments[i].DueDate;
            if (dueDate < issueDate)
            {
                findings.Add(Finding.Error(
                    $"installments[{i}].due_date",
                    RuleIds.DUE_DATE_ORDER,
                    "on or after " + Finding.Date(issueDate),
                    Finding.Date(dueDate)));
            }
        }
    }

    // Two-digit year then two-digit month, from the DateOnly's own (local) fields.
    private static string YearMonthText(DateOnly date) =>
        (date.Year % 100).ToString("00", CultureInfo.InvariantCulture)
        + date.Month.ToString("00", CultureInfo.InvariantCulture);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
