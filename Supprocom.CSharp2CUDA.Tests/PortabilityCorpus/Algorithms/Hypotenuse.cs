using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class Hypotenuse
{
    public static double Calculate(double left, double right) =>
        Math.Sqrt(left * left + right * right);
}
