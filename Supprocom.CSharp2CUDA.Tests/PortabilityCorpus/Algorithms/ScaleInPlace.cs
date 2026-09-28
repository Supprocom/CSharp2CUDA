using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class ScaleInPlace
{
#pragma warning restore CA1515
    public static void Apply(Span<double> values, double scale)
    {
#pragma warning disable HLQ013 // The corpus verifies indexed writable Span lowering.
        for (var index = 0; index < values.Length; index++)
            values[index] *= scale;
#pragma warning restore HLQ013
    }
}
