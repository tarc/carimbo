using System.Text.Json.Nodes;

namespace Carimbo.Extraction;

/// <summary>Thrown when a schema exceeds the structured-output budget.</summary>
public sealed class SchemaBudgetExceededException(string message) : Exception(message);

/// <summary>Counts a schema against the structured-output budget. RED stub: counts nothing.</summary>
public static class SchemaBudget
{
    public const int MaxOptional = 24;

    public const int MaxUnion = 16;

    public sealed record Counts(int Optional, int Union);

    public static Counts Count(JsonObject schema) => new(0, 0);

    public static void EnsureWithin(JsonObject schema)
    {
    }
}
