using Carimbo.Domain;

namespace Carimbo.Extraction;

/// <summary>
/// The single composition of a model-facing schema for any exported Domain root other than the extraction
/// contract's own (which composes the same three steps in <see cref="ExtractionContract"/>): project the
/// canonical schema, check the structured-output budget, serialize. The agent plan reuses it for the Decision.
/// </summary>
public static class ModelFacingSchema
{
    /// <summary>The model-facing schema of <paramref name="rootType"/> as committed text.</summary>
    /// <exception cref="SchemaProjectionException">The canonical schema cannot be expressed to the structured-output API.</exception>
    /// <exception cref="SchemaBudgetExceededException">The projected schema exceeds the structured-output budget.</exception>
    public static string Json(Type rootType)
    {
        ArgumentNullException.ThrowIfNull(rootType);

        var projected = ModelSchemaProjector.Project(CanonicalSchema.Export(rootType));
        SchemaBudget.EnsureWithin(projected);
        return CanonicalSchema.Serialize(projected);
    }
}
