using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class PositiveCount
{
    public static int Calculate(ReadOnlySpan<double> values)
    {
        var result = 0;
        foreach (var value in values)
        {
            if (value > 0.0)
                result++;
        }
        return result;
    }
}
