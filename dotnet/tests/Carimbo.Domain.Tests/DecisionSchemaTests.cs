using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Carimbo.Domain.Tests;

/// <summary>Pins the Decision wire shape, its canonical schema file, its typed enums and its null boundary (AGT-02).</summary>
public class DecisionSchemaTests
{
    private const string WireText =
        "{\"evidence\":[{\"source\":\"get_invoice\",\"ref\":\"recipient.tax_id\",\"value\":\"69979991000100\"}],"
        + "\"reason\":\"Recipient is not the buyer.\",\"outcome\":\"reject\",\"cause\":\"none\"}";

    private static readonly string[] OutcomeNames = ["approve", "reject", "escalate"];

    private static readonly string[] CauseNames =
        ["none", "extraction_not_success", "policy_warning", "over_limit", "tool_failed", "step_budget_exhausted"];

    private static string CommittedPath => Path.Combine(RepoRoot.Find(), "schema", "decision.schema.json");

    private static Decision Sample() =>
        new(
            [new EvidenceItem("get_invoice", "recipient.tax_id", "69979991000100")],
            "Recipient is not the buyer.",
            DecisionOutcome.Reject,
            DecisionCause.None);

    // ---------------------------------------------------------------- wire shape

    [Fact]
    public void A_decision_is_written_in_schema_order_with_exact_snake_case_names()
    {
        Assert.Equal(WireText, JsonSerializer.Serialize(Sample(), Wire.Options));
    }

    [Fact]
    public void A_decision_round_trips_byte_for_byte()
    {
        var parsed = JsonSerializer.Deserialize<Decision>(WireText, Wire.Options)!;

        Assert.Equal(DecisionOutcome.Reject, parsed.Outcome);
        Assert.Equal(DecisionCause.None, parsed.Cause);
        Assert.Equal("Recipient is not the buyer.", parsed.Reason);
        var item = Assert.Single(parsed.Evidence);
        Assert.Equal(new EvidenceItem("get_invoice", "recipient.tax_id", "69979991000100"), item);
        Assert.Equal(WireText, JsonSerializer.Serialize(parsed, Wire.Options));
    }

    [Fact]
    public void The_decision_enums_list_their_wire_names_in_declaration_order()
    {
        Assert.Equal(OutcomeNames, Wire.EnumNames(typeof(DecisionOutcome)));
        Assert.Equal(CauseNames, Wire.EnumNames(typeof(DecisionCause)));
    }

    // ---------------------------------------------------------------- canonical schema

    [Fact]
    public void Export_is_deterministic()
    {
        Assert.Equal(CanonicalSchema.ExportJson(typeof(Decision)), CanonicalSchema.ExportJson(typeof(Decision)));
    }

    [Fact]
    public void Export_equals_the_committed_decision_schema_file()
    {
        var expected = new UTF8Encoding(false).GetBytes(CanonicalSchema.ExportJson(typeof(Decision)));
        var committed = File.ReadAllBytes(CommittedPath);

        Assert.True(
            expected.AsSpan().SequenceEqual(committed),
            "schema/decision.schema.json is stale. Run: dotnet run --project dotnet/tools/SchemaExport");
    }

    [Fact]
    public void Root_declares_draft_2020_12_titles_itself_Decision_and_lists_the_required_members_in_order()
    {
        var root = CanonicalSchema.Export(typeof(Decision));

        Assert.Equal(CanonicalSchema.Draft, root["$schema"]!.GetValue<string>());
        Assert.Equal("$schema", root.First().Key);
        Assert.Equal("Decision", root["title"]!.GetValue<string>());
        Assert.False(root["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["evidence", "reason", "outcome", "cause"],
            root["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToList());
        Assert.Equal(
            ["evidence", "reason", "outcome", "cause"],
            root["properties"]!.AsObject().Select(pair => pair.Key).ToList());
    }

    [Fact]
    public void Defs_hold_exactly_EvidenceItem_with_three_required_string_members()
    {
        var root = CanonicalSchema.Export(typeof(Decision));

        var definitions = root["$defs"]!.AsObject();
        Assert.Equal(["EvidenceItem"], definitions.Select(pair => pair.Key).ToList());
        var item = definitions["EvidenceItem"]!;
        Assert.Equal(
            ["source", "ref", "value"],
            item["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToList());
        Assert.False(item["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("#/$defs/EvidenceItem", root["properties"]!["evidence"]!["items"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Outcome_and_cause_are_typed_string_enums_with_exactly_the_wire_names()
    {
        var properties = CanonicalSchema.Export(typeof(Decision))["properties"]!;

        AssertTypedEnum(properties["outcome"]!, OutcomeNames);
        AssertTypedEnum(properties["cause"]!, CauseNames);
    }

    [Fact]
    public void No_property_node_is_the_bare_literal_true()
    {
        AssertNoLiteralTruePropertyNode(CanonicalSchema.Export(typeof(Decision)), "$");
    }

    [Fact]
    public void Every_property_has_a_description_except_enum_and_ref_nodes()
    {
        var root = CanonicalSchema.Export(typeof(Decision));

        foreach (var owner in new[] { root, root["$defs"]!["EvidenceItem"]! })
        {
            foreach (var (name, property) in owner["properties"]!.AsObject())
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(property!["description"]?.GetValue<string>()),
                    $"{name} has no description");
            }
        }
    }

    [Fact]
    public void The_decision_is_not_reachable_from_the_invoice_schema_and_the_invoice_export_is_unchanged()
    {
        Assert.DoesNotContain("Decision", CanonicalSchema.ExportJson(), StringComparison.Ordinal);
        Assert.Equal(CanonicalSchema.ExportJson(typeof(Invoice)), CanonicalSchema.ExportJson());
    }

    // ---------------------------------------------------------------- strict parse

    [Theory]
    [InlineData("{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\",\"extra\":1}")]
    [InlineData("{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"Approve\",\"cause\":\"none\"}")]
    [InlineData("{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":0}")]
    [InlineData("{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"maybe\"}")]
    [InlineData("{\"evidence\":[],\"reason\":\"r\",\"outcome\":\"approve\"}")]
    [InlineData("{\"evidence\":[],\"reason\":null,\"outcome\":\"approve\",\"cause\":\"none\"}")]
    [InlineData("{\"evidence\":null,\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\"}")]
    [InlineData("{\"evidence\":[{\"source\":\"a\",\"ref\":\"b\"}],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\"}")]
    [InlineData("{\"evidence\":[{\"source\":\"a\",\"ref\":\"b\",\"value\":\"c\",\"extra\":1}],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\"}")]
    public void A_malformed_decision_is_rejected_by_the_strict_parser(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Decision>(json, Wire.Options));
    }

    // ---------------------------------------------------------------- null boundary

    [Fact]
    public void A_clean_decision_has_no_null_violations()
    {
        Assert.Empty(Sample().NullViolations());
    }

    [Fact]
    public void A_null_evidence_element_deserializes_but_is_named_by_path()
    {
        var json = "{\"evidence\":[null],\"reason\":\"r\",\"outcome\":\"approve\",\"cause\":\"none\"}";

        var parsed = JsonSerializer.Deserialize<Decision>(json, Wire.Options)!;

        Assert.Equal(["evidence[0]"], parsed.NullViolations());
    }

    [Fact]
    public void Null_members_are_named_by_path_in_schema_order()
    {
        var withNullReason = Sample() with { Reason = null! };
        var withNullList = Sample() with { Evidence = null! };
        var withNullCitation = Sample() with { Evidence = [new EvidenceItem("get_invoice", null!, "v"), null!] };

        Assert.Equal(["reason"], withNullReason.NullViolations());
        Assert.Equal(["evidence"], withNullList.NullViolations());
        Assert.Equal(["evidence[0].ref", "evidence[1]"], withNullCitation.NullViolations());
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertTypedEnum(JsonNode node, string[] names)
    {
        Assert.Equal("string", node["type"]!.GetValue<string>());
        Assert.Equal(names, node["enum"]!.AsArray().Select(name => name!.GetValue<string>()).ToArray());
    }

    private static void AssertNoLiteralTruePropertyNode(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties)
                {
                    foreach (var (name, property) in properties)
                    {
                        Assert.False(
                            property is JsonValue value && value.TryGetValue(out bool flag) && flag,
                            $"{path}.{name} is the literal true");
                    }
                }

                foreach (var (name, child) in obj)
                {
                    AssertNoLiteralTruePropertyNode(child, $"{path}.{name}");
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    AssertNoLiteralTruePropertyNode(child, path);
                }

                break;
        }
    }
}
