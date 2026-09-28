using System.Runtime.CompilerServices;

namespace Supprocom.CSharp2CUDA;

public readonly struct CudaInt32 : IEquatable<CudaInt32>
{
    private readonly int value;

    private CudaInt32(int value)
    {
        this.value = value;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "Implicit CUDA scalar conversion is the portable source contract; named helpers would add unsupported intrinsic surface.")]
    public static implicit operator CudaInt32(int value) => new(value);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "Implicit CUDA scalar conversion is the portable source contract; named helpers would add unsupported intrinsic surface.")]
    public static implicit operator int(CudaInt32 value) => value.value;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "Implicit CUDA scalar conversion is the portable source contract; named helpers would add unsupported intrinsic surface.")]
    public static implicit operator CudaInt32(bool value) => new(value ? 1 : 0);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "Implicit CUDA scalar conversion is the portable source contract; named helpers would add unsupported intrinsic surface.")]
    public static implicit operator bool(CudaInt32 value) => value.value != 0;

    public static bool operator ==(CudaInt32 left, CudaInt32 right) =>
        left.value == right.value;

    public static bool operator !=(CudaInt32 left, CudaInt32 right) =>
        left.value != right.value;

    public bool Equals(CudaInt32 other) => value == other.value;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1725:Parameter names should match base declaration", Justification = "Preserve the published Equals(instance:) named-argument source contract.")]
    public override bool Equals(object? instance) =>
        instance is CudaInt32 other && Equals(other);

    public override int GetHashCode() => value;
}
