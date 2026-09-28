using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Supprocom.CSharp2CUDA.Compiler;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

internal sealed class TestAnalyzerConfigOptionsProvider(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions global = new TestAnalyzerConfigOptions(values);

    public override AnalyzerConfigOptions GlobalOptions => global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        TestAnalyzerConfigOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        TestAnalyzerConfigOptions.Empty;
}
