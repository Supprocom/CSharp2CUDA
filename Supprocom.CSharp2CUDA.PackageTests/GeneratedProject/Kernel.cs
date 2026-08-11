using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.PackageTests.GeneratedProject;

public static class Kernel
{
    [CudaDevice]
    public static int Invoke(int value)
    {
        return GeneratedKernel.Increment(value);
    }
}
