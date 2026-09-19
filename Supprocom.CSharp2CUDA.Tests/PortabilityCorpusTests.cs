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

        using var runtime = CudaTestRuntime.Create(result.Source);
        Assert.NotNull(runtime);
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

    private static string FormatDiagnostics(CudaTranspilationResult result) => string.Join(
        Environment.NewLine,
        result.Diagnostics.Select(static diagnostic => diagnostic.ToString()));
}
