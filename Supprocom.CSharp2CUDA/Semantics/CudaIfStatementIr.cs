using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaIfStatementIr(
    CudaExpressionIr Condition,
    CudaStatementIr WhenTrue,
    CudaStatementIr? WhenFalse,
    Location Location) : CudaStatementIr(Location);
