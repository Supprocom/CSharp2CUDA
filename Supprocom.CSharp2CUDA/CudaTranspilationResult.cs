using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA;

public sealed class CudaTranspilationResult
{
    internal CudaTranspilationResult(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string? requestedOutputPath = null,
        ImmutableArray<CudaEntryPoint> entryPoints = default,
        ImmutableArray<CudaSourceMapEntry> sourceMap = default)
    {
        Source = source;
        Diagnostics = diagnostics;
        RequestedOutputPath = requestedOutputPath;
        EntryPoints = entryPoints.IsDefault ? [] : entryPoints;
        SourceMap = sourceMap.IsDefault ? [] : sourceMap;
    }

    public string Source { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public ImmutableArray<CudaEntryPoint> EntryPoints { get; }
    public ImmutableArray<CudaSourceMapEntry> SourceMap { get; }
    internal string? RequestedOutputPath { get; }
    public bool Succeeded => Diagnostics.All(static diagnostic =>
        diagnostic.Severity != DiagnosticSeverity.Error);
}
