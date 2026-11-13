namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class SourceSuiteCommand
{
    public const string Usage =
        "source-suite --package-directory <dir> --run-directory <dir> --package-version <version> "
        + "[--results-directory <dir>]";

    private static readonly string[] Required = ["package-directory", "run-directory", "package-version"];
    private static readonly string[] OptionalNames = ["results-directory"];
    private static readonly string[] PolicyFiles =
        ["Directory.Build.props", "Directory.Build.targets", "BannedSymbols.txt", ".editorconfig"];

    public static async Task<int> RunAsync(string[] args)
    {
        var values = Options.Parse(args, Required, OptionalNames);
        if (values is null)
            return Program.UsageError(Usage);
        var version = values["package-version"];
        var context = await RunContext.CreateAsync(
            "package-source-tests",
            values["package-directory"],
            values["run-directory"],
            $"Supprocom.CSharp2CUDA.{version}.nupkg",
            "The source-suite directory must be outside the source checkout.",
            runInRunRoot: false).ConfigureAwait(false);
        var exactPackageCuda = Path.Combine(
            context.RunBase,
            "package-tests",
            context.Hash[..16],
            "evidence",
            "external-dispatch-boundary.generated.cu");
        Check.That(
            File.Exists(exactPackageCuda),
            "source-suite",
            "The package build matrix did not retain the external dispatch CUDA source.");
        var resultsDirectory = Options.Optional(values, "results-directory") is { } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(context.EvidenceRoot, "trx");
        Directory.CreateDirectory(resultsDirectory);
        var sourceRoot = PrepareSource(context);
        await WriteConfigAsync(context).ConfigureAwait(false);
        await RunSuiteAsync(context, version, exactPackageCuda, resultsDirectory, sourceRoot)
            .ConfigureAwait(false);
        await CompareAssembliesAsync(context, version).ConfigureAwait(false);
        var summary = $"Package SHA-256: {context.Hash}\nThe exact-package source suite passed.\n";
        await File.WriteAllTextAsync(Path.Combine(context.EvidenceRoot, "summary.txt"), summary)
            .ConfigureAwait(false);
        Console.Out.WriteLine(summary.TrimEnd());
        return 0;
    }

    private static string PrepareSource(RunContext context)
    {
        var sourceRoot = Path.Combine(context.RunRoot, "source");
        PathGuard.RequireInside(
            sourceRoot,
            context.RunRoot,
            "source-suite",
            "The source-suite path is outside the test output directory.");
        if (Directory.Exists(sourceRoot))
            Directory.Delete(sourceRoot, recursive: true);
        var testSourceRoot = Path.Combine(sourceRoot, "Supprocom.CSharp2CUDA.Tests");
        Directory.CreateDirectory(testSourceRoot);
        var originalTestRoot = Path.Combine(context.RepositoryRoot, "Supprocom.CSharp2CUDA.Tests");
        foreach (var entry in new DirectoryInfo(originalTestRoot).EnumerateFileSystemInfos())
        {
            if (entry.Name is "bin" or "obj" or "TestResults")
                continue;
            if (entry is DirectoryInfo directory)
                CopyDirectory(directory.FullName, Path.Combine(testSourceRoot, entry.Name));
            else
                File.Copy(entry.FullName, Path.Combine(testSourceRoot, entry.Name), overwrite: true);
        }

        foreach (var policyFile in PolicyFiles)
        {
            File.Copy(
                Path.Combine(context.RepositoryRoot, policyFile),
                Path.Combine(sourceRoot, policyFile),
                overwrite: true);
        }

        return sourceRoot;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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
                "NetFabric.Hyperlinq.Analyzer",
                "xunit",
                "xunit.*",
                "Newtonsoft.Json",
                "runtime.*"
            ]);
    }

    private static async Task RunSuiteAsync(
        RunContext context,
        string version,
        string exactPackageCuda,
        string resultsDirectory,
        string sourceRoot)
    {
        var runner = context.CreateRunner(
        [
            ("CSHARP2CUDA_EXACT_PACKAGE_CUDA", exactPackageCuda),
            ("CSHARP2CUDA_EVIDENCE_DIRECTORY", Path.Combine(context.EvidenceRoot, "generated"))
        ]);
        var testProject = Path.Combine(
            sourceRoot,
            "Supprocom.CSharp2CUDA.Tests",
            "Supprocom.CSharp2CUDA.Tests.csproj");
        string[] common =
        [
            "-p:CSharp2CUDAPackageDirectory=" + context.PackageRoot,
            "-p:CSharp2CUDAPackageVersion=" + version,
            "-p:BaseOutputPath=" + PathGuard.WithTrailingSeparator(context.RunRoot, "test-bin"),
            "-p:BaseIntermediateOutputPath=" + PathGuard.WithTrailingSeparator(context.RunRoot, "test-obj")
        ];
        await runner.DotNetAsync("restore", 0, context.RestoreArguments(testProject, common))
            .ConfigureAwait(false);
        await runner.DotNetAsync(
            "test",
            0,
            [
                "test",
                testProject,
                "--configuration",
                "Release",
                "--no-restore",
                "--logger",
                "console;verbosity=minimal",
                "--logger",
                "trx;LogFileName=Supprocom.CSharp2CUDA.PackageTests.trx",
                "--results-directory",
                resultsDirectory,
                .. common
            ]).ConfigureAwait(false);
    }

    private static async Task CompareAssembliesAsync(RunContext context, string version)
    {
        var packageAssembly = Path.Combine(
            context.PackagesRoot,
            "supprocom.csharp2cuda",
            version,
            "lib",
            "net10.0",
            "Supprocom.CSharp2CUDA.dll");
        var testAssembly = Path.Combine(
            context.RunRoot,
            "test-bin",
            "Release",
            "net10.0",
            "Supprocom.CSharp2CUDA.dll");
        var packageHash = await FileHash.Sha256Async(packageAssembly).ConfigureAwait(false);
        var testHash = await FileHash.Sha256Async(testAssembly).ConfigureAwait(false);
        Check.That(
            string.Equals(packageHash, testHash, StringComparison.Ordinal),
            "source-suite",
            "The test output does not contain the exact package assembly.");
    }
}
