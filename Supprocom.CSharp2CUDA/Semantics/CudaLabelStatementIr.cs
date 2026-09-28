using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaLabelStatementIr(
    string Label,
    Location Location) : CudaStatementIr(Location);
