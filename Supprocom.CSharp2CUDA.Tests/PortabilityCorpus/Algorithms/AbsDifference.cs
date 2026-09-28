namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class AbsDifference
{
#pragma warning restore CA1515
    public static int Calculate(int left, int right) =>
        left >= right ? left - right : right - left;
}
