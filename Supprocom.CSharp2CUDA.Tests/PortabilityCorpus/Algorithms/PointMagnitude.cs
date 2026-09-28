namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
#pragma warning disable MA0048 // This paired ABI fixture must remain in one source file for standalone corpus compilation.
#pragma warning disable CA1815 // This data-only ABI fixture intentionally has no equality surface.
#pragma warning disable MA0008 // The fixture tests the implicit sequential layout used by ordinary C# structs.
public struct SamplePoint
{
#pragma warning restore MA0008
#pragma warning restore CA1815
#pragma warning restore MA0048
#pragma warning restore CA1515
#pragma warning disable CA1051 // Public fields are the ABI shape exercised by this corpus case.
    public double X;
    public double Y;
#pragma warning restore CA1051
}

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class PointMagnitude
{
#pragma warning restore CA1515
    public static double LengthSquared(SamplePoint point) =>
        point.X * point.X + point.Y * point.Y;
}
