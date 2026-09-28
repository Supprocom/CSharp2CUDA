using System.Runtime.CompilerServices;

namespace Supprocom.CSharp2CUDA;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "CUDA dimensions are device-only intrinsic placeholders, not comparable managed values.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance properties preserve Cuda.ThreadIdx.X device-axis syntax and must not be static.")]
public readonly struct CudaDimension
{
    public int X => throw ManagedExecutionException();
    public int Y => throw ManagedExecutionException();
    public int Z => throw ManagedExecutionException();

    private static InvalidOperationException ManagedExecutionException() =>
        new("CUDA dimensions are available only during C# to CUDA transpilation.");
}
