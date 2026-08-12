using Supprocom.CSharp2CUDA;

[TranspileToCUDA(@"cuda\MaintenanceContracts.cu")]
public static unsafe class MaintenanceContractKernel
{
    public struct Payload
    {
        [CudaInlineArray(3)]
        public int* values;
    }

    [CudaGlobal(Name = "maintenance_contracts")]
    public static void Run(byte* mapped, Payload* payload, ulong* output)
    {
        int ready = Cuda.VolatileLoadInt32(mapped, 0UL);
        payload->values[0] = ready;
        Cuda.VolatileStoreInt32(mapped, 4UL, ready + 1);
        output[0] = Cuda.GlobalTimer();
    }
}
