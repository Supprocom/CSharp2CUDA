using Supprocom.CSharp2CUDA;

public static unsafe class DedicatedKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
        int index = Cuda.ThreadIdx.X;
        values[index] += 1;
    }
}
