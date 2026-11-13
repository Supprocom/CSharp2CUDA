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

internal sealed record CudaFieldPlan(
    FieldDeclarationSyntax Declaration,
    VariableDeclaratorSyntax Variable,
    IFieldSymbol Symbol,
    int InlineArrayLength)
{
    // A pointer-typed field with an inline length is a fixed buffer or [CudaInlineArray] storage
    // and its element is the pointee. The element field of an [InlineArray] structure already has
    // the element type.
    public ITypeSymbol ElementType =>
        InlineArrayLength > 0 && Symbol.Type is IPointerTypeSymbol pointer
            ? pointer.PointedAtType
            : Symbol.Type;

    public bool IsInlineArrayElementField =>
        InlineArrayLength > 0 && Symbol.Type is not IPointerTypeSymbol;
}
