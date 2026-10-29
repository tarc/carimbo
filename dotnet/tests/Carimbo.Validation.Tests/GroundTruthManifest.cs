using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>
/// The manifest of a committed case directory (<c>data/skeleton</c>, <c>data/dataset</c>), read once and
/// shared by the ground-truth gates so each gate iterates the manifest and nothing is skipped (D-18).
/// </summary>
internal sealed class GroundTruthManifest
{
    private static readonly ConcurrentDictionary<string, GroundTruthManifest> Loaded = new();

    private readonly JsonObject root;

    private GroundTruthManifest(string directory, JsonObject root)
    {
        Directory = directory;
        this.root = root;
        Cases = root["cases"]!.AsArray().Select(node => (JsonObject)node!).ToArray();
    }

    /// <summary>The directory under <c>data/</c> this manifest describes.</summary>
    public string Directory { get; }

    /// <summary>The manifest's own <c>dataset</c> member.</summary>
    public string Name => root["dataset"]!.GetValue<string>();

    /// <summary>The date every case is validated at, parsed exactly as <c>yyyy-MM-dd</c>.</summary>
    public DateOnly AsOfDate =>
        DateOnly.ParseExact(root["as_of_date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The cases in manifest order.</summary>
    public IReadOnlyList<JsonObject> Cases { get; }

    /// <summary>The parsed manifest of <c>data/&lt;directory&gt;</c>, cached per directory name.</summary>
    public static GroundTruthManifest For(string directory) =>
        Loaded.GetOrAdd(directory, name => new GroundTruthManifest(
            name,
            (JsonObject)JsonNode.Parse(File.ReadAllText(RepoFiles.PathOf("data", name, "manifest.json")))!));

    /// <summary>The manifest entry of one case; throws when the id is not listed.</summary>
    public JsonObject CaseOf(string caseId) =>
        Cases.Single(entry => entry["case_id"]!.GetValue<string>() == caseId);

    /// <summary>Absolute path of the case's XML, taken from its <c>xml</c> member.</summary>
    public string XmlPathOf(string caseId) =>
        RepoFiles.PathOf("data", Directory, CaseOf(caseId)["xml"]!.GetValue<string>());

    /// <summary>One theory row per manifest case, in manifest order, so a failure names the case id.</summary>
    public TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var entry in Cases)
        {
            data.Add(entry["case_id"]!.GetValue<string>());
        }

        return data;
    }
}
