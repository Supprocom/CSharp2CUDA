namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class PackageCommand
{
    public const string Usage =
        "package --package-directory <dir> --run-directory <dir> --package-version <version> "
        + "[--expected-commit <sha>] [--expected-branch <name>]";

    private static readonly string[] Required = ["package-directory", "run-directory", "package-version"];
    private static readonly string[] OptionalNames = ["expected-commit", "expected-branch"];

    public static async Task<int> RunAsync(string[] args)
    {
        var values = Options.Parse(args, Required, OptionalNames);
        if (values is null)
            return Program.UsageError(Usage);
        var version = values["package-version"];
        var context = await RunContext.CreateAsync(
            "package-tests",
            values["package-directory"],
            values["run-directory"],
            $"Supprocom.CSharp2CUDA.{version}.nupkg",
            "The package test directory must be outside the source checkout.",
            runInRunRoot: false).ConfigureAwait(false);
        var commit = Options.Optional(values, "expected-commit")
            ?? await ProcessRunner.ReadGitAsync(
                context.RepositoryRoot,
                "Cannot read the repository commit.",
                "rev-parse",
                "HEAD").ConfigureAwait(false);
        Check.That(
            commit.Length == 40 && commit.All(Uri.IsHexDigit),
            "package",
            "The expected repository commit is invalid.");
        var branch = Options.Optional(values, "expected-branch")
            ?? await ProcessRunner.ReadGitAsync(
                context.RepositoryRoot,
                "Cannot read the repository branch.",
                "branch",
                "--show-current").ConfigureAwait(false);
        await WriteConfigAsync(context).ConfigureAwait(false);
        await new PackageVerifier(context, version).RunAsync(commit, branch).ConfigureAwait(false);
        var summary = $"Package SHA-256: {context.Hash}\nAll package build-integration checks passed.\n";
        await File.WriteAllTextAsync(Path.Combine(context.EvidenceRoot, "summary.txt"), summary)
            .ConfigureAwait(false);
        Console.Out.WriteLine(summary.TrimEnd());
        return 0;
    }

    private static Task WriteConfigAsync(RunContext context)
    {
        return context.WriteNuGetConfigAsync(
            ["Supprocom.CSharp2CUDA"],
            [
                "Microsoft.*",
                "System.*",
                "Meziantou.Analyzer",
                "Roslynator.Analyzers",
                "NetFabric.Hyperlinq.Analyzer"
            ]);
    }
}
