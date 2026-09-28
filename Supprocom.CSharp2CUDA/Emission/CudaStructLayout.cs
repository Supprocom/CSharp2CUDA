using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Compilation;

namespace Supprocom.CSharp2CUDA.Emission;

internal sealed record CudaStructLayout(
    int Size,
    int Alignment,
    int Pack,
    bool IsExplicit,
    ImmutableArray<CudaFieldLayout> Fields);
