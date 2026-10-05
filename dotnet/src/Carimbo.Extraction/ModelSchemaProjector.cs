using System.Text.Json.Nodes;

namespace Carimbo.Extraction;

/// <summary>Thrown when a canonical schema cannot be expressed to the structured-output API.</summary>
public sealed class SchemaProjectionException(string message) : Exception(message);

/// <summary>
/// Derives the model-facing schema from the canonical one: the exact JSON Schema sent to the
/// structured-output API. Pure. The input is never mutated and key order is preserved.
/// </summary>
/// <remarks>
/// Rules: remove the keywords the API does not support, rewrite <c>oneOf</c> to <c>anyOf</c>,
/// force <c>additionalProperties: false</c> on every object and drop the root <c>$schema</c>.
/// Everything else (pattern, format, description, title, required, enum, $defs, $ref) is kept.
/// Schemas the API cannot take at all (external or non-local $ref, recursion, allOf with $ref)
/// are rejected with <see cref="SchemaProjectionException"/>.
/// </remarks>
public static class ModelSchemaProjector
{
    private const string DefinitionPrefix = "#/$defs/";

    private static readonly HashSet<string> UnsupportedKeywords = new(StringComparer.Ordinal)
    {
        "minimum",
        "maximum",
        "exclusiveMinimum",
        "exclusiveMaximum",
        "multipleOf",
        "minLength",
        "maxLength",
        "maxItems",
        "uniqueItems",
        "minProperties",
        "maxProperties",
    };

    /// <summary>Keywords whose value is a single subschema.</summary>
    private static readonly HashSet<string> SubschemaKeywords = new(StringComparer.Ordinal) { "items", "not" };

    /// <summary>Keywords whose value is an array of subschemas.</summary>
    private static readonly HashSet<string> SubschemaArrayKeywords = new(StringComparer.Ordinal)
    {
        "anyOf",
        "oneOf",
        "allOf",
        "prefixItems",
    };

    /// <summary>Keywords whose value is a map from a name to a subschema.</summary>
    private static readonly HashSet<string> SubschemaMapKeywords = new(StringComparer.Ordinal)
    {
        "properties",
        "$defs",
    };

    public static JsonObject Project(JsonObject canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        var definitions = canonical["$defs"] as JsonObject;
        EnsureNoDefinitionCycle(canonical, definitions);

        var projected = ProjectSchema(canonical, definitions, isRoot: true);
        return projected;
    }

    private static JsonObject ProjectSchema(JsonObject schema, JsonObject? definitions, bool isRoot)
    {
        if (schema["allOf"] is JsonArray allOf
            && allOf.Any(member => member is JsonObject memberSchema && memberSchema.ContainsKey("$ref")))
        {
            throw new SchemaProjectionException("allOf containing a $ref cannot be sent to the structured-output API.");
        }

        if (schema.ContainsKey("oneOf") && schema.ContainsKey("anyOf"))
        {
            throw new SchemaProjectionException("A schema with both oneOf and anyOf cannot be projected unambiguously.");
        }

        var result = new JsonObject();
        foreach (var (keyword, value) in schema)
        {
            if (isRoot && keyword == "$schema")
            {
                continue;
            }

            if (UnsupportedKeywords.Contains(keyword))
            {
                continue;
            }

            if (keyword == "minItems" && !IsZeroOrOne(value))
            {
                continue;
            }

            if (keyword == "$ref")
            {
                result[keyword] = CheckedReference(value, definitions);
                continue;
            }

            var outputKeyword = keyword == "oneOf" ? "anyOf" : keyword;
            result[outputKeyword] = ProjectKeywordValue(keyword, value, definitions);
        }

        if (IsObjectSchema(schema))
        {
            result["additionalProperties"] = false;
        }

        return result;
    }

    private static JsonNode? ProjectKeywordValue(string keyword, JsonNode? value, JsonObject? definitions)
    {
        if (SubschemaKeywords.Contains(keyword) && value is JsonObject subschema)
        {
            return ProjectSchema(subschema, definitions, isRoot: false);
        }

        if (SubschemaArrayKeywords.Contains(keyword) && value is JsonArray members)
        {
            var projectedMembers = new JsonArray();
            foreach (var member in members)
            {
                projectedMembers.Add(member is JsonObject memberSchema
                    ? ProjectSchema(memberSchema, definitions, isRoot: false)
                    : member?.DeepClone());
            }

            return projectedMembers;
        }

        if (SubschemaMapKeywords.Contains(keyword) && value is JsonObject map)
        {
            var projectedMap = new JsonObject();
            foreach (var (name, entry) in map)
            {
                projectedMap[name] = entry is JsonObject entrySchema
                    ? ProjectSchema(entrySchema, definitions, isRoot: false)
                    : entry?.DeepClone();
            }

            return projectedMap;
        }

        return value?.DeepClone();
    }

    private static bool IsZeroOrOne(JsonNode? value) =>
        value is JsonValue number && number.TryGetValue(out int count) && count is 0 or 1;

    private static bool IsObjectSchema(JsonObject schema)
    {
        if (schema.ContainsKey("properties"))
        {
            return true;
        }

        return schema["type"] switch
        {
            JsonValue single when single.TryGetValue(out string? name) => name == "object",
            JsonArray names => names.Any(entry => entry is JsonValue v && v.TryGetValue(out string? n) && n == "object"),
            _ => false,
        };
    }

    private static JsonNode CheckedReference(JsonNode? value, JsonObject? definitions)
    {
        if (value is not JsonValue reference
            || !reference.TryGetValue(out string? target)
            || !target.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
        {
            throw new SchemaProjectionException(
                $"Unsupported $ref {value?.ToJsonString() ?? "null"}: only local references starting with '{DefinitionPrefix}' can be projected.");
        }

        var name = target[DefinitionPrefix.Length..];
        if (definitions is null || !definitions.ContainsKey(name))
        {
            throw new SchemaProjectionException($"$ref '{target}' does not resolve to an entry of $defs.");
        }

        return JsonValue.Create(target);
    }

    /// <summary>Rejects recursion, which the structured-output API does not support.</summary>
    private static void EnsureNoDefinitionCycle(JsonObject root, JsonObject? definitions)
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal); // false = in progress, true = done

        void Visit(string name)
        {
            if (state.TryGetValue(name, out var done))
            {
                if (!done)
                {
                    throw new SchemaProjectionException($"Recursive definition detected at '{name}': recursion is not supported.");
                }

                return;
            }

            state[name] = false;
            if (definitions is not null && definitions.TryGetPropertyValue(name, out var definition) && definition is not null)
            {
                foreach (var target in ReferencedDefinitions(definition))
                {
                    Visit(target);
                }
            }

            state[name] = true;
        }

        foreach (var target in ReferencedDefinitions(WithoutDefinitions(root)))
        {
            Visit(target);
        }

        if (definitions is not null)
        {
            foreach (var (name, _) in definitions)
            {
                Visit(name);
            }
        }
    }

    private static JsonNode WithoutDefinitions(JsonObject root)
    {
        var copy = new JsonObject();
        foreach (var (name, value) in root)
        {
            if (name != "$defs")
            {
                copy[name] = value?.DeepClone();
            }
        }

        return copy;
    }

    private static IEnumerable<string> ReferencedDefinitions(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (name == "$ref"
                        && child is JsonValue reference
                        && reference.TryGetValue(out string? target)
                        && target.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
                    {
                        yield return target[DefinitionPrefix.Length..];
                    }
                    else if (child is not null)
                    {
                        foreach (var nested in ReferencedDefinitions(child))
                        {
                            yield return nested;
                        }
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child is null)
                    {
                        continue;
                    }

                    foreach (var nested in ReferencedDefinitions(child))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
