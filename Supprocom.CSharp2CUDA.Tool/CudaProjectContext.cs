using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed class CudaProjectContext(
    MSBuildWorkspace workspace,
    Microsoft.CodeAnalysis.Project project,
    CSharpCompilation compilation,
    CudaProjectModel model) : IDisposable
{
    public Microsoft.CodeAnalysis.Project Project { get; } = project;
    public CSharpCompilation Compilation { get; } = compilation;
    public CudaProjectModel Model { get; } = model;

    public void Dispose() => workspace.Dispose();
}
