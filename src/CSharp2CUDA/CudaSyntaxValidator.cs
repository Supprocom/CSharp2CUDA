using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharp2CUDA;

internal sealed class CudaSyntaxValidator(ImmutableArray<Diagnostic>.Builder diagnostics)
    : CSharpSyntaxWalker
{
    public override void DefaultVisit(SyntaxNode node)
    {
        if (IsUnsupported(node))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.UnsupportedSyntax,
                node.GetLocation(),
                node.Kind().ToString()));
            return;
        }

        base.DefaultVisit(node);
    }

    private static bool IsUnsupported(SyntaxNode node)
    {
        if (node is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return true;
        }

        return node.Kind() switch
        {
        SyntaxKind.ObjectCreationExpression or
        SyntaxKind.ImplicitObjectCreationExpression or
        SyntaxKind.ArrayCreationExpression or
        SyntaxKind.ImplicitArrayCreationExpression or
        SyntaxKind.AnonymousObjectCreationExpression or
        SyntaxKind.SimpleLambdaExpression or
        SyntaxKind.ParenthesizedLambdaExpression or
        SyntaxKind.AnonymousMethodExpression or
        SyntaxKind.AwaitExpression or
        SyntaxKind.QueryExpression or
        SyntaxKind.LockStatement or
        SyntaxKind.TryStatement or
        SyntaxKind.ThrowStatement or
        SyntaxKind.ThrowExpression or
        SyntaxKind.UsingStatement or
        SyntaxKind.ForEachStatement or
        SyntaxKind.ForEachVariableStatement or
        SyntaxKind.YieldBreakStatement or
        SyntaxKind.YieldReturnStatement or
        SyntaxKind.LocalFunctionStatement or
        SyntaxKind.InterpolatedStringExpression or
        SyntaxKind.ConditionalAccessExpression or
        SyntaxKind.SwitchExpression or
        SyntaxKind.WithExpression or
        SyntaxKind.IsPatternExpression or
        SyntaxKind.FixedStatement or
        SyntaxKind.CheckedExpression or
            SyntaxKind.CheckedStatement => true,
            _ => false
        };
    }
}
