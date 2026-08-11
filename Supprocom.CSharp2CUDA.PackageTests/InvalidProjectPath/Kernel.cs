using Supprocom.CSharp2CUDA;

public static unsafe class InvalidProjectPathKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
        values[0] = 1;
    }
}
