using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Public visibility is required by test discovery or the externally compiled portability corpus.")]
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ToolSuite
{
    public const string Name = "CSharp2CUDA tool";
}
