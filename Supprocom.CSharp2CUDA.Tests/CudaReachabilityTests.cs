using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaReachabilityTests
{
    [Fact]
    public void TranspileInfersSameClassSourceHelper()
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
    public void TranspileInfersCrossClassSourceHelper()
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
    public void TranspileUsesGeneratedSourceHelper()
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
    public void TranspileEmitsOnlyTheReachedOverload()
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
    public void TranspileIgnoresUnreachableHostMethod()
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
    public void TranspileKeepsExplicitUncalledDeviceExport()
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
    public void TranspileRejectsRecursiveReachableMethods()
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
#pragma warning disable HLQ005 // Exactly one element is a validation invariant; First would silently accept duplicates.
        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => string.Equals(item.Id, "CS2CUDA027", StringComparison.Ordinal));
#pragma warning restore HLQ005
        Assert.Equal("run", diagnostic.Properties["RootSymbol"]);
        Assert.Contains("ExistingAlgorithm.First", diagnostic.Properties["CallPath"], StringComparison.Ordinal);
        Assert.Contains("iterative", diagnostic.Properties["SuggestedReplacement"], StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileReportsThePathToUnsupportedMetadataMethod()
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
#pragma warning disable HLQ005 // Exactly one element is a validation invariant; First would silently accept duplicates.
        var diagnostic = Assert.Single(result.Diagnostics, item => string.Equals(item.Id, "CS2CUDA026", StringComparison.Ordinal));
#pragma warning restore HLQ005
        Assert.Contains("run", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("ExistingAlgorithm.Calculate", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("System.Math.Log", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal("run", diagnostic.Properties["RootSymbol"]);
        Assert.Contains("System.Math.Log", diagnostic.Properties["FailingSymbol"], StringComparison.Ordinal);
        Assert.Contains("ExistingAlgorithm.Calculate", diagnostic.Properties["CallPath"], StringComparison.Ordinal);
        Assert.Contains("outside", diagnostic.Properties["SuggestedReplacement"], StringComparison.Ordinal);
    }

    private static string FormatDiagnostics(CudaTranspilationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
}
