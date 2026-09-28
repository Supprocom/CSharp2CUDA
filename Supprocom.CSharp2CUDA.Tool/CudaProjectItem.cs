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

internal sealed record CudaProjectItem(
    string Include,
    string? FullPath,
    ImmutableDictionary<string, string> Metadata);
