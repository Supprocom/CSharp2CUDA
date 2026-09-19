using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class PortabilityCorpusTests
{
    private static readonly string[] ExpectedEntryPoints =
    [
        "corpus_abs_difference",
        "corpus_binary_search",
        "corpus_clamp_value",
        "corpus_enum_weight",
        "corpus_exponential_decay",
        "corpus_fibonacci",
        "corpus_gcd",
        "corpus_generic_select",
        "corpus_hash_mix",
        "corpus_hypotenuse",
        "corpus_lerp",
        "corpus_log_growth",
        "corpus_maximum_value",
        "corpus_mean_value",
        "corpus_minimum_value",
        "corpus_polynomial",
        "corpus_positive_count",
        "corpus_point_magnitude",
        "corpus_power",
        "corpus_property_counter",
        "corpus_rotate_bits",
        "corpus_scale_in_place",
        "corpus_sum_values",
        "corpus_weighted_score"
    ];

    [Fact]
    public void OrdinaryAlgorithms_ExecuteAsManagedCode()
    {
        Assert.Equal(7, AbsDifference.Calculate(3, 10));
        Assert.Equal(4.0, ClampValue.Calculate(8.0, -2.0, 4.0));
        Assert.Equal(7.0, Polynomial.Evaluate(2.0));
        Assert.Equal(10, SumValues.Calculate([1, 2, 3, 4]));
        Assert.Equal(2.5, MeanValue.Calculate([1.0, 2.0, 3.0, 4.0]));
        Assert.Equal(-4, MinimumValue.Calculate([3, -4, 8]));
        Assert.Equal(8, MaximumValue.Calculate([3, -4, 8]));
        Assert.Equal(2, PositiveCount.Calculate([-1.0, 2.0, 0.0, 4.0]));
        Assert.Equal(2, BinarySearchValue.Find([1, 3, 5, 7], 5));
        Assert.Equal(6, GreatestCommonDivisor.Calculate(54, 24));
        Assert.Equal(12.5, LinearInterpolation.Calculate(10.0, 20.0, 0.25));
        Assert.Equal(
            8.0 * Math.Exp(-0.5),
            ExponentialDecay.Calculate(8.0, 0.25, 2.0));
        Assert.Equal(5.0, Hypotenuse.Calculate(3.0, 4.0));
        Assert.Equal(8.0, PowerValue.Calculate(2.0, 3.0));
        Assert.Equal(Math.Log(4.0), LogGrowth.Calculate(3.0));
        Assert.Equal(0x34567812u, RotateBits.Calculate(0x12345678u, 8));
        Assert.Equal(8, FibonacciValue.Calculate(6));
        Assert.Equal(14.0, WeightedScore.Calculate(10.0, 20.0, 15.0));
        Assert.Equal(25.0, PointMagnitude.LengthSquared(new SamplePoint { X = 3.0, Y = 4.0 }));
        Assert.Equal(9, EnumWeight.Calculate(ScoreBand.High));
        Assert.Equal(2.5, GenericSelect.Choose(false, 1.5, 2.5));
        Assert.Equal(8, PropertyCounter.Increment(7));

        var values = new[] { 1.0, -2.0, 4.0 };
        ScaleInPlace.Apply(values, 2.5);
        Assert.Equal([2.5, -5.0, 10.0], values);
        Assert.NotEqual(0u, HashMix.Calculate(123456789u));
    }

    [Fact]
    public void TranspileFiles_FollowsAllOrdinaryAlgorithmsFromSeparateAdapter()
    {
        var result = TranspileCorpus();

        Assert.True(result.Succeeded, FormatDiagnostics(result));
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Id == "CS0436");
        Assert.Equal(
            ExpectedEntryPoints.Order().ToArray(),
            result.EntryPoints.Select(static entry => entry.CudaName).Order().ToArray());
        Assert.Contains("AbsDifference_Calculate_", result.Source, StringComparison.Ordinal);
        Assert.Contains("BinarySearchValue_Find_", result.Source, StringComparison.Ordinal);
        Assert.Contains("GenericSelect_Choose_", result.Source, StringComparison.Ordinal);
        Assert.Contains("struct SamplePoint", result.Source, StringComparison.Ordinal);
        Assert.Contains("ScaleInPlace_Apply_", result.Source, StringComparison.Ordinal);
        Assert.NotEmpty(result.SourceMap);
    }

    [CudaFact]
    public void Nvrtc_CompilesCompleteOrdinaryAlgorithmCorpus()
    {
        var result = TranspileCorpus();
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        RetainCudaSource(result.Source);
        using var runtime = CudaTestRuntime.Create(result.Source + MathOracleSource);
        Assert.NotNull(runtime);
    }

    [CudaFact]
    public void Cuda_ExecutesCompleteOrdinaryAlgorithmCorpus()
    {
        var result = TranspileCorpus();
        Assert.True(result.Succeeded, FormatDiagnostics(result));
        RetainCudaSource(result.Source);
        using var runtime = CudaTestRuntime.Create(result.Source + MathOracleSource);

        Assert.Equal(7, ExecuteScalar<int>(runtime, "corpus_abs_difference", I32(3), I32(10)));
        AssertDoubleBits(4.0, ExecuteScalar<double>(
            runtime,
            "corpus_clamp_value",
            F64(8.0),
            F64(-2.0),
            F64(4.0)), "clamp");
        AssertDoubleBits(7.0, ExecuteScalar<double>(runtime, "corpus_polynomial", F64(2.0)),
            "polynomial");
        Assert.Equal(6, ExecuteScalar<int>(runtime, "corpus_gcd", I32(54), I32(24)));
        AssertDoubleBits(12.5, ExecuteScalar<double>(
            runtime,
            "corpus_lerp",
            F64(10.0),
            F64(20.0),
            F64(0.25)), "linear interpolation");
        AssertMathOracle(runtime, "exponential_decay", F64(8.0), F64(0.25), F64(2.0));
        AssertMathOracle(runtime, "hypotenuse", F64(3.0), F64(4.0));
        AssertMathOracle(runtime, "power", F64(2.0), F64(3.0));
        AssertMathOracle(runtime, "log_growth", F64(3.0));
        Assert.Equal(
            RotateBits.Calculate(0x12345678u, 8),
            ExecuteScalar<uint>(runtime, "corpus_rotate_bits", 0x12345678u, I32(8)));
        Assert.Equal(
            HashMix.Calculate(123456789u),
            ExecuteScalar<uint>(runtime, "corpus_hash_mix", 123456789u));
        AssertDoubleBits(14.0, ExecuteScalar<double>(
            runtime,
            "corpus_weighted_score",
            F64(10.0),
            F64(20.0),
            F64(15.0)), "weighted score");
        Assert.Equal(8, ExecuteScalar<int>(runtime, "corpus_fibonacci", I32(6)));
        AssertDoubleBits(25.0, ExecuteScalar<double>(
            runtime,
            "corpus_point_magnitude",
            F64(3.0),
            F64(4.0)), "point magnitude");
        Assert.Equal(9, ExecuteScalar<int>(runtime, "corpus_enum_weight", 3));
        AssertDoubleBits(2.5, ExecuteScalar<double>(
            runtime,
            "corpus_generic_select",
            0,
            F64(1.5),
            F64(2.5)), "generic selection");
        Assert.Equal(8, ExecuteScalar<int>(runtime, "corpus_property_counter", I32(7)));

        ExecuteArrayAlgorithms(runtime);
    }

    private static CudaTranspilationResult TranspileCorpus()
    {
        var corpus = Path.Combine(AppContext.BaseDirectory, "PortabilityCorpus");
        var paths = Directory.GetFiles(corpus, "*.cs", SearchOption.AllDirectories);
        Assert.Equal(25, paths.Length);
        return CudaTranspiler.TranspileFiles(
            paths,
            options: new CudaTranspilationOptions
            {
                SourceRoot = corpus,
                TranspileAttributedClassesOnly = true
            });
    }

    private static void ExecuteArrayAlgorithms(CudaTestRuntime runtime)
    {
        var integers = new[] { 1, 3, 5, 7 };
        var integerInput = runtime.Allocate<int>(integers);
        var integerOutput = runtime.Allocate<int>([0]);
        var doubles = new[] { -1.0, 2.0, 0.0, 4.0 };
        var doubleInput = runtime.Allocate<double>(doubles);
        var doubleOutput = runtime.Allocate<double>([0.0]);
        try
        {
            runtime.Launch("corpus_sum_values", 1, 1, 0, integerInput, I32(integers.Length),
                integerOutput);
            Assert.Equal(SumValues.Calculate(integers), runtime.Read<int>(integerOutput, 1)[0]);

            runtime.Launch("corpus_minimum_value", 1, 1, 0, integerInput,
                I32(integers.Length), integerOutput);
            Assert.Equal(MinimumValue.Calculate(integers), runtime.Read<int>(integerOutput, 1)[0]);

            runtime.Launch("corpus_maximum_value", 1, 1, 0, integerInput,
                I32(integers.Length), integerOutput);
            Assert.Equal(MaximumValue.Calculate(integers), runtime.Read<int>(integerOutput, 1)[0]);

            runtime.Launch("corpus_binary_search", 1, 1, 0, integerInput,
                I32(integers.Length), I32(5), integerOutput);
            Assert.Equal(BinarySearchValue.Find(integers, 5),
                runtime.Read<int>(integerOutput, 1)[0]);

            runtime.Launch("corpus_mean_value", 1, 1, 0, doubleInput, I32(doubles.Length),
                doubleOutput);
            AssertDoubleBits(MeanValue.Calculate(doubles),
                runtime.Read<double>(doubleOutput, 1)[0], "mean");

            runtime.Launch("corpus_positive_count", 1, 1, 0, doubleInput,
                I32(doubles.Length), integerOutput);
            Assert.Equal(PositiveCount.Calculate(doubles), runtime.Read<int>(integerOutput, 1)[0]);

            runtime.Launch("corpus_scale_in_place", 1, 1, 0, doubleInput,
                I32(doubles.Length), F64(2.5));
            ScaleInPlace.Apply(doubles, 2.5);
            var scaled = runtime.Read<double>(doubleInput, doubles.Length);
            for (var index = 0; index < scaled.Length; index++)
                AssertDoubleBits(doubles[index], scaled[index], $"scale index {index}");
        }
        finally
        {
            runtime.Free(doubleOutput);
            runtime.Free(doubleInput);
            runtime.Free(integerOutput);
            runtime.Free(integerInput);
        }
    }

    private static T ExecuteScalar<T>(
        CudaTestRuntime runtime,
        string functionName,
        params ulong[] arguments)
        where T : unmanaged
    {
        var output = runtime.Allocate<T>(new T[1]);
        try
        {
            var launchArguments = new ulong[arguments.Length + 1];
            arguments.CopyTo(launchArguments, 0);
            launchArguments[^1] = output;
            runtime.Launch(functionName, 1, 1, 0, launchArguments);
            return runtime.Read<T>(output, 1)[0];
        }
        finally
        {
            runtime.Free(output);
        }
    }

    private static void AssertMathOracle(
        CudaTestRuntime runtime,
        string name,
        params ulong[] arguments)
    {
        var actual = ExecuteScalar<double>(runtime, "corpus_" + name, arguments);
        var expected = ExecuteScalar<double>(runtime, "corpus_oracle_" + name, arguments);
        AssertDoubleBits(expected, actual, name);
    }

    private static void AssertDoubleBits(double expected, double actual, string operation)
    {
        var expectedBits = BitConverter.DoubleToUInt64Bits(expected);
        var actualBits = BitConverter.DoubleToUInt64Bits(actual);
        Assert.True(
            expectedBits == actualBits,
            $"{operation} produced 0x{actualBits:x16} instead of 0x{expectedBits:x16}.");
    }

    private static ulong F64(double value) => BitConverter.DoubleToUInt64Bits(value);

    private static ulong I32(int value) => unchecked((uint)value);

    private static void RetainCudaSource(string source)
    {
        var directory = Environment.GetEnvironmentVariable("CSHARP2CUDA_EVIDENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "portability-corpus.cu"), source);
    }

    private static string FormatDiagnostics(CudaTranspilationResult result) => string.Join(
        Environment.NewLine,
        result.Diagnostics.Select(static diagnostic => diagnostic.ToString()));

    private const string MathOracleSource = """

        extern "C" __global__ void corpus_oracle_exponential_decay(
            double initial,
            double rate,
            double time,
            double* output)
        {
            output[0] = __dmul_rn(initial, exp(__dmul_rn(-rate, time)));
        }

        extern "C" __global__ void corpus_oracle_hypotenuse(
            double left,
            double right,
            double* output)
        {
            output[0] = sqrt(__dadd_rn(__dmul_rn(left, left), __dmul_rn(right, right)));
        }

        extern "C" __global__ void corpus_oracle_power(
            double value,
            double power,
            double* output)
        {
            output[0] = pow(value, power);
        }

        extern "C" __global__ void corpus_oracle_log_growth(double value, double* output)
        {
            output[0] = log(__dadd_rn(value, 1.0));
        }
        """;
}
