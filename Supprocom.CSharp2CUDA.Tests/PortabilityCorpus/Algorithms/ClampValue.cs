using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class ClampValue
{
    public static double Calculate(double value, double minimum, double maximum) =>
        Math.Clamp(value, minimum, maximum);
}
