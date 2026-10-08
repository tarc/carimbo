using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Carimbo.Domain.Tests;

public class DomainTests
{
    private const string OversizedAmount = "99999999999999999999999999999999.00";

    private const string FixtureAccessKey = "35260311222333000181550010000001231000012346";

    private static string FixtureText =>
        File.ReadAllText(Path.Combine(RepoRoot.Find(), "data", "vectors", "valid-invoice.json"));

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

    // ---- Money ----------------------------------------------------------------------------------

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
    [InlineData("1.5")]
    [InlineData("1.500")]
    [InlineData("12.345")]
    [InlineData("+1.00")]
    [InlineData("1e3")]
    [InlineData(" 12.34")]
    [InlineData("12.34 ")]
    [InlineData("12.34\\n")]
    [InlineData("1,234.56")]
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

    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("-2.345", "-2.35")]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("1.005", "1.01")]
    [InlineData("2.675", "2.68")]
    [InlineData("2.344", "2.34")]
    [InlineData("-0.004", "0.00")]
    [InlineData("0.004", "0.00")]
    [InlineData("-0.00", "0.00")]
    public void Money_RoundHalfUp_rounds_half_away_from_zero_and_never_returns_negative_zero(string input, string expected)
    {
        var rounded = Money.RoundHalfUp(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, new Money(rounded).ToString());
        if (rounded == 0m)
        {
            Assert.False(decimal.IsNegative(rounded), "zero must not carry a sign");
        }
    }

    // ---- Decimal4 and Rate ----------------------------------------------------------------------

    [Theory]
    [InlineData("2.0000", "2.0000")]
    [InlineData("0.0000", "0.0000")]
    [InlineData("183737.4400", "183737.4400")]
    public void Decimal4_accepts_exactly_four_fraction_digits(string text, string expected)
    {
        var value = JsonSerializer.Deserialize<Decimal4>($"\"{text}\"", Wire.Options);

        Assert.Equal(expected, value.ToString());
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, Wire.Options));
    }

    [Theory]
    [InlineData("2.00")]
    [InlineData("2.00000")]
    [InlineData("2")]
    [InlineData("2,0000")]
    [InlineData("1.234,5600")]
    [InlineData("-1.0000")]
    [InlineData("+1.0000")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData(" 2.0000")]
    [InlineData("2.0000 ")]
    [InlineData("2.0000\\n")]
    public void Decimal4_rejects_every_other_form(string text)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Decimal4>($"\"{text}\"", Wire.Options));
    }

    [Theory]
    [InlineData("18.00", "18.00")]
    [InlineData("0.00", "0.00")]
    [InlineData("7.50", "7.50")]
    public void Rate_accepts_exactly_two_fraction_digits(string text, string expected)
    {
        var value = JsonSerializer.Deserialize<Rate>($"\"{text}\"", Wire.Options);

        Assert.Equal(expected, value.ToString());
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, Wire.Options));
    }

    [Theory]
    [InlineData("-1.00")]
    [InlineData("+1.00")]
    [InlineData("18.0")]
    [InlineData("18.000")]
    [InlineData("18")]
    [InlineData("18,00")]
    [InlineData("18.00%")]
    [InlineData("1e1")]
    [InlineData("")]
    [InlineData(" 18.00")]
    [InlineData("18.00\\n")]
    public void Rate_rejects_every_other_form(string text)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Rate>($"\"{text}\"", Wire.Options));
    }

    [Fact]
    public void Decimal4_and_Rate_reject_a_bare_json_number()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Decimal4>("2.0", Wire.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Rate>("18.0", Wire.Options));
    }

    [Fact]
    public void Decimal4_and_Rate_report_an_amount_too_large_for_a_decimal_as_a_json_error()
    {
        var big = new string('9', 40);

        Assert.Throws<FormatException>(() => Decimal4.Parse(big + ".0000"));
        Assert.Throws<FormatException>(() => Rate.Parse(big + ".00"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Decimal4>($"\"{big}.0000\"", Wire.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Rate>($"\"{big}.00\"", Wire.Options));
    }

    // ---- Invoice --------------------------------------------------------------------------------

    [Fact]
    public void The_shared_fixture_parses_strictly_into_the_v2_invoice()
    {
        var invoice = JsonSerializer.Deserialize<Invoice>(FixtureText, Wire.Options)!;

        Assert.Equal(FixtureAccessKey, invoice.AccessKey);
        Assert.Equal(123, invoice.Number);
        Assert.Equal(1, invoice.Series);
        Assert.Equal(new DateOnly(2026, 3, 15), invoice.IssueDate);
        Assert.Equal("VENDA DE MERCADORIA", invoice.OperationNature);
        Assert.Equal("11222333000181", invoice.Issuer.Cnpj);
        Assert.Equal("123456789012", invoice.Issuer.Ie);
        Assert.Equal("SP", invoice.Issuer.Uf);
        Assert.Equal("12ABC34501DE35", invoice.Recipient.TaxId);
        Assert.Equal(TaxIdKind.Cnpj, invoice.Recipient.TaxIdKind);
        Assert.Null(invoice.Recipient.Ie);
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal(2.0000m, invoice.Items[0].Quantity.Amount);
        Assert.Equal(3.5000m, invoice.Items[1].Quantity.Amount);
        Assert.Equal("040", invoice.Items[1].CstCsosn);
        Assert.Equal(155.00m, invoice.Totals.InvoiceTotal.Amount);
        Assert.Equal(2, invoice.Installments.Count);
        Assert.Equal(new DateOnly(2026, 4, 14), invoice.Installments[0].DueDate);
        Assert.Empty(invoice.PatternViolations());
    }

    [Fact]
    public void The_shared_fixture_round_trips_through_the_wire_options()
    {
        var invoice = JsonSerializer.Deserialize<Invoice>(FixtureText, Wire.Options)!;

        var written = JsonNode.Parse(JsonSerializer.Serialize(invoice, Wire.Options))!;
        var original = JsonNode.Parse(FixtureText)!;

        Assert.True(JsonNode.DeepEquals(original, written), written.ToJsonString());
    }

    [Fact]
    public void A_cpf_recipient_parses_and_has_no_pattern_violations()
    {
        var invoice = Parse(node =>
        {
            node["recipient"]!["tax_id"] = "52998224725";
            node["recipient"]!["tax_id_kind"] = "cpf";
        });

        Assert.Equal("52998224725", invoice.Recipient.TaxId);
        Assert.Equal(TaxIdKind.Cpf, invoice.Recipient.TaxIdKind);
        Assert.Empty(invoice.PatternViolations());
    }

    [Fact]
    public void Numeric_and_alphanumeric_cnpj_and_access_key_forms_are_accepted()
    {
        var alphanumeric = Parse(node =>
        {
            node["access_key"] = "352603AB1C2D3E000130550010000001231000012340";
            node["issuer"]!["cnpj"] = "AB1C2D3E000130";
            node["recipient"]!["tax_id"] = "11222333000181";
        });

        Assert.Empty(alphanumeric.PatternViolations());
    }

    [Theory]
    [InlineData("items[1].ncm", "7318150", "items[1].ncm")]
    [InlineData("items[0].ncm", "7318150A", "items[0].ncm")]
    [InlineData("items[0].cfop", "510", "items[0].cfop")]
    [InlineData("items[0].cst_csosn", "00", "items[0].cst_csosn")]
    [InlineData("items[1].cst_csosn", "01020", "items[1].cst_csosn")]
    [InlineData("recipient.tax_id", "1234567890123", "recipient.tax_id")]
    [InlineData("recipient.uf", "S", "recipient.uf")]
    [InlineData("issuer.uf", "sp", "issuer.uf")]
    [InlineData("issuer.cnpj", "11.222.333/0001-81", "issuer.cnpj")]
    [InlineData("access_key", FixtureAccessKey + "\n", "access_key")]
    public void PatternViolations_names_exactly_the_violating_path(string path, string value, string expected)
    {
        var invoice = Parse(node => SetPath(node, path, value));

        Assert.Equal([expected], invoice.PatternViolations());
    }

    [Theory]
    [InlineData("access_key")]
    [InlineData("issuer.cnpj")]
    [InlineData("recipient.tax_id")]
    public void An_empty_or_whitespace_identifier_is_a_pattern_violation(string path)
    {
        foreach (var value in new[] { string.Empty, " ", "\t", "  \n " })
        {
            var invoice = Parse(node => SetPath(node, path, value));

            Assert.Equal([path], invoice.PatternViolations());
        }
    }

    [Theory]
    [InlineData("access_key", FixtureAccessKey)]
    [InlineData("issuer.cnpj", "11222333000181")]
    [InlineData("recipient.tax_id", "12ABC34501DE35")]
    [InlineData("recipient.tax_id", "52998224725")]
    public void Identifiers_are_exact_ascii_strings_of_their_length(string path, string valid)
    {
        var arabicIndicDigit = '٣';
        var variants = new List<string>
        {
            valid + "\n",
            valid[..^1] + arabicIndicDigit,
            valid + "0",
            valid[..^1],
        };
        if (valid.ToLowerInvariant() != valid)
        {
            variants.Add(valid.ToLowerInvariant());
        }

        foreach (var variant in variants)
        {
            var invoice = Parse(node => SetPath(node, path, variant));

            Assert.Equal([path], invoice.PatternViolations());
        }
    }

    [Fact]
    public void PatternViolations_reports_every_violation_in_property_order()
    {
        var invoice = Parse(node =>
        {
            SetPath(node, "access_key", "bad");
            SetPath(node, "issuer.uf", "sp");
            SetPath(node, "recipient.tax_id", "x");
            SetPath(node, "items[1].cfop", "1");
            SetPath(node, "items[0].ncm", "1");
        });

        Assert.Equal(["access_key", "issuer.uf", "recipient.tax_id", "items[0].ncm", "items[1].cfop"], invoice.PatternViolations());
    }

    [Fact]
    public void The_fixture_has_no_NullViolations()
    {
        var invoice = JsonSerializer.Deserialize<Invoice>(FixtureText, Wire.Options)!;

        Assert.Empty(invoice.NullViolations());
    }

    [Fact]
    public void A_null_items_element_is_a_NullViolation_and_not_a_PatternViolation()
    {
        var invoice = Parse(node => node["items"] = new JsonArray((JsonNode?)null));

        Assert.Equal(["items[0]"], invoice.NullViolations());
        Assert.Empty(invoice.PatternViolations());
    }

    [Fact]
    public void A_null_installments_element_is_reported_at_its_index()
    {
        var invoice = Parse(node => node["installments"] = new JsonArray(node["installments"]![0]!.DeepClone(), null));

        Assert.Equal(["installments[1]"], invoice.NullViolations());
    }

    [Fact]
    public void A_json_null_state_registration_is_not_a_NullViolation()
    {
        var invoice = Parse(node =>
        {
            node["issuer"]!["ie"] = null;
            node["recipient"]!["ie"] = null;
        });

        Assert.Null(invoice.Issuer.Ie);
        Assert.Empty(invoice.NullViolations());
    }

    [Fact]
    public void NullViolations_lists_the_items_paths_before_the_installments_paths_and_descends_into_elements()
    {
        var fixture = JsonSerializer.Deserialize<Invoice>(FixtureText, Wire.Options)!;

        var lists = fixture with { Items = [null!], Installments = [null!, null!] };
        var nested = fixture with { Items = [fixture.Items[0], fixture.Items[1] with { Ncm = null! }] };

        Assert.Equal(["items[0]", "installments[0]", "installments[1]"], lists.NullViolations());
        Assert.Equal(["items[1].ncm"], nested.NullViolations());
    }

    [Fact]
    public void PatternViolations_covers_every_regular_expression_attribute_by_reflection()
    {
        // Every string member carrying [RegularExpression] anywhere under Invoice must be reachable
        // by the walker: a mutation of each one to a bad value is reported.
        var invoice = JsonSerializer.Deserialize<Invoice>(FixtureText, Wire.Options)!;
        var expected = new List<string>
        {
            "access_key",
            "issuer.cnpj",
            "issuer.uf",
            "recipient.tax_id",
            "recipient.uf",
            "items[0].ncm",
            "items[0].cst_csosn",
            "items[0].cfop",
        };

        var broken = invoice with
        {
            AccessKey = "!",
            Issuer = invoice.Issuer with { Cnpj = "!", Uf = "!" },
            Recipient = invoice.Recipient with { TaxId = "!", Uf = "!" },
            Items = [invoice.Items[0] with { Ncm = "!", CstCsosn = "!", Cfop = "!" }],
        };

        Assert.Equal(expected, broken.PatternViolations());
    }

    [Fact]
    public void Patterns_IsFullMatch_rejects_a_trailing_newline_that_the_dollar_anchor_accepts()
    {
        Assert.Matches(Patterns.AccessKey, FixtureAccessKey + "\n");
        Assert.False(Patterns.IsFullMatch(Patterns.AccessKey, FixtureAccessKey + "\n"));
        Assert.True(Patterns.IsFullMatch(Patterns.AccessKey, FixtureAccessKey));
        Assert.False(Patterns.IsFullMatch(Patterns.TaxId, "52998224725\n"));
    }

    [Fact]
    public void Strict_parsing_rejects_an_unknown_property()
    {
        Assert.Throws<JsonException>(() => Parse(node => node["surprise"] = true));
        Assert.Throws<JsonException>(() => Parse(node => node["items"]![0]!["surprise"] = "x"));
    }

    [Fact]
    public void Strict_parsing_rejects_a_missing_required_property()
    {
        Assert.Throws<JsonException>(() => Parse(node => node.Remove("installments")));
        Assert.Throws<JsonException>(() => Parse(node => node["recipient"]!.AsObject().Remove("ie")));
        Assert.Throws<JsonException>(() => Parse(node => node["totals"]!.AsObject().Remove("invoice_total")));
    }

    [Fact]
    public void Strict_parsing_rejects_a_null_name_but_admits_a_null_ie()
    {
        Assert.Throws<JsonException>(() => Parse(node => node["issuer"]!["name"] = null));
        Assert.Throws<JsonException>(() => Parse(node => node["items"] = null));

        var invoice = Parse(node => node["issuer"]!["ie"] = null);
        Assert.Null(invoice.Issuer.Ie);
    }

    [Fact]
    public void Strict_parsing_rejects_the_phase_1_top_level_amount()
    {
        Assert.Throws<JsonException>(() => Parse(node => node["total_amount"] = "155.00"));
    }

    [Fact]
    public void Strict_parsing_rejects_an_unknown_tax_id_kind()
    {
        Assert.Throws<JsonException>(() => Parse(node => node["recipient"]!["tax_id_kind"] = "passport"));
    }

    [Fact]
    public void Strict_parsing_rejects_truncated_json()
    {
        var json = FixtureText[..(FixtureText.Length / 2)];

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Invoice>(json, Wire.Options));
    }

    [Fact]
    public void Decision_outcome_serializes_as_a_snake_case_string()
    {
        var json = JsonSerializer.Serialize(new Decision(DecisionOutcome.Escalate, "needs a human"), Wire.Options);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("escalate", document.RootElement.GetProperty("outcome").GetString());
    }

    private static Invoice Parse(Action<JsonObject> mutate) =>
        JsonSerializer.Deserialize<Invoice>(MutatedText(mutate), Wire.Options)!;

    private static string MutatedText(Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(FixtureText)!.AsObject();
        mutate(node);
        return node.ToJsonString();
    }

    /// <summary>Sets a string at a dotted path whose segments may carry an index, for example <c>items[1].ncm</c>.</summary>
    private static void SetPath(JsonObject root, string path, string value)
    {
        JsonNode current = root;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            var name = bracket < 0 ? segment : segment[..bracket];
            var last = i == segments.Length - 1;
            if (last)
            {
                current.AsObject()[name] = value;
                return;
            }

            current = current[name]!;
            if (bracket >= 0)
            {
                var index = int.Parse(segment[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
                current = current[index]!;
            }
        }
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
