using Supprocom.CSharp2CUDA;

[TranspileToCUDA("../Escape.cu")]
public static unsafe class InvalidClassPathKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
        values[0] = 1;
    }
}
