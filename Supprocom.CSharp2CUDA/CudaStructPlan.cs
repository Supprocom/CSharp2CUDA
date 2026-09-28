using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Supprocom.CSharp2CUDA.Compilation;
using Supprocom.CSharp2CUDA.Emission;
using Supprocom.CSharp2CUDA.Semantics;

namespace Supprocom.CSharp2CUDA;

internal sealed class CudaStructPlan(
    TypeDeclarationSyntax syntax,
    INamedTypeSymbol symbol,
    INamedTypeSymbol definitionSymbol,
    SemanticModel model,
    string emittedName,
    bool isExternal)
{
    public TypeDeclarationSyntax Syntax { get; } = syntax;
    public INamedTypeSymbol Symbol { get; } = symbol;
    public INamedTypeSymbol DefinitionSymbol { get; } = definitionSymbol;
    public SemanticModel Model { get; } = model;
    public string EmittedName { get; } = emittedName;
    public bool IsExternal { get; } = isExternal;
    public bool IsPositionalRecord => Syntax is RecordDeclarationSyntax { ParameterList: not null };
    public List<CudaFieldPlan> Fields { get; } = [];
    public List<CudaPropertyPlan> Properties { get; } = [];
    public CudaStructLayout? Layout { get; set; }
}
