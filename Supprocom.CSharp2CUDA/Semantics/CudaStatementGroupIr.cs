using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaStatementGroupIr(
    ImmutableArray<CudaStatementIr> Statements,
    Location Location) : CudaStatementIr(Location);
