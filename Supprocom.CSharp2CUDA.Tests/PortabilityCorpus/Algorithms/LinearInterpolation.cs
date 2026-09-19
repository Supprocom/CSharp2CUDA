namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class LinearInterpolation
{
    public static double Calculate(double start, double end, double amount) =>
        start + (end - start) * amount;
}
