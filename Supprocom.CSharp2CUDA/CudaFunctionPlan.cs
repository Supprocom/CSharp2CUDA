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

internal sealed record CudaFunctionPlan(
    CudaFunctionSource Syntax,
    IMethodSymbol Symbol,
    IMethodSymbol DefinitionSymbol,
    SemanticModel Model,
    string EmittedName,
    CudaFunctionKind Kind,
    bool ExternC,
    bool IsExternal,
    bool IsPureExternal,
    bool EmitsDeclaration,
    bool HasDeviceAttribute,
    bool HasGlobalAttribute,
    bool HasExternalDeviceAttribute,
    bool IsInferred)
{
    public CudaFunctionBodyIr? Body { get; set; }
    public ImmutableArray<CudaCapturePlan> Captures { get; set; } = [];
    public bool HasInstance => !Syntax.IsConstructor &&
        !Syntax.IsLocalFunction &&
        !Syntax.IsAnonymousFunction &&
        !Symbol.IsStatic;
    public bool IsConstructor => Syntax.IsConstructor;
    public bool IsLocalFunction => Syntax.IsLocalFunction;
    public bool IsAnonymousFunction => Syntax.IsAnonymousFunction;
    public bool IsClosureFunction => IsLocalFunction || IsAnonymousFunction;
    public bool IsReadOnlyInstance => Symbol.IsReadOnly;

    public bool TryGetCapture(ISymbol symbol, out CudaCapturePlan capture)
    {
        foreach (var candidate in Captures)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.Symbol, symbol))
            {
                capture = candidate;
                return true;
            }
        }
        capture = null!;
        return false;
    }
}
