namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record ToolCommand(
    ToolCommandKind Kind,
    string? ProjectPath,
    string? Method,
    CudaAdapterMapping? Mapping,
    string? OutputPath);
