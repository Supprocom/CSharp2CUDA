using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaWhileStatementIr(
    CudaExpressionIr Condition,
    CudaStatementIr Body,
    Location Location) : CudaStatementIr(Location);
