using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaEmptyStatementIr(Location Location) : CudaStatementIr(Location);
