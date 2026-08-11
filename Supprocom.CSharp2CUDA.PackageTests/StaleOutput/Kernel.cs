using Supprocom.CSharp2CUDA;

[TranspileToCUDA("../ignored.cu")]
public static unsafe class StaleOutputKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
#if FAIL
        values[0] = missingValue;
#else
        values[0] = 1;
#endif
    }
}
