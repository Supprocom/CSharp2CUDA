using System.Numerics;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class RotateBits
{
#pragma warning restore CA1515
    public static uint Calculate(uint value, int count) =>
        BitOperations.RotateLeft(value, count);
}
