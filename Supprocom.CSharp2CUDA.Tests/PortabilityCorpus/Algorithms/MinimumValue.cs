using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class MinimumValue
{
#pragma warning restore CA1515
    public static int Calculate(ReadOnlySpan<int> values)
    {
        var result = values[0];
#pragma warning disable HLQ013 // Start at one after seeding from element zero.
        for (var index = 1; index < values.Length; index++)
        {
            if (values[index] < result)
                result = values[index];
        }
#pragma warning restore HLQ013
        return result;
    }
}
