using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Emission;

namespace Supprocom.CSharp2CUDA;

internal sealed record CudaModuleEmission(
    string Source,
    ImmutableArray<CudaEntryPoint> EntryPoints,
    ImmutableArray<CudaSourceMapEntry> SourceMap);
