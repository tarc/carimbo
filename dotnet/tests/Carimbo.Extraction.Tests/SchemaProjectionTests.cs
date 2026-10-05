using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Carimbo.Domain;
using Xunit;

namespace Carimbo.Extraction.Tests;

public class SchemaProjectionTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ---------------------------------------------------------------- projector rules

    [Theory]
    [InlineData("minimum", "0")]
    [InlineData("maximum", "10")]
    [InlineData("exclusiveMinimum", "0")]
    [InlineData("exclusiveMaximum", "10")]
    [InlineData("multipleOf", "2")]
    [InlineData("minLength", "1")]
    [InlineData("maxLength", "5")]
    [InlineData("maxItems", "3")]
    [InlineData("uniqueItems", "true")]
    [InlineData("minProperties", "1")]
    [InlineData("maxProperties", "4")]
    public void Unsupported_keyword_is_removed_wherever_it_appears(string keyword, string value)
    {
        var canonical = Parse($$"""
            {
              "type": "object",
              "properties": {
                "top": { "type": "string", "{{keyword}}": {{value}} },
                "list": { "type": "array", "items": { "type": "string", "{{keyword}}": {{value}} } }
              },
              "required": ["top", "list"],
              "{{keyword}}": {{value}},
              "$defs": {
                "Nested": {
                  "type": "object",
                  "properties": { "inner": { "type": "string", "{{keyword}}": {{value}} } },
                  "required": ["inner"],
                  "{{keyword}}": {{value}}
                }
              }
            }
            """);

        var projected = ModelSchemaProjector.Project(canonical);

        Assert.DoesNotContain($"\"{keyword}\"", projected.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_that_is_named_like_a_stripped_keyword_is_kept()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": { "minimum": { "type": "string" }, "maxLength": { "type": "string" } },
              "required": ["minimum", "maxLength"]
            }
            """);

        var projected = ModelSchemaProjector.Project(canonical);

        Assert.NotNull(projected["properties"]!["minimum"]);
        Assert.NotNull(projected["properties"]!["maxLength"]);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(5, false)]
    public void MinItems_is_kept_only_for_zero_and_one(int minItems, bool kept)
    {
        var canonical = Parse($$"""
            {
              "type": "object",
              "properties": { "list": { "type": "array", "items": { "type": "string" }, "minItems": {{minItems}} } },
              "required": ["list"]
            }
            """);

        var list = ModelSchemaProjector.Project(canonical)["properties"]!["list"]!.AsObject();

        Assert.Equal(kept, list.ContainsKey("minItems"));
        if (kept)
        {
            Assert.Equal(minItems, list["minItems"]!.GetValue<int>());
        }
    }

    [Fact]
    public void OneOf_becomes_anyOf_with_the_same_members()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": {
                "value": { "oneOf": [ { "type": "string" }, { "type": "integer" } ] }
              },
              "required": ["value"]
            }
            """);

        var value = ModelSchemaProjector.Project(canonical)["properties"]!["value"]!.AsObject();

        Assert.False(value.ContainsKey("oneOf"));
        var members = value["anyOf"]!.AsArray();
        Assert.Equal(2, members.Count);
        Assert.Equal("string", members[0]!["type"]!.GetValue<string>());
        Assert.Equal("integer", members[1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Every_object_ends_up_closed_even_when_it_was_open()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": {
                "open": { "type": "object", "properties": { "x": { "type": "string" } }, "required": ["x"], "additionalProperties": true },
                "bare": { "type": "object", "properties": { "y": { "type": "string" } }, "required": ["y"] },
                "ref": { "$ref": "#/$defs/Thing" }
              },
              "required": ["open", "bare", "ref"],
              "additionalProperties": true,
              "$defs": {
                "Thing": { "type": "object", "properties": { "z": { "type": "string" } }, "required": ["z"] }
              }
            }
            """);

        var projected = ModelSchemaProjector.Project(canonical);

        Assert.False(projected["additionalProperties"]!.GetValue<bool>());
        Assert.False(projected["properties"]!["open"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(projected["properties"]!["bare"]!["additionalProperties"]!.GetValue<bool>());
        Assert.False(projected["$defs"]!["Thing"]!["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void Root_schema_keyword_is_removed_and_supported_keywords_are_kept()
    {
        var canonical = Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "title": "Thing",
              "properties": {
                "code": { "type": "string", "pattern": "^[A-Z]+$", "description": "A code" },
                "day": { "type": "string", "format": "date" },
                "kind": { "type": "string", "enum": ["a", "b"] },
                "party": { "$ref": "#/$defs/Party" }
              },
              "required": ["code", "day", "kind", "party"],
              "$defs": {
                "Party": { "type": "object", "title": "Party", "properties": { "name": { "type": "string" } }, "required": ["name"] }
              }
            }
            """);

        var projected = ModelSchemaProjector.Project(canonical);

        Assert.False(projected.ContainsKey("$schema"));
        Assert.Equal("Thing", projected["title"]!.GetValue<string>());
        Assert.Equal("^[A-Z]+$", projected["properties"]!["code"]!["pattern"]!.GetValue<string>());
        Assert.Equal("A code", projected["properties"]!["code"]!["description"]!.GetValue<string>());
        Assert.Equal("date", projected["properties"]!["day"]!["format"]!.GetValue<string>());
        Assert.Equal(2, projected["properties"]!["kind"]!["enum"]!.AsArray().Count);
        Assert.Equal("#/$defs/Party", projected["properties"]!["party"]!["$ref"]!.GetValue<string>());
        Assert.Equal(4, projected["required"]!.AsArray().Count);
        Assert.Equal("Party", projected["$defs"]!["Party"]!["title"]!.GetValue<string>());
    }

    [Fact]
    public void Key_order_is_preserved()
    {
        var canonical = Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": { "b": { "type": "string" }, "a": { "type": "string" } },
              "required": ["b", "a"],
              "additionalProperties": false,
              "title": "T"
            }
            """);

        var projected = ModelSchemaProjector.Project(canonical);

        Assert.Equal(["type", "properties", "required", "additionalProperties", "title"], projected.Select(pair => pair.Key));
        Assert.Equal(["b", "a"], projected["properties"]!.AsObject().Select(pair => pair.Key));
    }

    [Fact]
    public void Project_does_not_mutate_its_input()
    {
        var canonical = Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "n": { "type": "integer", "minimum": 0, "maximum": 9 },
                "v": { "oneOf": [ { "type": "string", "maxLength": 3 }, { "type": "integer" } ] }
              },
              "required": ["n", "v"],
              "additionalProperties": true
            }
            """);
        var before = canonical.ToJsonString();

        _ = ModelSchemaProjector.Project(canonical);

        Assert.Equal(before, canonical.ToJsonString());
    }

    [Fact]
    public void A_ref_outside_the_local_defs_is_rejected()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": { "x": { "$ref": "https://example.com/other.json" } },
              "required": ["x"]
            }
            """);

        Assert.Throws<SchemaProjectionException>(() => ModelSchemaProjector.Project(canonical));
    }

    [Fact]
    public void A_ref_to_a_definition_that_does_not_exist_is_rejected()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": { "x": { "$ref": "#/$defs/Missing" } },
              "required": ["x"],
              "$defs": {}
            }
            """);

        Assert.Throws<SchemaProjectionException>(() => ModelSchemaProjector.Project(canonical));
    }

    [Fact]
    public void A_recursive_definition_cycle_is_rejected()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": { "node": { "$ref": "#/$defs/Node" } },
              "required": ["node"],
              "$defs": {
                "Node": {
                  "type": "object",
                  "properties": { "child": { "$ref": "#/$defs/Node" } },
                  "required": ["child"]
                }
              }
            }
            """);

        Assert.Throws<SchemaProjectionException>(() => ModelSchemaProjector.Project(canonical));
    }

    [Fact]
    public void A_mutual_definition_cycle_is_rejected()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": { "a": { "$ref": "#/$defs/A" } },
              "required": ["a"],
              "$defs": {
                "A": { "type": "object", "properties": { "b": { "$ref": "#/$defs/B" } }, "required": ["b"] },
                "B": { "type": "object", "properties": { "a": { "$ref": "#/$defs/A" } }, "required": ["a"] }
              }
            }
            """);

        Assert.Throws<SchemaProjectionException>(() => ModelSchemaProjector.Project(canonical));
    }

    [Fact]
    public void AllOf_that_contains_a_ref_is_rejected()
    {
        var canonical = Parse("""
            {
              "type": "object",
              "properties": { "x": { "allOf": [ { "$ref": "#/$defs/Thing" } ] } },
              "required": ["x"],
              "$defs": { "Thing": { "type": "object", "properties": { "z": { "type": "string" } }, "required": ["z"] } }
            }
            """);

        Assert.Throws<SchemaProjectionException>(() => ModelSchemaProjector.Project(canonical));
    }

    [Fact]
    public void The_canonical_invoice_schema_projects_to_itself_minus_the_dialect_keyword()
    {
        var canonical = CanonicalSchema.Export();

        var projected = ModelSchemaProjector.Project(canonical);

        var expected = (JsonObject)canonical.DeepClone();
        expected.Remove("$schema");
        Assert.True(JsonNode.DeepEquals(expected, projected), projected.ToJsonString());
    }

    // ---------------------------------------------------------------- budget counter

    private static JsonObject ObjectWithProperties(int count, string propertySchema, bool required)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            properties[$"p{i}"] = JsonNode.Parse(propertySchema);
            if (required)
            {
                requiredNames.Add($"p{i}");
            }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = requiredNames,
            ["additionalProperties"] = false,
        };
    }

    [Fact]
    public void Exactly_24_optional_properties_are_within_budget()
    {
        var schema = ObjectWithProperties(24, """{ "type": "string" }""", required: false);

        Assert.Equal(new SchemaBudget.Counts(24, 0), SchemaBudget.Count(schema));
        SchemaBudget.EnsureWithin(schema);
    }

    [Fact]
    public void Twenty_five_optional_properties_exceed_the_budget()
    {
        var schema = ObjectWithProperties(25, """{ "type": "string" }""", required: false);

        Assert.Equal(25, SchemaBudget.Count(schema).Optional);
        var exception = Assert.Throws<SchemaBudgetExceededException>(() => SchemaBudget.EnsureWithin(schema));
        Assert.Contains("25", exception.Message, StringComparison.Ordinal);
        Assert.Contains("24", exception.Message, StringComparison.Ordinal);
        Assert.Contains("16", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exactly_16_union_properties_are_within_budget()
    {
        var schema = ObjectWithProperties(16, """{ "type": ["string", "null"] }""", required: true);

        Assert.Equal(new SchemaBudget.Counts(0, 16), SchemaBudget.Count(schema));
        SchemaBudget.EnsureWithin(schema);
    }

    [Fact]
    public void Seventeen_union_properties_exceed_the_budget()
    {
        var schema = ObjectWithProperties(17, """{ "type": ["string", "null"] }""", required: true);

        Assert.Equal(17, SchemaBudget.Count(schema).Union);
        var exception = Assert.Throws<SchemaBudgetExceededException>(() => SchemaBudget.EnsureWithin(schema));
        Assert.Contains("17", exception.Message, StringComparison.Ordinal);
        Assert.Contains("16", exception.Message, StringComparison.Ordinal);
        Assert.Contains("24", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_typed_as_a_type_array_counts_as_a_union()
    {
        var schema = ObjectWithProperties(1, """{ "type": ["string", "null"] }""", required: true);

        Assert.Equal(1, SchemaBudget.Count(schema).Union);
    }

    [Fact]
    public void A_property_using_anyOf_counts_as_a_union()
    {
        var schema = ObjectWithProperties(
            1,
            """{ "anyOf": [ { "type": "string" }, { "type": "integer" } ] }""",
            required: true);

        Assert.Equal(new SchemaBudget.Counts(0, 1), SchemaBudget.Count(schema));
    }

    [Fact]
    public void A_property_that_is_both_optional_and_a_union_counts_in_both()
    {
        var schema = ObjectWithProperties(1, """{ "type": ["string", "null"] }""", required: false);

        Assert.Equal(new SchemaBudget.Counts(1, 1), SchemaBudget.Count(schema));
    }

    [Fact]
    public void A_definition_with_one_optional_property_referenced_twice_counts_two()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "first": { "$ref": "#/$defs/Party" },
                "second": { "$ref": "#/$defs/Party" }
              },
              "required": ["first", "second"],
              "additionalProperties": false,
              "$defs": {
                "Party": {
                  "type": "object",
                  "properties": { "name": { "type": "string" } },
                  "required": [],
                  "additionalProperties": false
                }
              }
            }
            """);

        Assert.Equal(new SchemaBudget.Counts(2, 0), SchemaBudget.Count(schema));
    }

    [Fact]
    public void Properties_inside_array_items_and_anyOf_members_are_counted()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "lines": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": { "note": { "type": "string" } },
                    "required": [],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["lines"],
              "additionalProperties": false
            }
            """);

        Assert.Equal(1, SchemaBudget.Count(schema).Optional);
    }

    [Fact]
    public void The_budget_limits_are_24_and_16()
    {
        Assert.Equal(24, SchemaBudget.MaxOptional);
        Assert.Equal(16, SchemaBudget.MaxUnion);
    }

    [Fact]
    public void The_projected_phase_1_invoice_counts_zero_optional_and_zero_union_properties()
    {
        var projected = ModelSchemaProjector.Project(CanonicalSchema.Export());

        Assert.Equal(new SchemaBudget.Counts(0, 0), SchemaBudget.Count(projected));
        SchemaBudget.EnsureWithin(projected);
    }

    // ---------------------------------------------------------------- committed model-facing schema

    private const string StaleHint = "Run: dotnet run --project dotnet/tools/SchemaExport";

    private static string CommittedModelSchemaPath =>
        Path.Combine(FindRepoRoot(), "schema", "invoice.model.schema.json");

    private static string FindRepoRoot()
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

    private static byte[] ReadCommittedModelSchema()
    {
        Assert.True(File.Exists(CommittedModelSchemaPath), "schema/invoice.model.schema.json is missing. " + StaleHint);
        return File.ReadAllBytes(CommittedModelSchemaPath);
    }

    [Fact]
    public void The_contract_sends_exactly_the_committed_model_schema_bytes()
    {
        var committed = ReadCommittedModelSchema();
        var sent = new UTF8Encoding(false).GetBytes(ExtractionContract.Default.OutputSchemaJson);

        Assert.True(
            sent.AsSpan().SequenceEqual(committed),
            "schema/invoice.model.schema.json is stale: it differs from the schema the extractor sends. " + StaleHint);
    }

    [Fact]
    public void The_contract_schema_hash_is_the_lowercase_sha256_of_the_committed_file()
    {
        var committed = ReadCommittedModelSchema();

        var expected = Convert.ToHexStringLower(SHA256.HashData(committed));

        Assert.Equal(expected, ExtractionContract.Default.OutputSchemaSha256);
    }

    [Fact]
    public void The_committed_model_schema_has_no_dialect_keyword_and_every_object_is_closed()
    {
        var committed = ReadCommittedModelSchema();
        var schema = (JsonObject)JsonNode.Parse(committed)!;

        Assert.DoesNotContain("\"$schema\"", Encoding.UTF8.GetString(committed), StringComparison.Ordinal);
        Assert.False(schema.ContainsKey("$schema"));
        var objects = new List<JsonObject>();
        CollectObjectSchemas(schema, objects);
        Assert.True(objects.Count >= 2, "expected the root and Party object schemas");
        foreach (var objectSchema in objects)
        {
            Assert.True(
                objectSchema["additionalProperties"] is JsonValue value && value.TryGetValue(out bool open) && !open,
                objectSchema.ToJsonString());
        }
    }

    [Fact]
    public void The_committed_model_schema_is_within_the_structured_output_budget()
    {
        var schema = (JsonObject)JsonNode.Parse(ReadCommittedModelSchema())!;

        SchemaBudget.EnsureWithin(schema);
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
}
