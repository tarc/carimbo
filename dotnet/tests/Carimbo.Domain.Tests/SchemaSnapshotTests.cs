using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Carimbo.Domain.Tests;

public class SchemaSnapshotTests
{
    private static string CommittedSchemaPath =>
        Path.Combine(RepoRoot.Find(), "schema", "invoice.schema.json");

    [Fact]
    public void Export_is_deterministic()
    {
        Assert.Equal(CanonicalSchema.ExportJson(), CanonicalSchema.ExportJson());
    }

    [Fact]
    public void Export_ends_with_one_lf_and_has_no_carriage_returns_or_bom()
    {
        var text = CanonicalSchema.ExportJson();

        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.NotEqual('﻿', text[0]);
    }

    [Fact]
    public void Export_equals_the_committed_schema_file()
    {
        var expected = new UTF8Encoding(false).GetBytes(CanonicalSchema.ExportJson());
        var committed = File.ReadAllBytes(CommittedSchemaPath);

        Assert.True(
            expected.AsSpan().SequenceEqual(committed),
            "schema/invoice.schema.json is stale. Run: dotnet run --project dotnet/tools/SchemaExport");
    }

    [Fact]
    public void Root_declares_draft_2020_12_and_titles_itself_Invoice()
    {
        var root = CanonicalSchema.Export();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root["$schema"]!.GetValue<string>());
        Assert.Equal(CanonicalSchema.Draft, root["$schema"]!.GetValue<string>());
        Assert.Equal("Invoice", root["title"]!.GetValue<string>());
        Assert.Equal("$schema", root.First().Key);
    }

    [Fact]
    public void Party_is_hoisted_into_defs_and_referenced_by_issuer_and_recipient()
    {
        var root = CanonicalSchema.Export();
        var properties = root["properties"]!.AsObject();

        Assert.NotNull(root["$defs"]!["Party"]);
        Assert.Equal("#/$defs/Party", properties["issuer"]!["$ref"]!.GetValue<string>());
        Assert.Equal("#/$defs/Party", properties["recipient"]!["$ref"]!.GetValue<string>());
        Assert.Null(properties["issuer"]!["properties"]);
    }

    [Fact]
    public void Total_amount_is_a_pattern_constrained_string_without_a_title()
    {
        var root = CanonicalSchema.Export();
        var expected = new JsonObject
        {
            ["type"] = "string",
            ["pattern"] = Patterns.Money,
        };

        Assert.True(JsonNode.DeepEquals(expected, root["properties"]!["total_amount"]));
    }

    [Fact]
    public void Access_key_and_cnpj_are_constrained_by_pattern()
    {
        var root = CanonicalSchema.Export();

        Assert.Equal(Patterns.AccessKey, root["properties"]!["access_key"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.Cnpj, root["$defs"]!["Party"]!["properties"]!["cnpj"]!["pattern"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(root["properties"]!["access_key"]!["description"]?.GetValue<string>()));
    }

    [Fact]
    public void Issue_date_has_the_date_format()
    {
        var root = CanonicalSchema.Export();

        Assert.Equal("date", root["properties"]!["issue_date"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public void Every_object_node_is_closed_with_additional_properties_false()
    {
        var root = CanonicalSchema.Export();
        var objects = new List<JsonObject>();
        CollectObjectSchemas(root, objects);

        Assert.True(objects.Count >= 2, "expected the root and Party object schemas");
        foreach (var schema in objects)
        {
            var closed = schema["additionalProperties"];
            Assert.True(closed is JsonValue value && value.TryGetValue(out bool flag) && !flag, schema.ToJsonString());
        }
    }

    [Fact]
    public void No_property_schema_is_the_unconstrained_literal_true()
    {
        var root = CanonicalSchema.Export();

        AssertNoTrueProperties(root);
        foreach (var (_, definition) in root["$defs"]!.AsObject())
        {
            AssertNoTrueProperties(definition!);
        }
    }

    [Fact]
    public void Decision_is_not_reachable_from_the_invoice_schema()
    {
        Assert.DoesNotContain("Decision", CanonicalSchema.ExportJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Defs_are_sorted_ordinally()
    {
        var keys = CanonicalSchema.Export()["$defs"]!.AsObject().Select(pair => pair.Key).ToList();

        Assert.Equal(keys.OrderBy(key => key, StringComparer.Ordinal), keys);
    }

    private static void CollectObjectSchemas(JsonNode? node, List<JsonObject> found)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["type"] is JsonValue type && type.TryGetValue(out string? name) && name == "object")
                {
                    found.Add(obj);
                }

                foreach (var (_, child) in obj)
                {
                    CollectObjectSchemas(child, found);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    CollectObjectSchemas(child, found);
                }

                break;
        }
    }

    private static void AssertNoTrueProperties(JsonNode schema)
    {
        foreach (var (name, property) in schema["properties"]!.AsObject())
        {
            var isTrueLiteral = property is JsonValue value && value.TryGetValue(out bool flag) && flag;
            Assert.False(isTrueLiteral, $"property '{name}' exported as the literal true");
        }
    }
}
