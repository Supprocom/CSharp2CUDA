using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaPlaceIr(
    ImmutableArray<CudaStatementIr> Prefix,
    string AccessCode,
    string AddressCode,
    ITypeSymbol Type,
    CudaEffectIr Effects,
    Location Location)
{
    public CudaViewMutability ViewMutability { get; init; }
}
