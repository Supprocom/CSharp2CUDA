using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA;

public sealed class CudaTranspilationResult
{
    internal CudaTranspilationResult(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string? requestedOutputPath = null)
    {
        Source = source;
        Diagnostics = diagnostics;
        RequestedOutputPath = requestedOutputPath;
    }

    public string Source { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    internal string? RequestedOutputPath { get; }
    public bool Succeeded => Diagnostics.All(static diagnostic =>
        diagnostic.Severity != DiagnosticSeverity.Error);
}
