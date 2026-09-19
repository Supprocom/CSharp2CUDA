namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class AbsDifference
{
    public static int Calculate(int left, int right) =>
        left >= right ? left - right : right - left;
}
