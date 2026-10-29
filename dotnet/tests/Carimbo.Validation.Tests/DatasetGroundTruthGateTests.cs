using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Carimbo.Domain;
using Carimbo.GroundTruth;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// The DATA-04 ground-truth gate over every case of <c>data/dataset</c> (D-18): each manifest case is
/// mapped by the documented .NET mapper and validated at the manifest as_of_date. Warnings are allowed,
/// errors are not. The oracle is never adjusted to make a case pass: a failing case is a generator bug.
/// </summary>
public class DatasetGroundTruthGateTests
{
    public static TheoryData<string> CaseIds => GroundTruthManifest.For("dataset").CaseIds();

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Mapped_ground_truth_has_no_validator_error_at_the_manifest_as_of_date(string caseId)
    {
        var manifest = GroundTruthManifest.For("dataset");
        var invoice = NfeXmlMapper.Load(manifest.XmlPathOf(caseId));

        var errors = new InvoiceValidator(new ValidationOptions())
            .Validate(invoice, manifest.AsOfDate)
            .Where(finding => finding.Severity == FindingSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            $"{caseId} at {manifest.AsOfDate:yyyy-MM-dd}: {errors.Length} error finding(s):\n" +
            string.Join('\n', errors.Select(f => $"  {f.RuleId} {f.Field} expected '{f.Expected}' actual '{f.Actual}'")));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Mapped_ground_truth_matches_the_manifest_expected_block(string caseId)
    {
        var manifest = GroundTruthManifest.For("dataset");
        var expected = (JsonObject)manifest.CaseOf(caseId)["expected"]!;
        var invoice = NfeXmlMapper.Load(manifest.XmlPathOf(caseId));

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
        var invoice = NfeXmlMapper.Load(GroundTruthManifest.For("dataset").XmlPathOf(caseId));

        var json = JsonSerializer.Serialize(invoice, Wire.Options);
        var again = JsonSerializer.Deserialize<Invoice>(json, Wire.Options)!;

        Assert.Empty(again.PatternViolations());
        Assert.Equal(json, JsonSerializer.Serialize(again, Wire.Options));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Only_st_deferral_cases_carry_the_unsupported_tax_code_warning(string caseId)
    {
        var manifest = GroundTruthManifest.For("dataset");
        var tags = manifest.CaseOf(caseId)["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()).ToArray();
        var invoice = NfeXmlMapper.Load(manifest.XmlPathOf(caseId));

        var warnings = new InvoiceValidator(new ValidationOptions())
            .Validate(invoice, manifest.AsOfDate)
            .Where(finding => finding.Severity == FindingSeverity.Warning)
            .ToArray();

        if (tags.Contains("st_deferral"))
        {
            Assert.Contains(warnings, finding => finding.RuleId == RuleIds.TAX_CODE_UNSUPPORTED);
        }
        else
        {
            Assert.True(
                warnings.Length == 0,
                $"{caseId} has no st_deferral tag but {warnings.Length} warning(s):\n" +
                string.Join('\n', warnings.Select(f => $"  {f.RuleId} {f.Field} expected '{f.Expected}' actual '{f.Actual}'")));
        }
    }

    [Fact]
    public void The_manifest_names_the_dataset_and_lists_unique_nfe_numbered_case_ids()
    {
        var manifest = GroundTruthManifest.For("dataset");
        var ids = manifest.Cases.Select(entry => entry["case_id"]!.GetValue<string>()).ToArray();

        Assert.Equal("dataset", manifest.Name);
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(new Regex("^nfe-[0-9]{4}$"), id));
    }

    [Fact]
    public void The_manifest_case_set_equals_the_xml_and_pdf_files_on_disk()
    {
        var manifest = GroundTruthManifest.For("dataset");
        var ids = manifest.Cases.Select(entry => entry["case_id"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        var files = Directory.GetFiles(RepoFiles.PathOf("data", "dataset")).Select(Path.GetFileName).ToArray();

        Assert.Equal(
            ids,
            files.Where(name => name!.EndsWith(".xml", StringComparison.Ordinal)).Select(name => Path.GetFileNameWithoutExtension(name)!).Order(StringComparer.Ordinal));
        Assert.Equal(
            ids,
            files.Where(name => name!.EndsWith(".pdf", StringComparison.Ordinal)).Select(name => Path.GetFileNameWithoutExtension(name)!).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Every_xml_matches_its_manifest_hash(string caseId)
    {
        var manifest = GroundTruthManifest.For("dataset");
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifest.XmlPathOf(caseId))));

        Assert.Equal(manifest.CaseOf(caseId)["xml_sha256"]!.GetValue<string>(), hash);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void A_twin_xml_is_a_byte_copy_of_its_base_xml(string caseId)
    {
        var manifest = GroundTruthManifest.For("dataset");
        var baseCase = manifest.CaseOf(caseId)["base_case"]!.GetValue<string>();

        Assert.Equal(File.ReadAllBytes(manifest.XmlPathOf(baseCase)), File.ReadAllBytes(manifest.XmlPathOf(caseId)));
    }
}
