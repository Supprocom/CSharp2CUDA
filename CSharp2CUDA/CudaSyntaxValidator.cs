using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharp2CUDA;

internal sealed class CudaSyntaxValidator(
    SemanticModel semanticModel,
    ImmutableArray<Diagnostic>.Builder diagnostics) : CSharpSyntaxWalker
{
    private static readonly HashSet<SyntaxKind> SupportedKinds =
    [
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.AddExpression,
        SyntaxKind.AddressOfExpression,
        SyntaxKind.AndAssignmentExpression,
        SyntaxKind.Argument,
        SyntaxKind.ArgumentList,
        SyntaxKind.ArrayInitializerExpression,
        SyntaxKind.ArrayRankSpecifier,
        SyntaxKind.ArrayType,
        SyntaxKind.BitwiseAndExpression,
        SyntaxKind.BitwiseNotExpression,
        SyntaxKind.BitwiseOrExpression,
        SyntaxKind.Block,
        SyntaxKind.BracketedArgumentList,
        SyntaxKind.BreakStatement,
        SyntaxKind.CaseSwitchLabel,
        SyntaxKind.CastExpression,
        SyntaxKind.ConditionalExpression,
        SyntaxKind.ContinueStatement,
        SyntaxKind.DefaultSwitchLabel,
        SyntaxKind.DivideAssignmentExpression,
        SyntaxKind.DivideExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ElseClause,
        SyntaxKind.EmptyStatement,
        SyntaxKind.EqualsExpression,
        SyntaxKind.EqualsValueClause,
        SyntaxKind.ExclusiveOrAssignmentExpression,
        SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.FalseLiteralExpression,
        SyntaxKind.ForStatement,
        SyntaxKind.GenericName,
        SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.IdentifierName,
        SyntaxKind.IfStatement,
        SyntaxKind.InvocationExpression,
        SyntaxKind.LeftShiftAssignmentExpression,
        SyntaxKind.LeftShiftExpression,
        SyntaxKind.LessThanExpression,
        SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.ModuloAssignmentExpression,
        SyntaxKind.ModuloExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.MultiplyExpression,
        SyntaxKind.NotEqualsExpression,
        SyntaxKind.NullLiteralExpression,
        SyntaxKind.NumericLiteralExpression,
        SyntaxKind.OrAssignmentExpression,
        SyntaxKind.ParenthesizedExpression,
        SyntaxKind.PointerIndirectionExpression,
        SyntaxKind.PointerMemberAccessExpression,
        SyntaxKind.PointerType,
        SyntaxKind.PostDecrementExpression,
        SyntaxKind.PostIncrementExpression,
        SyntaxKind.PreDecrementExpression,
        SyntaxKind.PreIncrementExpression,
        SyntaxKind.PredefinedType,
        SyntaxKind.ReturnStatement,
        SyntaxKind.RightShiftAssignmentExpression,
        SyntaxKind.RightShiftExpression,
        SyntaxKind.SimpleAssignmentExpression,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.StackAllocArrayCreationExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.SubtractExpression,
        SyntaxKind.SwitchSection,
        SyntaxKind.SwitchStatement,
        SyntaxKind.TrueLiteralExpression,
        SyntaxKind.TypeArgumentList,
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.UncheckedExpression,
        SyntaxKind.VariableDeclaration,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.WhileStatement
    ];

    public override void DefaultVisit(SyntaxNode node)
    {
        if (!SupportedKinds.Contains(node.Kind()) || HasUnsupportedOperator(node))
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        base.DefaultVisit(node);
    }

    private bool HasUnsupportedOperator(SyntaxNode node)
    {
        if (node is BinaryExpressionSyntax or AssignmentExpressionSyntax or
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax)
        {
            if (semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol
                {
                    MethodKind: MethodKind.UserDefinedOperator
                } method && !IsSupportedCudaOperator(method))
            {
                return true;
            }
        }

        return node switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.ModuloExpression) =>
                !IsIntegral(binary.Left) || !IsIntegral(binary.Right),
            AssignmentExpressionSyntax assignment when
                assignment.IsKind(SyntaxKind.ModuloAssignmentExpression) =>
                !IsIntegral(assignment.Left) || !IsIntegral(assignment.Right),
            _ => false
        };
    }

    private static bool IsSupportedCudaOperator(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == "CSharp2CUDA.CudaInt32" &&
        method.Name is "op_Equality" or "op_Inequality";

    private bool IsIntegral(ExpressionSyntax expression)
    {
        var type = semanticModel.GetTypeInfo(expression).Type;
        return type?.SpecialType is
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64;
    }

    private void ReportUnsupportedSyntax(SyntaxNode node) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedSyntax,
            node.GetLocation(),
            node.Kind().ToString()));
}
