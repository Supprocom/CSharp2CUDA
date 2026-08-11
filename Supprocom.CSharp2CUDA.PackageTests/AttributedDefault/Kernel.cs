using Supprocom.CSharp2CUDA;

[TranspileToCUDA("")]
public static unsafe class AttributedDefaultKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
        values[0] = 1;
    }
}
