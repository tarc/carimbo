using Carimbo.Domain;
using Carimbo.GroundTruth;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Decisions.Tests;

/// <summary>The labelling rule decision-labels-1 (D-34): order, first match wins, and the limit edge (AGT-04).</summary>
public class DecisionRulesTests
{
    private static readonly string Buyer = "CARIMBO00001" + Identifiers.CnpjCheckDigits("CARIMBO00001");

    private static readonly DecisionPolicy Policy = new(Buyer, Money.Parse("200000.00"));

    private static readonly CaseFinding Warning =
        new("items[0].cst_csosn", "TAX_CODE_UNSUPPORTED", "a CST or CSOSN code with a tax rule", "010", "warning");

    private static readonly CaseFinding Error =
        new("items[0].total", "ITEM_ARITH", "10.00", "11.00", "error");

    [Fact]
    public void A_clean_success_addressed_to_the_buyer_under_the_limit_is_approve_by_rule_5()
    {
        var label = DecisionRules.Label(Success(Buyer, "194615.23"), Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Approve, DecisionCause.None, 5), label);
    }

    [Fact]
    public void A_total_exactly_equal_to_the_limit_is_approve()
    {
        var label = DecisionRules.Label(Success(Buyer, "200000.00"), Policy);

        Assert.Equal(DecisionOutcome.Approve, label.Outcome);
        Assert.Equal(DecisionCause.None, label.Cause);
    }

    [Fact]
    public void A_total_one_cent_over_the_limit_is_escalate_over_limit_by_rule_4()
    {
        var label = DecisionRules.Label(Success(Buyer, "200000.01"), Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.OverLimit, 4), label);
    }

    [Fact]
    public void A_warning_finding_is_escalate_policy_warning_by_rule_3()
    {
        var label = DecisionRules.Label(Success(Buyer, "1000.00", Warning), Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.PolicyWarning, 3), label);
    }

    [Fact]
    public void A_recipient_other_than_the_buyer_is_reject_by_rule_2()
    {
        var label = DecisionRules.Label(Success("69979991000100", "1000.00"), Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Reject, DecisionCause.None, 2), label);
    }

    [Theory]
    [InlineData("validation_failed")]
    [InlineData("refused")]
    [InlineData("truncated")]
    [InlineData("schema_invalid")]
    [InlineData("infrastructure_failure")]
    public void A_status_other_than_success_is_escalate_extraction_not_success_by_rule_1(string status)
    {
        var @case = Success(Buyer, "1000.00", Error) with { Extraction = new CaseExtraction(status, null) };

        var label = DecisionRules.Label(@case, Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.ExtractionNotSuccess, 1), label);
    }

    [Fact]
    public void A_case_without_an_invoice_and_a_status_other_than_success_needs_no_invoice()
    {
        var @case = new DecisionCaseFile(
            "dc-test",
            new CaseExtraction("refused", new CaseFailure("refused", "The model declined to extract the document.")),
            null,
            []);

        Assert.Equal(DecisionOutcome.Escalate, DecisionRules.Label(@case, Policy).Outcome);
    }

    [Fact]
    public void Status_beats_a_recipient_mismatch()
    {
        var @case = Success("69979991000100", "9999999.00") with { Extraction = new CaseExtraction("validation_failed", null) };

        Assert.Equal(1, DecisionRules.Label(@case, Policy).Rule);
    }

    [Fact]
    public void A_recipient_mismatch_beats_over_limit_and_a_warning()
    {
        var label = DecisionRules.Label(Success("47689519024", "3190646.05", Warning), Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Reject, DecisionCause.None, 2), label);
    }

    [Fact]
    public void A_warning_beats_over_limit()
    {
        var label = DecisionRules.Label(Success(Buyer, "3190646.05", Warning), Policy);

        Assert.Equal(new DecisionLabel(DecisionOutcome.Escalate, DecisionCause.PolicyWarning, 3), label);
    }

    [Fact]
    public void The_recipient_comparison_is_ordinal()
    {
        var label = DecisionRules.Label(Success(Buyer.ToLowerInvariant(), "1000.00"), Policy);

        Assert.Equal(DecisionOutcome.Reject, label.Outcome);
    }

    [Fact]
    public void The_labelling_version_is_decision_labels_1() =>
        Assert.Equal("decision-labels-1", DecisionRules.LabellingVersion);

    private static DecisionCaseFile Success(string recipientTaxId, string total, params CaseFinding[] findings)
    {
        var invoice = NfeXmlMapper.Load(Repo.PathOf("data", "skeleton", "case-001.xml"));
        invoice = invoice with
        {
            Recipient = invoice.Recipient with { TaxId = recipientTaxId, TaxIdKind = TaxIdKind.Cnpj },
            Totals = invoice.Totals with { InvoiceTotal = Money.Parse(total) },
        };
        return new DecisionCaseFile("dc-test", new CaseExtraction("success", null), invoice, findings);
    }
}
