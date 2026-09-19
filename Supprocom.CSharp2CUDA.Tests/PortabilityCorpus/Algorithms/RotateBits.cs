using System.Numerics;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class RotateBits
{
    public static uint Calculate(uint value, int count) =>
        BitOperations.RotateLeft(value, count);
}
