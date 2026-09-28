namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class SumValues
{
#pragma warning restore CA1515
    // A null guard would introduce an unsupported branch into this CUDA corpus fixture.
#pragma warning disable CA1062
    public static int Calculate(int[] values)
    {
        var result = 0;
#pragma warning disable HLQ013 // Explicit indexed array traversal is part of this portability case.
        for (var index = 0; index < values.Length; index++)
            result += values[index];
#pragma warning restore HLQ013
        return result;
    }
#pragma warning restore CA1062
}
