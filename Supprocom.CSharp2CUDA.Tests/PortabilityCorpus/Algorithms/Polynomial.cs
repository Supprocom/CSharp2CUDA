namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class Polynomial
{
    public static double Evaluate(double value) =>
        ((2.0 * value - 3.0) * value + 4.0) * value - 5.0;
}
