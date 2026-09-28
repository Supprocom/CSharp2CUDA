using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed record CudaVariableDeclarationStatementIr(
    string TypeName,
    string Name,
    CudaValueIr? Initializer,
    bool IsConst,
    Location Location) : CudaStatementIr(Location);
