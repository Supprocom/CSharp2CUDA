namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal sealed class RunContext
{
    private RunContext()
    {
    }

    public string PackageRoot { get; private init; } = string.Empty;

    public string RepositoryRoot { get; private init; } = string.Empty;

    public string RunBase { get; private init; } = string.Empty;

    public string Hash { get; private init; } = string.Empty;

    public string RunRoot { get; private init; } = string.Empty;

    public string EvidenceRoot { get; private init; } = string.Empty;

    public string PackagesRoot { get; private init; } = string.Empty;

    public string NuGetConfigPath { get; private init; } = string.Empty;

    public ProcessRunner Runner { get; private init; } = null!;

    private string WorkingDirectory { get; init; } = string.Empty;

    private (string Key, string Value)[] Environment { get; init; } = [];

    public static async Task<RunContext> CreateAsync(
        string kind,
        string packageDirectory,
        string runDirectory,
        string hashPackageFile,
        string outsideMessage,
        bool runInRunRoot)
    {
        var packageRoot = Path.GetFullPath(packageDirectory);
        var hashPath = Path.Combine(packageRoot, hashPackageFile);
        Check.That(File.Exists(hashPath), kind, $"The package {hashPackageFile} is missing.");
        var repositoryRoot = RepositoryLocator.Find();
        var hash = await FileHash.Sha256Async(hashPath).ConfigureAwait(false);
        var runBase = Path.GetFullPath(runDirectory);
        PathGuard.RequireOutside(runBase, repositoryRoot, kind, outsideMessage);
        var runRoot = Path.Combine(runBase, kind, hash[..16]);
        var evidenceRoot = Path.Combine(runRoot, "evidence");
        var packagesRoot = Path.Combine(runRoot, "packages");
        var temp = Path.Combine(runRoot, "temp");
        var dotnetHome = Path.Combine(runRoot, "dotnet-home");
        var httpCache = Path.Combine(runRoot, "nuget-http-cache");
        foreach (var directory in new[] { evidenceRoot, packagesRoot, temp, dotnetHome, httpCache })
            Directory.CreateDirectory(directory);

        (string Key, string Value)[] environment =
        [
            ("DOTNET_CLI_HOME", dotnetHome),
            ("NUGET_PACKAGES", packagesRoot),
            ("NUGET_HTTP_CACHE_PATH", httpCache),
            ("TEMP", temp),
            ("TMP", temp),
            ("TMPDIR", temp)
        ];
        var workingDirectory = runInRunRoot ? runRoot : string.Empty;
        return new RunContext
        {
            PackageRoot = packageRoot,
            RepositoryRoot = repositoryRoot,
            RunBase = runBase,
            Hash = hash,
            RunRoot = runRoot,
            EvidenceRoot = evidenceRoot,
            PackagesRoot = packagesRoot,
            NuGetConfigPath = Path.Combine(runRoot, "NuGet.config"),
            Environment = environment,
            WorkingDirectory = workingDirectory,
            Runner = new ProcessRunner(
                evidenceRoot,
                environment,
                runInRunRoot ? runRoot : null)
        };
    }

    public ProcessRunner CreateRunner((string Key, string Value)[] extraEnvironment)
    {
        return new ProcessRunner(
            EvidenceRoot,
            [.. Environment, .. extraEnvironment],
            WorkingDirectory.Length == 0 ? null : WorkingDirectory);
    }

    public Task WriteNuGetConfigAsync(string[] candidatePatterns, string[] nugetOrgPatterns)
    {
        var escapedRoot = System.Security.SecurityElement.Escape(PackageRoot) ?? string.Empty;
        var lines = new List<string>
        {
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<configuration>",
            "  <packageSources>",
            "    <clear />",
            $"    <add key=\"candidate\" value=\"{escapedRoot}\" />",
            "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />",
            "  </packageSources>",
            "  <packageSourceMapping>",
            "    <packageSource key=\"candidate\">"
        };
        AddPatterns(lines, candidatePatterns);
        lines.Add("    </packageSource>");
        lines.Add("    <packageSource key=\"nuget.org\">");
        AddPatterns(lines, nugetOrgPatterns);
        lines.Add("    </packageSource>");
        lines.Add("  </packageSourceMapping>");
        lines.Add("</configuration>");
        return File.WriteAllTextAsync(NuGetConfigPath, string.Join('\n', lines));
    }

    public string[] RestoreArguments(string project, string[] extra)
    {
        return
        [
            "restore",
            project,
            "--configfile",
            NuGetConfigPath,
            "--packages",
            PackagesRoot,
            "--force",
            "--no-cache",
            .. extra
        ];
    }

    private static void AddPatterns(List<string> lines, string[] patterns)
    {
        foreach (var pattern in patterns)
            lines.Add($"      <package pattern=\"{pattern}\" />");
    }
}
