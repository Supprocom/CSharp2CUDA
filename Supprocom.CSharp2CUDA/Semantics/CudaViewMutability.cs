using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

internal enum CudaViewMutability
{
    None,
    Writable,
    ReadOnly
}
