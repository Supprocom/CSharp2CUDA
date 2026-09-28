using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Semantics;

namespace Supprocom.CSharp2CUDA.Emission;

internal sealed record CudaBodyEmission(
    string Source,
    ImmutableArray<CudaSourceMapEntry> SourceMap);
