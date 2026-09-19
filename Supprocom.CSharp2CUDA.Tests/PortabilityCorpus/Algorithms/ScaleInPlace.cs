using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class ScaleInPlace
{
    public static void Apply(Span<double> values, double scale)
    {
        for (var index = 0; index < values.Length; index++)
            values[index] *= scale;
    }
}
