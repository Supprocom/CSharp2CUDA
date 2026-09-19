namespace Supprocom.CSharp2CUDA.Tool;

internal enum ToolCommandKind
{
    Check,
    Scaffold,
    Refresh,
    Help
}

internal sealed record ToolCommand(
    ToolCommandKind Kind,
    string? ProjectPath,
    string? Method,
    CudaAdapterMapping? Mapping,
    string? OutputPath);

internal enum CudaAdapterMapping
{
    Elementwise,
    SingleThread
}
