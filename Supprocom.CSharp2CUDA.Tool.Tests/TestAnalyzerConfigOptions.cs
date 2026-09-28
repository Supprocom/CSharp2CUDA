using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Supprocom.CSharp2CUDA.Compiler;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

internal sealed class TestAnalyzerConfigOptions(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public static TestAnalyzerConfigOptions Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal));

    public override bool TryGetValue(string key, out string value) =>
        values.TryGetValue(key, out value!);
}
