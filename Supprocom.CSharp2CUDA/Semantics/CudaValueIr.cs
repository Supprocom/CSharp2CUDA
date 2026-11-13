using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaValueIr(
    string Code,
    ITypeSymbol? Type,
    CudaEffectIr Effects,
    bool IsSimple,
    Location Location)
{
    public CudaViewMutability ViewMutability { get; init; }

    // True when Type is a pointer whose pointee lives in read-only storage, so a declaration
    // that holds this value must be a pointer to const.
    public bool PointsToReadOnly { get; init; }
}
