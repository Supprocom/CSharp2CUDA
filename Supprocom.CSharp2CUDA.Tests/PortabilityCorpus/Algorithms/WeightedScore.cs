namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class WeightedScore
{
#pragma warning restore CA1515
    public static double Calculate(
        double first,
        double second,
        double third) => first * 0.5 + second * 0.3 + third * 0.2;
}
