using System.Text.Json;
using Xunit;

namespace Carimbo.Domain.Tests;

public class DomainTests
{
    private const string OversizedAmount = "99999999999999999999999999999999.00";

    private const string ValidInvoiceJson = """
        {
          "access_key": "352601AB1C2D3E000130550010000001231000001230",
          "number": 1,
          "series": 1,
          "issue_date": "2026-03-04",
          "issuer": { "cnpj": "AB1C2D3E000130", "name": "Emitente SINTETICA Ltda" },
          "recipient": { "cnpj": "11222333000181", "name": "Destinataria SINTETICA SA" },
          "total_amount": "1234.50"
        }
        """;

    [Fact]
    public void Domain_assembly_references_only_the_base_class_library()
    {
        var offenders = typeof(Invoice).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => !(name.StartsWith("System", StringComparison.Ordinal)
                || name == "netstandard"
                || name == "mscorlib"))
            .ToList();

        Assert.True(offenders.Count == 0, "Carimbo.Domain must stay pure; unexpected references: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Domain_project_file_has_no_package_or_project_references()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot.Find(), "dotnet", "src", "Carimbo.Domain", "Carimbo.Domain.csproj"));

        Assert.DoesNotContain("PackageReference", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", csproj, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1234.50", "1234.50")]
    [InlineData("0.00", "0.00")]
    [InlineData("-0.01", "-0.01")]
    public void Money_accepts_the_invariant_two_decimal_form(string text, string expected)
    {
        var money = JsonSerializer.Deserialize<Money>($"\"{text}\"", Wire.Options);

        Assert.Equal(expected, money.ToString());
    }

    [Theory]
    [InlineData("12,34")]
    [InlineData("1.234,56")]
    [InlineData("12.3")]
    [InlineData("12.345")]
    [InlineData("1e3")]
    [InlineData(" 12.34")]
    [InlineData("")]
    public void Money_rejects_malformed_and_pt_br_amounts(string text)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>($"\"{text}\"", Wire.Options));
    }

    [Fact]
    public void Money_rejects_a_bare_json_number()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("1234.50", Wire.Options));
    }

    [Theory]
    [InlineData(OversizedAmount)]
    [InlineData("79228162514264337593543950336.00")]
    [InlineData("-79228162514264337593543950336.00")]
    public void Money_Parse_reports_an_amount_too_large_for_a_decimal_as_a_FormatException(string text)
    {
        Assert.Throws<FormatException>(() => Money.Parse(text));
    }

    [Fact]
    public void Money_rejects_an_amount_too_large_for_a_decimal_as_a_json_error()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>($"\"{OversizedAmount}\"", Wire.Options));
    }

    [Fact]
    public void Money_accepts_the_largest_decimal_amount()
    {
        const string largest = "79228162514264337593543950335.00";

        var money = Money.Parse(largest);

        Assert.Equal(decimal.MaxValue, money.Amount);
        Assert.Equal(largest, money.ToString());
    }

    [Fact]
    public void Money_round_trips_exactly()
    {
        var parsed = Money.Parse("1234.50");

        Assert.Equal(1234.50m, parsed.Amount);
        Assert.Equal("\"1234.50\"", JsonSerializer.Serialize(parsed, Wire.Options));
    }

    [Fact]
    public void Invoice_deserializes_valid_snake_case_json()
    {
        var invoice = JsonSerializer.Deserialize<Invoice>(ValidInvoiceJson, Wire.Options)!;

        Assert.Equal("352601AB1C2D3E000130550010000001231000001230", invoice.AccessKey);
        Assert.Equal(1, invoice.Number);
        Assert.Equal(1, invoice.Series);
        Assert.Equal(new DateOnly(2026, 3, 4), invoice.IssueDate);
        Assert.Equal("AB1C2D3E000130", invoice.Issuer.Cnpj);
        Assert.Equal("Destinataria SINTETICA SA", invoice.Recipient.Name);
        Assert.Equal(1234.50m, invoice.TotalAmount.Amount);
    }

    [Fact]
    public void Invoice_PatternViolations_is_empty_for_a_conformant_invoice()
    {
        var invoice = JsonSerializer.Deserialize<Invoice>(ValidInvoiceJson, Wire.Options)!;

        Assert.Empty(invoice.PatternViolations());
    }

    [Fact]
    public void Invoice_PatternViolations_names_each_violating_json_path()
    {
        var invoice = JsonSerializer.Deserialize<Invoice>(ValidInvoiceJson, Wire.Options)!;
        var broken = invoice with
        {
            AccessKey = "bad",
            Issuer = invoice.Issuer with { Cnpj = "11.222.333/0001-81" },
            Recipient = invoice.Recipient with { Cnpj = "x" },
        };

        Assert.Equal(["access_key", "issuer.cnpj", "recipient.cnpj"], broken.PatternViolations());
    }

    [Fact]
    public void Patterns_IsFullMatch_rejects_a_trailing_newline_that_the_dollar_anchor_accepts()
    {
        const string key = "352601AB1C2D3E000130550010000001231000001230";

        Assert.Matches(Patterns.AccessKey, key + "\n");
        Assert.False(Patterns.IsFullMatch(Patterns.AccessKey, key + "\n"));
        Assert.True(Patterns.IsFullMatch(Patterns.AccessKey, key));
    }

    [Fact]
    public void Strict_parsing_rejects_an_unknown_property()
    {
        var json = ValidInvoiceJson.Replace("\"series\": 1,", "\"series\": 1, \"surprise\": true,", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Invoice>(json, Wire.Options));
    }

    [Fact]
    public void Strict_parsing_rejects_a_missing_required_property()
    {
        var json = ValidInvoiceJson.Replace("\"series\": 1,", string.Empty, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Invoice>(json, Wire.Options));
    }

    [Fact]
    public void Strict_parsing_rejects_a_null_name()
    {
        var json = ValidInvoiceJson.Replace("\"Emitente SINTETICA Ltda\"", "null", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Invoice>(json, Wire.Options));
    }

    [Fact]
    public void Strict_parsing_rejects_truncated_json()
    {
        var json = ValidInvoiceJson[..(ValidInvoiceJson.Length / 2)];

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Invoice>(json, Wire.Options));
    }

    [Fact]
    public void Decision_outcome_serializes_as_a_snake_case_string()
    {
        var json = JsonSerializer.Serialize(new Decision(DecisionOutcome.Escalate, "needs a human"), Wire.Options);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("escalate", document.RootElement.GetProperty("outcome").GetString());
    }
}

/// <summary>Locates the repository root from the test binary's directory.</summary>
internal static class RepoRoot
{
    public static string Find()
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

        throw new InvalidOperationException("Could not find the repo root (dotnet/Carimbo.slnx) above " + AppContext.BaseDirectory);
    }
}
