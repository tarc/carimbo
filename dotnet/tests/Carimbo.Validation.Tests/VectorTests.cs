using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Domain;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>Shared access to the repository's committed vector and fixture files, and a one-line validator.</summary>
internal static class RepoFiles
{
    /// <summary>The reference date every test validates against unless it says otherwise.</summary>
    public static readonly DateOnly Reference = new(2026, 10, 1);

    private static readonly string Root = FindRoot();

    public static JsonObject ReadVectors() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "data", "vectors", "validator-vectors.json")))!;

    /// <summary>Absolute path of a file under the repository root.</summary>
    public static string PathOf(params string[] segments) => Path.Combine([Root, .. segments]);

    public static Invoice LoadValidInvoice() => Deserialize(ValidInvoiceNode());

    /// <summary>Parses the shared fixture, lets <paramref name="mutate"/> edit the JSON, and reads it back strictly.</summary>
    public static Invoice MutateValidInvoice(Action<JsonObject> mutate)
    {
        var node = ValidInvoiceNode();
        mutate(node);
        return Deserialize(node);
    }

    public static IReadOnlyList<ValidationFinding> Validate(Invoice invoice) =>
        new InvoiceValidator(new ValidationOptions()).Validate(invoice, Reference);

    public static IReadOnlyList<ValidationFinding> Validate(Invoice invoice, DateOnly referenceDate) =>
        new InvoiceValidator(new ValidationOptions()).Validate(invoice, referenceDate);

    public static string[] RuleIdsOf(IEnumerable<ValidationFinding> findings) =>
        findings.Select(finding => finding.RuleId).ToArray();

    public static string[] RuleIdsOn(IEnumerable<ValidationFinding> findings, params string[] fields) =>
        findings.Where(finding => fields.Contains(finding.Field)).Select(finding => finding.RuleId).ToArray();

    public static decimal Decimal(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static JsonObject ValidInvoiceNode() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "data", "vectors", "valid-invoice.json")))!;

    private static Invoice Deserialize(JsonObject node) =>
        JsonSerializer.Deserialize<Invoice>(node.ToJsonString(), Wire.Options)!;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet", "Carimbo.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root (dotnet/Carimbo.slnx) above " + AppContext.BaseDirectory);
    }
}

/// <summary>The shared vector file and the fixture, run through the .NET validator (D-09).</summary>
public class VectorTests
{
    public static TheoryData<string, bool, string?> CnpjVectors => IdentifierVectors("cnpj");

    public static TheoryData<string, bool, string?> CpfVectors => IdentifierVectors("cpf");

    public static TheoryData<string, bool, string?> AccessKeyVectors => IdentifierVectors("access_key");

    public static TheoryData<string, string> RoundingVectors
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var vector in RepoFiles.ReadVectors()["rounding"]!.AsArray())
            {
                data.Add((string)vector!["input"]!, (string)vector["expected"]!);
            }

            return data;
        }
    }

    public static TheoryData<int, string> SumToleranceCapCases
    {
        get
        {
            var data = new TheoryData<int, string>();
            foreach (var vector in RepoFiles.ReadVectors()["sum_tolerance"]!["cap_cases"]!.AsArray())
            {
                data.Add((int)vector!["n"]!, (string)vector["expected"]!);
            }

            return data;
        }
    }

    public static TheoryData<string, string, int, bool> SumToleranceWithinCases
    {
        get
        {
            var data = new TheoryData<string, string, int, bool>();
            foreach (var vector in RepoFiles.ReadVectors()["sum_tolerance"]!["within"]!.AsArray())
            {
                data.Add((string)vector!["computed"]!, (string)vector["printed"]!, (int)vector["n"]!, (bool)vector["within"]!);
            }

            return data;
        }
    }

    private static TheoryData<string, bool, string?> IdentifierVectors(string section)
    {
        var data = new TheoryData<string, bool, string?>();
        foreach (var vector in RepoFiles.ReadVectors()[section]!.AsArray())
        {
            data.Add((string)vector!["value"]!, (bool)vector["valid"]!, (string?)vector["rule"]);
        }

        return data;
    }

    [Fact]
    public void The_shared_fixture_validates_clean_at_the_reference_date()
    {
        Assert.Empty(RepoFiles.Validate(RepoFiles.LoadValidInvoice()));
    }

    // ---- identifiers ----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(CnpjVectors))]
    public void Cnpj_vectors_match_the_check_digit_routine(string value, bool valid, string? rule)
    {
        Assert.Equal(valid, Identifiers.IsValidCnpj(value));
        Assert.Equal(valid, rule is null);
    }

    [Theory]
    [MemberData(nameof(CnpjVectors))]
    public void Cnpj_vectors_at_the_issuer_give_exactly_the_vectors_rule(string value, bool valid, string? rule)
    {
        var invoice = RepoFiles.MutateValidInvoice(node => node["issuer"]!["cnpj"] = value);

        var onField = RepoFiles.RuleIdsOn(RepoFiles.Validate(invoice), "issuer.cnpj");

        Assert.Equal(valid ? [] : new[] { rule! }, onField);
    }

    [Theory]
    [MemberData(nameof(CnpjVectors))]
    public void Cnpj_vectors_at_a_cnpj_recipient_give_exactly_the_vectors_rule(string value, bool valid, string? rule)
    {
        var invoice = RepoFiles.MutateValidInvoice(node =>
        {
            node["recipient"]!["tax_id"] = value;
            node["recipient"]!["tax_id_kind"] = "cnpj";
        });

        var onFields = RepoFiles.RuleIdsOn(RepoFiles.Validate(invoice), "recipient.tax_id", "recipient.tax_id_kind");

        Assert.Equal(valid ? [] : new[] { rule! }, onFields);
    }

    [Theory]
    [MemberData(nameof(CpfVectors))]
    public void Cpf_vectors_match_the_check_digit_routine(string value, bool valid, string? rule)
    {
        Assert.Equal(valid, Identifiers.IsValidCpf(value));
        Assert.Equal(valid, rule is null);
    }

    [Theory]
    [MemberData(nameof(CpfVectors))]
    public void Cpf_vectors_at_a_cpf_recipient_give_exactly_the_vectors_rule(string value, bool valid, string? rule)
    {
        var invoice = RepoFiles.MutateValidInvoice(node =>
        {
            node["recipient"]!["tax_id"] = value;
            node["recipient"]!["tax_id_kind"] = "cpf";
        });

        var onFields = RepoFiles.RuleIdsOn(RepoFiles.Validate(invoice), "recipient.tax_id", "recipient.tax_id_kind");

        Assert.Equal(valid ? [] : new[] { rule! }, onFields);
    }

    [Fact]
    public void A_valid_cpf_declared_as_cnpj_is_a_kind_mismatch_and_nothing_else()
    {
        var invoice = RepoFiles.MutateValidInvoice(node =>
        {
            node["recipient"]!["tax_id"] = "52998224725";
            node["recipient"]!["tax_id_kind"] = "cnpj";
        });

        var findings = RepoFiles.Validate(invoice);

        var finding = Assert.Single(findings);
        Assert.Equal(RuleIds.TAX_ID_KIND_MISMATCH, finding.RuleId);
        Assert.Equal("recipient.tax_id_kind", finding.Field);
        Assert.Equal("cpf", finding.Expected);
        Assert.Equal("cnpj", finding.Actual);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void A_valid_cnpj_declared_as_cpf_is_a_kind_mismatch_and_nothing_else()
    {
        var invoice = RepoFiles.MutateValidInvoice(node => node["recipient"]!["tax_id_kind"] = "cpf");

        Assert.Equal([RuleIds.TAX_ID_KIND_MISMATCH], RepoFiles.RuleIdsOf(RepoFiles.Validate(invoice)));
    }

    [Theory]
    [MemberData(nameof(AccessKeyVectors))]
    public void Access_key_vectors_match_the_check_digit_routine(string value, bool valid, string? rule)
    {
        Assert.Equal(valid, Identifiers.IsValidAccessKey(value));
        Assert.Equal(valid, rule is null);
    }

    [Theory]
    [MemberData(nameof(AccessKeyVectors))]
    public void Access_key_vectors_give_the_vectors_rule_on_the_key_itself(string value, bool valid, string? rule)
    {
        var invoice = RepoFiles.MutateValidInvoice(node => node["access_key"] = value);

        var own = RepoFiles.Validate(invoice)
            .Where(finding => finding.Field == "access_key"
                && (finding.RuleId == RuleIds.KEY_FORMAT || finding.RuleId == RuleIds.KEY_CHECK_DIGIT))
            .Select(finding => finding.RuleId)
            .ToArray();

        Assert.Equal(valid ? [] : new[] { rule! }, own);
    }

    [Fact]
    public void A_wrong_check_digit_reports_the_computed_and_the_printed_digit()
    {
        var invoice = RepoFiles.MutateValidInvoice(node => node["access_key"] = "35260311222333000181550010000001231000012347");

        var finding = Assert.Single(RepoFiles.Validate(invoice));

        Assert.Equal(RuleIds.KEY_CHECK_DIGIT, finding.RuleId);
        Assert.Equal("access_key", finding.Field);
        Assert.Equal("6", finding.Expected);
        Assert.Equal("7", finding.Actual);
    }

    [Fact]
    public void The_check_digit_routines_reproduce_the_published_examples()
    {
        Assert.Equal("35", Identifiers.CnpjCheckDigits("12ABC34501DE"));
        Assert.Equal("81", Identifiers.CnpjCheckDigits("112223330001"));
        Assert.Equal("25", Identifiers.CpfCheckDigits("529982247"));
        Assert.Equal(6, Identifiers.AccessKeyCheckDigit("3526031122233300018155001000000123100001234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12abc34501de")]
    [InlineData("12ABC34501DE\n")]
    [InlineData("١٢ABC34501DE")]
    public void The_digit_routines_refuse_input_that_is_not_their_alphabet(string notABase)
    {
        Assert.Throws<ArgumentException>(() => Identifiers.CnpjCheckDigits(notABase));
        Assert.False(Identifiers.IsValidCnpj(notABase + "35"));
    }

    [Fact]
    public void Is_valid_routines_are_false_for_null_and_for_arabic_indic_digits()
    {
        Assert.False(Identifiers.IsValidCnpj(null));
        Assert.False(Identifiers.IsValidCpf(null));
        Assert.False(Identifiers.IsValidAccessKey(null));
        Assert.False(Identifiers.IsValidCpf("٥٢٩٩٨٢٢٤٧٢٥"));
        Assert.False(Identifiers.IsValidAccessKey("٣٥" + "260311222333000181550010000001231000012346"));
    }

    [Fact]
    public void The_uf_table_has_the_27_units_with_distinct_ibge_codes()
    {
        Assert.Equal(27, Identifiers.UfCodes.Count);
        Assert.Equal(27, Identifiers.UfCodes.Values.Distinct().Count());
        Assert.Equal("35", Identifiers.UfCodes["SP"]);
        Assert.Equal("33", Identifiers.UfCodes["RJ"]);
        Assert.Equal("53", Identifiers.UfCodes["DF"]);
    }

    [Theory]
    [InlineData("issuer", "XX", "issuer.uf")]
    [InlineData("recipient", "XX", "recipient.uf")]
    [InlineData("recipient", "sp", "recipient.uf")]
    [InlineData("recipient", "S", "recipient.uf")]
    public void An_unknown_uf_is_reported_at_its_field(string party, string uf, string field)
    {
        var invoice = RepoFiles.MutateValidInvoice(node => node[party]!["uf"] = uf);

        var finding = Assert.Single(RepoFiles.Validate(invoice));

        Assert.Equal(RuleIds.UF_UNKNOWN, finding.RuleId);
        Assert.Equal(field, finding.Field);
        Assert.Equal(uf, finding.Actual);
    }

    // ---- rounding and tolerance -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(RoundingVectors))]
    public void Rounding_vectors_are_half_away_from_zero_with_no_negative_zero(string input, string expected)
    {
        var rounded = Money.RoundHalfUp(decimal.Parse(input, CultureInfo.InvariantCulture));

        Assert.Equal(expected, rounded.ToString("0.00", CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(SumToleranceCapCases))]
    public void Sum_tolerance_is_tolerance_times_max_n_one_capped(int n, string expected)
    {
        Assert.Equal(RepoFiles.Decimal(expected), new ValidationOptions().SumTolerance(n));
    }

    [Theory]
    [MemberData(nameof(SumToleranceWithinCases))]
    public void Sum_tolerance_comparison_is_inclusive(string computed, string printed, int n, bool within)
    {
        var tolerance = new ValidationOptions().SumTolerance(n);

        Assert.True(SafeMath.TryWithin(RepoFiles.Decimal(computed), RepoFiles.Decimal(printed), tolerance, out var actual));
        Assert.Equal(within, actual);
    }

    [Fact]
    public void The_difference_of_extreme_amounts_is_reported_as_overflow_not_thrown()
    {
        Assert.False(SafeMath.TryWithin(decimal.MaxValue, decimal.MinValue, 1.00m, out _));
    }

    [Fact]
    public void Options_defaults_equal_the_tolerance_block_of_the_vector_file()
    {
        var block = RepoFiles.ReadVectors()["tolerance"]!;
        var options = new ValidationOptions();

        Assert.Equal(RepoFiles.Decimal((string)block["tolerance"]!), options.Tolerance);
        Assert.Equal(RepoFiles.Decimal((string)block["sum_cap"]!), options.SumToleranceCap);
        Assert.Equal(new DateOnly(2006, 4, 1), ValidationOptions.EarliestIssueDate);
    }

    // ---- the finding shape ----------------------------------------------------------------------

    [Fact]
    public void A_finding_serializes_with_snake_case_members_and_a_lowercase_severity()
    {
        var node = (JsonObject)JsonSerializer.SerializeToNode(
            new ValidationFinding("items[0].total", RuleIds.ITEM_ARITH, "10.00", "10.05", FindingSeverity.Error),
            Wire.Options)!;

        Assert.Equal(["field", "rule_id", "expected", "actual", "severity"], node.Select(pair => pair.Key).ToArray());
        Assert.Equal("error", (string)node["severity"]!);

        var warning = (JsonObject)JsonSerializer.SerializeToNode(
            new ValidationFinding("items[0].cst_csosn", RuleIds.TAX_CODE_UNSUPPORTED, "", "010", FindingSeverity.Warning),
            Wire.Options)!;
        Assert.Equal("warning", (string)warning["severity"]!);
    }

    [Fact]
    public void Rule_ids_are_unique_and_each_value_equals_its_name()
    {
        var fields = typeof(RuleIds).GetFields().Where(field => field.IsLiteral).ToList();

        Assert.Equal(30, fields.Count);
        Assert.All(fields, field => Assert.Equal(field.Name, (string)field.GetRawConstantValue()!));
        Assert.Equal(30, fields.Select(field => field.Name).Distinct().Count());
    }

    [Fact]
    public void Validating_the_same_invoice_twice_gives_identical_findings()
    {
        var invoice = RepoFiles.MutateValidInvoice(node =>
        {
            node["issuer"]!["uf"] = "XX";
            node["recipient"]!["uf"] = "YY";
        });

        Assert.Equal(RepoFiles.Validate(invoice), RepoFiles.Validate(invoice));
    }
}
