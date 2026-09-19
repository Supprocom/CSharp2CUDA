using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaReachabilityTests
{
    [Fact]
    public void Transpile_InfersSameClassSourceHelper()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output)
                {
                    output[0] = Double(4);
                }

                private static int Double(int value)
                {
                    return value * 2;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.Contains("__device__ int cs2cuda_", result.Source, StringComparison.Ordinal);
        Assert.Contains("return csharp2cuda_i32_mul(value, 2);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_InfersCrossClassSourceHelper()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Double(int value)
                {
                    return value * 2;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output)
                {
                    output[0] = ExistingAlgorithm.Double(4);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.Contains("__device__ int cs2cuda_", result.Source, StringComparison.Ordinal);
        Assert.Contains("return csharp2cuda_i32_mul(value, 2);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_UsesGeneratedSourceHelper()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output)
                {
                    output[0] = GeneratedAlgorithm.Double(4);
                }
            }
            """;
        const string generated = """
            internal static class GeneratedAlgorithm
            {
                public static int Double(int value)
                {
                    return value * 2;
                }
            }
            """;
        var compilation = CudaTestCompiler.CreateCompilation(source);
        var generatedTree = CSharpSyntaxTree.ParseText(
            SourceText.From(generated, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp14),
            "GeneratedAlgorithm.g.cs");

        var result = CudaTestCompiler.Transpile(compilation.AddSyntaxTrees(generatedTree));

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.Contains("__device__ int cs2cuda_", result.Source, StringComparison.Ordinal);
        Assert.Contains("return csharp2cuda_i32_mul(value, 2);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsOnlyTheReachedOverload()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Score(int value)
                {
                    return value + 1;
                }

                public static double Score(double value)
                {
                    return value + 1.0;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output)
                {
                    output[0] = ExistingAlgorithm.Score(4);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.Contains("return csharp2cuda_i32_add(value, 1);", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("__device__ double cs2cuda_", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_IgnoresUnreachableHostMethod()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output)
                {
                    output[0] = 1;
                }

                private static string Normalize(string value)
                {
                    return value.Trim();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.DoesNotContain("Normalize", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Trim", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_KeepsExplicitUncalledDeviceExport()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class KernelModule
            {
                [CudaDevice(Name = "exported_value")]
                public static int ExportedValue()
                {
                    return 7;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.Contains("__device__ int exported_value();", result.Source, StringComparison.Ordinal);
        Assert.Contains("__device__ int exported_value()", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsRecursiveReachableMethods()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int First(int value)
                {
                    return Second(value);
                }

                private static int Second(int value)
                {
                    return value == 0 ? 0 : First(value - 1);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(int* output)
                {
                    output[0] = ExistingAlgorithm.First(4);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == "CS2CUDA027");
        Assert.Equal("run", diagnostic.Properties["RootSymbol"]);
        Assert.Contains("ExistingAlgorithm.First", diagnostic.Properties["CallPath"]);
        Assert.Contains("iterative", diagnostic.Properties["SuggestedReplacement"]);
    }

    [Fact]
    public void Transpile_ReportsThePathToUnsupportedMetadataMethod()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static double Calculate(double value)
                {
                    return Math.Log(value, 2.0);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class KernelModule
            {
                [CudaGlobal(Name = "run")]
                private static void Run(double* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(1.0);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "CS2CUDA026");
        Assert.Contains("run", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("ExistingAlgorithm.Calculate", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("System.Math.Log", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("run", diagnostic.Properties["RootSymbol"]);
        Assert.Contains("System.Math.Log", diagnostic.Properties["FailingSymbol"]);
        Assert.Contains("ExistingAlgorithm.Calculate", diagnostic.Properties["CallPath"]);
        Assert.Contains("outside", diagnostic.Properties["SuggestedReplacement"]);
    }

    private static string FormatDiagnostics(CudaTranspilationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
}
