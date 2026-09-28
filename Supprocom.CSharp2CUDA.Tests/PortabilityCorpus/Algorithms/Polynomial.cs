namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class Polynomial
{
#pragma warning restore CA1515
    public static double Evaluate(double value) =>
        ((2.0 * value - 3.0) * value + 4.0) * value - 5.0;
}
