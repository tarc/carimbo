using System.Text.Json.Nodes;

namespace Carimbo.Extraction;

/// <summary>Thrown when a canonical schema cannot be expressed to the structured-output API.</summary>
public sealed class SchemaProjectionException(string message) : Exception(message);

/// <summary>Derives the model-facing schema from the canonical one. RED stub: returns a plain copy.</summary>
public static class ModelSchemaProjector
{
    public static JsonObject Project(JsonObject canonical) => (JsonObject)canonical.DeepClone();
}
