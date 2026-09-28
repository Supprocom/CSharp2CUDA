using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record CudaMethodSelection(
    IMethodSymbol Symbol,
    string CanonicalName);
