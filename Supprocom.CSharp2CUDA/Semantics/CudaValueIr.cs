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
}
