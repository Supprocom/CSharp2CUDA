using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.PackageTests.Manual.Inputs;

public static class MultiHelper
{
    [CudaDevice]
    public static int AddOne(int value)
    {
        return value + 1;
    }
}
