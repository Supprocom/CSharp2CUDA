namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class RepositoryLocator
{
    private const string SolutionFile = "Supprocom.CSharp2CUDA.slnx";

    public static string Find()
    {
        return Search(Directory.GetCurrentDirectory())
            ?? Search(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException(
                "Cannot find the repository root that contains " + SolutionFile + ".");
    }

    private static string? Search(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }
}
