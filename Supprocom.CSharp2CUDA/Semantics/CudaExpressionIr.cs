using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaExpressionIr(
    ImmutableArray<CudaStatementIr> Prefix,
    CudaValueIr Value);
