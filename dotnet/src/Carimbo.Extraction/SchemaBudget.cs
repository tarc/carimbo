using System.Text.Json.Nodes;

namespace Carimbo.Extraction;

/// <summary>Thrown when a schema exceeds the structured-output budget.</summary>
public sealed class SchemaBudgetExceededException(string message) : Exception(message);

/// <summary>
/// Counts a schema against the structured-output budget: at most 24 optional properties and 16
/// union-typed properties. Optional means a property not listed in its object's <c>required</c>.
/// Union means a property schema that uses <c>anyOf</c> or a <c>type</c> array. Each <c>$ref</c> is
/// expanded per use, so a definition referenced twice counts twice (conservative).
/// </summary>
public static class SchemaBudget
{
    public const int MaxOptional = 24;

    public const int MaxUnion = 16;

    private const string DefinitionPrefix = "#/$defs/";

    public sealed record Counts(int Optional, int Union);

    public static Counts Count(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var optional = 0;
        var union = 0;
        Walk(schema, schema["$defs"] as JsonObject, [], ref optional, ref union);
        return new Counts(optional, union);
    }

    public static void EnsureWithin(JsonObject schema)
    {
        var counts = Count(schema);
        if (counts.Optional > MaxOptional || counts.Union > MaxUnion)
        {
            throw new SchemaBudgetExceededException(
                $"Schema exceeds the structured-output budget: {counts.Optional} optional properties (limit {MaxOptional}) "
                + $"and {counts.Union} union properties (limit {MaxUnion}).");
        }
    }

    private static void Walk(
        JsonObject schema,
        JsonObject? definitions,
        List<string> activeReferences,
        ref int optional,
        ref int union)
    {
        schema = Resolve(schema, definitions, activeReferences, out var reference);
        try
        {
            if (schema["properties"] is JsonObject properties)
            {
                var required = RequiredNames(schema);
                foreach (var (name, property) in properties)
                {
                    if (!required.Contains(name))
                    {
                        optional++;
                    }

                    if (property is JsonObject propertySchema)
                    {
                        var resolved = Resolve(propertySchema, definitions, activeReferences, out var propertyReference);
                        try
                        {
                            if (IsUnion(resolved))
                            {
                                union++;
                            }
                        }
                        finally
                        {
                            Release(activeReferences, propertyReference);
                        }

                        Walk(propertySchema, definitions, activeReferences, ref optional, ref union);
                    }
                }
            }

            if (schema["items"] is JsonObject items)
            {
                Walk(items, definitions, activeReferences, ref optional, ref union);
            }

            foreach (var keyword in new[] { "anyOf", "oneOf", "allOf", "prefixItems" })
            {
                if (schema[keyword] is JsonArray members)
                {
                    foreach (var member in members)
                    {
                        if (member is JsonObject memberSchema)
                        {
                            Walk(memberSchema, definitions, activeReferences, ref optional, ref union);
                        }
                    }
                }
            }
        }
        finally
        {
            Release(activeReferences, reference);
        }
    }

    private static HashSet<string> RequiredNames(JsonObject schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema["required"] is JsonArray required)
        {
            foreach (var entry in required)
            {
                if (entry is JsonValue value && value.TryGetValue(out string? name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static bool IsUnion(JsonObject schema) => schema.ContainsKey("anyOf") || schema["type"] is JsonArray;

    /// <summary>Follows a <c>$ref</c> to its definition. Throws on a cycle or an unresolvable reference.</summary>
    private static JsonObject Resolve(
        JsonObject schema,
        JsonObject? definitions,
        List<string> activeReferences,
        out string? reference)
    {
        reference = null;
        if (schema["$ref"] is not JsonValue refValue || !refValue.TryGetValue(out string? target))
        {
            return schema;
        }

        if (!target.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
        {
            throw new SchemaProjectionException($"Unsupported $ref '{target}': only '{DefinitionPrefix}' references can be counted.");
        }

        var name = target[DefinitionPrefix.Length..];
        if (activeReferences.Contains(name))
        {
            throw new SchemaProjectionException($"Recursive definition detected at '{name}': recursion is not supported.");
        }

        if (definitions?[name] is not JsonObject definition)
        {
            throw new SchemaProjectionException($"$ref '{target}' does not resolve to an entry of $defs.");
        }

        activeReferences.Add(name);
        reference = name;
        return definition;
    }

    private static void Release(List<string> activeReferences, string? reference)
    {
        if (reference is not null)
        {
            activeReferences.RemoveAt(activeReferences.Count - 1);
        }
    }
}
