namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public enum ScoreBand : byte
{
    Low = 1,
    Medium = 2,
    High = 3
}

public static class EnumWeight
{
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
