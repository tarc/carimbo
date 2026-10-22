using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Domain;
using Xunit;

namespace Carimbo.Decisions.Tests;

/// <summary>The committed data/decision directory read back through the read-only store (AGT-04, D-34).</summary>
public class DecisionCaseTests
{
    private static readonly string Directory = Repo.PathOf("data", "decision");

    public static TheoryData<string> ValidIds => new()
    {
        "dc-001",
        "a",
        "0",
        "a" + new string('-', 63),
        new string('a', 64),
    };

    public static IEnumerable<object?[]> InvalidIds =>
    [
        [null],
        [""],
        ["DC-001"],
        ["../policy"],
        [".."],
        ["a/b"],
        ["a\\b"],
        ["-dc"],
        ["dc_001"],
        ["dc-001\n"],
        ["dc-001 "],
        [new string('a', 65)],
    ];

    [Theory]
    [MemberData(nameof(ValidIds))]
    public void Case_ids_of_the_allowed_form_are_valid(string id) => Assert.True(CaseIds.IsValid(id));

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public void Case_ids_outside_the_allowed_form_are_invalid(string? id) => Assert.False(CaseIds.IsValid(id));

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public void Loading_an_invalid_id_throws_before_any_file_access_and_names_no_path(string? id)
    {
        var store = new CaseStore(Directory);

        var error = Assert.Throws<InvalidCaseIdException>(() => store.Load(id!));

        Assert.DoesNotContain(Directory, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/", error.Message, StringComparison.Ordinal);
        Assert.False(store.Exists(id!));
    }

    [Fact]
    public void Loading_a_valid_unknown_id_throws_case_not_found_without_a_path()
    {
        var store = new CaseStore(Directory);

        var error = Assert.Throws<CaseNotFoundException>(() => store.Load("dc-999"));

        Assert.DoesNotContain(Directory, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/", error.Message, StringComparison.Ordinal);
        Assert.False(store.Exists("dc-999"));
    }

    [Fact]
    public void The_generated_dc_001_and_the_policy_read_back_to_the_label_expected_json_records()
    {
        var store = new CaseStore(Directory);

        var @case = store.Load("dc-001");
        var policy = store.LoadPolicy();
        var label = DecisionRules.Label(@case, policy);

        var entry = ExpectedEntries().Single(node => node["case_id"]!.GetValue<string>() == "dc-001");
        Assert.True(store.Exists("dc-001"));
        Assert.Equal("dc-001", @case.CaseId);
        Assert.Equal(WireName(label.Outcome), entry["decision"]!.GetValue<string>());
        Assert.Equal(WireName(label.Cause), entry["cause"]!.GetValue<string>());
        Assert.Equal(label.Rule, entry["rule"]!.GetValue<int>());
        Assert.Equal(DecisionOutcome.Approve, label.Outcome);
    }

    [Fact]
    public void A_case_file_holds_exactly_the_four_served_members()
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "cases", "dc-001.json")))!;

        Assert.Equal(["case_id", "extraction", "findings", "invoice"], node.Select(pair => pair.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_store_never_writes_into_the_case_directory()
    {
        var before = Snapshot();
        var store = new CaseStore(Directory);

        _ = store.Load("dc-001");
        _ = store.LoadPolicy();
        _ = store.Exists("dc-001");
        _ = Record.Exception(() => store.Load("dc-999"));

        Assert.Equal(before, Snapshot());
    }

    private static IEnumerable<JsonObject> ExpectedEntries() =>
        ((JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "expected.json")))!)["cases"]!
        .AsArray()
        .Select(node => (JsonObject)node!);

    private static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, Wire.Options).Trim('"');

    private static string[] Snapshot() =>
        [.. System.IO.Directory.EnumerateFileSystemEntries(Directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + "|" + new FileInfo(path).Length : "dir"))];
}
