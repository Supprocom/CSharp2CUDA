using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Emission;

namespace Supprocom.CSharp2CUDA;

internal sealed record CudaModuleSection(
    string Source,
    ImmutableArray<CudaSourceMapEntry> SourceMap)
{
    public static CudaModuleSection Raw(string source) => new(source, []);
}
