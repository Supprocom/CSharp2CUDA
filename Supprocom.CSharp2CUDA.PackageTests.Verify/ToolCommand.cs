using System.Text.RegularExpressions;

namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static partial class ToolCommand
{
    public const string Usage =
        "tool --package-directory <dir> --run-directory <dir> --package-version <version>";

    private const string Method = "ExistingAlgorithms.DistanceTransform.Calculate(double)";
    private static readonly string[] Required = ["package-directory", "run-directory", "package-version"];

    public static async Task<int> RunAsync(string[] args)
    {
        var values = Options.Parse(args, Required, []);
        if (values is null)
            return Program.UsageError(Usage);
        var version = values["package-version"];
        var context = await RunContext.CreateAsync(
            "tool-package-tests",
            values["package-directory"],
            values["run-directory"],
            $"Supprocom.CSharp2CUDA.Tool.{version}.nupkg",
            "The tool test directory must be outside the source checkout.",
            runInRunRoot: true).ConfigureAwait(false);
        var corePackage = Path.Combine(context.PackageRoot, $"Supprocom.CSharp2CUDA.{version}.nupkg");
        Check.That(File.Exists(corePackage), "tool", "The core package is missing.");
        var coreHash = await FileHash.Sha256Async(corePackage).ConfigureAwait(false);
        var toolRoot = Path.Combine(context.RunRoot, "tool");
        var inputRoot = Path.Combine(context.RunRoot, "input");
        var scaffoldRoot = Path.Combine(context.RunRoot, "scaffold");
        ResetDirectories(context.RunRoot, inputRoot, scaffoldRoot, toolRoot);
        CopyInput(context, inputRoot);
        var inputSource = Path.Combine(inputRoot, "Algorithm.cs");
        var inputHash = await FileHash.Sha256Async(inputSource).ConfigureAwait(false);
        await context.WriteNuGetConfigAsync(
            ["Supprocom.CSharp2CUDA", "Supprocom.CSharp2CUDA.Tool"],
            ["Microsoft.*", "System.*"]).ConfigureAwait(false);
        var tool = await InstallAsync(context, version, toolRoot).ConfigureAwait(false);
        var inputProject = Path.Combine(inputRoot, "ToolInput.csproj");
        await context.Runner.DotNetAsync("input-restore", 0, context.RestoreArguments(inputProject, []))
            .ConfigureAwait(false);
        await CheckAndScaffoldAsync(context, tool, inputProject, scaffoldRoot).ConfigureAwait(false);
        Check.That(
            string.Equals(await FileHash.Sha256Async(inputSource).ConfigureAwait(false), inputHash, StringComparison.Ordinal),
            "tool-scaffold",
            "The scaffold command changed the existing algorithm source.");
        var cudaHash = await BuildScaffoldAsync(context, version, tool, scaffoldRoot).ConfigureAwait(false);
        await WriteSummaryAsync(context, coreHash, inputHash, cudaHash).ConfigureAwait(false);
        return 0;
    }

    [GeneratedRegex(@"elementwise\s+Compatible", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ElementwiseCompatible();

    [GeneratedRegex(@"single-thread\s+Compatible", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SingleThreadCompatible();

    private static async Task WriteSummaryAsync(
        RunContext context,
        string coreHash,
        string inputHash,
        string cudaHash)
    {
        var summary = string.Join(
            '\n',
            $"ToolPackageSha256={context.Hash}",
            $"CorePackageSha256={coreHash}",
            $"AlgorithmSourceSha256={inputHash}",
            $"GeneratedCudaSha256={cudaHash}",
            "InstalledTool=passed",
            "ScaffoldBuild=passed",
            "RefreshDeterminism=passed");
        await File.WriteAllTextAsync(Path.Combine(context.EvidenceRoot, "summary.txt"), summary)
            .ConfigureAwait(false);
        Console.Out.WriteLine(summary);
    }

    private static void CopyInput(RunContext context, string inputRoot)
    {
        Directory.CreateDirectory(inputRoot);
        var fixtureRoot = Path.Combine(context.RepositoryRoot, "Supprocom.CSharp2CUDA.PackageTests", "ToolInput");
        foreach (var file in Directory.EnumerateFiles(fixtureRoot))
            File.Copy(file, Path.Combine(inputRoot, Path.GetFileName(file)), overwrite: true);
    }

    private static void ResetDirectories(string runRoot, params string[] directories)
    {
        foreach (var directory in directories)
        {
            PathGuard.RequireInside(
                directory,
                runRoot,
                "tool",
                "A tool test path is outside the test run directory.");
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> InstallAsync(RunContext context, string version, string toolRoot)
    {
        await context.Runner.DotNetAsync(
            "tool-install",
            0,
            [
                "tool",
                "install",
                "Supprocom.CSharp2CUDA.Tool",
                "--tool-path",
                toolRoot,
                "--version",
                version,
                "--configfile",
                context.NuGetConfigPath,
                "--no-cache"
            ]).ConfigureAwait(false);
        foreach (var name in new[] { "csharp2cuda", "csharp2cuda.exe" })
        {
            var candidate = Path.Combine(toolRoot, name);
            if (File.Exists(candidate))
                return candidate;
        }

        Check.Fail("tool-install", "The installed csharp2cuda command is missing.");
        return string.Empty;
    }

    private static async Task CheckAndScaffoldAsync(
        RunContext context,
        string tool,
        string inputProject,
        string scaffoldRoot)
    {
        var output = await context.Runner.RunAsync(
            "tool-check",
            tool,
            ["check", "--project", inputProject, "--method", Method]).ConfigureAwait(false);
        Check.That(
            ElementwiseCompatible().IsMatch(output) && SingleThreadCompatible().IsMatch(output),
            "tool-check",
            "The installed tool did not report both supported mappings.");
        await context.Runner.RunAsync(
            "tool-scaffold",
            tool,
            [
                "scaffold",
                "--project",
                inputProject,
                "--method",
                Method,
                "--mapping",
                "elementwise",
                "--output",
                scaffoldRoot
            ]).ConfigureAwait(false);
    }

    private static async Task<string> BuildScaffoldAsync(
        RunContext context,
        string version,
        string tool,
        string scaffoldRoot)
    {
        var projects = Directory.GetFiles(scaffoldRoot, "*.csproj", SearchOption.TopDirectoryOnly);
        Check.That(projects.Length == 1, "tool-scaffold", "The scaffold does not contain exactly one project.");
        var projectSource = await File.ReadAllTextAsync(projects[0]).ConfigureAwait(false);
        Check.That(
            projectSource.Contains(
                $"PackageReference Include=\"Supprocom.CSharp2CUDA\" Version=\"{version}\"",
                StringComparison.Ordinal),
            "tool-scaffold",
            "The scaffold does not use the exact core package version.");
        await context.Runner.DotNetAsync("scaffold-restore", 0, context.RestoreArguments(projects[0], []))
            .ConfigureAwait(false);
        await context.Runner.DotNetAsync(
            "scaffold-build",
            0,
            ["build", projects[0], "--configuration", "Release", "--no-restore"]).ConfigureAwait(false);
        var first = await SingleCudaHashAsync(
            scaffoldRoot,
            "The scaffold build did not create exactly one CUDA file.").ConfigureAwait(false);
        var cudaSource = await File.ReadAllTextAsync(first.Path).ConfigureAwait(false);
        Check.That(
            cudaSource.Contains(
                "extern \"C\" __global__ void cs2cuda_DistanceTransform_Calculate_elementwise_",
                StringComparison.Ordinal)
            && cudaSource.Contains("sqrt(", StringComparison.Ordinal),
            "scaffold-build",
            "The scaffold CUDA source does not contain the selected algorithm.");
        await context.Runner.RunAsync("tool-refresh", tool, ["refresh", "--output", scaffoldRoot])
            .ConfigureAwait(false);
        await context.Runner.DotNetAsync(
            "scaffold-rebuild",
            0,
            ["build", projects[0], "--configuration", "Release", "--no-restore", "-t:Rebuild"])
            .ConfigureAwait(false);
        var second = await SingleCudaHashAsync(
            scaffoldRoot,
            "The refreshed scaffold did not reproduce the same CUDA bytes.").ConfigureAwait(false);
        Check.That(
            string.Equals(first.Hash, second.Hash, StringComparison.Ordinal),
            "scaffold-rebuild",
            "The refreshed scaffold did not reproduce the same CUDA bytes.");
        return first.Hash;
    }

    private static async Task<(string Path, string Hash)> SingleCudaHashAsync(string root, string failure)
    {
        var files = Directory.GetFiles(root, "*.cu", SearchOption.AllDirectories);
        Check.That(files.Length == 1, "scaffold-build", failure);
        return (files[0], await FileHash.Sha256Async(files[0]).ConfigureAwait(false));
    }
}
