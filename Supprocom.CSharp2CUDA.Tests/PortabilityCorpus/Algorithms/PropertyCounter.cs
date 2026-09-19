namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public struct SampleCounter
{
    public int Value { get; set; }

    public SampleCounter(int value)
    {
        Value = value;
    }

    public void Increment() => Value++;
}

public static class PropertyCounter
{
    public static int Increment(int value)
    {
        var counter = new SampleCounter(value);
        counter.Increment();
        return counter.Value;
    }
}
