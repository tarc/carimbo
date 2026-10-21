using System.Text;
using System.Text.Json.Nodes;
using Carimbo.Domain;
using Xunit;

namespace Carimbo.Extraction.Tests;

/// <summary>Pins the model-facing Decision schema: its committed file, its budget and its typed enums (AGT-02).</summary>
public class DecisionModelSchemaTests
{
    private static readonly string[] OutcomeNames = ["approve", "reject", "escalate"];

    private static readonly string[] CauseNames =
        ["none", "extraction_not_success", "policy_warning", "over_limit", "tool_failed", "step_budget_exhausted"];

    private static string CommittedPath => Path.Combine(FindRepoRoot(), "schema", "decision.model.schema.json");

    private static JsonObject Model() => (JsonObject)JsonNode.Parse(ModelFacingSchema.Json(typeof(Decision)))!;

    [Fact]
    public void Json_is_deterministic()
    {
        Assert.Equal(ModelFacingSchema.Json(typeof(Decision)), ModelFacingSchema.Json(typeof(Decision)));
    }

    [Fact]
    public void Json_equals_the_committed_model_facing_schema_file()
    {
        var expected = new UTF8Encoding(false).GetBytes(ModelFacingSchema.Json(typeof(Decision)));
        var committed = File.ReadAllBytes(CommittedPath);

        Assert.True(
            expected.AsSpan().SequenceEqual(committed),
            "schema/decision.model.schema.json is stale. Run: dotnet run --project dotnet/tools/SchemaExport");
    }

    [Fact]
    public void The_projected_schema_is_inside_the_structured_output_budget_with_nothing_optional_or_union()
    {
        var counts = SchemaBudget.Count(Model());

        Assert.Equal(0, counts.Optional);
        Assert.Equal(0, counts.Union);
    }

    [Fact]
    public void The_projection_only_drops_the_dialect_marker_from_the_canonical_schema()
    {
        var canonical = CanonicalSchema.Export(typeof(Decision));
        var model = Model();

        Assert.Null(model["$schema"]);
        canonical.Remove("$schema");
        Assert.True(JsonNode.DeepEquals(canonical, model), "the model-facing schema drifted from the canonical one");
    }

    [Fact]
    public void Root_requires_evidence_reason_outcome_and_cause_in_that_order_with_no_extra_members()
    {
        var model = Model();

        Assert.Equal("Decision", model["title"]!.GetValue<string>());
        Assert.False(model["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["evidence", "reason", "outcome", "cause"],
            model["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToList());
    }

    [Fact]
    public void Outcome_and_cause_are_typed_string_enums_with_exactly_the_wire_names()
    {
        var properties = Model()["properties"]!;

        AssertTypedEnum(properties["outcome"]!, OutcomeNames);
        AssertTypedEnum(properties["cause"]!, CauseNames);
    }

    [Fact]
    public void No_property_node_is_the_bare_literal_true()
    {
        AssertNoLiteralTruePropertyNode(Model(), "$");
    }

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
}
