using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CSharp2CUDA.Tests;

public sealed class CudaTranspilerTests
{
    [Theory]
    [InlineData("Advanced")]
    [InlineData("Complex")]
    [InlineData("DeviceDispatch")]
    [InlineData("Geometry")]
    [InlineData("Graph")]
    [InlineData("Matrix")]
    [InlineData("Probability")]
    [InlineData("Scalar")]
    [InlineData("SequencePath")]
    [InlineData("Statistics")]
    [InlineData("Transport")]
    [InlineData("Vector")]
    public void Transpile_ProducesExactCatalog(string catalog)
    {
        var goldenDirectory = Path.Combine(AppContext.BaseDirectory, "Golden");
        var source = File.ReadAllText(
            Path.Combine(goldenDirectory, $"{catalog}Module.cs.txt"));
        var expected = File.ReadAllText(
            Path.Combine(goldenDirectory, $"{catalog}CudaBlockCatalog.cu"));

        var result = CudaTranspiler.Transpile(source, path: $"{catalog}Module.cs");

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal(expected, result.Source);
    }

    [Fact]
    public void Transpile_ProducesExactReferenceDeviceModule()
    {
        (string Catalog, string EntryPoint)[] catalogs =
        [
            ("Scalar", "mathblocks_scalar"),
            ("Vector", "mathblocks_vector"),
            ("Complex", "mathblocks_complex"),
            ("Matrix", "mathblocks_matrix"),
            ("Probability", "mathblocks_probability"),
            ("SequencePath", "mathblocks_sequence_path"),
            ("Statistics", "mathblocks_statistics"),
            ("Geometry", "mathblocks_geometry"),
            ("Graph", "mathblocks_graph"),
            ("Advanced", "mathblocks_advanced"),
            ("Transport", "mathblocks_transport")
        ];
        var goldenDirectory = Path.Combine(AppContext.BaseDirectory, "Golden");
        var builder = new StringBuilder();

        foreach (var (catalog, entryPoint) in catalogs)
        {
            var source = File.ReadAllText(
                Path.Combine(goldenDirectory, $"{catalog}Module.cs.txt"));
            var result = CudaTranspiler.Transpile(source, path: $"{catalog}Module.cs");

            Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
            AppendDeviceCatalog(builder, result.Source, entryPoint);
        }

        var dispatchInput = File.ReadAllText(
            Path.Combine(goldenDirectory, "DeviceDispatchModule.cs.txt"));
        var dispatch = CudaTranspiler.Transpile(
            dispatchInput,
            path: "DeviceDispatchModule.cs");
        Assert.True(dispatch.Succeeded, FormatDiagnostics(dispatch.Diagnostics));
        builder.Append('\n').Append(dispatch.Source);

        var module = builder.ToString();
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(module)));

        Assert.Equal(286737, Encoding.UTF8.GetByteCount(module));
        Assert.Equal(7705, module.Count(character => character == '\n') + 1);
        Assert.Equal(
            "DA04432356B6281B0D5CDC539DF7A9FFD416B7488C049956F3F13543B1095A4F",
            fingerprint);
    }

    [Fact]
    public void Transpile_ProducesExactReferenceNumericHelpers()
    {
        const string source = """
            using System;
            using CSharp2CUDA;

            [CudaTranslationUnit]
            internal static unsafe class ScalarModule
            {
                public struct MathBlockSlot
                {
                    public double scalar_value;
                    public ulong data_pointer;
                    public ulong scratch_pointer;
                    public int boolean_value;
                    public int valid;
                    public int rows;
                    public int columns;
                    public int count;
                    public int capacity;
                }

                [CudaDevice]
                private static double mathblocks_positive_infinity()
                {
                    return BitConverter.Int64BitsToDouble(unchecked((long)0x7ff0000000000000UL));
                }

                [CudaDevice]
                private static double mathblocks_quiet_nan()
                {
                    return BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000000UL));
                }

                [CudaDevice]
                private static double mathblocks_square_root(double value)
                {
                    if (value == 0.0 || (double.IsInfinity(value) && value > 0.0))
                        return value;
                    if (value < 0.0 || double.IsNaN(value))
                        return mathblocks_quiet_nan();

                    double scaled = value;
                    double correction = 1.0;
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(scaled);
                    if ((bits & 0x7ff0000000000000UL) == 0UL)
                    {
                        scaled *= BitConverter.Int64BitsToDouble(unchecked((long)0x4350000000000000UL));
                        correction = BitConverter.Int64BitsToDouble(unchecked((long)0x3e40000000000000UL));
                        bits = (ulong)BitConverter.DoubleToInt64Bits(scaled);
                    }

                    double estimate = BitConverter.Int64BitsToDouble((long)((bits >> 1) + 0x1ff8000000000000UL));
                    for (int iteration = 0; iteration < 7; iteration++)
                        estimate = 0.5 * (estimate + scaled / estimate);
                    return estimate * correction;
                }
            }
            """;

        const string expected = """
            struct MathBlockSlot
            {
                double scalar_value;
                unsigned long long data_pointer;
                unsigned long long scratch_pointer;
                int boolean_value;
                int valid;
                int rows;
                int columns;
                int count;
                int capacity;
            };

            __device__ double mathblocks_positive_infinity()
            {
                return __longlong_as_double((long long)0x7ff0000000000000ull);
            }

            __device__ double mathblocks_quiet_nan()
            {
                return __longlong_as_double((long long)0x7ff8000000000000ull);
            }

            __device__ double mathblocks_square_root(double value)
            {
                if (value == 0.0 || (isinf(value) && value > 0.0))
                    return value;
                if (value < 0.0 || isnan(value))
                    return mathblocks_quiet_nan();

                double scaled = value;
                double correction = 1.0;
                unsigned long long bits = (unsigned long long)__double_as_longlong(scaled);
                if ((bits & 0x7ff0000000000000ull) == 0ull)
                {
                    scaled *= __longlong_as_double((long long)0x4350000000000000ull);
                    correction = __longlong_as_double((long long)0x3e40000000000000ull);
                    bits = (unsigned long long)__double_as_longlong(scaled);
                }

                double estimate = __longlong_as_double((long long)((bits >> 1) + 0x1ff8000000000000ull));
                for (int iteration = 0; iteration < 7; iteration++)
                    estimate = 0.5 * (estimate + scaled / estimate);
                return estimate * correction;
            }
            """;

        var result = CudaTranspiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal(expected, result.Source);
    }

    [Fact]
    public void Transpile_ProducesExactKernelIntrinsics()
    {
        const string source = """
            using CSharp2CUDA;

            [CudaTranslationUnit]
            internal static unsafe class KernelModule
            {
                public struct MathBlockSlot
                {
                    public int valid;
                }

                [CudaGlobal]
                private static void mathblocks_kernel(
                    [CudaReadOnly] MathBlockSlot** inputs,
                    MathBlockSlot* output)
                {
                    int thread = Cuda.ThreadIdx.X;
                    if (Cuda.BlockIdx.X != 0)
                        return;

                    MathBlockSlot* first = Cuda.ReadOnly(inputs[0]);
                    Cuda.SyncThreads();
                    Cuda.AtomicExchange(ref output->valid, 0);
                }
            }
            """;

        const string expected = """
            struct MathBlockSlot
            {
                int valid;
            };

            extern "C" __global__ void mathblocks_kernel(
                const MathBlockSlot* const* inputs,
                MathBlockSlot* output)
            {
                int thread = threadIdx.x;
                if (blockIdx.x != 0)
                    return;

                const MathBlockSlot* first = inputs[0];
                __syncthreads();
                atomicExch(&output->valid, 0);
            }
            """;

        var result = CudaTranspiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal(expected, result.Source);
    }

    [Fact]
    public void Transpile_RejectsManagedAllocation()
    {
        const string source = """
            using CSharp2CUDA;

            [CudaTranslationUnit]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static void invalid_method()
                {
                    _ = new object();
                }
            }
            """;

        var result = CudaTranspiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsMutableReferenceParameter()
    {
        const string source = """
            using CSharp2CUDA;

            [CudaTranslationUnit]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static void invalid_method(ref int value)
                {
                    value++;
                }
            }
            """;

        var result = CudaTranspiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsDynamicStackAllocation()
    {
        const string source = """
            using CSharp2CUDA;

            [CudaTranslationUnit]
            internal static unsafe class InvalidModule
            {
                [CudaDevice]
                private static double invalid_method(int count)
                {
                    double* values = stackalloc double[count];
                    return values[0];
                }
            }
            """;

        var result = CudaTranspiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsUninitializedReadOnlyStackAllocation()
    {
        const string source = """
            using System;
            using CSharp2CUDA;

            [CudaTranslationUnit]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static double invalid_method()
                {
                    ReadOnlySpan<double> values = stackalloc double[3];
                    return values[0];
                }
            }
            """;

        var result = CudaTranspiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    private static void AppendDeviceCatalog(
        StringBuilder builder,
        string source,
        string entryPoint)
    {
        var globalDeclaration = $"extern \"C\" __global__ void {entryPoint}(";
        var deviceDeclaration = $"__device__ void {entryPoint}_dispatch(";
        var declarationIndex = source.IndexOf(globalDeclaration, StringComparison.Ordinal);
        Assert.True(declarationIndex >= 0, $"Missing CUDA entry point '{entryPoint}'.");
        Assert.Equal(
            -1,
            source.IndexOf(
                globalDeclaration,
                declarationIndex + globalDeclaration.Length,
                StringComparison.Ordinal));

        var deviceSource = source.Replace(
            globalDeclaration,
            deviceDeclaration,
            StringComparison.Ordinal);
        deviceSource = entryPoint == "mathblocks_scalar"
            ? deviceSource.Replace("blockIdx.x != 0 || ", string.Empty, StringComparison.Ordinal)
            : deviceSource.Replace("blockIdx.x != 0", "false", StringComparison.Ordinal);
        builder.Append(deviceSource).Append('\n');
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}
