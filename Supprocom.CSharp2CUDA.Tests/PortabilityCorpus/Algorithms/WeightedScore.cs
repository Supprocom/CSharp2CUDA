namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class WeightedScore
{
    public static double Calculate(
        double first,
        double second,
        double third) => first * 0.5 + second * 0.3 + third * 0.2;
}
