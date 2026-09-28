using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaFixedArrayDeclarationStatementIr(
    string ElementTypeName,
    string Name,
    int Length,
    bool IsConst,
    ImmutableArray<CudaValueIr> Initializers,
    Location Location) : CudaStatementIr(Location)
{
    public CudaFixedArrayBindingKind BindingKind { get; init; }

    public string? BindingName { get; init; }

    public bool ZeroInitialize { get; init; }
}
