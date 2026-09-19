namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

[TranspileToCUDA]
internal static unsafe class CorpusCudaAdapters
{
    [CudaGlobal(Name = "corpus_abs_difference")]
    private static void AbsDifferenceKernel(int left, int right, int* output) =>
        output[0] = AbsDifference.Calculate(left, right);

    [CudaGlobal(Name = "corpus_clamp_value")]
    private static void ClampValueKernel(
        double value,
        double minimum,
        double maximum,
        double* output) => output[0] = ClampValue.Calculate(value, minimum, maximum);

    [CudaGlobal(Name = "corpus_polynomial")]
    private static void PolynomialKernel(double value, double* output) =>
        output[0] = Polynomial.Evaluate(value);

    [CudaGlobal(Name = "corpus_sum_values")]
    private static void SumValuesKernel(
        int* values,
        int length,
        int* output) => output[0] = SumValues.Calculate(Cuda.Array<int>(values, length));

    [CudaGlobal(Name = "corpus_mean_value")]
    private static void MeanValueKernel(
        [CudaReadOnly] double* values,
        int length,
        double* output) =>
        output[0] = MeanValue.Calculate(Cuda.ReadOnlyArray<double>(values, length));

    [CudaGlobal(Name = "corpus_minimum_value")]
    private static void MinimumValueKernel(
        [CudaReadOnly] int* values,
        int length,
        int* output) =>
        output[0] = MinimumValue.Calculate(Cuda.ReadOnlyArray<int>(values, length));

    [CudaGlobal(Name = "corpus_maximum_value")]
    private static void MaximumValueKernel(
        [CudaReadOnly] int* values,
        int length,
        int* output) =>
        output[0] = MaximumValue.Calculate(Cuda.ReadOnlyArray<int>(values, length));

    [CudaGlobal(Name = "corpus_positive_count")]
    private static void PositiveCountKernel(
        [CudaReadOnly] double* values,
        int length,
        int* output) =>
        output[0] = PositiveCount.Calculate(Cuda.ReadOnlyArray<double>(values, length));

    [CudaGlobal(Name = "corpus_binary_search")]
    private static void BinarySearchKernel(
        [CudaReadOnly] int* values,
        int length,
        int target,
        int* output) => output[0] = BinarySearchValue.Find(
        Cuda.ReadOnlyArray<int>(values, length),
        target);

    [CudaGlobal(Name = "corpus_gcd")]
    private static void GreatestCommonDivisorKernel(int left, int right, int* output) =>
        output[0] = GreatestCommonDivisor.Calculate(left, right);

    [CudaGlobal(Name = "corpus_lerp")]
    private static void LinearInterpolationKernel(
        double start,
        double end,
        double amount,
        double* output) =>
        output[0] = LinearInterpolation.Calculate(start, end, amount);

    [CudaGlobal(Name = "corpus_exponential_decay")]
    private static void ExponentialDecayKernel(
        double initial,
        double rate,
        double time,
        double* output) => output[0] = ExponentialDecay.Calculate(initial, rate, time);

    [CudaGlobal(Name = "corpus_hypotenuse")]
    private static void HypotenuseKernel(double left, double right, double* output) =>
        output[0] = Hypotenuse.Calculate(left, right);

    [CudaGlobal(Name = "corpus_power")]
    private static void PowerValueKernel(double value, double power, double* output) =>
        output[0] = PowerValue.Calculate(value, power);

    [CudaGlobal(Name = "corpus_log_growth")]
    private static void LogGrowthKernel(double value, double* output) =>
        output[0] = LogGrowth.Calculate(value);

    [CudaGlobal(Name = "corpus_rotate_bits")]
    private static void RotateBitsKernel(uint value, int count, uint* output) =>
        output[0] = RotateBits.Calculate(value, count);

    [CudaGlobal(Name = "corpus_hash_mix")]
    private static void HashMixKernel(uint value, uint* output) =>
        output[0] = HashMix.Calculate(value);

    [CudaGlobal(Name = "corpus_weighted_score")]
    private static void WeightedScoreKernel(
        double first,
        double second,
        double third,
        double* output) => output[0] = WeightedScore.Calculate(first, second, third);

    [CudaGlobal(Name = "corpus_scale_in_place")]
    private static void ScaleInPlaceKernel(double* values, int length, double scale) =>
        ScaleInPlace.Apply(Cuda.Array<double>(values, length), scale);

    [CudaGlobal(Name = "corpus_fibonacci")]
    private static void FibonacciKernel(int index, int* output) =>
        output[0] = FibonacciValue.Calculate(index);

    [CudaGlobal(Name = "corpus_point_magnitude")]
    private static void PointMagnitudeKernel(double x, double y, double* output)
    {
        SamplePoint point = default;
        point.X = x;
        point.Y = y;
        output[0] = PointMagnitude.LengthSquared(point);
    }

    [CudaGlobal(Name = "corpus_enum_weight")]
    private static void EnumWeightKernel(byte band, int* output) =>
        output[0] = EnumWeight.Calculate((ScoreBand)band);

    [CudaGlobal(Name = "corpus_generic_select")]
    private static void GenericSelectKernel(
        bool chooseFirst,
        double first,
        double second,
        double* output) =>
        output[0] = GenericSelect.Choose(chooseFirst, first, second);

    [CudaGlobal(Name = "corpus_property_counter")]
    private static void PropertyCounterKernel(int value, int* output) =>
        output[0] = PropertyCounter.Increment(value);
}
