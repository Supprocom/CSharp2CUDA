using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaTrapStatementIr(Location Location) : CudaStatementIr(Location);
