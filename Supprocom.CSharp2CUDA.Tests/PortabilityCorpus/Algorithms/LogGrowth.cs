using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class LogGrowth
{
#pragma warning restore CA1515
    public static double Calculate(double value) => Math.Log(value + 1.0);
}
