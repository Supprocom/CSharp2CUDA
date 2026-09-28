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

internal sealed class CudaFunctionSource
{
    private CudaFunctionSource(
        SyntaxNode node,
        SyntaxToken identifier,
        ParameterListSyntax parameterList,
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody,
        TypeSyntax? returnType,
        SyntaxTokenList modifiers,
        SyntaxList<AttributeListSyntax> attributeLists,
        TypeParameterListSyntax? typeParameterList,
        ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
        bool isConstructor,
        bool isLocalFunction,
        bool isAccessor,
        bool isAnonymousFunction = false,
        ExpressionSyntax? directExpressionBody = null)
    {
        Node = node;
        Identifier = identifier;
        ParameterList = parameterList;
        Body = body;
        ExpressionBody = expressionBody;
        ReturnType = returnType;
        Modifiers = modifiers;
        AttributeLists = attributeLists;
        TypeParameterList = typeParameterList;
        ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
        IsConstructor = isConstructor;
        IsLocalFunction = isLocalFunction;
        IsAccessor = isAccessor;
        IsAnonymousFunction = isAnonymousFunction;
        DirectExpressionBody = directExpressionBody;
    }

    public SyntaxNode Node { get; }
    public SyntaxToken Identifier { get; }
    public ParameterListSyntax ParameterList { get; }
    public BlockSyntax? Body { get; }
    public ArrowExpressionClauseSyntax? ExpressionBody { get; }
    public ExpressionSyntax? DirectExpressionBody { get; }
    public ExpressionSyntax? ExpressionBodyExpression =>
        DirectExpressionBody ?? ExpressionBody?.Expression;
    public TypeSyntax? ReturnType { get; }
    public SyntaxTokenList Modifiers { get; }
    public SyntaxList<AttributeListSyntax> AttributeLists { get; }
    public TypeParameterListSyntax? TypeParameterList { get; }
    public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
    public bool IsConstructor { get; }
    public bool IsLocalFunction { get; }
    public bool IsAccessor { get; }
    public bool IsAnonymousFunction { get; }

    public Location GetLocation() => Node.GetLocation();

    public static CudaFunctionSource Create(MethodDeclarationSyntax syntax) => new(
        syntax,
        syntax.Identifier,
        syntax.ParameterList,
        syntax.Body,
        syntax.ExpressionBody,
        syntax.ReturnType,
        syntax.Modifiers,
        syntax.AttributeLists,
        syntax.TypeParameterList,
        syntax.ExplicitInterfaceSpecifier,
        false,
        false,
        false);

    public static CudaFunctionSource Create(LocalFunctionStatementSyntax syntax) => new(
        syntax,
        syntax.Identifier,
        syntax.ParameterList,
        syntax.Body,
        syntax.ExpressionBody,
        syntax.ReturnType,
        syntax.Modifiers,
        syntax.AttributeLists,
        syntax.TypeParameterList,
        null,
        false,
        true,
        false);

    public static CudaFunctionSource Create(ConstructorDeclarationSyntax syntax) => new(
        syntax,
        syntax.Identifier,
        syntax.ParameterList,
        syntax.Body,
        syntax.ExpressionBody,
        null,
        syntax.Modifiers,
        syntax.AttributeLists,
        null,
        null,
        true,
        false,
        false);

    public static CudaFunctionSource Create(OperatorDeclarationSyntax syntax) => new(
        syntax,
        syntax.OperatorToken,
        syntax.ParameterList,
        syntax.Body,
        syntax.ExpressionBody,
        syntax.ReturnType,
        syntax.Modifiers,
        syntax.AttributeLists,
        null,
        null,
        false,
        false,
        false);

    public static CudaFunctionSource Create(ConversionOperatorDeclarationSyntax syntax) => new(
        syntax,
        syntax.ImplicitOrExplicitKeyword,
        syntax.ParameterList,
        syntax.Body,
        syntax.ExpressionBody,
        syntax.Type,
        syntax.Modifiers,
        syntax.AttributeLists,
        null,
        null,
        false,
        false,
        false);

    public static CudaFunctionSource Create(
        AnonymousFunctionExpressionSyntax syntax,
        IMethodSymbol method)
    {
        var parameters = syntax switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList,
            SimpleLambdaExpressionSyntax simple => SyntaxFactory.ParameterList(
                SyntaxFactory.SingletonSeparatedList(simple.Parameter)),
            AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList ??
                SyntaxFactory.ParameterList(),
            _ => SyntaxFactory.ParameterList()
        };
        var identifier = parameters.Parameters.FirstOrDefault()?.Identifier ??
            syntax.GetFirstToken();
        return new CudaFunctionSource(
            syntax,
            identifier,
            parameters,
            syntax.Body as BlockSyntax,
            null,
            null,
            default,
            default,
            null,
            null,
            false,
            false,
            false,
            true,
            syntax.Body as ExpressionSyntax);
    }

    public static CudaFunctionSource Create(
        AccessorDeclarationSyntax syntax,
        IMethodSymbol method)
    {
        var parent = syntax.Parent?.Parent;
        return parent switch
        {
            PropertyDeclarationSyntax property => CreateAccessor(
                syntax,
                method,
                property.Identifier,
                property.Type,
                property.Modifiers,
                property.AttributeLists,
                property.ExplicitInterfaceSpecifier,
                []),
            IndexerDeclarationSyntax indexer => CreateAccessor(
                syntax,
                method,
                indexer.ThisKeyword,
                indexer.Type,
                indexer.Modifiers,
                indexer.AttributeLists,
                indexer.ExplicitInterfaceSpecifier,
                indexer.ParameterList.Parameters),
            _ => throw new InvalidOperationException("CUDA accessor source is invalid.")
        };
    }

    public static CudaFunctionSource Create(
        PropertyDeclarationSyntax syntax,
        IMethodSymbol method)
    {
        if (syntax.ExpressionBody is null || method.MethodKind != MethodKind.PropertyGet)
            throw new InvalidOperationException("CUDA property source is invalid.");
        return new CudaFunctionSource(
            syntax,
            syntax.Identifier,
            SyntaxFactory.ParameterList(),
            null,
            syntax.ExpressionBody,
            syntax.Type,
            syntax.Modifiers,
            syntax.AttributeLists,
            null,
            syntax.ExplicitInterfaceSpecifier,
            false,
            false,
            true);
    }

    public static CudaFunctionSource Create(
        IndexerDeclarationSyntax syntax,
        IMethodSymbol method)
    {
        if (syntax.ExpressionBody is null || method.MethodKind != MethodKind.PropertyGet)
            throw new InvalidOperationException("CUDA indexer source is invalid.");
        return new CudaFunctionSource(
            syntax,
            syntax.ThisKeyword,
            SyntaxFactory.ParameterList(syntax.ParameterList.Parameters),
            null,
            syntax.ExpressionBody,
            syntax.Type,
            syntax.Modifiers,
            syntax.AttributeLists,
            null,
            syntax.ExplicitInterfaceSpecifier,
            false,
            false,
            true);
    }

    private static CudaFunctionSource CreateAccessor(
        AccessorDeclarationSyntax syntax,
        IMethodSymbol method,
        SyntaxToken identifier,
        TypeSyntax propertyType,
        SyntaxTokenList modifiers,
        SyntaxList<AttributeListSyntax> attributeLists,
        ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
        SeparatedSyntaxList<ParameterSyntax> sourceParameters)
    {
        var parameters = sourceParameters.ToList();
        if (method.MethodKind is MethodKind.PropertySet)
        {
            parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                .WithType(propertyType.WithoutTrivia()));
        }
        var returnType = method.ReturnsVoid
            ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
            : propertyType;
        return new CudaFunctionSource(
            syntax,
            identifier,
            SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)),
            syntax.Body,
            syntax.ExpressionBody,
            returnType,
            modifiers,
            attributeLists,
            null,
            explicitInterfaceSpecifier,
            false,
            false,
            true);
    }
}
