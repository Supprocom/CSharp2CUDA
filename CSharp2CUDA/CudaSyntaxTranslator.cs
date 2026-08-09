using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharp2CUDA;

internal sealed class CudaSyntaxTranslator(
    CudaEmissionPlan plan,
    SemanticModel semanticModel,
    ImmutableArray<Diagnostic>.Builder diagnostics) : CSharpSyntaxRewriter
{
    private int deepReadOnlyContext;
    private readonly List<KeyValuePair<string, string>> fixedLocalArrays = [];
    private readonly string sourceText = semanticModel.SyntaxTree.GetText().ToString();
    private int fixedLocalArrayMarker;

    public string ExpandFixedLocalArrays(string text)
    {
        foreach (var fixedLocalArray in fixedLocalArrays)
        {
            text = text.Replace(
                fixedLocalArray.Key + ";",
                fixedLocalArray.Value,
                StringComparison.Ordinal);
        }
        fixedLocalArrays.Clear();
        return text;
    }

    public string TranslateType(TypeSyntax syntax, bool deepReadOnly)
    {
        var type = semanticModel.GetTypeInfo(syntax).Type;
        if (type is null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.UnsupportedType,
                syntax.GetLocation(),
                syntax.ToString()));
            return "csharp2cuda_unsupported_type";
        }
        return plan.FormatType(type, deepReadOnly, syntax.GetLocation());
    }

    public override SyntaxNode? VisitPredefinedType(PredefinedTypeSyntax node) =>
        CreateTypeNode(TranslateType(node, deepReadOnlyContext > 0), node);

    public override SyntaxNode? VisitPointerType(PointerTypeSyntax node) =>
        CreateTypeNode(TranslateType(node, deepReadOnlyContext > 0), node);

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.ValueText == "var")
        {
            var type = semanticModel.GetTypeInfo(node).Type;
            if (type is not null)
                return CreateTypeNode(plan.FormatType(type, false, node.GetLocation()), node);
        }

        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (plan.TryGetIdentifier(symbol, out var name))
            return CreateIdentifier(name, node);
        if (symbol is INamedTypeSymbol namedType &&
            namedType.ToDisplayString() == "CSharp2CUDA.CudaInt32")
        {
            return CreateTypeNode("int", node);
        }
        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var symbol = semanticModel.GetDeclaredSymbol(node);
        var visited = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node)!;
        if (!plan.TryGetIdentifier(symbol, out var name))
            return visited;
        return visited.WithIdentifier(CreateIdentifierToken(node.Identifier, name));
    }

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.NullLiteralExpression))
            return SyntaxFactory.IdentifierName("nullptr").WithTriviaFrom(node);
        if (!node.IsKind(SyntaxKind.NumericLiteralExpression))
            return base.VisitLiteralExpression(node);

        var replacement = TranslateNumericLiteral(node.Token);
        if (replacement == node.Token.Text)
            return base.VisitLiteralExpression(node);
        return node.WithToken(CreateNumericLiteralToken(node.Token, replacement));
    }

    public override SyntaxNode? VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.UncheckedKeyword))
            return Visit(node.Expression)?.WithTriviaFrom(node);
        return base.VisitCheckedExpression(node);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        if (plan.IsFixedLocalArray(node))
            return TranslateFixedLocalArray(node);

        var readOnly = node.Declaration.Variables.Count > 0 &&
            node.Declaration.Variables.All(variable => IsCudaMethod(
                variable.Initializer?.Value,
                nameof(Cuda.ReadOnly)));
        if (readOnly)
            deepReadOnlyContext++;
        var visited = base.VisitLocalDeclarationStatement(node);
        if (readOnly)
            deepReadOnlyContext--;
        return visited;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var method = semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
        var call = method is null ? null : plan.GetCallPlan(method);
        if (call is null)
            return base.VisitInvocationExpression(node);

        return call.Kind switch
        {
            CudaCallKind.PlannedFunction or CudaCallKind.Direct =>
                ReplaceInvocationName(node, call.Name),
            CudaCallKind.Atomic => TranslateAtomic(node, call.Name),
            CudaCallKind.BooleanToInteger => TranslateBooleanToInteger(node),
            CudaCallKind.IntegerToBoolean => TranslateIntegerToBoolean(node),
            CudaCallKind.SignedToUnsigned => TranslateSignedToUnsigned(node),
            CudaCallKind.Unwrap => UnwrapSingleArgument(node),
            _ => base.VisitInvocationExpression(node)
        };
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (plan.TryGetDimensionReplacement(node, out var replacement))
            return SyntaxFactory.ParseExpression(replacement).WithTriviaFrom(node);

        if (semanticModel.GetSymbolInfo(node).Symbol is IFieldSymbol field &&
            plan.TryGetIdentifier(field, out var fieldName))
        {
            var target = (ExpressionSyntax)Visit(node.Expression)!;
            var name = SyntaxFactory.IdentifierName(fieldName).WithTriviaFrom(node.Name);
            return node.WithExpression(target).WithName(name);
        }
        return base.VisitMemberAccessExpression(node);
    }

    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (!plan.TryGetBinaryHelper(node, out var helper))
            return base.VisitBinaryExpression(node);
        return CreateHelperCall(helper, node, node.Left, node.Right);
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (!plan.TryGetAssignmentHelper(node, out var helper))
            return base.VisitAssignmentExpression(node);
        return CreateHelperCall(helper, node, node.Left, node.Right);
    }

    public override SyntaxNode? VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        var constant = semanticModel.GetConstantValue(node);
        if (node.IsKind(SyntaxKind.UnaryMinusExpression) &&
            constant is { HasValue: true, Value: not null } &&
            constant.Value is sbyte or short or int or long)
        {
            return SyntaxFactory.ParseExpression(FormatIntegralConstant(constant.Value))
                .WithTriviaFrom(node);
        }
        if (!plan.TryGetPrefixHelper(node, out var helper))
            return base.VisitPrefixUnaryExpression(node);
        return CreateHelperCall(helper, node, node.Operand);
    }

    public override SyntaxNode? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (!plan.TryGetPostfixHelper(node, out var helper))
            return base.VisitPostfixUnaryExpression(node);
        return CreateHelperCall(helper, node, node.Operand);
    }

    public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
    {
        if (plan.TryGetCastHelper(node, out var helper))
        {
            var convertedExpression = (ExpressionSyntax)Visit(node.Expression)!;
            var unsignedType = helper == "csharp2cuda_i32_from_bits"
                ? "unsigned int"
                : "unsigned long long";
            var cast = SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName(unsignedType),
                Parenthesize(convertedExpression));
            return CreateTranslatedHelperCall(helper, node, cast);
        }

        var type = semanticModel.GetTypeInfo(node.Type).Type;
        if (type is null)
            return base.VisitCastExpression(node);
        var translatedType = CreateTypeNode(
            plan.FormatType(type, false, node.Type.GetLocation()),
            node.Type);
        var expression = (ExpressionSyntax)Visit(node.Expression)!;
        return node.WithType(translatedType).WithExpression(expression);
    }

    public override SyntaxNode? VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        var constant = semanticModel.GetConstantValue(node.Value);
        if (!constant.HasValue || constant.Value is null)
            return base.VisitCaseSwitchLabel(node);
        var value = SyntaxFactory.ParseExpression(FormatIntegralConstant(constant.Value))
            .WithTriviaFrom(node.Value);
        return node.WithValue(value);
    }

    private SyntaxNode TranslateAtomic(InvocationExpressionSyntax node, string name)
    {
        var arguments = node.ArgumentList.Arguments;
        var location = (ExpressionSyntax)Visit(arguments[0].Expression)!;
        var address = SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.AddressOfExpression,
            location.WithoutLeadingTrivia())
            .WithLeadingTrivia(location.GetLeadingTrivia());
        var first = arguments[0]
            .WithRefKindKeyword(default)
            .WithExpression(address);
        var second = (ArgumentSyntax)Visit(arguments[1])!;
        var rewrittenArguments = SyntaxFactory.SeparatedList(
            [first, second],
            [arguments.GetSeparator(0)]);
        return node
            .WithExpression(SyntaxFactory.IdentifierName(name).WithTriviaFrom(node.Expression))
            .WithArgumentList(node.ArgumentList.WithArguments(rewrittenArguments));
    }

    private SyntaxNode TranslateBooleanToInteger(InvocationExpressionSyntax node)
    {
        var expression = VisitSingleArgument(node);
        var translated = SyntaxFactory.ConditionalExpression(
            Parenthesize(expression),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(1)),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(0)));
        return Parenthesize(translated).WithTriviaFrom(node);
    }

    private SyntaxNode TranslateIntegerToBoolean(InvocationExpressionSyntax node)
    {
        var expression = VisitSingleArgument(node);
        var translated = SyntaxFactory.BinaryExpression(
            SyntaxKind.NotEqualsExpression,
            Parenthesize(expression),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(0)));
        return Parenthesize(translated).WithTriviaFrom(node);
    }

    private SyntaxNode TranslateSignedToUnsigned(InvocationExpressionSyntax node)
    {
        var expression = VisitSingleArgument(node);
        var cast = SyntaxFactory.CastExpression(
            SyntaxFactory.ParseTypeName("unsigned long long"),
            Parenthesize(expression));
        return Parenthesize(cast).WithTriviaFrom(node);
    }

    private SyntaxNode UnwrapSingleArgument(InvocationExpressionSyntax node) =>
        VisitSingleArgument(node).WithTriviaFrom(node);

    private ExpressionSyntax VisitSingleArgument(InvocationExpressionSyntax node) =>
        (ExpressionSyntax)Visit(node.ArgumentList.Arguments[0].Expression)!;

    private SyntaxNode ReplaceInvocationName(InvocationExpressionSyntax node, string name) =>
        node.WithExpression(SyntaxFactory.IdentifierName(name).WithTriviaFrom(node.Expression))
            .WithArgumentList((ArgumentListSyntax)Visit(node.ArgumentList)!);

    private ExpressionSyntax CreateHelperCall(
        string helper,
        ExpressionSyntax original,
        params ExpressionSyntax[] operands)
    {
        var translated = operands.Select(operand => (ExpressionSyntax)Visit(operand)!).ToArray();
        return CreateTranslatedHelperCall(helper, original, translated);
    }

    private static ExpressionSyntax CreateTranslatedHelperCall(
        string helper,
        ExpressionSyntax original,
        params ExpressionSyntax[] operands)
    {
        var arguments = operands.Select(operand =>
            SyntaxFactory.Argument(operand.WithoutTrivia())).ToArray();

        var separators = Enumerable.Range(0, Math.Max(0, arguments.Length - 1))
            .Select(static _ => SyntaxFactory.Token(SyntaxKind.CommaToken)
                .WithTrailingTrivia(SyntaxFactory.Space));
        var list = SyntaxFactory.SeparatedList(arguments, separators);
        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(helper),
                SyntaxFactory.ArgumentList(list))
            .WithTriviaFrom(original);
    }

    private bool IsCudaMethod(ExpressionSyntax? expression, string name)
    {
        if (expression is not InvocationExpressionSyntax invocation)
            return false;
        var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return method?.Name == name &&
            method.ContainingType.ToDisplayString() == "CSharp2CUDA.Cuda";
    }

    private StatementSyntax TranslateFixedLocalArray(LocalDeclarationStatementSyntax node)
    {
        var variable = node.Declaration.Variables[0];
        var stack = (StackAllocArrayCreationExpressionSyntax)variable.Initializer!.Value;
        var arrayType = (ArrayTypeSyntax)stack.Type;
        var size = arrayType.RankSpecifiers[0].Sizes[0];
        var local = (ILocalSymbol)semanticModel.GetDeclaredSymbol(variable)!;
        TryGetFixedArrayElementType(local.Type, out var elementType, out var readOnly);
        var translatedSize = ((int)semanticModel.GetConstantValue(size).Value!)
            .ToString(CultureInfo.InvariantCulture);
        var localName = plan.GetIdentifier(local);
        var replacement = $"{(readOnly ? "const " : string.Empty)}" +
            $"{plan.FormatType(elementType, false, arrayType.ElementType.GetLocation())} " +
            $"{localName}[{translatedSize}]";
        if (stack.Initializer is not null)
        {
            var initializer = (InitializerExpressionSyntax)Visit(stack.Initializer)!;
            var lineBreak = stack.Type.GetTrailingTrivia()
                .Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                ? "\n"
                : string.Empty;
            replacement += " =" + lineBreak + initializer.ToFullString();
        }
        replacement += ";";

        string marker;
        do
        {
            marker = $"csharp2cuda_generated_fixed_array_{fixedLocalArrayMarker++}";
        }
        while (sourceText.Contains(marker, StringComparison.Ordinal));
        fixedLocalArrays.Add(new(marker, replacement));
        return SyntaxFactory.ExpressionStatement(SyntaxFactory.IdentifierName(marker))
            .WithTriviaFrom(node);
    }

    private static bool TryGetFixedArrayElementType(
        ITypeSymbol localType,
        out ITypeSymbol elementType,
        out bool readOnly)
    {
        if (localType is IPointerTypeSymbol pointer)
        {
            elementType = pointer.PointedAtType;
            readOnly = false;
            return true;
        }

        var named = (INamedTypeSymbol)localType;
        elementType = named.TypeArguments[0];
        readOnly = named.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>";
        return true;
    }

    private static ParenthesizedExpressionSyntax Parenthesize(ExpressionSyntax expression) =>
        SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia());

    private static TypeSyntax CreateTypeNode(string text, TypeSyntax original)
    {
        var identifier = SyntaxFactory.Identifier(
            original.GetLeadingTrivia(),
            SyntaxKind.IdentifierToken,
            text,
            text,
            original.GetTrailingTrivia());
        return SyntaxFactory.IdentifierName(identifier);
    }

    private static IdentifierNameSyntax CreateIdentifier(
        string text,
        IdentifierNameSyntax original) =>
        SyntaxFactory.IdentifierName(CreateIdentifierToken(original.Identifier, text));

    private static SyntaxToken CreateIdentifierToken(SyntaxToken original, string text) =>
        SyntaxFactory.Identifier(
            original.LeadingTrivia,
            SyntaxKind.IdentifierToken,
            text,
            text,
            original.TrailingTrivia);

    private static string TranslateNumericLiteral(SyntaxToken token)
    {
        var text = token.Text.Replace("_", string.Empty, StringComparison.Ordinal);
        return token.Value switch
        {
            int => StripIntegralSuffix(text),
            uint => StripIntegralSuffix(text) + "u",
            long => StripIntegralSuffix(text) + "LL",
            ulong => StripIntegralSuffix(text) + "ull",
            float => StripFloatingSuffix(text) + "f",
            double => StripFloatingSuffix(text),
            _ => text
        };
    }

    private static string StripIntegralSuffix(string text)
    {
        while (text.Length > 0 && text[^1] is 'u' or 'U' or 'l' or 'L')
            text = text[..^1];
        return text;
    }

    private static string StripFloatingSuffix(string text) =>
        text.Length > 0 && text[^1] is 'f' or 'F' or 'd' or 'D'
            ? text[..^1]
            : text;

    private static string FormatIntegralConstant(object value) => value switch
    {
        sbyte number => number.ToString(CultureInfo.InvariantCulture),
        byte number => number.ToString(CultureInfo.InvariantCulture),
        short number => number.ToString(CultureInfo.InvariantCulture),
        ushort number => number.ToString(CultureInfo.InvariantCulture),
        int.MinValue => "(-2147483647 - 1)",
        int number => number.ToString(CultureInfo.InvariantCulture),
        uint number => number.ToString(CultureInfo.InvariantCulture) + "u",
        long.MinValue => "(-9223372036854775807LL - 1LL)",
        long number => number.ToString(CultureInfo.InvariantCulture) + "LL",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "ull",
        _ => throw new InvalidOperationException(
            $"Switch constant '{value}' does not have an integral CUDA translation.")
    };

    private static SyntaxToken CreateNumericLiteralToken(SyntaxToken original, string text) =>
        original.Value switch
        {
            int value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            uint value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            long value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            ulong value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            float value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            double value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            _ => original
        };
}
