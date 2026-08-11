using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.Tests.FileInputs;

public static class ManualKernel
{
    [CudaDevice]
    public static int Double(int value)
    {
        return value * 2;
    }
}
