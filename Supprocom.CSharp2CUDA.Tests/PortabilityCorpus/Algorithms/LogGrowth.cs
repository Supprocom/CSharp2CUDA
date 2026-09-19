using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class LogGrowth
{
    public static double Calculate(double value) => Math.Log(value + 1.0);
}
