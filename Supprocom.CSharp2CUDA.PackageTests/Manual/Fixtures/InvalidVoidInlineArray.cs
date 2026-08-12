using Supprocom.CSharp2CUDA;

internal static unsafe class InvalidVoidInlineArrayModule
{
    public struct InvalidStorage
    {
        [CudaInlineArray(3)]
        public void* values;
    }

    [CudaGlobal]
    private static void Run()
    {
    }
}
