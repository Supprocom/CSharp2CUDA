using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaPortableProfileExpansionTests
{
    [Fact]
    public void Transpile_ReadmeThinAdapterExample()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static float Apply(float value, float scale)
                {
                    float positive = MathF.Max(value, 0.0f);
                    return positive * scale;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ApplyOnGpu
            {
                [CudaGlobal(Name = "apply_values")]
                private static void Kernel(float* input, float* output, int count, float scale)
                {
                    int index = Cuda.BlockIdx.X * Cuda.BlockDim.X + Cuda.ThreadIdx.X;
                    if (index < count)
                        output[index] = ExistingAlgorithm.Apply(input[index], scale);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("apply_values", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_f32_maximum", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsEscapingStackLiftedArrayIdentity()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int[] Create(int value)
                {
                    int[] storage = [value, 2, 3];
                    int[] alias = storage;
                    return alias;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class EscapeAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = ExistingAlgorithm.Create(value)[0];
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "CS2CUDA033");
    }

    [Fact]
    public void Transpile_LowersPositionalReadonlyRecordStructBehavior()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal readonly record struct Vec2(int X, int Y)
            {
                public static Vec2 operator +(Vec2 left, Vec2 right) =>
                    new(left.X + right.X, left.Y + right.Y);

                public int Sum() => X + Y;
            }

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int value)
                {
                    Vec2 left = new(value, 2);
                    Vec2 right = new(3, 4);
                    Vec2 sum = left + right;
                    (Vec2 Vector, int Extra) pair = (sum, 5);
                    var (vector, extra) = pair;
                    var (x, y) = vector;
                    bool equal = vector == sum && vector.Equals(sum);
                    bool tupleEqual = pair == (sum, 5);
                    return x + y + vector.Sum() + extra +
                        (equal ? 1 : 0) + (tupleEqual ? 1 : 0);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class RecordAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct Vec2", result.Source, StringComparison.Ordinal);
        Assert.Contains("int X;", result.Source, StringComparison.Ordinal);
        Assert.Contains("int Y;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersRefLocalsRefReturnsReassignmentAndRefForeach()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                private static ref int Select(int[] values, int index) =>
                    ref values[index];

                public static int Calculate(int seed)
                {
                    int[] values = [seed, 2, 3];
                    ref int current = ref values[0];
                    current += 1;
                    current = ref values[1];
                    ref readonly int read = ref values[2];
                    scoped ref int selected = ref Select(values, 0);

                    foreach (ref int item in values.AsSpan())
                        item++;
                    foreach (ref readonly int item in values.AsSpan())
                        seed += item;

                    return current + read + selected + seed;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class RefAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(seed);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("int* current", result.Source, StringComparison.Ordinal);
        Assert.Contains("const int* read", result.Source, StringComparison.Ordinal);
        Assert.Contains("&((", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_ClosureConvertsDirectlyInvokedLambdasWithoutDelegates()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int seed)
                {
                    int total = seed;
                    int read = ((Func<int, int>)(value => value + seed))(2);
                    int written = ((Func<int>)(() =>
                    {
                        total++;
                        return total;
                    }))();
                    return read + written;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class LambdaAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(seed);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_capture_seed", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_capture_total", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Func", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_ClosureConvertsReadWriteAndTransitiveLocalFunctionCaptures()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static unsafe class ExistingAlgorithm
            {
                public static int Calculate(int seed, int* values)
                {
                    int total = seed;

                    int Add(int amount)
                    {
                        total += amount;
                        values[0] += seed;
                        return total + values[0];
                    }

                    int Relay(int amount) => Add(amount);
                    return Relay(2) + Relay(3);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class CaptureAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* values, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(seed, values);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_capture_total", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_capture_seed", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_capture_values", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersClosedTuplesNestedDeconstructionAndEquality()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                private static (int First, int Second) Pair(int value) =>
                    (value, value + 1);

                public static int Calculate(int value)
                {
                    (int First, int Second) pair = Pair(value++);
                    var (first, second) = pair;
                    (first, second) = (second, first);
                    (int, (int, int)) nested = (first, (second, value));
                    var (x, (y, z)) = nested;
                    bool equal = pair == (second, first);
                    return pair.First + pair.Item2 + x + y + z + (equal ? 1 : 0);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class TupleAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct csharp2cuda_tuple2", result.Source, StringComparison.Ordinal);
        Assert.Contains(".item1", result.Source, StringComparison.Ordinal);
        Assert.Contains(".item2", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LowersRelationalLogicalPatternsBindingsAndGuards()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Classify(int value)
                {
                    bool ordinary = value is not (< 0 or > 100);
                    int statementResult;
                    switch (value)
                    {
                        case < 0:
                            statementResult = -1;
                            break;
                        case var captured when captured > 90:
                            statementResult = captured;
                            break;
                        default:
                            statementResult = ordinary ? value : 0;
                            break;
                    }

                    return statementResult + (value switch
                    {
                        >= 10 and <= 20 => 1,
                        var captured when captured > 50 => captured,
                        0 or 1 => 2,
                        _ => 3
                    });
                }
            }

            [TranspileToCUDA]
            internal static unsafe class PatternAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = ExistingAlgorithm.Classify(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("&&", result.Source, StringComparison.Ordinal);
        Assert.Contains("captured", result.Source, StringComparison.Ordinal);
        Assert.Contains("asm volatile(\"trap;\")", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_PreservesNestedBreakAndOuterContinueInPatternSwitches()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int value)
                {
                    int result = 0;
                    for (int index = 0; index < 3; index++)
                    {
                        switch (value + index)
                        {
                            case > 10:
                                if (index == 1)
                                    break;
                                result += 100;
                                break;
                            case var current when current > 0:
                                result += current;
                                continue;
                            default:
                                result--;
                                break;
                        }

                        result += 10;
                    }

                    return result;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class PatternControlFlowAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(value);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("goto csharp2cuda_switch_break_", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_switch_break_", result.Source, StringComparison.Ordinal);
        Assert.Contains("goto csharp2cuda_for_continue_", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EvaluatesTextuallyEarlyDefaultAfterPatternCases()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int value)
                {
                    int result;
                    switch (value)
                    {
                        default:
                            result = 1;
                            break;
                        case > 0:
                            result = 2;
                            break;
                    }

                    return result;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class PatternDefaultAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(value);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        var relational = result.Source.IndexOf("> (0)", StringComparison.Ordinal);
        var fallback = result.Source.IndexOf("&& (true)", StringComparison.Ordinal);
        Assert.True(relational >= 0 && fallback > relational, result.Source);
    }

    [Fact]
    public void Transpile_LowersSourceDefinedOperatorsAndConversionsToDeviceCalls()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public readonly struct Score
                {
                    public readonly int Value;

                    public Score(int value) => Value = value;

                    public static Score operator +(Score left, Score right) =>
                        new(left.Value + right.Value);

                    public static Score operator -(Score value) => new(-value.Value);

                    public static bool operator <(Score left, Score right) =>
                        left.Value < right.Value;

                    public static bool operator >(Score left, Score right) =>
                        left.Value > right.Value;

                    public static Score operator ++(Score value) => new(value.Value + 1);

                    public static implicit operator Score(int value) => new(value);

                    public static explicit operator int(Score value) => value.Value;
                }

                public static int Calculate(int seed)
                {
                    Score value = seed;
                    Score increment = new(2);
                    value += increment;
                    value++;
                    Score negated = -value;
                    return value > negated ? (int)value : (int)negated;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class OperatorAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(seed);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("cs2cuda_", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("operator", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsUserDefinedOperatorsWithoutReachableSourceBodies()
    {
        const string dependencySource = """
            public readonly struct ExternalScore
            {
                public readonly int Value;

                public ExternalScore(int value) => Value = value;

                public static implicit operator ExternalScore(int value) => new(value);
            }
            """;
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class OperatorAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    ExternalScore score = value;
                    output[0] = score.Value;
                }
            }
            """;

        var dependency = CudaTestCompiler.CreateCompilation(dependencySource)
            .WithAssemblyName("ExternalPortableOperators");
        using var image = new MemoryStream();
        var emit = dependency.Emit(image);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics));

        var compilation = CudaTestCompiler.CreateCompilation(source)
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        var result = CudaTestCompiler.Transpile(compilation);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA038");
    }

    [Fact]
    public void Transpile_StackLiftsFixedCollectionsAndExpandedParams()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                private static int Sum(params int[] values)
                {
                    int result = 0;
                    foreach (int value in values)
                        result += value;
                    return result;
                }

                private static int SumReadOnly(params ReadOnlySpan<int> values)
                {
                    int result = 0;
                    foreach (int value in values)
                        result += value;
                    return result;
                }

                private static int Relay(int[] values) => Sum(values);

                private static int CountOrMissing(params int[] values) =>
                    values is null ? -1 : values.Length;

                public static int Calculate(int seed)
                {
                    int[] zeroed = new int[3];
                    int[] array = [seed, 2, 3];
                    Span<int> writable = [seed, 4, 5];
                    ReadOnlySpan<int> readOnly = [seed, 6, 7];
                    writable[1] = array[1];
                    return zeroed[0] + writable[1] + readOnly[2] +
                        Sum(seed, 8, 9) + SumReadOnly(seed, 10, 11) +
                        Relay(array) + CountOrMissing(null);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class CollectionAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(seed);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("_storage[3] = {}", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_array_view<int> array", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_readonly_array_view<int> readOnly",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("_params[3]", result.Source, StringComparison.Ordinal);
        Assert.Contains("nullptr, 0, true", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_TracksExpandedParamsStorageThroughHelperReturns()
    {
        const string acceptedSource = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                private static int[] Echo(params int[] values) => values;

                public static int Calculate(int seed) => Echo(seed, 2)[0];
            }

            [TranspileToCUDA]
            internal static unsafe class ParamsAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;
        const string rejectedSource = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                private static int[] Echo(params int[] values) => values;

                public static int[] Create(int seed) => Echo(seed, 2);
            }

            [TranspileToCUDA]
            internal static unsafe class ParamsAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Create(seed)[0];
            }
            """;
        var accepted = CudaTestCompiler.Transpile(acceptedSource);
        var rejected = CudaTestCompiler.Transpile(rejectedSource);

        Assert.True(accepted.Succeeded, FormatDiagnostics(accepted.Diagnostics));
        Assert.False(rejected.Succeeded);
        Assert.Empty(rejected.Source);
        Assert.Contains(
            rejected.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA033");
    }

    [Fact]
    public void Transpile_LowersRangesFromEndIndexesAndSpanOperations()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Update(int[] values, int replacement)
                {
                    Span<int> all = values.AsSpan();
                    Span<int> middle = all[1..^1];
                    middle.Fill(replacement);
                    Span<int> destination = all[..middle.Length];
                    middle.CopyTo(destination);
                    bool copied = middle.TryCopyTo(destination);
                    all[^1] = copied ? all[^2] : -1;
                    all[0..1].Clear();
                    return all[^1];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class SpanAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count, int replacement, int* output)
                {
                    output[0] = ExistingAlgorithm.Update(
                        Cuda.Array(values, count),
                        replacement);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(".as_span()", result.Source, StringComparison.Ordinal);
        Assert.Contains(".slice(", result.Source, StringComparison.Ordinal);
        Assert.Contains(".fill(replacement)", result.Source, StringComparison.Ordinal);
        Assert.Contains(".copy_to(", result.Source, StringComparison.Ordinal);
        Assert.Contains(".try_copy_to(", result.Source, StringComparison.Ordinal);
        Assert.Contains(".clear()", result.Source, StringComparison.Ordinal);
        Assert.Contains(".get_length()", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_CopiesArrayRangesAndRejectsEscapingRangeStorage()
    {
        const string acceptedSource = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int seed)
                {
                    int[] values = [seed, 2, 3];
                    int[] copy = values[1..];
                    copy[0] = 9;
                    return values[1] + copy[0];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class RangeAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;
        const string rejectedSource = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int[] Slice(int[] values) => values[1..];
            }

            [TranspileToCUDA]
            internal static unsafe class RangeAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count, int* output) =>
                    output[0] = ExistingAlgorithm.Slice(Cuda.Array(values, count))[0];
            }
            """;
        const string rejectedRefSource = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static ref int First(int[] values)
                {
                    int[] copy = values[1..];
                    return ref copy[0];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class RangeAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count, int* output) =>
                    output[0] = ExistingAlgorithm.First(Cuda.Array(values, count));
            }
            """;

        var accepted = CudaTestCompiler.Transpile(acceptedSource);
        var rejected = CudaTestCompiler.Transpile(rejectedSource);
        var rejectedRef = CudaTestCompiler.Transpile(rejectedRefSource);

        Assert.True(accepted.Succeeded, FormatDiagnostics(accepted.Diagnostics));
        Assert.Contains("csharp2cuda_copy_array", accepted.Source, StringComparison.Ordinal);
        Assert.False(rejected.Succeeded);
        Assert.Empty(rejected.Source);
        Assert.Contains(
            rejected.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA039");
        Assert.False(rejectedRef.Succeeded);
        Assert.Empty(rejectedRef.Source);
        Assert.Contains(
            rejectedRef.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA039");
    }

    [Fact]
    public void Transpile_MapsIntegralMathMathFConstantsAndRemainingBitOperations()
    {
        const string source = """
            using System;
            using System.Numerics;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static ulong Calculate(int value, uint bits)
                {
                    int magnitude = Math.Abs(value);
                    int bounded = Math.Clamp(magnitude, 1, 64);
                    int direction = Math.Sign(value);
                    int smallest = Math.Min(bounded, 32);
                    int largest = Math.Max(smallest, 2);
                    bool power = BitOperations.IsPow2(bits);
                    uint rounded = BitOperations.RoundUpToPowerOf2(bits);
                    float constants = MathF.E + MathF.PI + MathF.Tau;
                    return (ulong)(largest + direction + (power ? 1 : 0) + rounded + (int)constants);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class IntegralMathAdapter
            {
                [CudaGlobal]
                private static void Run(int value, uint bits, ulong* output)
                {
                    output[0] = ExistingAlgorithm.Calculate(value, bits);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_integral_abs(value)", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_integral_clamp", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_integral_sign", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_integral_minimum", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_integral_maximum", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_is_pow2", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_u32_round_up_to_power_of_two",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_MapsEveryPortableIntegralMathOverload()
    {
        const string source = """
            using System;
            using System.Numerics;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static ulong Calculate(
                    sbyte i8,
                    byte u8,
                    short i16,
                    ushort u16,
                    int i32,
                    uint u32,
                    long i64,
                    ulong u64)
                {
                    sbyte abs8 = Math.Abs(i8);
                    short abs16 = Math.Abs(i16);
                    int abs32 = Math.Abs(i32);
                    long abs64 = Math.Abs(i64);
                    int signs = Math.Sign(i8) + Math.Sign(i16) +
                        Math.Sign(i32) + Math.Sign(i64);
                    ulong extrema =
                        (ulong)Math.Clamp(Math.Max(Math.Min(i8, (sbyte)7), (sbyte)1), (sbyte)1, (sbyte)7) +
                        Math.Clamp(Math.Max(Math.Min(u8, (byte)7), (byte)1), (byte)1, (byte)7) +
                        (ulong)Math.Clamp(Math.Max(Math.Min(i16, (short)7), (short)1), (short)1, (short)7) +
                        Math.Clamp(Math.Max(Math.Min(u16, (ushort)7), (ushort)1), (ushort)1, (ushort)7) +
                        (ulong)Math.Clamp(Math.Max(Math.Min(i32, 7), 1), 1, 7) +
                        Math.Clamp(Math.Max(Math.Min(u32, 7u), 1u), 1u, 7u) +
                        (ulong)Math.Clamp(Math.Max(Math.Min(i64, 7L), 1L), 1L, 7L) +
                        Math.Clamp(Math.Max(Math.Min(u64, 7UL), 1UL), 1UL, 7UL);
                    bool powers = BitOperations.IsPow2(i32) &&
                        BitOperations.IsPow2(i64) &&
                        BitOperations.IsPow2(u32) &&
                        BitOperations.IsPow2(u64);
                    return extrema + (ulong)(abs8 + abs16 + abs32 + abs64 + signs) +
                        BitOperations.RoundUpToPowerOf2(u32) +
                        BitOperations.RoundUpToPowerOf2(u64) +
                        (powers ? 1UL : 0UL) + (ulong)(MathF.E + MathF.PI + MathF.Tau);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class IntegralAdapter
            {
                [CudaGlobal]
                private static void Run(ulong* output) =>
                    output[0] = ExistingAlgorithm.Calculate(-1, 2, -3, 4, 8, 8, 16, 16);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_integral_abs", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_integral_sign", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_u64_round_up_to_power_of_two", result.Source, StringComparison.Ordinal);
        Assert.Contains("2.7182817", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_DoesNotTreatFixedArrayElementReadsAsStorageEscapes()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int seed)
                {
                    int[] values = [seed, 2, 3];
                    int first = values[0];
                    bool matched = values[1] == 2;
                    return first + (matched ? values[2] : 0);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class FixedReadAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA033");
    }

    [Fact]
    public void Transpile_RejectsFixedArrayEscapeThroughAssignedAlias()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int[] Create(int seed)
                {
                    int[] values = [seed, 2, 3];
                    int[] alias;
                    alias = values;
                    return alias;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class AliasEscapeAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Create(seed)[0];
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA033");
    }

    [Fact]
    public void Transpile_RejectsFixedManagedStoragePassedToUnknownCode()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate()
                {
                    int[] values = [1, 2];
                    return Array.IndexOf(values, 2);
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ExternalEscapeAdapter
            {
                [CudaGlobal]
                private static void Run(int* output) =>
                    output[0] = ExistingAlgorithm.Calculate();
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA033");
    }

    [Fact]
    public void Transpile_RejectsFixedPointerStorageReturnedFromDeviceFunction()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class PointerEscapeAdapter
            {
                [CudaDevice]
                private static int* Create()
                {
                    int* values = stackalloc int[2];
                    return values;
                }

                [CudaGlobal]
                private static void Run(int* output) => output[0] = Create()[0];
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA033");
    }

    [Fact]
    public void Transpile_RejectsRefReturnIntoFixedArrayStorage()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static ref int First(int seed)
                {
                    int[] values = [seed, 2, 3];
                    return ref values[0];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class RefEscapeAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.First(seed);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA035");
    }

    [Fact]
    public void Transpile_RejectsDeviceInternalTypesBehindAbiPointers()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal readonly record struct Pair(int Left, int Right);

            [TranspileToCUDA]
            internal static unsafe class InvalidAbiAdapter
            {
                [CudaExternalDevice]
                private static void External(Pair* pair) =>
                    throw new System.NotSupportedException();

                [CudaGlobal]
                private static void Run(Pair* pair)
                {
                    External(pair);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.True(
            result.Diagnostics.Count(static diagnostic => diagnostic.Id == "CS2CUDA036") >= 2,
            FormatDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void Transpile_RecordEqualityUsesRecordFloatingPointSemantics()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal readonly record struct Measurement(float Value)
            {
                public int Tag { get; init; }
            }

            internal static class ExistingAlgorithm
            {
                public static bool Equal(float left, float right) =>
                    new Measurement(left) == new Measurement(right);
            }

            [TranspileToCUDA]
            internal static unsafe class EqualityAdapter
            {
                [CudaGlobal]
                private static void Run(float left, float right, bool* output) =>
                    output[0] = ExistingAlgorithm.Equal(left, right);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("isnan", result.Source, StringComparison.Ordinal);
        Assert.Contains(".Tag", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RepresentsEmptyCollectionsWithoutZeroLengthCudaArrays()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate()
                {
                    int[] array = [];
                    Span<int> span = [];
                    ReadOnlySpan<int> readOnly = [];
                    return array.Length + span.Length + readOnly.Length;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class EmptyAdapter
            {
                [CudaGlobal]
                private static void Run(int* output) =>
                    output[0] = ExistingAlgorithm.Calculate();
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.DoesNotContain("_storage[0]", result.Source, StringComparison.Ordinal);
        Assert.Contains("(nullptr, 0, false)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsOversizedFixedLocalStorage()
    {
        var elements = string.Join(", ", Enumerable.Repeat("1", 1025));
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate()
                {
                    int[] values = [{{elements}}];
                    return values[0];
                }
            }

            [TranspileToCUDA]
            internal static unsafe class OversizedAdapter
            {
                [CudaGlobal]
                private static void Run(int* output) =>
                    output[0] = ExistingAlgorithm.Calculate();
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA040");
    }

    [Fact]
    public void Transpile_EvaluatesRangeReceiverAndEndpointsOnceInCSharpOrder()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class RangeOrderAdapter
            {
                [CudaDevice(Name = "view_once")]
                private static Span<int> View(int[] values, ref int order)
                {
                    order = order * 10 + 1;
                    return values.AsSpan();
                }

                [CudaDevice(Name = "endpoint_once")]
                private static int Endpoint(ref int order, int digit)
                {
                    order = order * 10 + digit;
                    return 1;
                }

                [CudaGlobal]
                private static void Run(int* values, int count, int* output)
                {
                    int order = 0;
                    Span<int> range = View(Cuda.Array(values, count), ref order)[
                        Endpoint(ref order, 2)..^Endpoint(ref order, 3)];
                    output[0] = order + range.Length;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        var receiver = result.Source.IndexOf(" = view_once(", StringComparison.Ordinal);
        var start = result.Source.IndexOf(" = endpoint_once(", receiver + 1, StringComparison.Ordinal);
        var end = result.Source.IndexOf(" = endpoint_once(", start + 1, StringComparison.Ordinal);
        Assert.True(receiver >= 0 && start > receiver && end > start, result.Source);
        Assert.Contains("csharp2cuda_index_from_end", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_AppliesUserDefinedCompoundResultConversion()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public readonly struct Counter
                {
                    public readonly int Value;

                    public Counter(int value) => Value = value;

                    public static int operator +(Counter left, int right) =>
                        left.Value + right;

                    public static implicit operator Counter(int value) => new(value);
                }

                public static int Calculate(int seed)
                {
                    Counter value = new(seed);
                    value += 3;
                    return value.Value;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class CompoundAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
        Assert.Contains("op_Addition", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("op_Implicit", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transpile_PreservesContextualConversionsAndTupleElementConversions()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public readonly struct Score
                {
                    public readonly int Value;

                    public Score(int value) => Value = value;

                    public static implicit operator Score(int value) => new(value);
                }

                private static Score Make(int value) => value;

                private static int Read(Score value) => value.Value;

                public static long Calculate(int seed)
                {
                    Score local = seed;
                    Score[] values = [seed + 1];
                    (int First, int Second) pair = (seed + 2, seed + 3);
                    (long First, long Second) widened = pair;
                    return Read(seed) + Make(seed).Value + local.Value +
                        values[0].Value + widened.First + widened.Second;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ConversionAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, long* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.DoesNotContain("Score local = seed;", result.Source, StringComparison.Ordinal);
        Assert.Contains("op_Implicit", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            result.Source.Split("op_Implicit", StringSplitOptions.None).Length >= 7,
            result.Source);
        Assert.Contains("csharp2cuda_tuple2<long long, long long>", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RejectsObservableDelegateWithClosureDiagnostic()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static int Calculate(int seed)
                {
                    Func<int> deferred = () => seed + 1;
                    return deferred();
                }
            }

            [TranspileToCUDA]
            internal static unsafe class ClosureEscapeAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA034");
    }

    [Fact]
    public void Transpile_UsesSpecificDiagnosticsForUnsupportedPatternAndSpanOperation()
    {
        const string patternSource = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class PatternAdapter
            {
                [CudaGlobal]
                private static void Run(int value, int* output)
                {
                    output[0] = value is int captured ? captured : 0;
                }
            }
            """;
        const string spanSource = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                public static void Reverse(Span<int> values) => values.Reverse();
            }

            [TranspileToCUDA]
            internal static unsafe class SpanAdapter
            {
                [CudaGlobal]
                private static void Run(int* values, int count) =>
                    ExistingAlgorithm.Reverse(Cuda.Array(values, count));
            }
            """;

        var pattern = CudaTestCompiler.Transpile(patternSource);
        var span = CudaTestCompiler.Transpile(spanSource);

        Assert.False(pattern.Succeeded);
        Assert.Contains(
            pattern.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA037");
        Assert.False(span.Succeeded);
        Assert.Contains(
            span.Diagnostics,
            static diagnostic => diagnostic.Id == "CS2CUDA039");
    }

    [Fact]
    public void Transpile_ComposesRangesParamsCapturesRefsConversionsAndTuples()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            internal static class ExistingAlgorithm
            {
                private readonly struct Score
                {
                    public readonly int Value;

                    public Score(int value) => Value = value;

                    public static implicit operator Score(int value) => new(value);
                    public static explicit operator int(Score value) => value.Value;
                }

                private static int Sum(params ReadOnlySpan<int> values)
                {
                    int result = 0;
                    foreach (int value in values)
                        result += value;
                    return result;
                }

                private static int Relay(ReadOnlySpan<int> values) => Sum(values);

                public static int Calculate(int seed)
                {
                    int[] values = [seed, 2, 3, 4];
                    int captured = 0;

                    void Add(ref int value)
                    {
                        captured += value;
                        value += 1;
                    }

                    foreach (ref int value in values.AsSpan()[1..^1])
                        Add(ref value);

                    Span<int> source = values.AsSpan()[1..];
                    source.CopyTo(values.AsSpan()[..source.Length]);
                    int sum = Relay(values.AsSpan()[..]);
                    Score score = seed;
                    int selected = seed switch
                    {
                        var value when value > 0 => (int)score,
                        _ => 0
                    };
                    (int First, (int Second, int Third)) tuple =
                        (selected, (captured, sum));
                    var (first, (second, third)) = tuple;
                    return first + second + third;
                }
            }

            [TranspileToCUDA]
            internal static unsafe class CompositionAdapter
            {
                [CudaGlobal]
                private static void Run(int seed, int* output) =>
                    output[0] = ExistingAlgorithm.Calculate(seed);
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("csharp2cuda_capture_captured", result.Source, StringComparison.Ordinal);
        Assert.Contains(".copy_to(", result.Source, StringComparison.Ordinal);
        Assert.Contains("csharp2cuda_tuple2", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_invalid", result.Source, StringComparison.Ordinal);
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}
