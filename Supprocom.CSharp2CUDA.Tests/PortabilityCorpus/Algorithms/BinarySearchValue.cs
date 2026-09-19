using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class BinarySearchValue
{
    public static int Find(ReadOnlySpan<int> values, int target)
    {
        var low = 0;
        var high = values.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var value = values[middle];
            if (value == target)
                return middle;
            if (value < target)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return -1;
    }
}
