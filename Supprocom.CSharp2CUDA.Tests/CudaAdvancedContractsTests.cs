using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaAdvancedContractsTests
{
    [Fact]
    public void Assembly_UsesSupprocomIdentity()
    {
        var assembly = typeof(Cuda).Assembly;

        Assert.Equal("Supprocom.CSharp2CUDA", assembly.GetName().Name);
        Assert.All(assembly.GetExportedTypes(), type => Assert.True(
            type.Namespace?.StartsWith("Supprocom.CSharp2CUDA", StringComparison.Ordinal) == true,
            type.FullName));
    }

    [Fact]
    public void Transpile_EmitsAdvancedCudaContracts()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class AdvancedModule
            {
                [CudaConstant]
                private static readonly int[] RoundOffsets = [1, -2, int.MinValue];

                [CudaGlobal(Name = "advanced_contracts")]
                private static void Run(
                    int* signed32,
                    uint* unsigned32,
                    long* signed64,
                    ulong* unsigned64,
                    double* output)
                {
                    int sharedCount = Cuda.Shared<int>();
                    int* sharedValues = Cuda.SharedArray<int>(8);
                    byte* dynamicStorage = Cuda.DynamicSharedBytes(8);
                    double* dynamicDoubles = Cuda.DynamicSharedView<double>(dynamicStorage, 0UL);
                    int* dynamicIntegers = Cuda.DynamicSharedView<int>(dynamicStorage, 2UL);

                    sharedCount = RoundOffsets[0] + AdvancedModule.RoundOffsets[1];
                    sharedValues[0] = sharedCount;
                    dynamicDoubles[0] = 1.0;
                    dynamicIntegers[0] = 2;

                    Cuda.ThreadFence();
                    Cuda.ThreadFenceSystem();
                    Cuda.SyncWarp();
                    Cuda.SyncWarp(0x0000ffffu);
                    int shuffled = Cuda.ShuffleDownSync(0x0000ffffu, sharedCount, 1u, 16);
                    Cuda.NanoSleep(32u);

                    Cuda.AtomicAdd(ref signed32[0], 0);
                    Cuda.AtomicExchange(ref signed32[0], shuffled);
                    Cuda.AtomicCompareExchange(ref signed32[0], 0, 1);
                    Cuda.AtomicXor(ref signed32[0], 2);
                    Cuda.AtomicMin(ref signed32[0], 3);

                    Cuda.AtomicAdd(ref unsigned32[0], 0u);
                    Cuda.AtomicExchange(ref unsigned32[0], 1u);
                    Cuda.AtomicCompareExchange(ref unsigned32[0], 1u, 2u);
                    Cuda.AtomicXor(ref unsigned32[0], 3u);
                    Cuda.AtomicMin(ref unsigned32[0], 4u);

                    Cuda.AtomicAdd(ref signed64[0], 0L);
                    Cuda.AtomicExchange(ref signed64[0], 1L);
                    Cuda.AtomicCompareExchange(ref signed64[0], 1L, 2L);
                    Cuda.AtomicXor(ref signed64[0], 3L);
                    Cuda.AtomicMin(ref signed64[0], 4L);

                    Cuda.AtomicAdd(ref unsigned64[0], 0UL);
                    Cuda.AtomicExchange(ref unsigned64[0], 1UL);
                    Cuda.AtomicCompareExchange(ref unsigned64[0], 1UL, 2UL);
                    Cuda.AtomicXor(ref unsigned64[0], 3UL);
                    Cuda.AtomicMin(ref unsigned64[0], 4UL);

                    double exact = Cuda.DoubleAddRoundNearest(
                        Cuda.DoubleSubtractRoundNearest(5.0, 2.0),
                        Cuda.DoubleDivideRoundNearest(
                            Cuda.DoubleMultiplyRoundNearest(3.0, 4.0),
                            2.0));
                    output[0] = exact + Cuda.Log1p(0.5) + Cuda.Sqrt(4.0) +
                        Cuda.Exp(1.0) + Cuda.Pow(2.0, 3.0) + Cuda.NaN();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "__device__ __constant__ int RoundOffsets[3] = { 1, -2, (-2147483647 - 1) };",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("__shared__ int sharedCount;", result.Source, StringComparison.Ordinal);
        Assert.Contains("__shared__ int sharedValues[8];", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "extern __shared__ __align__(8) unsigned char dynamicStorage[];",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "((double*)(dynamicStorage))+(0ull)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("__threadfence();", result.Source, StringComparison.Ordinal);
        Assert.Contains("__threadfence_system();", result.Source, StringComparison.Ordinal);
        Assert.Contains("__syncwarp(0x0000ffffu);", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "__shfl_down_sync(0x0000ffffu, sharedCount, 1u, 16)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("__nanosleep(32u);", result.Source, StringComparison.Ordinal);
        Assert.Contains("atomicCAS(&signed32[0], 0, 1);", result.Source, StringComparison.Ordinal);
        Assert.Contains("atomicXor(&unsigned64[0], 3ull);", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_i64_from_bits(atomicAdd((unsigned long long*)(&signed64[0]), (unsigned long long)(0LL)))",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("atomicMin(&signed64[0], 4LL);", result.Source, StringComparison.Ordinal);
        Assert.Contains("__dadd_rn(", result.Source, StringComparison.Ordinal);
        Assert.Contains("__dsub_rn(", result.Source, StringComparison.Ordinal);
        Assert.Contains("__dmul_rn(", result.Source, StringComparison.Ordinal);
        Assert.Contains("__ddiv_rn(", result.Source, StringComparison.Ordinal);
        Assert.Contains("log1p(0.5)", result.Source, StringComparison.Ordinal);
        Assert.Contains("sqrt(4.0)", result.Source, StringComparison.Ordinal);
        Assert.Contains("exp(1.0)", result.Source, StringComparison.Ordinal);
        Assert.Contains("pow(2.0, 3.0)", result.Source, StringComparison.Ordinal);
        Assert.Contains("nan(\"\")", result.Source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void Transpile_RejectsInvalidAdvancedContract(string statements, string diagnosticId)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InvalidModule
            {
                [CudaGlobal]
                private static void Run(byte* bytes, int runtimeValue)
                {
                    {{statements}}
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Theory]
    [MemberData(nameof(InvalidConstantSources))]
    public void Transpile_RejectsInvalidConstantArray(string declaration, string diagnosticId)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InvalidModule
            {
                {{declaration}}

                [CudaGlobal]
                private static void Run()
                {
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Theory]
    [MemberData(nameof(ConstantMutationSources))]
    public void Transpile_RejectsConstantArrayMutation(string statement)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InvalidModule
            {
                [CudaConstant]
                private static readonly int[] Values = [1];

                [CudaGlobal]
                private static void Run()
                {
                    {{statement}}
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);
        var errors = result.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.False(result.Succeeded);
        Assert.Single(errors);
        Assert.Equal("CS2CUDA020", errors[0].Id);
        Assert.Empty(result.Source);
    }

    [Fact]
    public void Transpile_RejectsSharedStorageInDeviceFunction()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InvalidModule
            {
                [CudaDevice]
                private static int Run()
                {
                    int value = Cuda.Shared<int>();
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA013");
    }

    [Fact]
    public void ManagedExactAndNamedMathContracts_KeepCSharpSemantics()
    {
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(0.1 + 0.2),
            BitConverter.DoubleToUInt64Bits(Cuda.DoubleAddRoundNearest(0.1, 0.2)));
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(0.1 - 0.2),
            BitConverter.DoubleToUInt64Bits(Cuda.DoubleSubtractRoundNearest(0.1, 0.2)));
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(0.1 * 0.2),
            BitConverter.DoubleToUInt64Bits(Cuda.DoubleMultiplyRoundNearest(0.1, 0.2)));
        Assert.Equal(
            BitConverter.DoubleToUInt64Bits(0.1 / 0.2),
            BitConverter.DoubleToUInt64Bits(Cuda.DoubleDivideRoundNearest(0.1, 0.2)));
        Assert.Equal(double.LogP1(0.5), Cuda.Log1p(0.5));
        Assert.Equal(Math.Sqrt(4.0), Cuda.Sqrt(4.0));
        Assert.Equal(Math.Exp(1.0), Cuda.Exp(1.0));
        Assert.Equal(Math.Pow(2.0, 3.0), Cuda.Pow(2.0, 3.0));
        Assert.True(double.IsNaN(Cuda.NaN()));
    }

    [Fact]
    public void ManagedGpuOnlyContracts_RejectExecution()
    {
        Assert.Throws<InvalidOperationException>(Cuda.ThreadFence);
        Assert.Throws<InvalidOperationException>(Cuda.ThreadFenceSystem);
        Assert.Throws<InvalidOperationException>(Cuda.SyncWarp);
        Assert.Throws<InvalidOperationException>(() => Cuda.SyncWarp(1u));
        Assert.Throws<InvalidOperationException>(() => Cuda.NanoSleep(1u));
        Assert.Throws<InvalidOperationException>(() => Cuda.Shared<int>());
    }

    public static TheoryData<string, string> InvalidSources => new()
    {
        { "decimal value = Cuda.Shared<decimal>();", "CS2CUDA014" },
        { "decimal* values = Cuda.SharedArray<decimal>(8);", "CS2CUDA014" },
        { "int* values = Cuda.SharedArray<int>(0);", "CS2CUDA013" },
        { "int* values = Cuda.SharedArray<int>(runtimeValue);", "CS2CUDA013" },
        { "byte* storage = Cuda.DynamicSharedBytes(3);", "CS2CUDA015" },
        { "byte* storage = Cuda.DynamicSharedBytes(runtimeValue);", "CS2CUDA015" },
        {
            "byte* first = Cuda.DynamicSharedBytes(8); byte* second = Cuda.DynamicSharedBytes(8);",
            "CS2CUDA013"
        },
        { "double* values = Cuda.DynamicSharedView<double>(bytes, 0UL);", "CS2CUDA015" },
        {
            "byte* storage = Cuda.DynamicSharedBytes(4); double* values = Cuda.DynamicSharedView<double>(storage, 0UL);",
            "CS2CUDA015"
        },
        {
            "byte* storage = Cuda.DynamicSharedBytes(8); decimal* values = Cuda.DynamicSharedView<decimal>(storage, 0UL);",
            "CS2CUDA014"
        },
        { "Cuda.SyncWarp(0u);", "CS2CUDA017" },
        { "Cuda.SyncWarp((uint)runtimeValue);", "CS2CUDA017" },
        { "_ = Cuda.ShuffleDownSync(0u, runtimeValue, 1u, 16);", "CS2CUDA017" },
        { "_ = Cuda.ShuffleDownSync(1u, runtimeValue, 1u, 3);", "CS2CUDA018" },
        { "_ = Cuda.ShuffleDownSync(1u, runtimeValue, 1u, runtimeValue);", "CS2CUDA018" },
        { "short value = 0; _ = Cuda.AtomicAdd(ref value, (short)1);", "CS2CUDA019" },
        { "short value = 0; _ = Cuda.AtomicExchange(ref value, (short)1);", "CS2CUDA019" },
        {
            "short value = 0; _ = Cuda.AtomicCompareExchange(ref value, (short)0, (short)1);",
            "CS2CUDA019"
        },
        { "short value = 0; _ = Cuda.AtomicXor(ref value, (short)1);", "CS2CUDA019" },
        { "short value = 0; _ = Cuda.AtomicMin(ref value, (short)1);", "CS2CUDA019" }
    };

    public static TheoryData<string, string> InvalidConstantSources => new()
    {
        { "[CudaConstant] private static int[] Values = [1];", "CS2CUDA013" },
        { "[CudaConstant] private static readonly double[] Values = [1.0];", "CS2CUDA014" },
        { "[CudaConstant] private static readonly int[] Values = [];", "CS2CUDA016" },
        {
            "[CudaConstant] private static readonly int[] Values = [GetValue()]; private static int GetValue() => 1;",
            "CS2CUDA016"
        }
    };

    public static TheoryData<string> ConstantMutationSources => new()
    {
        "Values[0] = 2;",
        "InvalidModule.Values[0] += 2;",
        "++Values[0];",
        "Values[0]++;",
        "--Values[0];",
        "Values[0]--;",
        "Cuda.AtomicAdd(ref Values[0], 1);",
        "Cuda.AtomicExchange(ref Values[0], 1);",
        "Cuda.AtomicCompareExchange(ref Values[0], 0, 1);",
        "Cuda.AtomicXor(ref Values[0], 1);",
        "Cuda.AtomicMin(ref Values[0], 1);"
    };

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}
