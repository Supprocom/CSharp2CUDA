namespace Supprocom.CSharp2CUDA;

public sealed class CudaTranspilationOptions
{
    public string NewLine { get; init; } = "\n";

    public bool EmitLineDirectives { get; init; } = true;

    public string? SourceRoot { get; init; }

    /// <summary>
    /// Gets the largest element count accepted for an <c>[InlineArray]</c> structure.
    /// The default is 4096.
    /// </summary>
    public int MaxInlineArrayLength { get; init; } = 4096;

    internal bool TranspileAttributedClassesOnly { get; init; }
}
