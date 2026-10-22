namespace Carimbo.Agent.Tests;

/// <summary>Locates files of the repository by walking up to the folder that holds dotnet/Carimbo.slnx.</summary>
internal static class Repo
{
    private static readonly string Root = FindRoot();

    public static string PathOf(params string[] segments) => Path.Combine([Root, .. segments]);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet", "Carimbo.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repo root above " + AppContext.BaseDirectory);
    }
}
