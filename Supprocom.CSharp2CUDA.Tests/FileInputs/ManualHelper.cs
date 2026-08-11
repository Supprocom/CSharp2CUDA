using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.Tests.FileInputs;

public static class ManualHelper
{
    [CudaDevice]
    public static int AddOne(int value)
    {
        return value + 1;
    }
}
