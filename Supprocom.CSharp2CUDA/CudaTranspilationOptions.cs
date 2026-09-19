namespace Supprocom.CSharp2CUDA;

public sealed class CudaTranspilationOptions
{
    public string NewLine { get; init; } = "\n";

    public bool EmitLineDirectives { get; init; } = true;

    public string? SourceRoot { get; init; }

    internal bool TranspileAttributedClassesOnly { get; init; }
}
