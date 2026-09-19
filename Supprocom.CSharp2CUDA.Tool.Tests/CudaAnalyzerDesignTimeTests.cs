using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Supprocom.CSharp2CUDA.Compiler;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

public sealed class CudaAnalyzerDesignTimeTests
{
    [Fact]
    public async Task DesignTimeBuild_ReportsCudaSemanticErrorsWithoutPayload()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class Kernel
            {
                [CudaGlobal]
                private static object Run() => new object();
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "CS2CUDA007");
        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "CS2CUDA023");
    }

    [Fact]
    public async Task DesignTimeBuild_ValidatesValidMarkerWithoutRequiringPayloadPath()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class Kernel
            {
                [CudaGlobal]
                private static void Run(int* output)
                {
                    output[0] = 1;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp14),
            Path.Combine(Path.GetTempPath(), "DesignTimeKernel.cs"));
        var references = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
                references[path] = MetadataReference.CreateFromFile(path);
        }
        references[typeof(Cuda).Assembly.Location] =
            MetadataReference.CreateFromFile(typeof(Cuda).Assembly.Location);
        var compilation = CSharpCompilation.Create(
            "DesignTimeInput",
            [tree],
            references.Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true));
        var globalOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.SupprocomCSharp2CUDAEnabled"] = "true",
            ["build_property.TranspileToCUDA"] = "false",
            ["build_property.DesignTimeBuild"] = "true",
            ["build_property.IsCrossTargetingBuild"] = "false",
            ["build_property.SupprocomCSharp2CUDASourceRoot"] = Path.GetTempPath()
        };
        var analyzerOptions = new AnalyzerOptions(
            [],
            new TestAnalyzerConfigOptionsProvider(globalOptions));
        return await compilation.WithAnalyzers(
                [new CudaTranspilationAnalyzer()],
                analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();
    }
}

internal sealed class TestAnalyzerConfigOptionsProvider(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions global = new TestAnalyzerConfigOptions(values);

    public override AnalyzerConfigOptions GlobalOptions => global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        TestAnalyzerConfigOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        TestAnalyzerConfigOptions.Empty;
}

internal sealed class TestAnalyzerConfigOptions(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public static TestAnalyzerConfigOptions Empty { get; } = new(
        new Dictionary<string, string>());

    public override bool TryGetValue(string key, out string value) =>
        values.TryGetValue(key, out value!);
}
