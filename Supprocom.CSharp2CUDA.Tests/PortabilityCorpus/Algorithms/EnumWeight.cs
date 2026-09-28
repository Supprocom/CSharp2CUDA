namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
#pragma warning disable MA0048 // This paired enum fixture must remain in one source file for standalone corpus compilation.
#pragma warning disable CA1028 // The corpus verifies byte-backed enum ABI, so changing to int invalidates the case.
public enum ScoreBand : byte
{
#pragma warning restore CA1028
#pragma warning restore MA0048
#pragma warning restore CA1515
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class EnumWeight
{
#pragma warning restore CA1515
    public static int Calculate(ScoreBand band)
    {
        switch (band)
        {
            case ScoreBand.Low:
                return 2;
            case ScoreBand.Medium:
                return 5;
            case ScoreBand.High:
                return 9;
            default:
                return 0;
        }
    }
}
