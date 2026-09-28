using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class PositiveCount
{
#pragma warning restore CA1515
    public static int Calculate(ReadOnlySpan<double> values)
    {
        var result = 0;
#pragma warning disable HLQ004 // The corpus verifies ordinary C# foreach over ReadOnlySpan.
        foreach (var value in values)
        {
            if (value > 0.0)
                result++;
        }
#pragma warning restore HLQ004
        return result;
    }
}
