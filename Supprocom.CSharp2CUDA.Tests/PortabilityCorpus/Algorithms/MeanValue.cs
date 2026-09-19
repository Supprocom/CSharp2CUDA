using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class MeanValue
{
    public static double Calculate(ReadOnlySpan<double> values)
    {
        var total = 0.0;
        for (var index = 0; index < values.Length; index++)
            total += values[index];
        return total / values.Length;
    }
}
