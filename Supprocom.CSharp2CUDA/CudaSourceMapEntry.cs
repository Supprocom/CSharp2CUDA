namespace Supprocom.CSharp2CUDA;

public sealed record CudaSourceMapEntry(
    string SourcePath,
    int SourceLine,
    int GeneratedLine);
