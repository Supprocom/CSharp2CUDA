using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaSourceMappingTests
{
    [Fact]
    public void Transpile_ReportsEntryPointsAndNormalizedSourceMappings()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output, int value)
                {
                    output[0] = value + 1;
                }
            }
            """;
        var sourceRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "csharp2cuda-source-map"));
        var sourcePath = Path.Combine(sourceRoot, "Algorithms", "Kernel.cs");

        var result = CudaTestCompiler.Transpile(
            source,
            new CudaTranspilationOptions { SourceRoot = sourceRoot },
            sourcePath);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        var entryPoint = Assert.Single(result.EntryPoints);
        Assert.Contains("KernelModule.Run", entryPoint.ManagedName, StringComparison.Ordinal);
        Assert.Equal("run", entryPoint.CudaName);
        Assert.NotEmpty(result.SourceMap);
        Assert.All(result.SourceMap, entry =>
            Assert.Equal("Algorithms/Kernel.cs", entry.SourcePath));
        Assert.Contains("\"Algorithms/Kernel.cs\"", result.Source, StringComparison.Ordinal);

        var sourceLine = source.Split('\n')
            .Select((line, index) => (line, index))
            .Single(item => item.line.Contains("output[0]", StringComparison.Ordinal))
            .index + 1;
        var generatedLines = result.Source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        Assert.Contains(result.SourceMap, entry =>
            entry.SourceLine == sourceLine &&
            entry.GeneratedLine > 0 &&
            entry.GeneratedLine <= generatedLines.Length &&
            generatedLines[entry.GeneratedLine - 1].Contains("output", StringComparison.Ordinal));
    }

    [Fact]
    public void Transpile_PreservesSourceMapWhenLineDirectivesAreDisabled()
    {
        var result = CudaTestCompiler.Transpile(
            MinimalKernel,
            new CudaTranspilationOptions { EmitLineDirectives = false });

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.DoesNotContain("#line", result.Source, StringComparison.Ordinal);
        Assert.NotEmpty(result.SourceMap);
        Assert.Single(result.EntryPoints);
    }

    [Fact]
    public void Transpile_SourceMapDoesNotDependOnNewLineStyle()
    {
        var lineFeed = CudaTestCompiler.Transpile(
            MinimalKernel,
            new CudaTranspilationOptions { NewLine = "\n" });
        var carriageReturn = CudaTestCompiler.Transpile(
            MinimalKernel,
            new CudaTranspilationOptions { NewLine = "\r\n" });

        Assert.True(lineFeed.Succeeded, FormatDiagnostics(lineFeed));
        Assert.True(carriageReturn.Succeeded, FormatDiagnostics(carriageReturn));
        Assert.Equal(lineFeed.EntryPoints.ToArray(), carriageReturn.EntryPoints.ToArray());
        Assert.Equal(lineFeed.SourceMap.ToArray(), carriageReturn.SourceMap.ToArray());
    }

    [Fact]
    public void Transpile_FailureReturnsNoEntryPointsOrSourceMap()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class KernelModule
            {
                [CudaGlobal]
                private static object Run() => new object();
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Empty(result.EntryPoints);
        Assert.Empty(result.SourceMap);
    }

    [Fact]
    public void Transpile_RejectsRelativeSourceRoot()
    {
        var compilation = CudaTestCompiler.CreateCompilation(MinimalKernel);

        var exception = Assert.Throws<ArgumentException>(() => CudaTranspiler.Transpile(
            compilation,
            new CudaTranspilationOptions { SourceRoot = "relative" }));

        Assert.Contains("fully qualified", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_OutputDoesNotDependOnSyntaxTreeOrder()
    {
        const string firstSource = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class FirstKernel
            {
                [CudaGlobal(Name = "first")]
                private static void Run(int* output) => output[0] = 1;
            }
            """;
        const string secondSource = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class SecondKernel
            {
                [CudaGlobal(Name = "second")]
                private static void Run(int* output) => output[0] = 2;
            }
            """;
        var firstTree = CSharpSyntaxTree.ParseText(firstSource, path: "first.cs");
        var secondTree = CSharpSyntaxTree.ParseText(secondSource, path: "second.cs");
        var template = CudaTestCompiler.CreateCompilation(string.Empty);
        var forward = template.RemoveAllSyntaxTrees().AddSyntaxTrees(firstTree, secondTree);
        var reverse = template.RemoveAllSyntaxTrees().AddSyntaxTrees(secondTree, firstTree);

        var first = CudaTestCompiler.Transpile(forward);
        var second = CudaTestCompiler.Transpile(reverse);

        Assert.True(first.Succeeded, FormatDiagnostics(first));
        Assert.True(second.Succeeded, FormatDiagnostics(second));
        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.EntryPoints.ToArray(), second.EntryPoints.ToArray());
        Assert.Equal(first.SourceMap.ToArray(), second.SourceMap.ToArray());
    }

    private const string MinimalKernel = """
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class KernelModule
        {
            [CudaGlobal(Name = "run")]
            private static void Run(int* output)
            {
                output[0] = 1;
            }
        }
        """;

    private static string FormatDiagnostics(CudaTranspilationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.ToString()));
}
