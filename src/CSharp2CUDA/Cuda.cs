namespace CSharp2CUDA;

public static unsafe class Cuda
{
    public static CudaDimension ThreadIdx => throw ManagedExecutionException();
    public static CudaDimension BlockIdx => throw ManagedExecutionException();
    public static CudaDimension BlockDim => throw ManagedExecutionException();
    public static CudaDimension GridDim => throw ManagedExecutionException();

    public static void SyncThreads() => throw ManagedExecutionException();

    public static int AtomicAdd(ref int location, int value) =>
        throw ManagedExecutionException();

    public static int AtomicExchange(ref int location, int value) =>
        throw ManagedExecutionException();

    public static CudaInt32 AtomicAdd(ref CudaInt32 location, int value) =>
        throw ManagedExecutionException();

    public static CudaInt32 AtomicExchange(ref CudaInt32 location, int value) =>
        throw ManagedExecutionException();

    public static int Int(bool value) => value ? 1 : 0;

    public static bool Bool(int value) => value != 0;

    public static ulong Unsigned(long value) => unchecked((ulong)value);

    public static T* ReadOnly<T>(T* pointer) where T : unmanaged => pointer;

    public static double FloatingRemainder(double left, double right) => left % right;

    public static double NearbyInteger(double value) => Math.Round(value, MidpointRounding.ToEven);

    public static bool SignBit(double value) =>
        BitConverter.DoubleToInt64Bits(value) < 0;

    private static InvalidOperationException ManagedExecutionException() =>
        new("CUDA intrinsics are available only during C# to CUDA transpilation.");
}

public readonly struct CudaInt32 : IEquatable<CudaInt32>
{
    private readonly int value;

    private CudaInt32(int value)
    {
        this.value = value;
    }

    public static implicit operator CudaInt32(int value) => new(value);

    public static implicit operator int(CudaInt32 value) => value.value;

    public static implicit operator CudaInt32(bool value) => new(value ? 1 : 0);

    public static implicit operator bool(CudaInt32 value) => value.value != 0;

    public static bool operator ==(CudaInt32 left, CudaInt32 right) =>
        left.value == right.value;

    public static bool operator !=(CudaInt32 left, CudaInt32 right) =>
        left.value != right.value;

    public bool Equals(CudaInt32 other) => value == other.value;

    public override bool Equals(object? instance) =>
        instance is CudaInt32 other && Equals(other);

    public override int GetHashCode() => value;
}

public readonly struct CudaDimension
{
    public int X => throw ManagedExecutionException();
    public int Y => throw ManagedExecutionException();
    public int Z => throw ManagedExecutionException();

    private static InvalidOperationException ManagedExecutionException() =>
        new("CUDA dimensions are available only during C# to CUDA transpilation.");
}
