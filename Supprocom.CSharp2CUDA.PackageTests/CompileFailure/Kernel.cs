using Supprocom.CSharp2CUDA;

public static class CompileFailureKernel
{
    [CudaDevice]
    public static int Broken(int value)
    {
        return missingValue + value;
    }
}
