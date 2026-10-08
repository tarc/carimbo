using System.Text;
using Carimbo.Domain;
using Carimbo.Extraction;

// Writes the committed JSON Schema files under <repo>/schema (canonical and model-facing), or with --check verifies them.
// Files are UTF-8 without BOM with LF endings, so the bytes are identical on every platform.

var check = args.Contains("--check", StringComparer.Ordinal);
var unknown = args.Where(arg => arg != "--check").ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine("Usage: SchemaExport [--check]");
    return 2;
}

var root = FindRepoRoot();
if (root is null)
{
    Console.Error.WriteLine("Could not find the repo root (a folder containing dotnet/Carimbo.slnx) above " + Directory.GetCurrentDirectory());
    return 2;
}

var outputs = new (string RelativePath, string Text)[]
{
    ("schema/invoice.schema.json", CanonicalSchema.ExportJson()),
    ("schema/invoice.model.schema.json", ExtractionContract.Default.OutputSchemaJson),
};

var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var stale = new List<string>();
foreach (var (relativePath, text) in outputs)
{
    var path = Path.Combine(root, relativePath);
    if (check)
    {
        var current = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (current is null || !current.AsSpan().SequenceEqual(encoding.GetBytes(text)))
        {
            stale.Add(relativePath);
        }

        continue;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, encoding.GetBytes(text));
    Console.Out.Write("wrote " + relativePath + "\n");
}

if (stale.Count > 0)
{
    foreach (var path in stale)
    {
        Console.Out.Write("stale: " + path + "\n");
    }

    Console.Out.Write("Run: dotnet run --project dotnet/tools/SchemaExport\n");
    return 1;
}

if (check)
{
    Console.Out.Write("schema files are up to date\n");
}

return 0;

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
