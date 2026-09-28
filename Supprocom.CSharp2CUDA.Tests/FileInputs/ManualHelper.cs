using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.Tests.FileInputs;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class ManualHelper
{
#pragma warning restore CA1515
    [CudaDevice]
    public static int AddOne(int value)
    {
        return value + 1;
    }
}
