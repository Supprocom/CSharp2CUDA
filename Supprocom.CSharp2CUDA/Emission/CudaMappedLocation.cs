using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Emission;

internal sealed record CudaMappedLocation(
    string SourcePath,
    int SourceLine);
