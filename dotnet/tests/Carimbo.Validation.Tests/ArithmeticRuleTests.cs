using System.Text.Json.Nodes;
using Carimbo.Domain;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// Item, tax, totals and installment rules. Each mutation of the shared fixture returns exactly the rule-id
/// set the plan lists, asserted as a set, plus the field of the first finding where it matters.
/// </summary>
public class ArithmeticRuleTests
{
    private static Invoice With(Action<JsonObject> mutate) => RepoFiles.MutateValidInvoice(mutate);

    private static HashSet<string> RuleSet(Invoice invoice) =>
        RepoFiles.Validate(invoice).Select(finding => finding.RuleId).ToHashSet();

    private static JsonNode Item(JsonObject node, int index) => node["items"]![index]!;

    private static void AssertRules(Invoice invoice, params string[] expected) =>
        Assert.Equal(expected.ToHashSet(), RuleSet(invoice));

    [Fact]
    public void The_fixture_still_validates_to_an_empty_list()
    {
        Assert.Empty(RepoFiles.Validate(RepoFiles.LoadValidInvoice()));
    }

    // ---- items ---------------------------------------------------------------------------------

    [Fact]
    public void A_wrong_line_total_trips_the_item_the_ipi_and_the_product_sum_rules()
    {
        // The IPI base is the item total, so 101.00 x 10% = 10.10 no longer matches 10.00.
        var invoice = With(node => Item(node, 0)["total"] = "101.00");

        var findings = RepoFiles.Validate(invoice);

        AssertRules(invoice, RuleIds.ITEM_ARITH, RuleIds.TAX_ARITH_IPI, RuleIds.TOTAL_SUM_PRODUCTS);
        Assert.Equal("items[0].total", findings[0].Field);
        Assert.Equal(RuleIds.ITEM_ARITH, findings[0].RuleId);
        Assert.Equal("100.00", findings[0].Expected);
        Assert.Equal("101.00", findings[0].Actual);
    }

    [Fact]
    public void An_icms_amount_one_cent_off_passes_and_two_cents_off_fails_only_the_item_rule()
    {
        AssertRules(With(node => Item(node, 0)["icms_amount"] = "18.01"));

        var invoice = With(node => Item(node, 0)["icms_amount"] = "18.02");
        AssertRules(invoice, RuleIds.TAX_ARITH_ICMS);
        Assert.Equal("items[0].icms_amount", RepoFiles.Validate(invoice)[0].Field);
    }

    [Fact]
    public void An_icms_amount_on_a_not_taxed_cst_is_its_own_rule_and_breaks_the_icms_sum()
    {
        var invoice = With(node => Item(node, 1)["icms_amount"] = "1.00");

        AssertRules(invoice, RuleIds.TAX_NOT_TAXED_AMOUNT, RuleIds.TOTAL_SUM_ICMS);
        var first = RepoFiles.Validate(invoice)[0];
        Assert.Equal("items[1].icms_amount", first.Field);
        Assert.Equal("0.00", first.Expected);
        Assert.Equal("1.00", first.Actual);
    }

    [Fact]
    public void An_unsupported_cst_is_a_warning_and_skips_the_icms_arithmetic()
    {
        var invoice = With(node => Item(node, 0)["cst_csosn"] = "010");

        var finding = Assert.Single(RepoFiles.Validate(invoice));

        Assert.Equal(RuleIds.TAX_CODE_UNSUPPORTED, finding.RuleId);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal("items[0].cst_csosn", finding.Field);
        Assert.Equal("010", finding.Actual);
    }

    [Fact]
    public void An_unsupported_cst_with_a_wrong_icms_amount_is_still_only_the_warning_and_the_sum()
    {
        var invoice = With(node =>
        {
            Item(node, 0)["cst_csosn"] = "010";
            Item(node, 0)["icms_amount"] = "30.00";
        });

        AssertRules(invoice, RuleIds.TAX_CODE_UNSUPPORTED, RuleIds.TOTAL_SUM_ICMS);
    }

    [Fact]
    public void Mixing_three_and_four_digit_codes_is_a_regime_mismatch()
    {
        var invoice = With(node => Item(node, 1)["cst_csosn"] = "0102");

        AssertRules(invoice, RuleIds.REGIME_CODE_MISMATCH);
        Assert.Equal("items", RepoFiles.Validate(invoice)[0].Field);
    }

    [Theory]
    [InlineData("000", "18.00", "100.00", "18.00", false)] // Normal 00 taxed, correct
    [InlineData("020", "18.00", "100.00", "18.00", false)] // Normal 20 taxed, correct
    [InlineData("090", "18.00", "100.00", "18.00", false)] // Normal 90 taxed, correct
    [InlineData("0900", "18.00", "100.00", "18.00", false)] // Simples 900 taxed, correct
    [InlineData("000", "18.00", "100.00", "20.00", true)] // taxed, wrong amount
    [InlineData("0900", "18.00", "100.00", "20.00", true)] // Simples 900, wrong amount
    public void Taxed_codes_are_checked_against_base_times_rate(string code, string rate, string baseAmount, string amount, bool fails)
    {
        var invoice = With(node =>
        {
            Item(node, 0)["cst_csosn"] = code;
            Item(node, 0)["icms_rate"] = rate;
            Item(node, 0)["icms_base"] = baseAmount;
            Item(node, 0)["icms_amount"] = amount;
            Item(node, 1)["cst_csosn"] = code.Length == 3 ? "040" : "0400";
        });

        var ids = RepoFiles.Validate(invoice).Select(finding => finding.RuleId).ToList();

        Assert.Equal(fails, ids.Contains(RuleIds.TAX_ARITH_ICMS));
    }

    [Theory]
    [InlineData("040")]
    [InlineData("041")]
    [InlineData("050")]
    [InlineData("060")]
    [InlineData("0101")]
    [InlineData("0102")]
    [InlineData("0103")]
    [InlineData("0300")]
    [InlineData("0400")]
    [InlineData("0500")]
    public void Not_taxed_codes_must_carry_a_zero_icms_amount(string code)
    {
        var zeroed = With(node =>
        {
            foreach (var index in new[] { 0, 1 })
            {
                Item(node, index)["cst_csosn"] = code;
                Item(node, index)["icms_base"] = "0.00";
                Item(node, index)["icms_rate"] = "0.00";
                Item(node, index)["icms_amount"] = "0.00";
            }

            node["totals"]!["icms_base"] = "0.00";
            node["totals"]!["icms_amount"] = "0.00";
        });
        Assert.DoesNotContain(RuleIds.TAX_NOT_TAXED_AMOUNT, RuleSet(zeroed));
        Assert.DoesNotContain(RuleIds.TAX_CODE_UNSUPPORTED, RuleSet(zeroed));
        Assert.DoesNotContain(RuleIds.REGIME_CODE_MISMATCH, RuleSet(zeroed));

        var taxed = With(node =>
        {
            foreach (var index in new[] { 0, 1 })
            {
                Item(node, index)["cst_csosn"] = code;
            }
        });
        Assert.Contains(RuleIds.TAX_NOT_TAXED_AMOUNT, RuleSet(taxed));
    }

    [Theory]
    [InlineData("010")]
    [InlineData("030")]
    [InlineData("0201")]
    [InlineData("0200")]
    [InlineData("0104")]
    public void Every_other_code_is_an_unsupported_warning(string code)
    {
        var invoice = With(node =>
        {
            Item(node, 0)["cst_csosn"] = code;
            Item(node, 1)["cst_csosn"] = code;
        });

        var warnings = RepoFiles.Validate(invoice).Where(finding => finding.RuleId == RuleIds.TAX_CODE_UNSUPPORTED).ToList();

        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, finding => Assert.Equal(FindingSeverity.Warning, finding.Severity));
    }

    [Fact]
    public void An_ipi_amount_off_by_a_euro_trips_the_item_and_the_sum_rule()
    {
        var invoice = With(node => Item(node, 0)["ipi_amount"] = "11.00");

        AssertRules(invoice, RuleIds.TAX_ARITH_IPI, RuleIds.TOTAL_SUM_IPI);
    }

    [Fact]
    public void Item_arithmetic_rounds_half_up_before_it_compares()
    {
        // 3 x 0.8350 = 2.5050 -> 2.51 (half up); the printed 2.50 is exactly one cent off, which the inclusive tolerance accepts.
        var invoice = With(node =>
        {
            Item(node, 0)["quantity"] = "3.0000";
            Item(node, 0)["unit_price"] = "0.8350";
            Item(node, 0)["total"] = "2.50";
            Item(node, 0)["icms_base"] = "0.00";
            Item(node, 0)["icms_rate"] = "0.00";
            Item(node, 0)["icms_amount"] = "0.00";
            Item(node, 0)["ipi_rate"] = "0.00";
            Item(node, 0)["ipi_amount"] = "0.00";
        });

        Assert.DoesNotContain(RuleIds.ITEM_ARITH, RuleSet(invoice));

        var twoCentsOff = With(node =>
        {
            Item(node, 0)["quantity"] = "3.0000";
            Item(node, 0)["unit_price"] = "0.8350";
            Item(node, 0)["total"] = "2.49";
            Item(node, 0)["ipi_rate"] = "0.00";
            Item(node, 0)["ipi_amount"] = "0.00";
        });
        Assert.Contains(RuleIds.ITEM_ARITH, RuleSet(twoCentsOff));
    }

    // ---- totals --------------------------------------------------------------------------------

    [Fact]
    public void A_wrong_invoice_total_breaks_the_formula_and_the_installment_sum()
    {
        AssertRules(With(node => node["totals"]!["invoice_total"] = "156.00"), RuleIds.TOTAL_VNF_FORMULA, RuleIds.DUP_SUM);
    }

    [Fact]
    public void Without_installments_only_the_formula_fails()
    {
        var invoice = With(node =>
        {
            node["totals"]!["invoice_total"] = "156.00";
            node["installments"] = new JsonArray();
        });

        AssertRules(invoice, RuleIds.TOTAL_VNF_FORMULA);
    }

    [Fact]
    public void Zero_installments_never_evaluate_the_installment_sum()
    {
        AssertRules(With(node => node["installments"] = new JsonArray()));
    }

    [Fact]
    public void An_installment_sum_exactly_tolerance_times_n_off_passes_and_one_more_cent_fails()
    {
        // Two installments allow 0.02: 155.02 passes, 155.03 does not.
        Assert.Empty(RepoFiles.Validate(With(node => node["installments"]![1]!["amount"] = "77.52")));

        AssertRules(
            With(node => node["installments"]![1]!["amount"] = "77.53"),
            RuleIds.DUP_SUM);
    }

    [Fact]
    public void The_icms_base_sum_tolerance_is_exactly_two_cents_for_two_items()
    {
        AssertRules(With(node => node["totals"]!["icms_base"] = "100.02"));
        AssertRules(With(node => node["totals"]!["icms_base"] = "100.03"), RuleIds.TOTAL_SUM_ICMS_BASE);
    }

    [Fact]
    public void The_formula_tolerance_is_a_single_cent()
    {
        AssertRules(With(node => node["totals"]!["invoice_total"] = "155.01"));
        AssertRules(With(node => node["totals"]!["invoice_total"] = "155.02"), RuleIds.TOTAL_VNF_FORMULA);
    }

    [Fact]
    public void An_invoice_with_no_items_starts_with_items_empty_and_sums_with_n_equal_one()
    {
        var empty = RepoFiles.LoadValidInvoice() with { Items = [] };

        var findings = RepoFiles.Validate(empty);
        Assert.Equal(RuleIds.ITEMS_EMPTY, findings[0].RuleId);
        Assert.Equal("items", findings[0].Field);

        var withinACent = empty with { Totals = empty.Totals with { ProductsTotal = new Money(0.01m) } };
        Assert.DoesNotContain(RuleIds.TOTAL_SUM_PRODUCTS, RepoFiles.RuleIdsOf(RepoFiles.Validate(withinACent)));

        var twoCents = empty with { Totals = empty.Totals with { ProductsTotal = new Money(0.02m) } };
        Assert.Contains(RuleIds.TOTAL_SUM_PRODUCTS, RepoFiles.RuleIdsOf(RepoFiles.Validate(twoCents)));
    }

    [Fact]
    public void The_sum_tolerance_grows_with_the_item_count_up_to_the_cap()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var template = invoice.Items[1] with { Total = new Money(0.01m), Quantity = new Decimal4(1m), UnitPrice = new Decimal4(0.01m) };
        var items = Enumerable.Repeat(template, 150).ToList();

        // Σ = 1.50; printed 0.50 differs by 1.00 = the cap, which is inclusive; 0.49 differs by 1.01.
        var atCap = invoice with { Items = items, Totals = invoice.Totals with { ProductsTotal = new Money(0.50m) } };
        Assert.DoesNotContain(RuleIds.TOTAL_SUM_PRODUCTS, RepoFiles.RuleIdsOf(RepoFiles.Validate(atCap)));

        var pastCap = invoice with { Items = items, Totals = invoice.Totals with { ProductsTotal = new Money(0.49m) } };
        Assert.Contains(RuleIds.TOTAL_SUM_PRODUCTS, RepoFiles.RuleIdsOf(RepoFiles.Validate(pastCap)));
    }

    // ---- overflow ------------------------------------------------------------------------------

    [Fact]
    public void An_overflowing_line_total_is_an_overflow_finding_on_that_field()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var items = invoice.Items.ToList();
        items[0] = items[0] with { Quantity = new Decimal4(decimal.MaxValue), UnitPrice = new Decimal4(2m) };

        var findings = RepoFiles.Validate(invoice with { Items = items });

        var overflow = Assert.Single(findings, finding => finding.RuleId == RuleIds.ARITH_OVERFLOW && finding.Field == "items[0].total");
        Assert.Equal("a representable amount", overflow.Expected);
        Assert.Equal(string.Empty, overflow.Actual);
        Assert.Equal(FindingSeverity.Error, overflow.Severity);
        Assert.DoesNotContain(findings, finding => finding.RuleId == RuleIds.ITEM_ARITH);
    }

    [Fact]
    public void Item_totals_that_overflow_when_added_are_an_overflow_on_the_products_total()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var items = invoice.Items.Select(item => item with { Total = new Money(decimal.MaxValue) }).ToList();

        var findings = RepoFiles.Validate(invoice with { Items = items });

        Assert.Contains(findings, finding => finding.RuleId == RuleIds.ARITH_OVERFLOW && finding.Field == "totals.products_total");
        Assert.DoesNotContain(findings, finding => finding.RuleId == RuleIds.TOTAL_SUM_PRODUCTS);
    }

    [Fact]
    public void A_formula_that_overflows_is_an_overflow_on_the_invoice_total()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var totals = invoice.Totals with { ProductsTotal = new Money(decimal.MaxValue), Freight = new Money(decimal.MaxValue) };

        var findings = RepoFiles.Validate(invoice with { Totals = totals });

        Assert.Contains(findings, finding => finding.RuleId == RuleIds.ARITH_OVERFLOW && finding.Field == "totals.invoice_total");
    }

    [Fact]
    public void A_difference_that_overflows_is_an_overflow_not_a_pass()
    {
        var invoice = RepoFiles.LoadValidInvoice();
        var totals = invoice.Totals with { InvoiceTotal = new Money(decimal.MinValue), ProductsTotal = new Money(decimal.MaxValue), Freight = new Money(0m), Discount = new Money(0m), IpiAmount = new Money(0m) };

        var findings = RepoFiles.Validate(invoice with { Totals = totals });

        Assert.Contains(findings, finding => finding.RuleId == RuleIds.ARITH_OVERFLOW && finding.Field == "totals.invoice_total");
    }

    // ---- order and determinism -----------------------------------------------------------------

    [Fact]
    public void Findings_are_ordered_identity_key_dates_items_totals_installments_and_item_zero_before_item_one()
    {
        var invoice = With(node =>
        {
            node["issuer"]!["uf"] = "XX";
            node["recipient"]!["uf"] = "YY";
            node["installments"]![0]!["due_date"] = "2026-03-01";
            Item(node, 1)["total"] = "36.00";
            Item(node, 0)["total"] = "101.00";
            node["totals"]!["invoice_total"] = "156.00";
        });

        var findings = RepoFiles.Validate(invoice);

        Assert.Equal(
            [
                RuleIds.UF_UNKNOWN,
                RuleIds.UF_UNKNOWN,
                RuleIds.DUE_DATE_ORDER,
                RuleIds.ITEM_ARITH,
                RuleIds.TAX_ARITH_IPI,
                RuleIds.ITEM_ARITH,
                RuleIds.TOTAL_SUM_PRODUCTS,
                RuleIds.TOTAL_VNF_FORMULA,
                RuleIds.DUP_SUM,
            ],
            RepoFiles.RuleIdsOf(findings));
        Assert.Equal("items[0].total", findings[3].Field);
        Assert.Equal("items[1].total", findings[5].Field);
    }

    [Fact]
    public void The_same_input_twice_gives_sequence_equal_lists()
    {
        var invoice = With(node =>
        {
            Item(node, 0)["total"] = "101.00";
            node["totals"]!["invoice_total"] = "156.00";
        });

        Assert.True(RepoFiles.Validate(invoice).SequenceEqual(RepoFiles.Validate(invoice)));
    }
}
