using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

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
        var sourcePath = Path.Combine(goldenDirectory, $"{catalog}Module.cs");
        var expected = File.ReadAllText(
            Path.Combine(goldenDirectory, $"{catalog}CudaBlockCatalog.cu"));

        var result = CudaTranspiler.TranspileFile(sourcePath);

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
            var sourcePath = Path.Combine(goldenDirectory, $"{catalog}Module.cs");
            var result = CudaTranspiler.TranspileFile(sourcePath);

            Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
            AppendDeviceCatalog(builder, result.Source, entryPoint);
        }

        var dispatch = CudaTranspiler.TranspileFile(
            Path.Combine(goldenDirectory, "DeviceDispatchModule.cs"));
        Assert.True(dispatch.Succeeded, FormatDiagnostics(dispatch.Diagnostics));
        builder.Append('\n').Append(dispatch.Source);

        var module = builder.ToString();
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(module)));

        Assert.Equal(504799, Encoding.UTF8.GetByteCount(module));
        Assert.Equal(11917, module.Count(character => character == '\n') + 1);
        Assert.Equal(
            "EEFF3D494A9F8499F66164DAEA5BA8BA7C813D2E37A0357987A4BC46A13DA92A",
            fingerprint);
    }

    [Fact]
    public void Transpile_ProducesExactReferenceNumericHelpers()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
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

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct MathBlockSlot;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "__device__ double mathblocks_square_root(double value);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "__longlong_as_double(csharp2cuda_i64_from_bits((unsigned long long)(0x7ff0000000000000ull)))",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_u64_shr(bits, 1)", result.Source, StringComparison.Ordinal);
        Assert.Contains("isinf(value)", result.Source, StringComparison.Ordinal);
        Assert.Contains("isnan(value)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_ProducesExactKernelIntrinsics()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
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

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("int thread = threadIdx.x;", result.Source, StringComparison.Ordinal);
        Assert.Contains("if (blockIdx.x != 0)", result.Source, StringComparison.Ordinal);
        Assert.Contains("const MathBlockSlot* first = inputs[0];", result.Source, StringComparison.Ordinal);
        Assert.Contains("__syncthreads();", result.Source, StringComparison.Ordinal);
        Assert.Contains("atomicExch(&output->valid, 0);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsManagedAllocation()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static void invalid_method()
                {
                    _ = new object();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsMutableReferenceParameter()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static void invalid_method(ref int value)
                {
                    value++;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsDynamicStackAllocation()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
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

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsUninitializedReadOnlyStackAllocation()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
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

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RewritesCallsToValidatedCudaName()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class FunctionNames
            {
                public const string Add = "cuda_add";
            }

            [TranspileToCUDA]
            internal static class ValidModule
            {
                [CudaDevice(Name = FunctionNames.Add)]
                private static int Add(int left, int right)
                {
                    return left + right;
                }

                [CudaDevice]
                private static int CallAdd(int left, int right)
                {
                    return Add(left, right);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "__device__ int cuda_add(int left, int right);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "return csharp2cuda_i32_add(left, right);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("return cuda_add(left, right);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RewritesNamedCallAcrossTranslationUnits()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ProviderModule
            {
                [CudaDevice(Name = "cuda_add")]
                internal static int Add(int left, int right)
                {
                    return left + right;
                }
            }

            [TranspileToCUDA]
            internal static class CallerModule
            {
                [CudaDevice]
                private static int CallAdd(int left, int right)
                {
                    return ProviderModule.Add(left, right);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("return cuda_add(left, right);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsConstantCudaNameInjection()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class FunctionNames
            {
                public const string Injection =
                    "safe() { return 1; }\n__device__ int injected";
            }

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice(Name = FunctionNames.Injection)]
                private static int Safe()
                {
                    return 1;
                }

                [CudaGlobal(Name = FunctionNames.Injection)]
                private static void Kernel()
                {
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Equal(
            2,
            result.Diagnostics.Count(diagnostic => diagnostic.Id == "CS2CUDA010"));
    }

    [Theory]
    [InlineData("return")]
    [InlineData("9invalid")]
    [InlineData("_invalid")]
    [InlineData("invalid.name")]
    public void Transpile_RejectsInvalidCudaName(string name)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice(Name = "{{name}}")]
                private static int Safe()
                {
                    return 1;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA010");
    }

    [Theory]
    [InlineData("int", "int value", "return value >>> 1;")]
    [InlineData("int", "", "return default;")]
    [InlineData("int", "", "return default(int);")]
    [InlineData("double", "double left, double right", "return left % right;")]
    [InlineData("double", "double left, double right", "left %= right; return left;")]
    public void Transpile_RejectsUnsafeExpression(
        string returnType,
        string parameters,
        string statements)
    {
        var result = TranspileDeviceMethod(returnType, parameters, statements);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_AcceptsIntegralRemainder()
    {
        var result = TranspileDeviceMethod(
            "int",
            "int left, int right",
            "return left % right;");

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("return left % right;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsSystemChar()
    {
        var result = TranspileDeviceMethod(
            "char",
            "char value",
            "return value;");

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA007");
    }

    [Fact]
    public void Transpile_RejectsInvalidIdentifiersAcrossDeclarations()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                public struct @class
                {
                    public int @template;
                }

                [CudaDevice]
                private static int Test(int @class)
                {
                    int @template = @class;
                    return @template;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Equal(
            4,
            result.Diagnostics.Count(diagnostic => diagnostic.Id == "CS2CUDA010"));
    }

    [Fact]
    public void Transpile_NormalizesEscapedSafeIdentifiers()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ValidModule
            {
                [CudaDevice]
                private static int Echo(int @value)
                {
                    int @result = @value;
                    return @result;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("Echo(int value)", result.Source, StringComparison.Ordinal);
        Assert.Contains("int result = value;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("@", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsUnmappedRuntimeMember()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidModule
            {
                [CudaDevice]
                private static double Read()
                {
                    return Math.PI;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_PreservesPublicConversionIntrinsics()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ConversionModule
            {
                [CudaDevice]
                private static bool ToBoolean(int value)
                {
                    return Cuda.Bool(value) == true;
                }

                [CudaDevice]
                private static ulong Shift(long value)
                {
                    return Cuda.Unsigned(value) >> 1;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("((value)!=0)", result.Source, StringComparison.Ordinal);
        Assert.Contains("(unsigned long long)", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_u64_shr", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_DeclaresFunctionsBeforeDefinitions()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ForwardModule
            {
                [CudaDevice]
                private static int Call(int value)
                {
                    return Later(value);
                }

                [CudaDevice]
                private static int Later(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        var prototype = result.Source.IndexOf(
            "__device__ int Later(int value);",
            StringComparison.Ordinal);
        var definition = result.Source.IndexOf(
            "__device__ int Later(int value)\n{",
            StringComparison.Ordinal);
        Assert.True(prototype >= 0);
        Assert.True(definition > prototype);
    }

    [Fact]
    public void Transpile_RejectsDuplicateEmittedSignature()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class DuplicateModule
            {
                [CudaDevice(Name = "same")]
                private static int First(int value)
                {
                    return value;
                }

                [CudaDevice(Name = "same")]
                private static int Second(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA011");
    }

    [Fact]
    public void Transpile_RejectsOptionalParameter()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class OptionalModule
            {
                [CudaDevice]
                private static int Add(int value = 1)
                {
                    return value;
                }

                [CudaDevice]
                private static int Call()
                {
                    return Add();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_NormalizesNumericSeparators()
    {
        var result = TranspileDeviceMethod("int", "", "return 1_000;");

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("return 1000;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("1_000", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsEnumType()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal enum Mode
            {
                First
            }

            [TranspileToCUDA]
            internal static class EnumModule
            {
                [CudaDevice]
                private static Mode Read(Mode value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA007");
    }

    [Fact]
    public void Transpile_RejectsStaticStructField()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class StaticFieldModule
            {
                public struct Data
                {
                    public static int Value;
                }

                [CudaDevice]
                private static int Read()
                {
                    return Data.Value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_LowersSignedOverflowAndMaskedShift()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class IntegerModule
            {
                [CudaDevice]
                private static int Add(int left, int right)
                {
                    return left + right;
                }

                [CudaDevice]
                private static int Shift(int value, int count)
                {
                    return value << count;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_i32_add(left, right)", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_i32_shl(value, count)", result.Source, StringComparison.Ordinal);
        Assert.Contains("(unsigned int)count & 31u", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsUnsequencedImpureCallArguments()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class OrderModule
            {
                [CudaDevice]
                private static int Next(int* value)
                {
                    return (*value)++;
                }

                [CudaDevice]
                private static int Pair(int left, int right)
                {
                    return left + right;
                }

                [CudaDevice]
                private static int Read(int* value)
                {
                    return Pair(Next(value), Next(value));
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA006");
    }

    [Fact]
    public void Transpile_RejectsUnqualifiedRuntimeConstant()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;
            using static System.Math;

            [TranspileToCUDA]
            internal static class ConstantModule
            {
                [CudaDevice]
                private static double Read()
                {
                    return PI;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsPointerSubtraction()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class PointerModule
            {
                [CudaDevice]
                private static long Difference(int* left, int* right)
                {
                    return left - right;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsReservedCudaRuntimeIdentifier()
    {
        var result = TranspileDeviceMethod(
            "int",
            "int threadIdx",
            "return threadIdx;");

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA010");
    }

    [Fact]
    public void Transpile_RejectsMixedConditionalNumericTypes()
    {
        var result = TranspileDeviceMethod(
            "long",
            "bool condition",
            "return condition ? -1 : 1u;");

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsUnsequencedSimpleAssignment()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class AssignmentModule
            {
                [CudaDevice]
                private static int Next(int* state)
                {
                    return (*state)++;
                }

                [CudaDevice]
                private static void Write(int* output, int* state)
                {
                    output[Next(state)] = Next(state);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Theory]
    [InlineData("uint")]
    [InlineData("double")]
    public void Transpile_RejectsUnsequencedNativeBinaryOperands(string type)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class BinaryModule
            {
                [CudaDevice]
                private static {{type}} Next({{type}}* state)
                {
                    return (*state)++;
                }

                [CudaDevice]
                private static {{type}} Read({{type}}* state)
                {
                    return Next(state) - Next(state);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsUnsequencedCompoundAssignmentTarget()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class CompoundModule
            {
                [CudaDevice]
                private static int Next(int* state)
                {
                    return (*state)++;
                }

                [CudaDevice]
                private static void Write(int* output, int* state)
                {
                    output[Next(state)] += *state;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsUnsequencedExternalFunctionOperands()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ExternalModule
            {
                [CudaExternal]
                private static int Next() => throw new NotSupportedException();

                [CudaDevice]
                private static int Read()
                {
                    return Next() - Next();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_AcceptsExplicitlyPureExternalFunctionOperands()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ExternalModule
            {
                [CudaExternal(IsPure = true)]
                private static int Sample() => throw new NotSupportedException();

                [CudaDevice]
                private static int Read()
                {
                    return Sample() - Sample();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "csharp2cuda_i32_sub(Sample(), Sample())",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsPureExternalWritablePointer()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class ExternalModule
            {
                [CudaExternal(IsPure = true)]
                private static int Read(int* value) => throw new NotSupportedException();

                [CudaDevice]
                private static int Call(int* value)
                {
                    return Read(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void Transpile_RejectsCheckedOverflowCompilation()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class CheckedModule
            {
                [CudaDevice]
                private static int Add(int left, int right)
                {
                    return left + right;
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp14));
        var references = GetCompilationReferences();
        var compilation = CSharpCompilation.Create(
            "CheckedInput",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                checkOverflow: true));

        var result = CudaTestCompiler.Transpile(compilation);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA012");
    }

    [Fact]
    public void Transpile_AppliesEveryIntegralComparisonPromotion()
    {
        var source = new StringBuilder("""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ComparisonModule
            {
            """);
        var expectedDefinitions = new List<string>();
        var methodIndex = 0;

        foreach (var left in IntegralTypes)
        {
            foreach (var right in IntegralTypes)
            {
                var promoted = GetIntegralPromotion(left, right);
                if (promoted is null)
                    continue;

                foreach (var comparison in IntegralComparisons)
                {
                    var leftExpression = FormatPromotedOperand(left, promoted, "left");
                    var rightExpression = FormatPromotedOperand(right, promoted, "right");
                    var expected = $"{leftExpression} {comparison.Token} {rightExpression}";
                    var methodName = $"Compare{methodIndex++}";
                    expectedDefinitions.Add($$"""
                        __device__ bool {{methodName}}({{left.CudaName}} left, {{right.CudaName}} right)
                        {
                            return {{expected}};
                        }
                        """);
                    source.AppendLine($$"""

                            [CudaDevice]
                            private static bool {{methodName}}({{left.CSharpName}} left, {{right.CSharpName}} right)
                            {
                                return left {{comparison.Token}} right;
                            }
                        """);
                }
            }
        }
        source.AppendLine("}");

        var result = CudaTestCompiler.Transpile(source.ToString());

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal(336, expectedDefinitions.Count);
        foreach (var expected in expectedDefinitions)
            Assert.Contains(expected, result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesExactMathMinMaxBits()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class FloatingModule
            {
                [CudaDevice]
                private static double Maximum(double left, double right)
                {
                    return Math.Max(left, right);
                }

                [CudaDevice]
                private static double Minimum(double left, double right)
                {
                    return Math.Min(left, right);
                }
            }
            """;
        const string maximumHelper = """
            static __device__ __forceinline__ double csharp2cuda_f64_maximum(double left, double right)
            {
                if (left != right)
                {
                    if (!isnan(left))
                        return right < left ? left : right;
                    return left;
                }
                return signbit(right) ? left : right;
            }
            """;
        const string minimumHelper = """
            static __device__ __forceinline__ double csharp2cuda_f64_minimum(double left, double right)
            {
                if (left != right)
                {
                    if (!isnan(left))
                        return left < right ? left : right;
                    return left;
                }
                return signbit(left) ? left : right;
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(maximumHelper, result.Source, StringComparison.Ordinal);
        Assert.Contains(minimumHelper, result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "return csharp2cuda_f64_maximum(left, right);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "return csharp2cuda_f64_minimum(left, right);",
            result.Source,
            StringComparison.Ordinal);

        const ulong positiveZeroBits = 0x0000000000000000UL;
        const ulong negativeZeroBits = 0x8000000000000000UL;
        const ulong firstNaNBits = 0x7ff8000000000001UL;
        const ulong secondNaNBits = 0xfff8000000000002UL;
        var positiveZero = BitConverter.UInt64BitsToDouble(positiveZeroBits);
        var negativeZero = BitConverter.UInt64BitsToDouble(negativeZeroBits);
        var firstNaN = BitConverter.UInt64BitsToDouble(firstNaNBits);
        var secondNaN = BitConverter.UInt64BitsToDouble(secondNaNBits);
        (double Left, double Right, ulong Maximum, ulong Minimum)[] cases =
        [
            (firstNaN, 1.0, firstNaNBits, firstNaNBits),
            (1.0, secondNaN, secondNaNBits, secondNaNBits),
            (firstNaN, secondNaN, firstNaNBits, firstNaNBits),
            (positiveZero, negativeZero, positiveZeroBits, negativeZeroBits),
            (negativeZero, positiveZero, positiveZeroBits, negativeZeroBits),
            (negativeZero, negativeZero, negativeZeroBits, negativeZeroBits),
            (positiveZero, positiveZero, positiveZeroBits, positiveZeroBits)
        ];

        foreach (var item in cases)
        {
            var maximum = Math.Max(item.Left, item.Right);
            var minimum = Math.Min(item.Left, item.Right);
            if (double.IsNaN(item.Left) || double.IsNaN(item.Right))
            {
                Assert.True(double.IsNaN(maximum));
                Assert.True(double.IsNaN(minimum));
                continue;
            }

            Assert.Equal(
                item.Maximum,
                BitConverter.DoubleToUInt64Bits(maximum));
            Assert.Equal(
                item.Minimum,
                BitConverter.DoubleToUInt64Bits(minimum));
        }
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
        var definitionIndex = source.IndexOf(
            globalDeclaration,
            declarationIndex + globalDeclaration.Length,
            StringComparison.Ordinal);
        Assert.True(definitionIndex > declarationIndex);
        Assert.Equal(
            -1,
            source.IndexOf(
                globalDeclaration,
                definitionIndex + globalDeclaration.Length,
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

    private static CudaTranspilationResult TranspileDeviceMethod(
        string returnType,
        string parameters,
        string statements)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class TestModule
            {
                [CudaDevice]
                private static {{returnType}} Test({{parameters}})
                {
                    {{statements}}
                }
            }
            """;
        return CudaTestCompiler.Transpile(source);
    }

    private static IReadOnlyCollection<MetadataReference> GetCompilationReferences()
    {
        var references = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase);
        var trustedAssemblies = Assert.IsType<string>(
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            references[path] = MetadataReference.CreateFromFile(path);
        var assemblyPath = typeof(Cuda).Assembly.Location;
        references[assemblyPath] = MetadataReference.CreateFromFile(assemblyPath);
        return references.Values;
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));

    private static IntegralType? GetIntegralPromotion(IntegralType left, IntegralType right)
    {
        if (left.CSharpName == "ulong" || right.CSharpName == "ulong")
        {
            var other = left.CSharpName == "ulong" ? right : left;
            return other.CSharpName is "sbyte" or "short" or "int" or "long"
                ? null
                : IntegralTypes.Single(type => type.CSharpName == "ulong");
        }

        if (left.CSharpName == "long" || right.CSharpName == "long")
            return IntegralTypes.Single(type => type.CSharpName == "long");

        if (left.CSharpName == "uint" || right.CSharpName == "uint")
        {
            var other = left.CSharpName == "uint" ? right : left;
            var promotedName = other.CSharpName is "sbyte" or "short" or "int"
                ? "long"
                : "uint";
            return IntegralTypes.Single(type => type.CSharpName == promotedName);
        }

        return IntegralTypes.Single(type => type.CSharpName == "int");
    }

    private static string FormatPromotedOperand(
        IntegralType source,
        IntegralType promoted,
        string name) => source == promoted ? name : $"({promoted.CudaName})({name})";

    private static readonly IntegralType[] IntegralTypes =
    [
        new("sbyte", "signed char"),
        new("byte", "unsigned char"),
        new("short", "short"),
        new("ushort", "unsigned short"),
        new("int", "int"),
        new("uint", "unsigned int"),
        new("long", "long long"),
        new("ulong", "unsigned long long")
    ];

    private static readonly IntegralComparison[] IntegralComparisons =
    [
        new("=="),
        new("!="),
        new("<"),
        new("<="),
        new(">"),
        new(">=")
    ];

    private sealed record IntegralType(string CSharpName, string CudaName);

    private sealed record IntegralComparison(string Token);
}
