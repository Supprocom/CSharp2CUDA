namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class FibonacciValue
{
#pragma warning restore CA1515
    public static int Calculate(int index)
    {
        var previous = 0;
        var current = 1;
        for (var position = 0; position < index; position++)
        {
            var next = previous + current;
            previous = current;
            current = next;
        }
        return previous;
    }
}
