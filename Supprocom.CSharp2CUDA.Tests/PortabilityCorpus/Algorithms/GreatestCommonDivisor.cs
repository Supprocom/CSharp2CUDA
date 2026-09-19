namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class GreatestCommonDivisor
{
    public static int Calculate(int left, int right)
    {
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }
        return left;
    }
}
