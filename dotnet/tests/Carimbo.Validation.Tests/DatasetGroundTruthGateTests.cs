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
}
