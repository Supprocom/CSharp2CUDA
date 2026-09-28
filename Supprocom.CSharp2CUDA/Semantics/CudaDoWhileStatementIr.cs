using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaDoWhileStatementIr(
    CudaStatementIr Body,
    CudaExpressionIr Condition,
    string ContinueLabel,
    Location Location) : CudaStatementIr(Location);
