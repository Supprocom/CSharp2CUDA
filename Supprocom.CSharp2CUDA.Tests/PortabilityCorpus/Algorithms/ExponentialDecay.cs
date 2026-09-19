using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class ExponentialDecay
{
    public static double Calculate(double initial, double rate, double time) =>
        initial * Math.Exp(-rate * time);
}
