using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaPortableProfileTests
{
    [Fact]
    public void Transpile_FollowsOrdinaryArrayAlgorithm()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static double Sum(double[] values)
                {
                    double result = 0.0;
                    for (int index = 0; index < values.Length; index++)
                        result += values[index];
                    return result;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ArrayAdapter
            {
                [CudaGlobal(Name = "sum_values")]
                private static void Run(double* values, int count, double* output)
                {
                    if (Cuda.BlockIdx.X == 0 && Cuda.ThreadIdx.X == 0)
                        output[0] = ExistingAlgorithm.Sum(Cuda.ReadOnlyArray(values, count));
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "struct csharp2cuda_readonly_array_view",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(".get_length()", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_readonly_array_view<double>(values, count, (values == nullptr))",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("ExistingAlgorithm_Sum_", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ArrayViewContracts_RejectManagedExecution()
    {
        Assert.Throws<InvalidOperationException>(() => InvokeArray());
        Assert.Throws<InvalidOperationException>(() => InvokeReadOnlyArray());

        static unsafe double[] InvokeArray()
        {
            double value = 1.0;
            return Cuda.Array(&value, 1);
        }

        static unsafe double[] InvokeReadOnlyArray()
        {
            double value = 1.0;
            return Cuda.ReadOnlyArray(&value, 1);
        }
    }

    [Fact]
    public void Transpile_RejectsWriteThroughReadOnlyArrayView()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static void Clear(int[] values)
                {
                    values[0] = 0;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ArrayAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count)
                {
                    ExistingAlgorithm.Clear(Cuda.ReadOnlyArray(values, count));
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA031");
    }

    [Fact]
    public void Transpile_AllowsWriteThroughWritableArrayView()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static void Clear(int[] values)
                {
                    values[0] = 0;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ArrayAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count)
                {
                    ExistingAlgorithm.Clear(Cuda.Array(values, count));
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_array_view<int>", result.Source, StringComparison.Ordinal);
        Assert.Contains("asm volatile(\"trap;\")", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesArrayNullStateAndSpanDefaultState()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int ArrayLength()
                {
                    int[] values = default;
                    return values.Length;
                }

                public static int SpanLength()
                {
                    Span<int> values = default;
                    return values.Length;
                }
            }

            [TranspileToCUDA]
            internal static class ViewAdapter
            {
                [CudaDevice]
                private static int ArrayLength() => ExistingAlgorithm.ArrayLength();

                [CudaDevice]
                private static int SpanLength() => ExistingAlgorithm.SpanLength();
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("(nullptr, 0, true)", result.Source, StringComparison.Ordinal);
        Assert.Contains("(nullptr, 0, false)", result.Source, StringComparison.Ordinal);
        Assert.Contains("if (is_null) asm volatile(\"trap;\")", result.Source, StringComparison.Ordinal);
        Assert.Contains(".get_length()", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_UsesNormalReadOnlySpanAndSlice()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int SumTail(ReadOnlySpan<int> values)
                {
                    ReadOnlySpan<int> tail = values.Slice(1);
                    int result = 0;
                    for (int index = 0; index < tail.Length; index++)
                        result += tail[index];
                    return result;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class SpanAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count, int* output)
                {
                    ReadOnlySpan<int> view = new ReadOnlySpan<int>(values, count);
                    output[0] = ExistingAlgorithm.SumTail(view);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "csharp2cuda_readonly_array_view<int>",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(".slice(1)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_ConvertsWritableSpanToReadOnlySpan()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int ReadFirst(Span<int> values)
                {
                    ReadOnlySpan<int> readOnly = values;
                    return readOnly[0];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class SpanAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int* output)
                {
                    Span<int> view = new Span<int>(values, 1);
                    output[0] = ExistingAlgorithm.ReadFirst(view);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_readonly_array_view<int>", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_ConvertsArrayViewsToSpanParameters()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int ReadFirst(ReadOnlySpan<int> values) => values[0];

                public static void Scale(Span<double> values, double factor)
                {
                    values[0] *= factor;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class SpanAdapter
            {
                [CudaGlobal]
                private static void Run(int* input, double* output)
                {
                    int first = ExistingAlgorithm.ReadFirst(Cuda.ReadOnlyArray(input, 1));
                    ExistingAlgorithm.Scale(Cuda.Array(output, 1), first);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_readonly_array_view<int>", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_array_view<double>", result.Source, StringComparison.Ordinal);
        Assert.Contains("__dmul_rn", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersArrayForeach()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Sum(int[] values)
                {
                    int result = 0;
                    foreach (int value in values)
                        result += value;
                    return result;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ForeachAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count, int* output)
                {
                    output[0] = ExistingAlgorithm.Sum(Cuda.ReadOnlyArray(values, count));
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("while (true)", result.Source, StringComparison.Ordinal);
        Assert.Contains(".get_length()", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersEnumSwitchWithUnderlyingValues()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal enum OperationKind : byte
            {
                Add = 1,
                Multiply = 3
            }

            internal static class ExistingAlgorithm
            {
                public static int Weight(OperationKind operation)
                {
                    switch (operation)
                    {
                        case OperationKind.Add:
                            return 2;
                        case OperationKind.Multiply:
                            return 4;
                        default:
                            return 0;
                    }
                }
            }

            [TranspileToCUDA]
            internal static unsafe class EnumAdapter
            {
                [CudaGlobal]
                private static void Run(byte operation, int* output)
                {
                    output[0] = ExistingAlgorithm.Weight((OperationKind)operation);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("switch (operation)", result.Source, StringComparison.Ordinal);
        Assert.Contains("case 1:", result.Source, StringComparison.Ordinal);
        Assert.Contains("case 3:", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_DiscoversOrdinaryStructureAndExpressionBody()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Point
            {
                public double X;
                public double Y;
            }

            internal static class ExistingAlgorithm
            {
                public static double LengthSquared(Point value) =>
                    value.X * value.X + value.Y * value.Y;
            }

            [TranspileToCUDA]
            internal static unsafe class StructureAdapter
            {
                [CudaGlobal]
                private static void Run(Point* values, double* output)
                {
                    output[0] = ExistingAlgorithm.LengthSquared(values[0]);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct Point", result.Source, StringComparison.Ordinal);
        Assert.Contains("double X;", result.Source, StringComparison.Ordinal);
        Assert.Contains("double Y;", result.Source, StringComparison.Ordinal);
        Assert.Contains("ExistingAlgorithm_LengthSquared_", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersReadOnlyStructureInstanceMethod()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Point
            {
                public double X;
                public double Y;

                public readonly double LengthSquared() => X * X + Y * Y;
            }

            [TranspileToCUDA]
            internal static unsafe class StructureAdapter
            {
                [CudaGlobal]
                private static void Run(Point* values, double* output)
                {
                    output[0] = values[0].LengthSquared();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("const Point* csharp2cuda_this", result.Source, StringComparison.Ordinal);
        Assert.Contains("LengthSquared_", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_SpecializesClosedGenericMethodConstructions()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static T Select<T>(bool selectLeft, T left, T right)
                    where T : unmanaged => selectLeft ? left : right;
            }

            [TranspileToCUDA]
            internal static unsafe class GenericAdapter
            {
                [CudaGlobal]
                private static void Run(int* integers, double* doubles)
                {
                    integers[0] = ExistingAlgorithm.Select(true, integers[1], integers[2]);
                    doubles[0] = ExistingAlgorithm.Select(false, doubles[1], doubles[2]);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int cs2cuda_global_namespace_ExistingAlgorithm_Select_", result.Source, StringComparison.Ordinal);
        Assert.Contains("__device__ double cs2cuda_global_namespace_ExistingAlgorithm_Select_", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("<T>", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_SpecializesClosedGenericStructure()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Pair<T> where T : unmanaged
            {
                public T First;
                public T Second;

                public readonly T Select(bool first) => first ? First : Second;
            }

            [TranspileToCUDA]
            internal static unsafe class GenericStructureAdapter
            {
                [CudaGlobal]
                private static void Run(Pair<int>* values, int* output)
                {
                    output[0] = values[0].Select(true);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct cs2cuda_global_namespace_Pair_", result.Source, StringComparison.Ordinal);
        Assert.Contains("int First;", result.Source, StringComparison.Ordinal);
        Assert.Contains("int Second;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Pair<T>", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_UsesExactRuntimeSymbolsInsteadOfLookalikeNames()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class MathLookalike
            {
                public static double Abs(double value) => value < 0.0 ? -value : value;
            }

            [TranspileToCUDA]
            internal static unsafe class ExactSymbolAdapter
            {
                [CudaGlobal]
                private static void Run(double* input, double* output)
                {
                    output[0] = MathLookalike.Abs(input[0]);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("MathLookalike_Abs_", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("fabs(", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_MapsExactFrameworkMathAndBitSymbols()
    {
        const string source = """
            using System;
            using System.Numerics;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static double Scalar(double value)
                {
                    return Math.Sin(value) + Math.PI +
                        Math.Max(value, double.NegativeZero);
                }

                public static float Scalar32(float value)
                {
                    return MathF.Sqrt(value) + MathF.Min(value, float.PositiveInfinity);
                }

                public static uint Bits(uint value, int count)
                {
                    return BitOperations.RotateLeft(value, count) +
                        (uint)BitOperations.PopCount(value);
                }
            }

            [TranspileToCUDA]
            internal static class FrameworkAdapter
            {
                [CudaDevice]
                private static double Scalar(double value) => ExistingAlgorithm.Scalar(value);

                [CudaDevice]
                private static float Scalar32(float value) => ExistingAlgorithm.Scalar32(value);

                [CudaDevice]
                private static uint Bits(uint value, int count) => ExistingAlgorithm.Bits(value, count);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        string[] mappings =
        [
            "sin(",
            "csharp2cuda_f64_maximum(",
            "sqrtf(",
            "csharp2cuda_f32_minimum(",
            "csharp2cuda_u32_rotate_left(",
            "__popc("
        ];
        foreach (var mapping in mappings)
            Assert.Contains(mapping, result.Source, StringComparison.Ordinal);
        Assert.Contains("0x8000000000000000ull", result.Source, StringComparison.Ordinal);
        Assert.Contains("0x7f800000u", result.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int[] values")]
    [InlineData("System.Span<int> values")]
    [InlineData("ref int value")]
    [InlineData("out int value")]
    [InlineData("in int value")]
    public void Transpile_RejectsManagedOrByReferenceKernelParameters(string parameter)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class InvalidKernelAbi
            {
                [CudaGlobal]
                private static void Run({{parameter}})
                {
                    {{(parameter.StartsWith("out ", StringComparison.Ordinal) ? "value = 0;" : string.Empty)}}
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA028");
    }

    [Theory]
    [InlineData("int[] values = new int[4];")]
    [InlineData("object value = new object();")]
    [InlineData("object value = 1;")]
    public void Transpile_RejectsManagedAllocationAndObjectStorage(string statement)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class ManagedAllocationAdapter
            {
                [CudaDevice]
                private static void Run()
                {
                    {{statement}}
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA030");
    }

    [Fact]
    public void Transpile_LowersRefInOutAndOptionalArguments()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Update(in int input, ref int state, out int doubled, int scale = 2)
                {
                    state += input;
                    doubled = state * scale;
                    return state;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ByReferenceAdapter
            {
                [CudaGlobal]
                private static void Run(int* input, int* output)
                {
                    int state = 1;
                    int doubled;
                    int value = ExistingAlgorithm.Update(in input[0], ref state, out doubled);
                    output[0] = value + doubled;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("const int* input", result.Source, StringComparison.Ordinal);
        Assert.Contains("int* state", result.Source, StringComparison.Ordinal);
        Assert.Contains("int* doubled", result.Source, StringComparison.Ordinal);
        Assert.Contains(", 2)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersOrdinaryExtensionMethod()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingExtensions
            {
                public static int Twice(this int value) => value * 2;
            }

            [TranspileToCUDA]
            internal static unsafe class ExtensionAdapter
            {
                [CudaGlobal]
                private static void Run(int* input, int* output)
                {
                    output[0] = input[0].Twice();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("ExistingExtensions_Twice_", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersDoLoopAndSwitchExpression()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal enum Sign : byte
            {
                Negative,
                Zero,
                Positive
            }

            internal static class ExistingAlgorithm
            {
                public static int Accumulate(int count, Sign sign)
                {
                    int index = 0;
                    int sum = 0;
                    do
                    {
                        index++;
                        if ((index & 1) == 0)
                            continue;
                        sum += index;
                    }
                    while (index < count);

                    return sign switch
                    {
                        Sign.Negative => -sum,
                        Sign.Zero => 0,
                        _ => sum
                    };
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ControlFlowAdapter
            {
                [CudaGlobal]
                private static void Run(int count, byte sign, int* output)
                {
                    output[0] = ExistingAlgorithm.Accumulate(count, (Sign)sign);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_do_continue_", result.Source, StringComparison.Ordinal);
        Assert.Contains("switch (", result.Source, StringComparison.Ordinal);
        Assert.Contains("case 0:", result.Source, StringComparison.Ordinal);
        Assert.Contains("default:", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersNoncapturingLocalFunction()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Score(int value)
                {
                    int Double(int item) => item * 2;
                    return Double(value) + 1;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class LocalFunctionAdapter
            {
                [CudaGlobal]
                private static void Run(int* input, int* output)
                {
                    output[0] = ExistingAlgorithm.Score(input[0]);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("Double_", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersStructureConstructor()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Point
            {
                public int X;
                public int Y;

                public Point(int x, int y)
                {
                    X = x;
                    Y = y;
                }

                public readonly int Sum() => X + Y;
            }

            internal static class ExistingAlgorithm
            {
                public static int Add(int left, int right)
                {
                    Point point = new Point(left, right);
                    return point.Sum();
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ConstructorAdapter
            {
                [CudaGlobal]
                private static void Run(int* input, int* output)
                {
                    output[0] = ExistingAlgorithm.Add(input[0], input[1]);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_constructed", result.Source, StringComparison.Ordinal);
        Assert.Contains("Point_ctor_", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transpile_LowersInternalStructureAutoProperty()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Counter
            {
                public int Value { get; set; }

                public Counter(int value)
                {
                    Value = value;
                }

                public void Increment() => Value++;
            }

            internal static class ExistingAlgorithm
            {
                public static int Count(int value)
                {
                    Counter counter = new Counter(value);
                    counter.Increment();
                    return counter.Value;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class PropertyAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = ExistingAlgorithm.Count(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("int Value;", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_constructed", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsAutoPropertyAcrossKernelAbi()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Counter
            {
                public int Value { get; set; }
            }

            [TranspileToCUDA]
            internal static unsafe class PropertyAbiAdapter
            {
                [CudaGlobal]
                private static void Run(Counter* values)
                {
                    values[0].Value++;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA032");
    }

    [Fact]
    public void Transpile_LowersSourceDefinedStructureProperty()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Counter
            {
                private int value;

                public int Value
                {
                    readonly get => value + 1;
                    set => this.value = value - 1;
                }
            }

            internal static class ExistingAlgorithm
            {
                public static int Normalize(int value)
                {
                    Counter counter = default;
                    counter.Value = value;
                    return counter.Value;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class SourcePropertyAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = ExistingAlgorithm.Normalize(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("get_Value", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set_Value", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transpile_LowersSourceDefinedStructureIndexer()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Pair
            {
                private int first;
                private int second;

                public int this[int index]
                {
                    readonly get => index == 0 ? first : second;
                    set
                    {
                        if (index == 0)
                            first = value;
                        else
                            second = value;
                    }
                }
            }

            internal static class ExistingAlgorithm
            {
                public static int Select(int left, int right)
                {
                    Pair pair = default;
                    pair[0] = left;
                    pair[1] = right;
                    return pair[1];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class SourceIndexerAdapter
            {
                [CudaGlobal]
                private static void Run(int* input, int* output)
                {
                    output[0] = ExistingAlgorithm.Select(input[0], input[1]);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("get_Item", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set_Item", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transpile_LowersSourceDefinedPropertyMutations()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Counter
            {
                private short value;

                public short Value
                {
                    readonly get => value;
                    set => this.value = value;
                }
            }

            internal static class ExistingAlgorithm
            {
                public static short Update(short value, short amount)
                {
                    Counter counter = default;
                    counter.Value = value;
                    counter.Value += amount;
                    return counter.Value++;
                }
            }

            [TranspileToCUDA]
            internal static class PropertyAdapter
            {
                [CudaDevice]
                private static short Update(short value, short amount) =>
                    ExistingAlgorithm.Update(value, amount);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("get_Value", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set_Value", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csharp2cuda_i16_from_bits", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_i16_post_increment", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsSequentialKernelAbiLayoutChecks()
    {
        const string source = """
            using System.Runtime.InteropServices;
            using Supprocom.CSharp2CUDA;

            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            internal struct Entry
            {
                public byte Kind;
                public int Value;
            }

            [TranspileToCUDA]
            internal static unsafe class LayoutAdapter
            {
                [CudaGlobal]
                private static void Run(Entry* entries, int* output)
                {
                    output[0] = entries[0].Value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("#pragma pack(push, 4)", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Entry) == 8", result.Source, StringComparison.Ordinal);
        Assert.Contains("offsetof(Entry, Kind) == 0", result.Source, StringComparison.Ordinal);
        Assert.Contains("offsetof(Entry, Value) == 4", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsAlignedExplicitKernelAbiStorage()
    {
        const string source = """
            using System.Runtime.InteropServices;
            using Supprocom.CSharp2CUDA;

            [StructLayout(LayoutKind.Explicit, Size = 8)]
            internal struct Bits
            {
                [FieldOffset(0)] public int Integer;
                [FieldOffset(0)] public float Single;
                [FieldOffset(4)] public short Tail;
            }

            [TranspileToCUDA]
            internal static unsafe class LayoutAdapter
            {
                [CudaGlobal]
                private static void Run(Bits* values, int* output)
                {
                    values[0].Single = 1.0f;
                    output[0] = values[0].Integer + values[0].Tail;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct alignas(4) Bits", result.Source, StringComparison.Ordinal);
        Assert.Contains("unsigned char csharp2cuda_storage[8];", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Bits) == 8", result.Source, StringComparison.Ordinal);
        Assert.Contains("+ 4)", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("float Single;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsUnalignedExplicitKernelAbiField()
    {
        const string source = """
            using System.Runtime.InteropServices;
            using Supprocom.CSharp2CUDA;

            [StructLayout(LayoutKind.Explicit)]
            internal struct InvalidEntry
            {
                [FieldOffset(1)] public int Value;
            }

            [TranspileToCUDA]
            internal static unsafe class LayoutAdapter
            {
                [CudaGlobal]
                private static void Run(InvalidEntry* values)
                {
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA032");
    }

    [Fact]
    public void Transpile_ValidatesNestedKernelAbiLayouts()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal struct Inner
            {
                public short Value;
            }

            internal struct Outer
            {
                public byte Kind;
                public Inner Nested;
            }

            [TranspileToCUDA]
            internal static unsafe class LayoutAdapter
            {
                [CudaGlobal]
                private static void Run(Outer* values, short* output)
                {
                    output[0] = values[0].Nested.Value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("static_assert(sizeof(Inner) == 2", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Outer) == 4", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesNarrowSignedIntegralConversions()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class NumericAdapter
            {
                [CudaDevice]
                private static sbyte ToSByte(long value) => (sbyte)value;

                [CudaDevice]
                private static short ToInt16(ulong value) => (short)value;

                [CudaDevice]
                private static char ToChar(int value) => (char)value;
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_i8_from_bits", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_i16_from_bits", result.Source, StringComparison.Ordinal);
        Assert.Contains("((unsigned short)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesFloatingToIntegralConversionBoundaries()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class NumericAdapter
            {
                [CudaDevice]
                private static int ToInt32(double value) => (int)value;

                [CudaDevice]
                private static uint ToUInt32(double value) => (uint)value;

                [CudaDevice]
                private static long ToInt64(double value) => (long)value;

                [CudaDevice]
                private static ulong ToUInt64(double value) => (ulong)value;
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        string[] helpers =
        [
            "csharp2cuda_f64_to_i32",
            "csharp2cuda_f64_to_u32",
            "csharp2cuda_f64_to_i64",
            "csharp2cuda_f64_to_u64"
        ];
        foreach (var helper in helpers)
            Assert.Contains(helper, result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesLogicalRightShiftForSignedValues()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class NumericAdapter
            {
                [CudaDevice]
                private static int ShiftInt32(int value, int count) => value >>> count;

                [CudaDevice]
                private static long ShiftInt64(long value, int count) => value >>> count;
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_i32_ushr", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_i64_ushr", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_UsesNoncontractingFloatingArithmetic()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class NumericAdapter
            {
                [CudaDevice]
                private static double DoubleValue(double left, double right) =>
                    ((left + right) * left - right) / left % right;

                [CudaDevice]
                private static float SingleValue(float left, float right) =>
                    ((left + right) * left - right) / left % right;
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        string[] intrinsics =
        [
            "__dadd_rn", "__dsub_rn", "__dmul_rn", "__ddiv_rn", "fmod",
            "__fadd_rn", "__fsub_rn", "__fmul_rn", "__fdiv_rn", "fmodf"
        ];
        foreach (var intrinsic in intrinsics)
            Assert.Contains(intrinsic, result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesNarrowIntegralMutation()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static class NumericAdapter
            {
                [CudaDevice]
                private static short Update(short value, short amount)
                {
                    value += amount;
                    value++;
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_i16_from_bits", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_i16_post_increment", result.Source, StringComparison.Ordinal);
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}
