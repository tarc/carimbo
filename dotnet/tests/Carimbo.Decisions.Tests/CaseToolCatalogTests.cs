using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Domain;
using Xunit;

namespace Carimbo.Decisions.Tests;

/// <summary>What each read-only tool returns for the committed decision cases, and that none of it carries a label (AGT-01, AGT-04, D-35).</summary>
public class CaseToolCatalogTests
{
    private static readonly string Directory = Repo.PathOf("data", "decision");

    private static readonly string[] AllIds = ["dc-001", "dc-002", "dc-003", "dc-004", "dc-005", "dc-006", "dc-007", "dc-008"];

    private static readonly string[] ForbiddenMembers = ["label", "intent", "decision", "cause", "labelling_version"];

    private static readonly string[] ForbiddenText = ["decision-labels", "approve-", "escalate-", "reject-"];

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
    public void Names_are_the_four_tools_in_a_fixed_order()
    {
        Assert.Equal(
            ["get_extraction_status", "get_invoice", "get_validator_findings", "get_policy"],
            CaseToolCatalog.Names);
        Assert.Equal("get_extraction_status", CaseToolCatalog.GetExtractionStatus);
        Assert.Equal("get_invoice", CaseToolCatalog.GetInvoice);
        Assert.Equal("get_validator_findings", CaseToolCatalog.GetValidatorFindings);
        Assert.Equal("get_policy", CaseToolCatalog.GetPolicy);
    }

    [Fact]
    public void Every_tool_has_a_non_empty_description_and_the_case_id_parameter_is_described()
    {
        string[] descriptions =
        [
            CaseToolCatalog.GetExtractionStatusDescription,
            CaseToolCatalog.GetInvoiceDescription,
            CaseToolCatalog.GetValidatorFindingsDescription,
            CaseToolCatalog.GetPolicyDescription,
            CaseToolCatalog.CaseIdParameterDescription,
        ];

        Assert.All(descriptions, description => Assert.False(string.IsNullOrWhiteSpace(description)));
        Assert.Equal("The case id, for example dc-001.", CaseToolCatalog.CaseIdParameterDescription);
    }

    [Fact]
    public void The_refused_case_status_names_the_failure()
    {
        var @case = Load("dc-005");

        var node = Parse(CaseToolCatalog.StatusJson(@case));

        Assert.Equal("dc-005", node["case_id"]!.GetValue<string>());
        Assert.Equal("refused", node["status"]!.GetValue<string>());
        Assert.Equal("refused", node["failure"]!["kind"]!.GetValue<string>());
        Assert.Equal("The model declined to extract the document.", node["failure"]!["message"]!.GetValue<string>());
        Assert.Equal(["case_id", "status", "failure"], Members(node));
    }

    [Fact]
    public void The_success_case_status_has_a_null_failure()
    {
        var node = Parse(CaseToolCatalog.StatusJson(Load("dc-001")));

        Assert.Equal("success", node["status"]!.GetValue<string>());
        Assert.True(node.ContainsKey("failure"));
        Assert.Null(node["failure"]);
    }

    [Fact]
    public void The_invoice_rendering_equals_the_wire_serialization_of_the_case_invoice()
    {
        var @case = Load("dc-001");

        var node = Parse(CaseToolCatalog.InvoiceJson(@case));

        Assert.Equal(["case_id", "invoice"], Members(node));
        Assert.Equal("dc-001", node["case_id"]!.GetValue<string>());
        var expected = JsonNode.Parse(JsonSerializer.Serialize(@case.Invoice, Wire.Options))!;
        Assert.True(JsonNode.DeepEquals(expected, node["invoice"]));
    }

    [Fact]
    public void A_case_without_an_invoice_renders_invoice_null_with_a_note_and_never_an_error()
    {
        var node = Parse(CaseToolCatalog.InvoiceJson(Load("dc-005")));

        Assert.Equal(["case_id", "invoice", "note"], Members(node));
        Assert.Null(node["invoice"]);
        Assert.Equal("No invoice: the extraction status is refused.", node["note"]!.GetValue<string>());
    }

    [Fact]
    public void Findings_render_in_the_stored_order_with_their_five_members()
    {
        var @case = Load("dc-003");
        Assert.NotEmpty(@case.Findings);

        var node = Parse(CaseToolCatalog.FindingsJson(@case));

        Assert.Equal(["case_id", "findings"], Members(node));
        var rendered = node["findings"]!.AsArray();
        Assert.Equal(@case.Findings.Count, rendered.Count);
        for (var i = 0; i < rendered.Count; i++)
        {
            var finding = @case.Findings[i];
            var item = rendered[i]!.AsObject();
            Assert.Equal(["field", "rule_id", "expected", "actual", "severity"], Members(item));
            Assert.Equal(finding.Field, item["field"]!.GetValue<string>());
            Assert.Equal(finding.RuleId, item["rule_id"]!.GetValue<string>());
            Assert.Equal(finding.Expected, item["expected"]!.GetValue<string>());
            Assert.Equal(finding.Actual, item["actual"]!.GetValue<string>());
            Assert.Equal(finding.Severity, item["severity"]!.GetValue<string>());
        }

        Assert.Contains(rendered, item => item!["rule_id"]!.GetValue<string>() == "TAX_CODE_UNSUPPORTED");
    }

    [Fact]
    public void Findings_keep_a_reversed_stored_order()
    {
        var @case = Load("dc-003");
        var reversed = @case with { Findings = [.. @case.Findings.Reverse()] };

        var rendered = Parse(CaseToolCatalog.FindingsJson(reversed))["findings"]!.AsArray();

        Assert.Equal(
            reversed.Findings.Select(finding => finding.RuleId),
            rendered.Select(item => item!["rule_id"]!.GetValue<string>()));
    }

    [Fact]
    public void A_case_with_no_findings_renders_an_empty_list()
    {
        Assert.Equal("""{"case_id":"dc-001","findings":[]}""", CaseToolCatalog.FindingsJson(Load("dc-001")));
    }

    [Fact]
    public void The_policy_renders_the_buyer_cnpj_and_the_limit_as_a_money_string()
    {
        var policy = new CaseStore(Directory).LoadPolicy();

        var json = CaseToolCatalog.PolicyJson(policy);

        Assert.Equal($$"""{"buyer_cnpj":"{{policy.BuyerCnpj}}","approval_limit":"200000.00"}""", json);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Every_rendering_is_compact_single_line_json_and_deterministic(string id)
    {
        foreach (var render in Renderings(id))
        {
            Assert.DoesNotContain('\n', render());
            Assert.DoesNotContain('\r', render());
            Assert.DoesNotContain("  ", render(), StringComparison.Ordinal);
            Assert.Equal(render(), render());
            Assert.Equal(render(), render().Trim());
            _ = JsonNode.Parse(render());
        }
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void No_rendering_carries_a_label_a_rule_name_or_a_filesystem_path(string id)
    {
        var root = Repo.PathOf();
        foreach (var render in Renderings(id))
        {
            var text = render();
            var leaks = LeaksIn(JsonNode.Parse(text), "$");
            Assert.True(leaks.Count == 0, $"{id}: label-like content at {string.Join(", ", leaks)}");
            Assert.DoesNotContain(root, text, StringComparison.Ordinal);
            Assert.DoesNotContain("data/decision", text, StringComparison.Ordinal);
            Assert.DoesNotContain("expected.json", text, StringComparison.Ordinal);
            Assert.DoesNotContain("manifest.json", text, StringComparison.Ordinal);
            Assert.DoesNotContain("cases/", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_leak_walk_flags_a_forbidden_member_and_a_label_like_string()
    {
        var polluted = JsonNode.Parse("""{"a":{"cause":"none"},"b":[{"x":"approve-simples"}],"c":"decision-labels-1","d":"Reject-x"}""");

        var leaks = LeaksIn(polluted, "$");

        Assert.Equal(["$.a.cause", "$.b[0].x", "$.c", "$.d"], leaks);
    }

    private static DecisionCaseFile Load(string id) => new CaseStore(Directory).Load(id);

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static string[] Members(JsonObject node) => [.. node.Select(pair => pair.Key)];

    private static List<Func<string>> Renderings(string id)
    {
        var store = new CaseStore(Directory);
        var @case = store.Load(id);
        var policy = store.LoadPolicy();
        return
        [
            () => CaseToolCatalog.StatusJson(@case),
            () => CaseToolCatalog.InvoiceJson(@case),
            () => CaseToolCatalog.FindingsJson(@case),
            () => CaseToolCatalog.PolicyJson(policy),
        ];
    }

    // Paths of forbidden members (any depth, case-insensitive) and of strings that name the rule or an intent.
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
                if (ForbiddenText.Any(forbidden => text.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
                {
                    leaks.Add(path);
                }

                break;
        }

        return leaks;
    }
}
