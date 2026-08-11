using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.PackageTests.GeneratedAttributed;

[TranspileToCUDA("cuda/GeneratedAttributed.cu")]
public static class Kernel
{
    [CudaDevice]
    public static int Invoke(int value)
    {
        return GeneratedKernel.Increment(value);
    }
}
