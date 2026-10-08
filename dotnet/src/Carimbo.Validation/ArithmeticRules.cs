using Carimbo.Domain;

namespace Carimbo.Validation;

/// <summary>
/// Decimal arithmetic whose overflow is reported instead of thrown. A model-controlled amount that fits a
/// decimal can still overflow when multiplied or added, and a validator must answer with a finding, not an
/// exception. Each <c>catch</c> is scoped to the single operation, never around a whole rule.
/// </summary>
internal static class SafeMath
{
    public static bool TryMultiply(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left * right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    public static bool TryAdd(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left + right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    public static bool TrySubtract(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left - right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    /// <summary>
    /// The inclusive comparison <c>|computed - printed| &lt;= tolerance</c>. False when the difference itself
    /// overflows (so <paramref name="within"/> must not be read).
    /// </summary>
    public static bool TryWithin(decimal computed, decimal printed, decimal tolerance, out bool within)
    {
        if (!TrySubtract(computed, printed, out var difference))
        {
            within = false;
            return false;
        }

        within = Math.Abs(difference) <= tolerance;
        return true;
    }

    /// <summary>The sum of <paramref name="values"/>; false when an addition overflows.</summary>
    public static bool TrySum(IEnumerable<decimal> values, out decimal result)
    {
        result = 0m;
        foreach (var value in values)
        {
            if (!TryAdd(result, value, out result))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Item, tax, totals and installment arithmetic (D-06 to D-08, D-23). Every computed value is rounded half up
/// to cents with <see cref="Money.RoundHalfUp"/> before the inclusive comparison against the printed value;
/// a computation that overflows becomes an ARITH_OVERFLOW finding on the field being checked, never an
/// exception and never silence.
/// </summary>
internal static class ArithmeticRules
{
    private enum IcmsTreatment
    {
        Taxed,
        NotTaxed,
        Unsupported,
    }

    public static void Append(Invoice invoice, ValidationOptions options, List<ValidationFinding> findings)
    {
        var items = invoice.Items;

        if (items.Count == 0)
        {
            findings.Add(Finding.Error("items", RuleIds.ITEMS_EMPTY, "at least one item", "0 items"));
        }

        if (MixesRegimeCodeLengths(items))
        {
            findings.Add(Finding.Error(
                "items",
                RuleIds.REGIME_CODE_MISMATCH,
                "cst_csosn codes of one length (3 digits for Regime Normal, 4 for Simples Nacional)",
                "3-digit and 4-digit codes"));
        }

        for (var i = 0; i < items.Count; i++)
        {
            AppendItem(items[i], $"items[{i}]", options, findings);
        }

        AppendTotals(invoice, options, findings);
        AppendInstallments(invoice, options, findings);
    }

    private static void AppendItem(LineItem item, string path, ValidationOptions options, List<ValidationFinding> findings)
    {
        // quantity x unit_price against the line total.
        if (SafeMath.TryMultiply(item.Quantity.Amount, item.UnitPrice.Amount, out var lineTotal))
        {
            Compare(findings, path + ".total", RuleIds.ITEM_ARITH, Money.RoundHalfUp(lineTotal), item.Total.Amount, options.Tolerance);
        }
        else
        {
            findings.Add(Overflow(path + ".total"));
        }

        switch (Classify(item.CstCsosn))
        {
            case IcmsTreatment.Taxed:
                CheckPercentage(
                    findings,
                    path + ".icms_amount",
                    RuleIds.TAX_ARITH_ICMS,
                    item.IcmsBase.Amount,
                    item.IcmsRate.Amount,
                    item.IcmsAmount.Amount,
                    options.Tolerance);
                break;
            case IcmsTreatment.NotTaxed:
                if (item.IcmsAmount.Amount != 0m)
                {
                    findings.Add(Finding.Error(
                        path + ".icms_amount",
                        RuleIds.TAX_NOT_TAXED_AMOUNT,
                        Finding.Amount(0m),
                        Finding.Amount(item.IcmsAmount.Amount)));
                }

                break;
            default:
                findings.Add(Finding.Warning(
                    path + ".cst_csosn",
                    RuleIds.TAX_CODE_UNSUPPORTED,
                    "a CST or CSOSN code with a tax rule",
                    item.CstCsosn));
                break;
        }

        // The DANFE has no IPI base column; the base is the item total (RESEARCH A7).
        CheckPercentage(
            findings,
            path + ".ipi_amount",
            RuleIds.TAX_ARITH_IPI,
            item.Total.Amount,
            item.IpiRate.Amount,
            item.IpiAmount.Amount,
            options.Tolerance);
    }

    private static void AppendTotals(Invoice invoice, ValidationOptions options, List<ValidationFinding> findings)
    {
        var items = invoice.Items;
        var totals = invoice.Totals;
        var sumTolerance = options.SumTolerance(items.Count);

        CheckSum(findings, "totals.products_total", RuleIds.TOTAL_SUM_PRODUCTS, items.Select(item => item.Total.Amount), totals.ProductsTotal.Amount, sumTolerance);
        CheckSum(findings, "totals.icms_base", RuleIds.TOTAL_SUM_ICMS_BASE, items.Select(item => item.IcmsBase.Amount), totals.IcmsBase.Amount, sumTolerance);
        CheckSum(findings, "totals.icms_amount", RuleIds.TOTAL_SUM_ICMS, items.Select(item => item.IcmsAmount.Amount), totals.IcmsAmount.Amount, sumTolerance);
        CheckSum(findings, "totals.ipi_amount", RuleIds.TOTAL_SUM_IPI, items.Select(item => item.IpiAmount.Amount), totals.IpiAmount.Amount, sumTolerance);

        // vNF = products - discount + ICMS ST + freight + insurance + other expenses + IPI (MOC rejection 610,
        // DANFE-visible subset). Evaluated left to right; any overflow replaces the finding.
        var formulaFits =
            SafeMath.TrySubtract(totals.ProductsTotal.Amount, totals.Discount.Amount, out var formula)
            && SafeMath.TryAdd(formula, totals.IcmsStAmount.Amount, out formula)
            && SafeMath.TryAdd(formula, totals.Freight.Amount, out formula)
            && SafeMath.TryAdd(formula, totals.Insurance.Amount, out formula)
            && SafeMath.TryAdd(formula, totals.OtherExpenses.Amount, out formula)
            && SafeMath.TryAdd(formula, totals.IpiAmount.Amount, out formula);
        if (formulaFits)
        {
            Compare(findings, "totals.invoice_total", RuleIds.TOTAL_VNF_FORMULA, Money.RoundHalfUp(formula), totals.InvoiceTotal.Amount, options.Tolerance);
        }
        else
        {
            findings.Add(Overflow("totals.invoice_total"));
        }
    }

    // With no installments (a DANFE without a FATURA block) there is nothing to sum, so nothing is evaluated.
    private static void AppendInstallments(Invoice invoice, ValidationOptions options, List<ValidationFinding> findings)
    {
        var installments = invoice.Installments;
        if (installments.Count == 0)
        {
            return;
        }

        CheckSum(
            findings,
            "installments",
            RuleIds.DUP_SUM,
            installments.Select(installment => installment.Amount.Amount),
            invoice.Totals.InvoiceTotal.Amount,
            options.SumTolerance(installments.Count));
    }

    // The ICMS family comes from the CST (3 digits: origin plus a 2-digit CST) or the CSOSN (4 digits: origin
    // plus a 3-digit CSOSN); the rule keys on the code without the origin digit.
    private static IcmsTreatment Classify(string cstCsosn)
    {
        if (!Patterns.IsFullMatch(Patterns.CstCsosn, cstCsosn))
        {
            return IcmsTreatment.Unsupported;
        }

        var code = cstCsosn[1..];
        if (cstCsosn.Length == 3)
        {
            return code switch
            {
                "00" or "20" or "90" => IcmsTreatment.Taxed,
                "40" or "41" or "50" or "60" => IcmsTreatment.NotTaxed,
                _ => IcmsTreatment.Unsupported,
            };
        }

        return code switch
        {
            "900" => IcmsTreatment.Taxed,
            "101" or "102" or "103" or "300" or "400" or "500" => IcmsTreatment.NotTaxed,
            _ => IcmsTreatment.Unsupported,
        };
    }

    private static bool MixesRegimeCodeLengths(IReadOnlyList<LineItem> items)
    {
        var threeDigit = false;
        var fourDigit = false;
        foreach (var item in items)
        {
            if (Patterns.IsFullMatch(Patterns.CstCsosn, item.CstCsosn))
            {
                threeDigit |= item.CstCsosn.Length == 3;
                fourDigit |= item.CstCsosn.Length == 4;
            }
        }

        return threeDigit && fourDigit;
    }

    // printed =~ round(basis x rate / 100)
    private static void CheckPercentage(
        List<ValidationFinding> findings,
        string field,
        string ruleId,
        decimal basis,
        decimal rate,
        decimal printed,
        decimal tolerance)
    {
        if (!SafeMath.TryMultiply(basis, rate, out var product))
        {
            findings.Add(Overflow(field));
            return;
        }

        Compare(findings, field, ruleId, Money.RoundHalfUp(product / 100m), printed, tolerance);
    }

    private static void CheckSum(
        List<ValidationFinding> findings,
        string field,
        string ruleId,
        IEnumerable<decimal> values,
        decimal printed,
        decimal tolerance)
    {
        if (!SafeMath.TrySum(values, out var sum))
        {
            findings.Add(Overflow(field));
            return;
        }

        Compare(findings, field, ruleId, Money.RoundHalfUp(sum), printed, tolerance);
    }

    // The inclusive comparison |computed - printed| <= tolerance; Expected is the computed value.
    private static void Compare(
        List<ValidationFinding> findings,
        string field,
        string ruleId,
        decimal computed,
        decimal printed,
        decimal tolerance)
    {
        if (!SafeMath.TryWithin(computed, printed, tolerance, out var within))
        {
            findings.Add(Overflow(field));
        }
        else if (!within)
        {
            findings.Add(Finding.Error(field, ruleId, Finding.Amount(computed), Finding.Amount(printed)));
        }
    }

    private static ValidationFinding Overflow(string field) =>
        Finding.Error(field, RuleIds.ARITH_OVERFLOW, "a representable amount", string.Empty);
}
