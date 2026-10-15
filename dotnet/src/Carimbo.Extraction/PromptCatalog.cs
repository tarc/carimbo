namespace Carimbo.Extraction;

/// <summary>Catalogued extraction prompts, selectable per request by version (D-30). Skeleton: filled in by the next commit.</summary>
public static class PromptCatalog
{
    public const string DefaultVersion = "extract-003";

    public static IReadOnlyList<string> Versions { get; } = [DefaultVersion];

    public static bool TryGet(string version, out string text)
    {
        text = string.Empty;
        return false;
    }
}
