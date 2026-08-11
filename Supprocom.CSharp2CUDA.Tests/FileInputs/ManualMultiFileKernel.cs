using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.Tests.FileInputs;

public static unsafe class ManualMultiFileKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
        int index = Cuda.ThreadIdx.X;
        values[index] = ManualHelper.AddOne(values[index]);
    }
}
