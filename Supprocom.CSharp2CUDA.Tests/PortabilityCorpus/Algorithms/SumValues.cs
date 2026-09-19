namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class SumValues
{
    public static int Calculate(int[] values)
    {
        var result = 0;
        for (var index = 0; index < values.Length; index++)
            result += values[index];
        return result;
    }
}
