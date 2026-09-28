using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaForStatementIr(
    ImmutableArray<CudaStatementIr> Initializers,
    CudaExpressionIr? Condition,
    ImmutableArray<CudaExpressionIr> Incrementors,
    CudaStatementIr Body,
    string ContinueLabel,
    Location Location) : CudaStatementIr(Location);
