using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaExpressionStatementIr(
    CudaExpressionIr Expression,
    Location Location) : CudaStatementIr(Location);
