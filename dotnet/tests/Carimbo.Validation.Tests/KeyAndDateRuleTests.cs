using System.Text.Json.Nodes;
using Carimbo.Domain;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// Access-key cross-checks and date plausibility. Each mutation changes one thing (a key is rebuilt with a
/// recomputed check digit) so it trips exactly the intended rule, and every test asserts the full rule-id list.
/// </summary>
public class KeyAndDateRuleTests
{
    // cUF 35, AAMM 2603, CNPJ 11222333000181, model 55, series 001, number 000000123, tpEmis 1, cNF 00001234.
    private const string FixtureBody = "3526031122233300018155001000000123100001234";

    private static string KeyOf(string body43) =>
        body43 + Identifiers.AccessKeyCheckDigit(body43).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Rules(Invoice invoice) => RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice));

    private static Invoice With(Action<JsonObject> mutate) => RepoFiles.MutateValidInvoice(mutate);

    // ---- key cross-checks ----------------------------------------------------------------------

    [Fact]
    public void The_fixture_key_is_the_documented_body_and_check_digit()
    {
        Assert.Equal(RepoFiles.LoadValidInvoice().AccessKey, KeyOf(FixtureBody));
    }

    [Fact]
    public void A_different_issuer_uf_is_a_key_uf_mismatch_with_both_ibge_codes()
    {
        var findings = RepoFiles.Validate(With(node => node["issuer"]!["uf"] = "RJ"));

        var finding = Assert.Single(findings);
        Assert.Equal(RuleIds.KEY_UF_MISMATCH, finding.RuleId);
        Assert.Equal("access_key", finding.Field);
        Assert.Equal("33", finding.Expected);
        Assert.Equal("35", finding.Actual);
    }

    [Fact]
    public void An_unknown_issuer_uf_is_only_unknown_and_never_a_key_uf_mismatch()
    {
        Assert.Equal([RuleIds.UF_UNKNOWN], Rules(With(node => node["issuer"]!["uf"] = "XX")));
    }

    [Fact]
    public void A_different_issuer_cnpj_is_a_key_issuer_mismatch_and_no_cnpj_finding()
    {
        var findings = RepoFiles.Validate(With(node => node["issuer"]!["cnpj"] = "12ABC34501DE35"));

        var finding = Assert.Single(findings);
        Assert.Equal(RuleIds.KEY_ISSUER_CNPJ_MISMATCH, finding.RuleId);
        Assert.Equal("12ABC34501DE35", finding.Expected);
        Assert.Equal("11222333000181", finding.Actual);
    }

    [Fact]
    public void An_issue_date_in_another_month_is_a_year_month_mismatch()
    {
        var findings = RepoFiles.Validate(With(node => node["issue_date"] = "2026-04-01"));

        var finding = Assert.Single(findings);
        Assert.Equal(RuleIds.KEY_YEAR_MONTH_MISMATCH, finding.RuleId);
        Assert.Equal("2604", finding.Expected);
        Assert.Equal("2603", finding.Actual);
    }

    [Fact]
    public void The_last_day_of_march_matches_2603_and_not_2604()
    {
        var march = With(node => node["issue_date"] = "2026-03-31");
        Assert.Empty(RepoFiles.Validate(march));

        var aprilKey = With(node =>
        {
            node["issue_date"] = "2026-03-31";
            node["access_key"] = KeyOf(FixtureBody.Remove(2, 4).Insert(2, "2604"));
        });
        var finding = Assert.Single(RepoFiles.Validate(aprilKey));
        Assert.Equal(RuleIds.KEY_YEAR_MONTH_MISMATCH, finding.RuleId);
        Assert.Equal("2603", finding.Expected);
        Assert.Equal("2604", finding.Actual);
    }

    [Fact]
    public void A_key_for_april_with_the_first_of_april_is_clean()
    {
        var invoice = With(node =>
        {
            node["issue_date"] = "2026-04-01";
            node["access_key"] = KeyOf(FixtureBody.Remove(2, 4).Insert(2, "2604"));
        });

        Assert.Empty(RepoFiles.Validate(invoice));
    }

    [Fact]
    public void A_different_series_and_a_different_number_each_have_their_own_rule()
    {
        Assert.Equal([RuleIds.KEY_SERIES_MISMATCH], Rules(With(node => node["series"] = 2)));
        Assert.Equal([RuleIds.KEY_NUMBER_MISMATCH], Rules(With(node => node["number"] = 124)));
    }

    [Fact]
    public void Series_and_number_are_compared_as_integers_not_as_text()
    {
        // Segment 001 equals series 1 and 000000123 equals number 123 (the fixture is already clean).
        Assert.Empty(RepoFiles.Validate(RepoFiles.LoadValidInvoice()));

        var series12 = With(node =>
        {
            node["series"] = 12;
            node["access_key"] = KeyOf(FixtureBody.Remove(22, 3).Insert(22, "012"));
        });
        Assert.Empty(RepoFiles.Validate(series12));

        var number124 = With(node =>
        {
            node["number"] = 124;
            node["access_key"] = KeyOf(FixtureBody.Remove(25, 9).Insert(25, "000000124"));
        });
        Assert.Empty(RepoFiles.Validate(number124));
    }

    [Fact]
    public void Series_and_number_report_the_segment_text()
    {
        var findings = RepoFiles.Validate(With(node => node["series"] = 2));

        var finding = Assert.Single(findings);
        Assert.Equal("2", finding.Expected);
        Assert.Equal("001", finding.Actual);
    }

    [Fact]
    public void A_key_for_model_65_is_a_model_mismatch()
    {
        var invoice = With(node => node["access_key"] = KeyOf(FixtureBody.Remove(20, 2).Insert(20, "65")));

        var finding = Assert.Single(RepoFiles.Validate(invoice));

        Assert.Equal(RuleIds.KEY_MODEL_MISMATCH, finding.RuleId);
        Assert.Equal("55", finding.Expected);
        Assert.Equal("65", finding.Actual);
    }

    [Fact]
    public void A_key_that_fails_its_format_gets_no_cross_check_findings()
    {
        var invoice = With(node =>
        {
            node["access_key"] = "35260312abc34501de35550010000001231000012347";
            node["issuer"]!["uf"] = "RJ";
            node["series"] = 9;
            node["number"] = 9;
        });

        Assert.Equal([RuleIds.KEY_FORMAT], Rules(invoice));
    }

    [Fact]
    public void A_wrong_check_digit_does_not_stop_the_cross_checks_and_comes_first()
    {
        var invoice = With(node =>
        {
            node["access_key"] = "35260311222333000181550010000001231000012347";
            node["issuer"]!["uf"] = "RJ";
        });

        Assert.Equal([RuleIds.KEY_CHECK_DIGIT, RuleIds.KEY_UF_MISMATCH], Rules(invoice));
    }

    [Fact]
    public void Several_segment_mismatches_are_listed_in_the_documented_order()
    {
        var invoice = With(node =>
        {
            node["issuer"]!["uf"] = "RJ";
            node["issuer"]!["cnpj"] = "12ABC34501DE35";
            node["issue_date"] = "2026-04-01";
            node["series"] = 2;
            node["number"] = 124;
        });

        Assert.Equal(
            [
                RuleIds.KEY_UF_MISMATCH,
                RuleIds.KEY_ISSUER_CNPJ_MISMATCH,
                RuleIds.KEY_YEAR_MONTH_MISMATCH,
                RuleIds.KEY_SERIES_MISMATCH,
                RuleIds.KEY_NUMBER_MISMATCH,
            ],
            Rules(invoice));
    }

    // ---- dates ---------------------------------------------------------------------------------

    [Fact]
    public void An_issue_date_one_day_after_the_reference_passes_and_two_days_fails()
    {
        var invoice = RepoFiles.LoadValidInvoice(); // issued 2026-03-15

        Assert.Empty(RepoFiles.Validate(invoice, new DateOnly(2026, 3, 14)));

        var finding = Assert.Single(RepoFiles.Validate(invoice, new DateOnly(2026, 3, 13)));
        Assert.Equal(RuleIds.DATE_PLAUSIBLE, finding.RuleId);
        Assert.Equal("issue_date", finding.Field);
        Assert.Equal("2006-04-01 to 2026-03-14", finding.Expected);
        Assert.Equal("2026-03-15", finding.Actual);
    }

    [Fact]
    public void The_earliest_issue_date_is_the_first_of_april_2006()
    {
        var onTheDay = With(node =>
        {
            node["issue_date"] = "2006-04-01";
            node["access_key"] = KeyOf(FixtureBody.Remove(2, 4).Insert(2, "0604"));
        });
        Assert.Empty(RepoFiles.Validate(onTheDay));

        var dayBefore = With(node =>
        {
            node["issue_date"] = "2006-03-31";
            node["access_key"] = KeyOf(FixtureBody.Remove(2, 4).Insert(2, "0603"));
        });
        Assert.Equal([RuleIds.DATE_PLAUSIBLE], Rules(dayBefore));
    }

    [Fact]
    public void The_extreme_reference_dates_do_not_throw()
    {
        var invoice = RepoFiles.LoadValidInvoice();

        Assert.Empty(RepoFiles.Validate(invoice, DateOnly.MaxValue));
        Assert.Equal([RuleIds.DATE_PLAUSIBLE], RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice, DateOnly.MinValue)));

        var farFuture = With(node => node["issue_date"] = "9999-12-31");
        var onMaxReference = RepoFiles.RuleIdsOf(RepoFiles.Validate(farFuture, DateOnly.MaxValue));
        Assert.DoesNotContain(RuleIds.DATE_PLAUSIBLE, onMaxReference);
    }

    [Fact]
    public void A_due_date_before_the_issue_date_is_reported_at_that_installment()
    {
        var invoice = With(node => node["installments"]![1]!["due_date"] = "2026-03-14");

        var finding = Assert.Single(RepoFiles.Validate(invoice));

        Assert.Equal(RuleIds.DUE_DATE_ORDER, finding.RuleId);
        Assert.Equal("installments[1].due_date", finding.Field);
        Assert.Equal("on or after 2026-03-15", finding.Expected);
        Assert.Equal("2026-03-14", finding.Actual);
    }

    [Fact]
    public void A_due_date_equal_to_the_issue_date_passes()
    {
        var invoice = With(node => node["installments"]![0]!["due_date"] = "2026-03-15");

        Assert.Empty(RepoFiles.Validate(invoice));
    }

    [Fact]
    public void Key_findings_come_before_date_findings_and_due_dates_follow_the_index_order()
    {
        var invoice = With(node =>
        {
            node["issuer"]!["uf"] = "RJ";
            node["installments"]![0]!["due_date"] = "2026-03-01";
            node["installments"]![1]!["due_date"] = "2026-03-02";
        });

        var findings = RepoFiles.Validate(invoice);

        Assert.Equal([RuleIds.KEY_UF_MISMATCH, RuleIds.DUE_DATE_ORDER, RuleIds.DUE_DATE_ORDER], RepoFiles.RuleIdsOf(findings));
        Assert.Equal("installments[0].due_date", findings[1].Field);
        Assert.Equal("installments[1].due_date", findings[2].Field);
    }
}
