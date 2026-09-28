namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class LinearInterpolation
{
#pragma warning restore CA1515
    public static double Calculate(double start, double end, double amount) =>
        start + (end - start) * amount;
}
