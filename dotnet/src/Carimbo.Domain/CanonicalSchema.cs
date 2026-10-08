using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Carimbo.Domain;

/// <summary>
/// Exports the canonical JSON Schema (Draft 2020-12) of <see cref="Invoice"/> from the C# records.
/// Pure and BCL-only. The exporter runs over <see cref="Wire.Options"/>, the same options that parse
/// model output, so the schema describes exactly what the deserializer accepts.
/// </summary>
public static class CanonicalSchema
{
    /// <summary>The JSON Schema dialect of every exported schema.</summary>
    public const string Draft = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>The canonical schema as a fresh node tree, with <c>$schema</c> first and <c>$defs</c> last.</summary>
    public static JsonObject Export()
    {
        var options = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = TransformSchemaNode,
        };
        var exported = (JsonObject)JsonSchemaExporter.GetJsonSchemaAsNode(Wire.Options, typeof(Invoice), options);

        var definitions = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        var rebuilt = Rebuild(exported, isRoot: true, definitions).AsObject();

        // Fresh nodes only: a node that already has a parent cannot be assigned elsewhere.
        var root = new JsonObject { ["$schema"] = Draft };
        foreach (var (name, value) in rebuilt)
        {
            root[name] = value?.DeepClone();
        }

        if (definitions.Count > 0)
        {
            var defs = new JsonObject();
            foreach (var (title, definition) in definitions)
            {
                defs[title] = definition.DeepClone();
            }

            root["$defs"] = defs;
        }

        return root;
    }

    /// <summary>The canonical schema as text, see <see cref="Serialize"/>.</summary>
    public static string ExportJson() => Serialize(Export());

    /// <summary>
    /// Indented JSON with LF line endings, relaxed escaping and one trailing newline. The same
    /// serialization is used for every committed schema file.
    /// </summary>
    public static string Serialize(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var writerOptions = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, writerOptions))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static JsonNode TransformSchemaNode(JsonSchemaExporterContext context, JsonNode schema)
    {
        // Money has a custom converter, which the exporter reports as the literal `true` (any value).
        // This check must come before any "is it an object" guard or the amount silently stays unconstrained.
        // The node carries no title: a title would make code generators emit a wrapper type for it.
        if (context.TypeInfo.Type == typeof(Money))
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = Patterns.Money,
            };
        }

        if (schema is not JsonObject node)
        {
            return schema;
        }

        var attributes = context.PropertyInfo?.AttributeProvider?.GetCustomAttributes(false);
        if (attributes is not null)
        {
            foreach (var attribute in attributes)
            {
                switch (attribute)
                {
                    case System.ComponentModel.DescriptionAttribute description:
                        node["description"] = description.Description;
                        break;
                    case System.ComponentModel.DataAnnotations.RegularExpressionAttribute regex:
                        node["pattern"] = regex.Pattern;
                        break;
                }
            }
        }

        if (node["type"] is JsonValue type && type.TryGetValue(out string? typeName) && typeName == "object")
        {
            node["additionalProperties"] = false;
            node["title"] = context.TypeInfo.Type.Name;
        }

        return node;
    }

    /// <summary>
    /// Rebuilds the tree into new nodes, moving every titled non-root object schema into the
    /// definitions map and leaving a <c>$ref</c> behind.
    /// </summary>
    private static JsonNode Rebuild(JsonNode node, bool isRoot, SortedDictionary<string, JsonNode> definitions)
    {
        switch (node)
        {
            case JsonObject source:
                var copy = new JsonObject();
                foreach (var (name, child) in source)
                {
                    copy[name] = child is null ? null : Rebuild(child, isRoot: false, definitions);
                }

                if (!isRoot
                    && source["title"] is JsonValue title
                    && title.TryGetValue(out string? titleText)
                    && source["type"] is JsonValue type
                    && type.TryGetValue(out string? typeName)
                    && typeName == "object")
                {
                    if (definitions.TryGetValue(titleText, out var existing))
                    {
                        if (!JsonNode.DeepEquals(existing, copy))
                        {
                            throw new InvalidOperationException($"Two different object schemas share the title '{titleText}'.");
                        }
                    }
                    else
                    {
                        definitions[titleText] = copy;
                    }

                    return new JsonObject { ["$ref"] = "#/$defs/" + titleText };
                }

                return copy;

            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(item is null ? null : Rebuild(item, isRoot: false, definitions));
                }

                return items;

            default:
                return node.DeepClone();
        }
    }
}
