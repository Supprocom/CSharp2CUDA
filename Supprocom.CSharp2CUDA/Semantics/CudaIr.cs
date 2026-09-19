using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

[Flags]
internal enum CudaEffectIr
{
    None = 0,
    Read = 1,
    Write = 2,
    Call = 4,
    Volatile = 8,
    Atomic = 16,
    Barrier = 32,
    Trap = 64
}

internal sealed record CudaFunctionBodyIr(CudaBlockStatementIr Body);

internal abstract record CudaStatementIr(Location Location);

internal sealed record CudaBlockStatementIr(
    ImmutableArray<CudaStatementIr> Statements,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaStatementGroupIr(
    ImmutableArray<CudaStatementIr> Statements,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaVariableDeclarationStatementIr(
    string TypeName,
    string Name,
    CudaValueIr? Initializer,
    bool IsConst,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaFixedArrayDeclarationStatementIr(
    string ElementTypeName,
    string Name,
    int Length,
    bool IsConst,
    ImmutableArray<CudaValueIr> Initializers,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaStorageDeclarationStatementIr(
    string ElementTypeName,
    string Name,
    CudaStorageKind Kind,
    int Length,
    int Alignment,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaExpressionStatementIr(
    CudaExpressionIr Expression,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaIfStatementIr(
    CudaExpressionIr Condition,
    CudaStatementIr WhenTrue,
    CudaStatementIr? WhenFalse,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaWhileStatementIr(
    CudaExpressionIr Condition,
    CudaStatementIr Body,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaDoWhileStatementIr(
    CudaStatementIr Body,
    CudaExpressionIr Condition,
    string ContinueLabel,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaForStatementIr(
    ImmutableArray<CudaStatementIr> Initializers,
    CudaExpressionIr? Condition,
    ImmutableArray<CudaExpressionIr> Incrementors,
    CudaStatementIr Body,
    string ContinueLabel,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaSwitchStatementIr(
    CudaExpressionIr Value,
    ImmutableArray<CudaSwitchSectionIr> Sections,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaSwitchSectionIr(
    ImmutableArray<string?> Labels,
    ImmutableArray<CudaStatementIr> Statements,
    Location Location);

internal sealed record CudaReturnStatementIr(
    CudaExpressionIr? Expression,
    Location Location) : CudaStatementIr(Location);

internal sealed record CudaBreakStatementIr(Location Location) : CudaStatementIr(Location);

internal sealed record CudaContinueStatementIr(Location Location) : CudaStatementIr(Location);

internal sealed record CudaEmptyStatementIr(Location Location) : CudaStatementIr(Location);

internal sealed record CudaTrapStatementIr(Location Location) : CudaStatementIr(Location);

internal sealed record CudaExpressionIr(
    ImmutableArray<CudaStatementIr> Prefix,
    CudaValueIr Value);

internal sealed record CudaValueIr(
    string Code,
    ITypeSymbol? Type,
    CudaEffectIr Effects,
    bool IsSimple,
    Location Location)
{
    public CudaViewMutability ViewMutability { get; init; }
}

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

internal enum CudaViewMutability
{
    None,
    Writable,
    ReadOnly
}
