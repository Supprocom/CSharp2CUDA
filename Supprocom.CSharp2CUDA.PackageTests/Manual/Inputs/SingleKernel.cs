using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.PackageTests.Manual.Inputs;

public static class SingleKernel
{
    [CudaDevice]
    public static int Double(int value)
    {
        return value * 2;
    }
}
