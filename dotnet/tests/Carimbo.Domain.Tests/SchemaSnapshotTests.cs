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
    public void Defs_hold_exactly_the_five_record_types_and_are_referenced_by_the_invoice()
    {
        var root = CanonicalSchema.Export();
        var properties = root["properties"]!.AsObject();

        Assert.Equal(
            ["Installment", "LineItem", "Party", "Recipient", "Totals"],
            root["$defs"]!.AsObject().Select(pair => pair.Key).ToList());
        Assert.Equal("#/$defs/Party", properties["issuer"]!["$ref"]!.GetValue<string>());
        Assert.Equal("#/$defs/Recipient", properties["recipient"]!["$ref"]!.GetValue<string>());
        Assert.Equal("#/$defs/Totals", properties["totals"]!["$ref"]!.GetValue<string>());
        Assert.Equal("array", properties["items"]!["type"]!.GetValue<string>());
        Assert.Equal("#/$defs/LineItem", properties["items"]!["items"]!["$ref"]!.GetValue<string>());
        Assert.Equal("array", properties["installments"]!["type"]!.GetValue<string>());
        Assert.Equal("#/$defs/Installment", properties["installments"]!["items"]!["$ref"]!.GetValue<string>());
        Assert.Null(properties["issuer"]!["properties"]);
        Assert.Null(properties["total_amount"]);
    }

    [Fact]
    public void Invoice_properties_follow_the_v2_target_order()
    {
        var root = CanonicalSchema.Export();

        Assert.Equal(
            ["access_key", "number", "series", "issue_date", "operation_nature", "issuer", "recipient", "items", "totals", "installments"],
            root["properties"]!.AsObject().Select(pair => pair.Key).ToList());
        Assert.Equal(
            ["cnpj", "name", "ie", "uf"],
            root["$defs"]!["Party"]!["properties"]!.AsObject().Select(pair => pair.Key).ToList());
        Assert.Equal(
            ["tax_id", "tax_id_kind", "name", "ie", "uf"],
            root["$defs"]!["Recipient"]!["properties"]!.AsObject().Select(pair => pair.Key).ToList());
        Assert.Equal(
            ["code", "description", "ncm", "cst_csosn", "cfop", "unit", "quantity", "unit_price", "total", "icms_base", "icms_rate", "icms_amount", "ipi_rate", "ipi_amount"],
            root["$defs"]!["LineItem"]!["properties"]!.AsObject().Select(pair => pair.Key).ToList());
        Assert.Equal(
            ["icms_base", "icms_amount", "icms_st_base", "icms_st_amount", "products_total", "freight", "insurance", "discount", "other_expenses", "ipi_amount", "invoice_total"],
            root["$defs"]!["Totals"]!["properties"]!.AsObject().Select(pair => pair.Key).ToList());
        Assert.Equal(
            ["number", "due_date", "amount"],
            root["$defs"]!["Installment"]!["properties"]!.AsObject().Select(pair => pair.Key).ToList());
    }

    [Fact]
    public void Invoice_total_is_a_pattern_constrained_string_without_a_title()
    {
        var node = CanonicalSchema.Export()["$defs"]!["Totals"]!["properties"]!["invoice_total"]!;

        Assert.Equal("string", node["type"]!.GetValue<string>());
        Assert.Equal(Patterns.Money, node["pattern"]!.GetValue<string>());
        Assert.Null(node["title"]);
    }

    [Fact]
    public void Decimal4_and_Rate_are_untitled_pattern_strings()
    {
        var item = CanonicalSchema.Export()["$defs"]!["LineItem"]!["properties"]!;

        foreach (var (name, pattern) in new[]
        {
            ("quantity", Patterns.Decimal4),
            ("unit_price", Patterns.Decimal4),
            ("icms_rate", Patterns.Rate),
            ("ipi_rate", Patterns.Rate),
            ("total", Patterns.Money),
        })
        {
            Assert.Equal("string", item[name]!["type"]!.GetValue<string>());
            Assert.Equal(pattern, item[name]!["pattern"]!.GetValue<string>());
            Assert.Null(item[name]!["title"]);
        }
    }

    [Fact]
    public void Tax_id_kind_is_a_typed_string_enum()
    {
        var node = CanonicalSchema.Export()["$defs"]!["Recipient"]!["properties"]!["tax_id_kind"]!;
        var expected = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("cnpj", "cpf"),
        };

        Assert.True(JsonNode.DeepEquals(expected, node), node.ToJsonString());
        Assert.Equal("type", node.AsObject().First().Key);
    }

    [Fact]
    public void Every_enum_node_declares_a_type()
    {
        var root = CanonicalSchema.Export();

        AssertEveryEnumIsTyped(root);
    }

    [Fact]
    public void Identifier_state_and_item_code_members_are_constrained_by_pattern()
    {
        var root = CanonicalSchema.Export();
        var defs = root["$defs"]!;

        Assert.Equal(Patterns.AccessKey, root["properties"]!["access_key"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.Cnpj, defs["Party"]!["properties"]!["cnpj"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.Uf, defs["Party"]!["properties"]!["uf"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.TaxId, defs["Recipient"]!["properties"]!["tax_id"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.Uf, defs["Recipient"]!["properties"]!["uf"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.Ncm, defs["LineItem"]!["properties"]!["ncm"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.CstCsosn, defs["LineItem"]!["properties"]!["cst_csosn"]!["pattern"]!.GetValue<string>());
        Assert.Equal(Patterns.Cfop, defs["LineItem"]!["properties"]!["cfop"]!["pattern"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(root["properties"]!["access_key"]!["description"]?.GetValue<string>()));
    }

    [Fact]
    public void Every_property_has_a_description_and_no_digit_shorthand_class_appears()
    {
        var root = CanonicalSchema.Export();

        AssertEveryPropertyDescribed(root, "$");
        foreach (var (name, definition) in root["$defs"]!.AsObject())
        {
            AssertEveryPropertyDescribed(definition!, name);
        }

        Assert.DoesNotContain("\\d", CanonicalSchema.ExportJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_property_is_required_and_only_the_two_ie_members_are_unions()
    {
        var root = CanonicalSchema.Export();
        var unions = new List<string>();

        foreach (var (name, schema) in Objects(root))
        {
            var properties = schema["properties"]!.AsObject().Select(pair => pair.Key).ToList();
            var required = schema["required"]!.AsArray().Select(entry => entry!.GetValue<string>()).ToList();
            Assert.Equal(properties.Order(StringComparer.Ordinal), required.Order(StringComparer.Ordinal));

            foreach (var (property, propertySchema) in schema["properties"]!.AsObject())
            {
                if (propertySchema!["type"] is JsonArray)
                {
                    unions.Add($"{name}.{property}");
                }
            }
        }

        Assert.Equal(["Party.ie", "Recipient.ie"], unions.Order(StringComparer.Ordinal));
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

        Assert.True(objects.Count >= 6, "expected the root and the five definition object schemas");
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

    /// <summary>The root (named after its title) and every definition, in schema order.</summary>
    private static IEnumerable<(string Name, JsonObject Schema)> Objects(JsonObject root)
    {
        yield return (root["title"]!.GetValue<string>(), root);
        foreach (var (name, definition) in root["$defs"]!.AsObject())
        {
            yield return (name, definition!.AsObject());
        }
    }

    private static void AssertEveryPropertyDescribed(JsonNode schema, string owner)
    {
        foreach (var (name, property) in schema["properties"]!.AsObject())
        {
            // Object-typed properties are $ref nodes (their description lives on the definition) and the
            // enum node is deliberately exactly {"type","enum"}.
            if (property!["$ref"] is not null || property["enum"] is not null)
            {
                continue;
            }

            Assert.False(
                string.IsNullOrWhiteSpace(property["description"]?.GetValue<string>()),
                $"{owner}.{name} has no description");
        }
    }

    private static void AssertEveryEnumIsTyped(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.ContainsKey("enum"))
                {
                    Assert.Equal("string", obj["type"]?.GetValue<string>());
                }

                foreach (var (_, child) in obj)
                {
                    AssertEveryEnumIsTyped(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    AssertEveryEnumIsTyped(child);
                }

                break;
        }
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
