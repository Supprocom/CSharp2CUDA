namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public struct SamplePoint
{
    public double X;
    public double Y;
}

public static class PointMagnitude
{
    public static double LengthSquared(SamplePoint point) =>
        point.X * point.X + point.Y * point.Y;
}
