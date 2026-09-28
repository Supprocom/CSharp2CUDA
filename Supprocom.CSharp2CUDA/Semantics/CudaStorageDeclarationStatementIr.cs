using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaStorageDeclarationStatementIr(
    string ElementTypeName,
    string Name,
    CudaStorageKind Kind,
    int Length,
    int Alignment,
    Location Location) : CudaStatementIr(Location);
