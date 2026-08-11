using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.PackageTests.Manual.Inputs;

public static class MultiKernel
{
    [CudaDevice]
    public static int Apply(int value)
    {
        return MultiHelper.AddOne(value);
    }
}
