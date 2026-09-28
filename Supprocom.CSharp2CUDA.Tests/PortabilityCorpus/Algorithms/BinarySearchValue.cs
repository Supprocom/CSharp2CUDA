using System;

namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class BinarySearchValue
{
#pragma warning restore CA1515
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
