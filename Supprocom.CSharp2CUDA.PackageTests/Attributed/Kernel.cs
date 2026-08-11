using Supprocom.CSharp2CUDA;

[TranspileToCUDA(@"cuda\Attributed.cu")]
public static unsafe class AttributedKernel
{
    [CudaGlobal]
    public static void Apply(int* values)
    {
        int index = Cuda.ThreadIdx.X;
        values[index] += 1;
    }
}
