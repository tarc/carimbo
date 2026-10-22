using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Carimbo.Decisions;
using Carimbo.Domain;
using Carimbo.GroundTruth;
using Carimbo.Validation;
using DecisionCases;

// Writes the committed decision cases under <repo>/data/decision (cases, policy, manifest, expected labels), or with --check
// verifies that a fresh regeneration is byte-identical to the directory, extra and missing files included.
// Files are UTF-8 without BOM with LF endings, so the bytes are identical on every platform.

var check = args.Contains("--check", StringComparer.Ordinal);
var unknown = args.Where(arg => arg != "--check").ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine("Usage: DecisionCases [--check]");
    return 2;
}

var root = FindRepoRoot();
if (root is null)
{
    Console.Error.WriteLine("Could not find the repo root (a folder containing dotnet/Carimbo.slnx) above " + Directory.GetCurrentDirectory());
    return 2;
}

SortedDictionary<string, byte[]> outputs;
try
{
    outputs = Generator.Render(root);
}
catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or GenerationException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var decisionDirectory = Path.Combine(root, "data", "decision");
var stale = new List<string>();
if (check)
{
    foreach (var (relativePath, bytes) in outputs)
    {
        var path = Path.Combine(decisionDirectory, relativePath);
        var current = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (current is null || !current.AsSpan().SequenceEqual(bytes))
        {
            stale.Add("data/decision/" + relativePath);
        }
    }

    stale.AddRange(ExistingFiles(decisionDirectory)
        .Where(relativePath => !outputs.ContainsKey(relativePath))
        .Select(relativePath => "data/decision/" + relativePath));
}
else
{
    foreach (var (relativePath, bytes) in outputs)
    {
        var path = Path.Combine(decisionDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        Console.Out.Write("wrote data/decision/" + relativePath + "\n");
    }

    // The directory is generated output: a file the specs no longer produce is removed.
    foreach (var relativePath in ExistingFiles(decisionDirectory).Where(relativePath => !outputs.ContainsKey(relativePath)))
    {
        File.Delete(Path.Combine(decisionDirectory, relativePath));
        Console.Out.Write("removed data/decision/" + relativePath + "\n");
    }
}

if (stale.Count > 0)
{
    foreach (var path in stale)
    {
        Console.Out.Write("stale: " + path + "\n");
    }

    Console.Out.Write("Run: dotnet run --project dotnet/tools/DecisionCases\n");
    return 1;
}

if (check)
{
    Console.Out.Write("decision cases are up to date\n");
}

return 0;

static IEnumerable<string> ExistingFiles(string directory) =>
    Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'))
        : [];

static string? FindRepoRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dotnet", "Carimbo.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

namespace DecisionCases
{
    /// <summary>Raised when the specs and the labelling rule disagree, or a spec cannot be built.</summary>
    internal sealed class GenerationException(string message) : Exception(message);

    /// <summary>Renders every output file of data/decision in memory, keyed by its path relative to that directory.</summary>
    internal static class Generator
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        public static SortedDictionary<string, byte[]> Render(string repoRoot)
        {
            var skeleton = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "skeleton", "manifest.json")))!;
            var asOf = DateOnly.ParseExact(skeleton["as_of_date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var validator = new InvoiceValidator(new ValidationOptions());
            var policy = new DecisionPolicy(CaseSpecs.BuyerCnpj, CaseSpecs.ApprovalLimit);

            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            var manifestCases = new JsonArray();
            var expectedCases = new JsonArray();
            foreach (var spec in CaseSpecs.All.OrderBy(spec => spec.CaseId, StringComparer.Ordinal))
            {
                var @case = Build(spec, repoRoot, asOf, validator);
                var label = DecisionRules.Label(@case, policy);
                if (label.Outcome != spec.IntendedOutcome || label.Cause != spec.IntendedCause)
                {
                    throw new GenerationException(
                        $"{spec.CaseId}: the spec intends {Describe(spec.IntendedOutcome, spec.IntendedCause)} but " +
                        $"{DecisionRules.LabellingVersion} gives {Describe(label.Outcome, label.Cause)}");
                }

                var caseBytes = Render(JsonSerializer.SerializeToNode(@case, Wire.Options)!);
                files["cases/" + spec.CaseId + ".json"] = caseBytes;
                manifestCases.Add(new JsonObject
                {
                    ["case_id"] = spec.CaseId,
                    ["intent"] = spec.Intent,
                    ["base_case"] = spec.BaseCase,
                    ["mutation"] = spec.Mutation,
                    ["forced_status"] = spec.ForcedStatus,
                    ["case_sha256"] = Sha256(caseBytes),
                });
                expectedCases.Add(new JsonObject
                {
                    ["case_id"] = spec.CaseId,
                    ["decision"] = JsonSerializer.SerializeToNode(label.Outcome, Wire.Options),
                    ["cause"] = JsonSerializer.SerializeToNode(label.Cause, Wire.Options),
                    ["rule"] = label.Rule,
                });
            }

            var policyBytes = Render(JsonSerializer.SerializeToNode(policy, Wire.Options)!);
            files["policy.json"] = policyBytes;
            files["manifest.json"] = Render(new JsonObject
            {
                ["dataset"] = "decision",
                ["dataset_version"] = "decision-001",
                ["labelling_version"] = DecisionRules.LabellingVersion,
                ["skeleton_dataset_version"] = skeleton["dataset_version"]!.GetValue<string>(),
                ["as_of_date"] = skeleton["as_of_date"]!.GetValue<string>(),
                ["cases"] = manifestCases,
                ["policy_sha256"] = Sha256(policyBytes),
            });
            files["expected.json"] = Render(new JsonObject
            {
                ["labelling_version"] = DecisionRules.LabellingVersion,
                ["decision_record"] = "D-34",
                ["cases"] = expectedCases,
            });
            return files;
        }

        private static DecisionCaseFile Build(CaseSpec spec, string repoRoot, DateOnly asOf, InvoiceValidator validator)
        {
            if (spec.ForcedStatus is not null)
            {
                return new DecisionCaseFile(
                    spec.CaseId,
                    new CaseExtraction(spec.ForcedStatus, spec.Failure),
                    null,
                    []);
            }

            var invoice = spec.Apply(NfeXmlMapper.Load(Path.Combine(repoRoot, "data", "skeleton", spec.BaseCase + ".xml")));
            var findings = validator.Validate(invoice, asOf)
                .Select(finding => new CaseFinding(
                    finding.Field,
                    finding.RuleId,
                    finding.Expected,
                    finding.Actual,
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(finding.Severity.ToString())))
                .ToList();
            var status = findings.Any(finding => finding.Severity == "error") ? "validation_failed" : "success";
            return new DecisionCaseFile(spec.CaseId, new CaseExtraction(status, null), invoice, findings);
        }

        private static byte[] Render(JsonNode node) => Utf8.GetBytes(CanonicalSchema.Serialize(node));

        private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static string Describe(DecisionOutcome outcome, DecisionCause cause) =>
            JsonNamingPolicy.SnakeCaseLower.ConvertName(outcome.ToString()) + "/" +
            JsonNamingPolicy.SnakeCaseLower.ConvertName(cause.ToString());
    }
}
