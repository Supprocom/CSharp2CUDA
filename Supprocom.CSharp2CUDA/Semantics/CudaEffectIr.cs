using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Semantics;

[Flags]
internal enum CudaEffectIr
{
    None = 0,
    Read = 1,
    Write = 2,
    Call = 4,
    Volatile = 8,
    Atomic = 16,
    Barrier = 32,
    Trap = 64
}
