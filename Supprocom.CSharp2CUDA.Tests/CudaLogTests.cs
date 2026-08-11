using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaLogTests
{
    [Fact]
    public void Cuda_LogHasOneExactPublicContract()
    {
        var methods = typeof(Cuda).GetMethods()
            .Where(method => method.Name == nameof(Cuda.Log))
            .ToArray();

        var method = Assert.Single(methods);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(double), method.ReturnType);
        Assert.Equal(typeof(double), Assert.Single(method.GetParameters()).ParameterType);
    }

    [Theory]
    [MemberData(nameof(RequiredLogInputs))]
    public void ManagedLog_MatchesMathLog(double value)
    {
        AssertPortableResult(Math.Log(value), Cuda.Log(value));
    }

    [Fact]
    public void Transpile_EmitsDirectLogForExactCudaSymbol()
    {
        var result = CudaTestCompiler.Transpile(GeneratedProbeSource);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("output[0] = log(value);", result.Source, StringComparison.Ordinal);
        Assert.Contains("log(1.01)", result.Source, StringComparison.Ordinal);
        Assert.Contains("log(0.99)", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Cuda.Log", result.Source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidLogSources))]
    public void Transpile_RejectsInvalidLogUse(string source, string diagnosticId)
    {
        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [CudaFact]
    public void Nvrtc_CompilesGeneratedLogAndWeeklyFormulaModule()
    {
        using var runtime = CreateRuntime();
        Assert.NotNull(runtime);
    }

    [CudaFact]
    public void Cuda_LogMatchesDirectHandwrittenOracle()
    {
        using var runtime = CreateRuntime();
        var output = runtime.Allocate<double>(new double[2]);
        try
        {
            foreach (var value in GetRequiredLogValues())
            {
                runtime.Launch(
                    "transpiled_log",
                    1,
                    1,
                    0,
                    BitConverter.DoubleToUInt64Bits(value),
                    output);
                runtime.Launch(
                    "handwritten_log",
                    1,
                    1,
                    0,
                    BitConverter.DoubleToUInt64Bits(value),
                    output + sizeof(double));

                var actual = runtime.Read<double>(output, 2);
                AssertPortableResult(actual[1], actual[0]);
            }
        }
        finally
        {
            runtime.Free(output);
        }
    }

    [CudaFact]
    public void Cuda_WeeklyProfitFormulaMatchesDirectHandwrittenOracleBits()
    {
        using var runtime = CreateRuntime();
        var output = runtime.Allocate<double>(new double[2]);
        try
        {
            foreach (var (wins, losses) in WeeklyProfitCases)
            {
                runtime.Launch(
                    "transpiled_week_profit",
                    1,
                    1,
                    0,
                    (ulong)(uint)wins,
                    (ulong)(uint)losses,
                    output);
                runtime.Launch(
                    "handwritten_week_profit",
                    1,
                    1,
                    0,
                    (ulong)(uint)wins,
                    (ulong)(uint)losses,
                    output + sizeof(double));

                var actual = runtime.Read<double>(output, 2);
                AssertPortableResult(actual[1], actual[0]);
            }
        }
        finally
        {
            runtime.Free(output);
        }
    }

    public static TheoryData<double> RequiredLogInputs => new()
    {
        1.01,
        0.99,
        0.5,
        1.0,
        2.0,
        -1.0,
        double.Epsilon,
        0.0,
        BitConverter.UInt64BitsToDouble(0x8000000000000000UL),
        double.PositiveInfinity,
        double.NegativeInfinity,
        BitConverter.UInt64BitsToDouble(0x7ff8000000001234UL)
    };

    public static TheoryData<string, string> InvalidLogSources => new()
    {
        {
            """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static double Run()
                {
                    return Cuda.Log("1.01");
                }
            }
            """,
            "CS1503"
        },
        {
            """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaConstant]
                private static readonly int[] Values = [(int)Cuda.Log(1.01)];

                [CudaDevice]
                private static int Run()
                {
                    return Values[0];
                }
            }
            """,
            "CS2CUDA016"
        },
        {
            """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static double Run(double value) => Cuda.Log(value);
            }
            """,
            "CS2CUDA005"
        },
        {
            """
            using Supprocom.CSharp2CUDA;

            internal static class CudaLookalike
            {
                public static double Log(double value)
                {
                    return value;
                }
            }

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static double Run(double value)
                {
                    return CudaLookalike.Log(value);
                }
            }
            """,
            "CS2CUDA006"
        }
    };

    private static readonly (int Wins, int Losses)[] WeeklyProfitCases =
    [
        (0, 0),
        (1, 0),
        (0, 1),
        (1, 1),
        (7, 3),
        (3, 7),
        (4, 0),
        (0, 4),
        (120, 0),
        (0, 120),
        (360, 0),
        (0, 360),
        (336, 336),
        (672, 0),
        (0, 672),
        (671, 1),
        (1, 671),
        (int.MaxValue, 0),
        (0, int.MaxValue)
    ];

    private static IEnumerable<double> GetRequiredLogValues()
    {
        foreach (var value in RequiredLogInputs)
            yield return value;
    }

    private static CudaTestRuntime CreateRuntime()
    {
        var result = CudaTestCompiler.Transpile(GeneratedProbeSource);
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        return CudaTestRuntime.Create(result.Source + Environment.NewLine + HandwrittenOracleSource);
    }

    private static void AssertPortableResult(double expected, double actual)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual));
            return;
        }

        Assert.Equal(double.IsFinite(expected), double.IsFinite(actual));
        Assert.Equal(double.IsPositiveInfinity(expected), double.IsPositiveInfinity(actual));
        Assert.Equal(double.IsNegativeInfinity(expected), double.IsNegativeInfinity(actual));
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(expected),
            BitConverter.DoubleToUInt64Bits(actual));
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));

    private const string GeneratedProbeSource = """
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class LogModule
        {
            [CudaGlobal(Name = "transpiled_log")]
            private static void LogValue(double value, double* output)
            {
                output[0] = Cuda.Log(value);
            }

            [CudaGlobal(Name = "transpiled_week_profit")]
            private static void WeekProfit(int wins, int losses, double* output)
            {
                output[0] = Cuda.Exp(
                    ((double)wins) * Cuda.Log(1.01) +
                    ((double)losses) * Cuda.Log(0.99)) * 10000.0 - 10000.0;
            }
        }
        """;

    private const string HandwrittenOracleSource = """
        extern "C" __global__ void handwritten_log(double value, double* output)
        {
            output[0] = log(value);
        }

        extern "C" __global__ void handwritten_week_profit(
            int wins,
            int losses,
            double* output)
        {
            output[0] = exp(
                ((double)wins) * log(1.01) +
                ((double)losses) * log(0.99)) * 10000.0 - 10000.0;
        }
        """;
}
