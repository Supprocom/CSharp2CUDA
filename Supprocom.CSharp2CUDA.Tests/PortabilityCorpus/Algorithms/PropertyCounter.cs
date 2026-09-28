namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
#pragma warning disable MA0048 // This paired ABI fixture must remain in one source file for standalone corpus compilation.
#pragma warning disable CA1815 // This mutable counter intentionally tests property access, not equality operators.
public struct SampleCounter
{
#pragma warning restore CA1815
#pragma warning restore MA0048
#pragma warning restore CA1515
    public int Value { get; set; }

    public SampleCounter(int value)
    {
        Value = value;
    }

    public void Increment() => Value++;
}

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class PropertyCounter
{
#pragma warning restore CA1515
    public static int Increment(int value)
    {
        var counter = new SampleCounter(value);
        counter.Increment();
        return counter.Value;
    }
}
