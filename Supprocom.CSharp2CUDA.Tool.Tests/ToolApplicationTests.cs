using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

[Collection(ToolSuite.Name)]
public sealed class ToolApplicationTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "csharp2cuda-tool-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckReportsCompatibleMappingsForOrdinaryMethod()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);

        var result = await RunAsync(
            "check",
            "--project",
            project,
            "--method",
            "Algorithms.Arithmetic.Twice(int)");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("elementwise   Compatible", result.Output, StringComparison.Ordinal);
        Assert.Contains("single-thread Compatible", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task ScaffoldCreatesNormalProjectWithoutChangingSource()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);
        var sourcePath = Path.Combine(Path.GetDirectoryName(project)!, "Algorithm.cs");
        var sourceHash = ComputeHash(sourcePath);
        var output = Path.Combine(testRoot, "scaffold");

        var result = await RunAsync(
            "scaffold",
            "--project",
            project,
            "--method",
            "Algorithms.Arithmetic.Twice(int)",
            "--mapping",
            "elementwise",
            "--output",
            output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(sourceHash, ComputeHash(sourcePath));
        var projectText = (await File.ReadAllTextAsync(Path.Combine(output, "Algorithms.Cuda.csproj")).ConfigureAwait(true));
        var adapter = (await File.ReadAllTextAsync(Path.Combine(output, "CudaAdapter.cs")).ConfigureAwait(true));
        Assert.Contains(
            "PackageReference Include=\"Supprocom.CSharp2CUDA\" Version=\"0.3.2\"",
            projectText,
            StringComparison.Ordinal);
        Assert.Contains(
            Path.Combine("Imported", "Algorithm.cs"),
            projectText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Program Files", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Algorithms.Arithmetic.Twice", adapter, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "csharp2cuda.json")));
    }

    [Fact]
    public async Task RefreshUpdatesLinksWhenOwnedFilesAreUnchanged()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);
        var output = Path.Combine(testRoot, "scaffold");
        var create = await ScaffoldAsync(project, output);
        Assert.Equal(0, create.ExitCode);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(project)!, "Helper.cs"),
            "namespace Algorithms; internal static class Helper { public static int One => 1; }\n",
            new UTF8Encoding(false)).ConfigureAwait(true);

        var refresh = await RunAsync("refresh", "--output", output).ConfigureAwait(true);

        Assert.Equal(0, refresh.ExitCode);
        Assert.Contains(
            Path.Combine("Imported", "Helper.cs"),
            (await File.ReadAllTextAsync(Path.Combine(output, "Algorithms.Cuda.csproj")).ConfigureAwait(true)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshRefusesAChangedOwnedFile()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);
        var output = Path.Combine(testRoot, "scaffold");
        var create = await ScaffoldAsync(project, output);
        Assert.Equal(0, create.ExitCode);
        await File.AppendAllTextAsync(
            Path.Combine(output, "CudaAdapter.cs"),
            "// user change\n",
            new UTF8Encoding(false)).ConfigureAwait(true);

        var refresh = await RunAsync("refresh", "--output", output).ConfigureAwait(true);

        Assert.Equal(2, refresh.ExitCode);
        Assert.Contains("CS2CUDA114", refresh.Error, StringComparison.Ordinal);
        Assert.Contains(
            "// user change",
            (await File.ReadAllTextAsync(Path.Combine(output, "CudaAdapter.cs")).ConfigureAwait(true)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScaffoldRefusesNonemptyOutputAndPreservesUserFile()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);
        var output = Path.Combine(testRoot, "scaffold");
        Directory.CreateDirectory(output);
        var userFile = Path.Combine(output, "keep.txt");
        await File.WriteAllTextAsync(userFile, "keep\n", new UTF8Encoding(false)).ConfigureAwait(true);

        var result = await ScaffoldAsync(project, output).ConfigureAwait(true);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("CS2CUDA112", result.Error, StringComparison.Ordinal);
        Assert.Equal("keep\n", (await File.ReadAllTextAsync(userFile).ConfigureAwait(true)));
    }

    [Fact]
    public async Task CheckAcceptsArrayAlgorithmOnlyForSingleThreadMapping()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Statistics
            {
                public static int Sum(int[] values)
                {
                    var result = 0;
                    for (var index = 0; index < values.Length; index++)
                        result += values[index];
                    return result;
                }
            }
            """);

        var result = await RunAsync(
            "check",
            "--project",
            project,
            "--method",
            "Algorithms.Statistics.Sum(int[])");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("elementwise   Not supported", result.Output, StringComparison.Ordinal);
        Assert.Contains("single-thread Compatible", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckUsesSourceGeneratorOutputFromAcceptedCompilation()
    {
        var project = CreateProject("""
            using System.Text.RegularExpressions;

            namespace Algorithms;

            public static partial class GeneratedSupport
            {
                [GeneratedRegex("^[a-z]+$")]
                private static partial Regex Pattern();

                public static bool Matches(string value) => Pattern().IsMatch(value);
            }

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);

        var result = await RunAsync(
            "check",
            "--project",
            project,
            "--method",
            "Algorithms.Arithmetic.Twice(int)");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("CS2CUDA103", result.Error, StringComparison.Ordinal);
        Assert.Contains("elementwise   Compatible", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshRejectsManifestPathTraversal()
    {
        var project = CreateProject("""
            namespace Algorithms;

            public static class Arithmetic
            {
                public static int Twice(int value) => value * 2;
            }
            """);
        var output = Path.Combine(testRoot, "scaffold");
        var create = await ScaffoldAsync(project, output);
        Assert.Equal(0, create.ExitCode);
        var manifestPath = Path.Combine(output, "csharp2cuda.json");
        var manifest = (await File.ReadAllTextAsync(manifestPath).ConfigureAwait(true))
            .Replace(
                "\"path\": \"CudaAdapter.cs\"",
                "\"path\": \"../outside.cs\"",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(manifestPath, manifest, new UTF8Encoding(false)).ConfigureAwait(true);

        var refresh = await RunAsync("refresh", "--output", output).ConfigureAwait(true);

        Assert.Equal(2, refresh.ExitCode);
        Assert.Contains("CS2CUDA113", refresh.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(testRoot, "outside.cs")));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
            Directory.Delete(testRoot, recursive: true);
    }

    private string CreateProject(string source)
    {
        var directory = Path.Combine(testRoot, "source");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Algorithms.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """ + "\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(directory, "Algorithm.cs"),
            source + "\n",
            new UTF8Encoding(false));
        return projectPath;
    }

    private static Task<ToolRunResult> ScaffoldAsync(string project, string output) => RunAsync(
        "scaffold",
        "--project",
        project,
        "--method",
        "Algorithms.Arithmetic.Twice(int)",
        "--mapping",
        "elementwise",
        "--output",
        output);

    private static async Task<ToolRunResult> RunAsync(params string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ToolApplication.RunAsync(
            arguments,
            output,
            error,
            timeout.Token).ConfigureAwait(true);
        return new ToolRunResult(exitCode, output.ToString(), error.ToString());
    }

    private static string ComputeHash(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path)));
}
