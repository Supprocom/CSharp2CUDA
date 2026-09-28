using Supprocom.CSharp2CUDA;

namespace Supprocom.CSharp2CUDA.Tests.FileInputs;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static unsafe class ManualMultiFileKernel
{
#pragma warning restore CA1515
    [CudaGlobal]
    public static void Apply(int* values)
    {
        int index = Cuda.ThreadIdx.X;
        values[index] = ManualHelper.AddOne(values[index]);
    }
}
