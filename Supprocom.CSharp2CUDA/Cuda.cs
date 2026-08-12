using System.Runtime.CompilerServices;

namespace Supprocom.CSharp2CUDA;

public static unsafe class Cuda
{
    public static CudaDimension ThreadIdx => throw ManagedExecutionException();
    public static CudaDimension BlockIdx => throw ManagedExecutionException();
    public static CudaDimension BlockDim => throw ManagedExecutionException();
    public static CudaDimension GridDim => throw ManagedExecutionException();

    public static void SyncThreads() => throw ManagedExecutionException();

    public static void ThreadFence() => throw ManagedExecutionException();

    public static void ThreadFenceSystem() => throw ManagedExecutionException();

    public static int VolatileLoad(int* address) =>
        throw ManagedExecutionException();

    public static ulong VolatileLoad(ulong* address) =>
        throw ManagedExecutionException();

    public static int VolatileLoadInt32(byte* address, ulong byteOffset) =>
        throw ManagedExecutionException();

    public static ulong VolatileLoadUInt64(byte* address, ulong byteOffset) =>
        throw ManagedExecutionException();

    public static void VolatileStore(int* address, int value) =>
        throw ManagedExecutionException();

    public static void VolatileStore(ulong* address, ulong value) =>
        throw ManagedExecutionException();

    public static void VolatileStoreInt32(byte* address, ulong byteOffset, int value) =>
        throw ManagedExecutionException();

    public static void VolatileStoreUInt64(byte* address, ulong byteOffset, ulong value) =>
        throw ManagedExecutionException();

    public static ulong GlobalTimer() => throw ManagedExecutionException();

    public static void SyncWarp() => throw ManagedExecutionException();

    public static void SyncWarp(uint mask) => throw ManagedExecutionException();

    public static int ShuffleDownSync(uint mask, int value, uint delta, int width) =>
        throw ManagedExecutionException();

    public static void NanoSleep(uint nanoseconds) => throw ManagedExecutionException();

    public static T Shared<T>() where T : unmanaged =>
        throw ManagedExecutionException();

    public static T* SharedArray<T>(int length) where T : unmanaged =>
        throw ManagedExecutionException();

    public static byte* DynamicSharedBytes(int alignment) =>
        throw ManagedExecutionException();

    public static T* DynamicSharedView<T>(byte* storage, ulong elementOffset)
        where T : unmanaged => throw ManagedExecutionException();

    public static int AtomicAdd(ref int location, int value) =>
        throw ManagedExecutionException();

    public static int AtomicExchange(ref int location, int value) =>
        throw ManagedExecutionException();

    public static int AtomicCompareExchange(ref int location, int compare, int value) =>
        throw ManagedExecutionException();

    public static int AtomicXor(ref int location, int value) =>
        throw ManagedExecutionException();

    public static int AtomicMin(ref int location, int value) =>
        throw ManagedExecutionException();

    public static uint AtomicAdd(ref uint location, uint value) =>
        throw ManagedExecutionException();

    public static uint AtomicExchange(ref uint location, uint value) =>
        throw ManagedExecutionException();

    public static uint AtomicCompareExchange(ref uint location, uint compare, uint value) =>
        throw ManagedExecutionException();

    public static uint AtomicXor(ref uint location, uint value) =>
        throw ManagedExecutionException();

    public static uint AtomicMin(ref uint location, uint value) =>
        throw ManagedExecutionException();

    public static long AtomicAdd(ref long location, long value) =>
        throw ManagedExecutionException();

    public static long AtomicExchange(ref long location, long value) =>
        throw ManagedExecutionException();

    public static long AtomicCompareExchange(ref long location, long compare, long value) =>
        throw ManagedExecutionException();

    public static long AtomicXor(ref long location, long value) =>
        throw ManagedExecutionException();

    public static long AtomicMin(ref long location, long value) =>
        throw ManagedExecutionException();

    public static ulong AtomicAdd(ref ulong location, ulong value) =>
        throw ManagedExecutionException();

    public static ulong AtomicExchange(ref ulong location, ulong value) =>
        throw ManagedExecutionException();

    public static ulong AtomicCompareExchange(ref ulong location, ulong compare, ulong value) =>
        throw ManagedExecutionException();

    public static ulong AtomicXor(ref ulong location, ulong value) =>
        throw ManagedExecutionException();

    public static ulong AtomicMin(ref ulong location, ulong value) =>
        throw ManagedExecutionException();

    public static T AtomicAdd<T>(ref T location, T value) where T : unmanaged =>
        throw ManagedExecutionException();

    public static T AtomicExchange<T>(ref T location, T value) where T : unmanaged =>
        throw ManagedExecutionException();

    public static T AtomicCompareExchange<T>(ref T location, T compare, T value)
        where T : unmanaged => throw ManagedExecutionException();

    public static T AtomicXor<T>(ref T location, T value) where T : unmanaged =>
        throw ManagedExecutionException();

    public static T AtomicMin<T>(ref T location, T value) where T : unmanaged =>
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double DoubleAddRoundNearest(double left, double right) => left + right;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double DoubleSubtractRoundNearest(double left, double right) => left - right;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double DoubleMultiplyRoundNearest(double left, double right) => left * right;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double DoubleDivideRoundNearest(double left, double right) => left / right;

    public static double Log1p(double value) => double.LogP1(value);

    public static double Log(double value) => Math.Log(value);

    public static double Sqrt(double value) => Math.Sqrt(value);

    public static double Exp(double value) => Math.Exp(value);

    public static double Pow(double value, double power) => Math.Pow(value, power);

    public static double NaN() => double.NaN;

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
