using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.Tests.FileInputs;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class ManualKernel
{
#pragma warning restore CA1515
    [CudaDevice]
#pragma warning disable CA1720 // This existing fixture's method name is part of the CUDA entry-point contract.
    public static int Double(int value)
    {
        return value * 2;
    }
#pragma warning restore CA1720
}
