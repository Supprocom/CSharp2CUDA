namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class FibonacciValue
{
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
