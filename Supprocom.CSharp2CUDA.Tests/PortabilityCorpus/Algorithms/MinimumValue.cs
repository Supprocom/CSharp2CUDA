using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class MinimumValue
{
    public static int Calculate(ReadOnlySpan<int> values)
    {
        var result = values[0];
        for (var index = 1; index < values.Length; index++)
        {
            if (values[index] < result)
                result = values[index];
        }
        return result;
    }
}
