using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.GroundTruth;
using Carimbo.Validation;
using Xunit;

namespace Carimbo.Decisions.Tests;

/// <summary>The eight committed decision cases: one rule path each, labels the documented rule recomputes (AGT-04, D-34).</summary>
public partial class DecisionDatasetTests
{
    private static readonly string Directory = Repo.PathOf("data", "decision");

    private static readonly string[] AllIds = ["dc-001", "dc-002", "dc-003", "dc-004", "dc-005", "dc-006", "dc-007", "dc-008"];

    private static readonly string[] ForbiddenMembers = ["label", "intent", "decision", "cause", "labelling_version"];

    // The oracle is written by hand from the rule list in D-34, not computed from the code under test.
    public static TheoryData<string, DecisionOutcome, DecisionCause, int> ExpectedLabels => new()
    {
        { "dc-001", DecisionOutcome.Approve, DecisionCause.None, 5 },
        { "dc-002", DecisionOutcome.Reject, DecisionCause.None, 2 },
        { "dc-003", DecisionOutcome.Escalate, DecisionCause.PolicyWarning, 3 },
        { "dc-004", DecisionOutcome.Reject, DecisionCause.None, 2 },
        { "dc-005", DecisionOutcome.Escalate, DecisionCause.ExtractionNotSuccess, 1 },
        { "dc-006", DecisionOutcome.Escalate, DecisionCause.OverLimit, 4 },
        { "dc-007", DecisionOutcome.Approve, DecisionCause.None, 5 },
        { "dc-008", DecisionOutcome.Escalate, DecisionCause.ExtractionNotSuccess, 1 },
    };

    public static TheoryData<string> CaseIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var id in AllIds)
            {
                data.Add(id);
            }

            return data;
        }
    }

    [Fact]
    public void The_manifest_lists_exactly_dc_001_to_dc_008_and_the_cases_directory_holds_those_files()
    {
        var manifest = ReadObject("manifest.json");

        Assert.Equal(AllIds, manifest["cases"]!.AsArray().Select(node => node!["case_id"]!.GetValue<string>()));
        Assert.Equal(
            AllIds.Select(id => id + ".json"),
            System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "cases")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("decision-labels-1", manifest["labelling_version"]!.GetValue<string>());
        Assert.Equal("decision-labels-1", ReadObject("expected.json")["labelling_version"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(ExpectedLabels))]
    public void Each_case_has_the_label_of_its_rule_path(string id, DecisionOutcome outcome, DecisionCause cause, int rule)
    {
        var store = new CaseStore(Directory);

        var recomputed = DecisionRules.Label(store.Load(id), store.LoadPolicy());
        var recorded = ExpectedEntry(id);

        Assert.Equal(new DecisionLabel(outcome, cause, rule), recomputed);
        Assert.Equal(WireName(outcome), recorded["decision"]!.GetValue<string>());
        Assert.Equal(WireName(cause), recorded["cause"]!.GetValue<string>());
        Assert.Equal(rule, recorded["rule"]!.GetValue<int>());
    }

    [Fact]
    public void Every_expected_json_entry_equals_the_rule_over_the_committed_files()
    {
        var store = new CaseStore(Directory);
        var policy = store.LoadPolicy();

        var entries = ReadObject("expected.json")["cases"]!.AsArray().Select(node => (JsonObject)node!).ToList();

        Assert.Equal(AllIds, entries.Select(entry => entry["case_id"]!.GetValue<string>()));
        foreach (var entry in entries)
        {
            var label = DecisionRules.Label(store.Load(entry["case_id"]!.GetValue<string>()), policy);
            Assert.Equal(WireName(label.Outcome), entry["decision"]!.GetValue<string>());
            Assert.Equal(WireName(label.Cause), entry["cause"]!.GetValue<string>());
            Assert.Equal(label.Rule, entry["rule"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Dc_003_holds_a_tax_code_unsupported_warning_and_no_error()
    {
        var @case = new CaseStore(Directory).Load("dc-003");

        Assert.Equal("success", @case.Extraction.Status);
        Assert.Contains(@case.Findings, f => f.RuleId == "TAX_CODE_UNSUPPORTED" && f.Severity == "warning");
        Assert.DoesNotContain(@case.Findings, f => f.Severity == "error");
    }

    [Fact]
    public void Dc_008_is_validation_failed_with_an_item_arithmetic_error_and_its_invoice_present()
    {
        var @case = new CaseStore(Directory).Load("dc-008");

        Assert.Equal("validation_failed", @case.Extraction.Status);
        Assert.NotNull(@case.Invoice);
        Assert.Contains(@case.Findings, f => f.RuleId == "ITEM_ARITH" && f.Severity == "error");
    }

    [Fact]
    public void Dc_005_is_a_refused_extraction_with_no_invoice_and_no_findings()
    {
        var @case = new CaseStore(Directory).Load("dc-005");

        Assert.Equal("refused", @case.Extraction.Status);
        Assert.Equal("refused", @case.Extraction.Failure?.Kind);
        Assert.Null(@case.Invoice);
        Assert.Empty(@case.Findings);
    }

    [Fact]
    public void Dc_002_is_the_ordering_trap_a_foreign_recipient_over_the_limit_that_is_rejected()
    {
        var store = new CaseStore(Directory);
        var @case = store.Load("dc-002");

        Assert.NotEqual(store.LoadPolicy().BuyerCnpj, @case.Invoice!.Recipient.TaxId);
        Assert.True(@case.Invoice.Totals.InvoiceTotal.Amount > store.LoadPolicy().ApprovalLimit.Amount);
        Assert.Equal(DecisionOutcome.Reject, DecisionRules.Label(@case, store.LoadPolicy()).Outcome);
    }

    [Fact]
    public void The_buyer_cnpj_is_valid_alphanumeric_and_no_skeleton_party()
    {
        var buyer = new CaseStore(Directory).LoadPolicy().BuyerCnpj;

        Assert.True(Identifiers.IsValidCnpj(buyer));
        Assert.Contains(buyer, char.IsLetter);
        foreach (var skeletonCase in new[] { "case-001", "case-002", "case-003" })
        {
            var invoice = NfeXmlMapper.Load(Repo.PathOf("data", "skeleton", skeletonCase + ".xml"));
            Assert.NotEqual(buyer, invoice.Issuer.Cnpj);
            Assert.NotEqual(buyer, invoice.Recipient.TaxId);
        }
    }

    [Fact]
    public void Committed_case_files_carry_no_label()
    {
        var manifestIds = ReadObject("manifest.json")["cases"]!.AsArray().Select(node => node!["case_id"]!.GetValue<string>()).ToList();
        Assert.All(manifestIds, id => Assert.Matches(NeutralId(), id));

        foreach (var path in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "cases")))
        {
            var node = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

            Assert.Equal(
                ["case_id", "extraction", "findings", "invoice"],
                node.Select(pair => pair.Key).Order(StringComparer.Ordinal));
            Assert.Empty(LeaksIn(node, "$"));
        }
    }

    [Fact]
    public void The_leak_walk_finds_a_forbidden_member_at_any_depth_and_the_labelling_text()
    {
        var node = JsonNode.Parse("""{"a":[{"b":{"Decision":1,"cause":2}}],"c":"uses decision-labels-1"}""")!;

        Assert.Equal(["$.a[0].b.Decision", "$.a[0].b.cause", "$.c"], LeaksIn(node, "$").Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Every_case_round_trips_through_the_strict_wire_options(string id)
    {
        var @case = new CaseStore(Directory).Load(id);

        var again = JsonSerializer.Serialize(@case, Wire.Options);

        Assert.Equal(
            File.ReadAllText(Path.Combine(Directory, "cases", id + ".json")).ReplaceLineEndings("\n"),
            CanonicalSchema.Serialize(JsonNode.Parse(again)!));
    }

    [GeneratedRegex("^dc-[0-9]{3}$")]
    private static partial Regex NeutralId();

    // Paths of forbidden members (any depth, ordinal and case-sensitive on the lowercase names) and of strings that name the rule.
    private static List<string> LeaksIn(JsonNode? node, string path)
    {
        var leaks = new List<string>();
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    var childPath = path + "." + name;
                    if (ForbiddenMembers.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        leaks.Add(childPath);
                    }

                    leaks.AddRange(LeaksIn(value, childPath));
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    leaks.AddRange(LeaksIn(array[i], $"{path}[{i}]"));
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (text.Contains("decision-labels", StringComparison.OrdinalIgnoreCase))
                {
                    leaks.Add(path);
                }

                break;
        }

        return leaks;
    }

    private static JsonObject ReadObject(string file) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, file)))!;

    private static JsonObject ExpectedEntry(string id) =>
        ReadObject("expected.json")["cases"]!.AsArray()
            .Select(node => (JsonObject)node!)
            .Single(entry => entry["case_id"]!.GetValue<string>() == id);

    private static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, Wire.Options).Trim('"');
}
