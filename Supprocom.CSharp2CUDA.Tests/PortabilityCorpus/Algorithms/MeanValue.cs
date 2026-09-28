using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class MeanValue
{
#pragma warning restore CA1515
    public static double Calculate(ReadOnlySpan<double> values)
    {
        var total = 0.0;
#pragma warning disable HLQ013 // Keep the indexed source form in the portability corpus.
        for (var index = 0; index < values.Length; index++)
            total += values[index];
#pragma warning restore HLQ013
        return total / values.Length;
    }
}
