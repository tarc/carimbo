using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// docs/DANFE-MAPPING.md must list every leaf of the committed canonical schema and nothing else
/// (DOM-02, D-05), so the documented mapping cannot drift from the extraction target.
/// </summary>
public class MappingDocTests
{
    [Fact]
    public void The_canonical_schema_has_42_leaf_paths()
    {
        Assert.Equal(42, SchemaLeafPaths().Count);
    }

    [Fact]
    public void Every_schema_leaf_is_a_row_of_the_mapping_doc_and_every_row_names_a_schema_leaf()
    {
        var schema = SchemaLeafPaths();
        var doc = DocPaths();

        var missing = schema.Except(doc).Order(StringComparer.Ordinal).ToArray();
        var extra = doc.Except(schema).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            "docs/DANFE-MAPPING.md is out of step with schema/invoice.schema.json.\n" +
            $"  in the schema but not in the doc: [{string.Join(", ", missing)}]\n" +
            $"  in the doc but not in the schema: [{string.Join(", ", extra)}]");
    }

    [Fact]
    public void The_mapping_doc_lists_each_path_once()
    {
        var rows = DocRows();

        Assert.Equal(rows.Count, rows.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Leaf property paths of the schema, following <c>$ref</c> into <c>$defs</c>; array members are written <c>name[].</c>.</summary>
    private static HashSet<string> SchemaLeafPaths()
    {
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(RepoFiles.PathOf("schema", "invoice.schema.json")))!;
        var definitions = (JsonObject)root["$defs"]!;
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        Walk(root, string.Empty);
        return leaves;

        void Walk(JsonObject node, string prefix)
        {
            foreach (var (name, child) in (JsonObject)node["properties"]!)
            {
                Visit((JsonObject)child!, prefix + name);
            }
        }

        void Visit(JsonObject node, string path)
        {
            var resolved = Resolve(node);
            if (resolved["properties"] is not null)
            {
                Walk(resolved, path + ".");
            }
            else if (resolved["type"] is JsonValue type && type.TryGetValue<string>(out var typeName) && typeName == "array" && resolved["items"] is JsonObject items)
            {
                Visit(items, path + "[]");
            }
            else
            {
                leaves.Add(path);
            }
        }

        JsonObject Resolve(JsonObject node)
        {
            if (node["$ref"] is JsonValue reference)
            {
                var text = reference.GetValue<string>();
                const string prefix = "#/$defs/";
                Assert.StartsWith(prefix, text, StringComparison.Ordinal);
                return (JsonObject)definitions[text[prefix.Length..]]!;
            }

            return node;
        }
    }

    private static HashSet<string> DocPaths() => DocRows().ToHashSet(StringComparer.Ordinal);

    /// <summary>The backticked first cell of every table row of the mapping doc.</summary>
    private static List<string> DocRows() =>
        File.ReadAllLines(RepoFiles.PathOf("docs", "DANFE-MAPPING.md"))
            .Select(line => Regex.Match(line, @"^\|\s*`([^`]+)`\s*\|", RegexOptions.CultureInvariant))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToList();
}
