using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Domain;
using Carimbo.GroundTruth;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// The ground-truth gate (D-17): every case of the committed manifest is mapped by the documented
/// .NET mapper and validated at the manifest as_of_date, so the mapping and the validators agree
/// before any model is involved. It iterates the manifest, so Phase 4 reuses it at dataset scale.
/// </summary>
public class GroundTruthGateTests
{
    public static TheoryData<string> CaseIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var entry in Cases())
            {
                data.Add(entry["case_id"]!.GetValue<string>());
            }

            return data;
        }
    }

    [Fact]
    public void The_manifest_lists_the_three_skeleton_cases_and_an_as_of_date()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), AsOfDate());
        Assert.Equal(["case-001", "case-002", "case-003"], Cases().Select(entry => entry["case_id"]!.GetValue<string>()));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Mapped_ground_truth_has_no_validator_error_at_the_manifest_as_of_date(string caseId)
    {
        var invoice = NfeXmlMapper.Load(RepoFiles.PathOf("data", "skeleton", XmlFileOf(caseId)));

        var errors = new InvoiceValidator(new ValidationOptions())
            .Validate(invoice, AsOfDate())
            .Where(finding => finding.Severity == FindingSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            $"{caseId} at {AsOfDate():yyyy-MM-dd}: {errors.Length} error finding(s):\n" +
            string.Join('\n', errors.Select(f => $"  {f.RuleId} {f.Field} expected '{f.Expected}' actual '{f.Actual}'")));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Mapped_ground_truth_matches_the_manifest_expected_block(string caseId)
    {
        var expected = (JsonObject)Cases().Single(entry => entry["case_id"]!.GetValue<string>() == caseId)["expected"]!;
        var invoice = NfeXmlMapper.Load(RepoFiles.PathOf("data", "skeleton", XmlFileOf(caseId)));

        Assert.Equal(expected["item_count"]!.GetValue<int>(), invoice.Items.Count);
        Assert.Equal(expected["installment_count"]!.GetValue<int>(), invoice.Installments.Count);
        Assert.Equal(expected["invoice_total"]!.GetValue<string>(), invoice.Totals.InvoiceTotal.ToString());
        Assert.Equal(expected["issuer_cnpj"]!.GetValue<string>(), invoice.Issuer.Cnpj);
        Assert.Equal(expected["recipient_tax_id"]!.GetValue<string>(), invoice.Recipient.TaxId);
        Assert.Equal(
            expected["recipient_tax_id_kind"]!.GetValue<string>(),
            JsonNamingPolicy.SnakeCaseLower.ConvertName(invoice.Recipient.TaxIdKind.ToString()));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Mapped_ground_truth_round_trips_through_the_wire_options_without_pattern_violations(string caseId)
    {
        var invoice = NfeXmlMapper.Load(RepoFiles.PathOf("data", "skeleton", XmlFileOf(caseId)));

        var json = JsonSerializer.Serialize(invoice, Wire.Options);
        var again = JsonSerializer.Deserialize<Invoice>(json, Wire.Options)!;

        Assert.Empty(again.PatternViolations());
        Assert.Equal(json, JsonSerializer.Serialize(again, Wire.Options));
    }

    [Fact]
    public void Case_003_maps_to_a_cpf_recipient_without_ie_and_an_isento_issuer()
    {
        var invoice = NfeXmlMapper.Load(RepoFiles.PathOf("data", "skeleton", "case-003.xml"));

        Assert.Equal(TaxIdKind.Cpf, invoice.Recipient.TaxIdKind);
        Assert.Null(invoice.Recipient.Ie);
        Assert.Equal("ISENTO", invoice.Issuer.Ie);
    }

    [Fact]
    public void Case_002_has_two_installments_and_nonzero_ipi_columns()
    {
        var invoice = NfeXmlMapper.Load(RepoFiles.PathOf("data", "skeleton", "case-002.xml"));

        Assert.Equal(2, invoice.Installments.Count);
        Assert.Contains(invoice.Items, item => item.IpiAmount.Amount != 0m);
        Assert.NotEqual(0m, invoice.Totals.IpiAmount.Amount);
    }

    private static JsonObject Manifest() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(RepoFiles.PathOf("data", "skeleton", "manifest.json")))!;

    private static DateOnly AsOfDate() =>
        DateOnly.ParseExact(Manifest()["as_of_date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IEnumerable<JsonObject> Cases() =>
        Manifest()["cases"]!.AsArray().Select(node => (JsonObject)node!);

    private static string XmlFileOf(string caseId) =>
        Cases().Single(entry => entry["case_id"]!.GetValue<string>() == caseId)["xml"]!.GetValue<string>();
}
