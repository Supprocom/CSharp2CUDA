using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharp2CUDA;

internal sealed class CudaSyntaxTranslator(
    SemanticModel semanticModel,
    ImmutableArray<Diagnostic>.Builder diagnostics) : CSharpSyntaxRewriter
{
    private static readonly IReadOnlyDictionary<string, string> MethodMappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Math.Abs(double)"] = "fabs",
            ["System.Math.Asin(double)"] = "asin",
            ["System.Math.Ceiling(double)"] = "ceil",
            ["System.Math.CopySign(double, double)"] = "copysign",
            ["System.Math.Floor(double)"] = "floor",
            ["System.Math.ILogB(double)"] = "ilogb",
            ["System.Math.Max(double, double)"] = "fmax",
            ["System.Math.Min(double, double)"] = "fmin",
            ["System.Math.ScaleB(double, int)"] = "ldexp",
            ["System.Math.Truncate(double)"] = "trunc",
            ["double.IsFinite(double)"] = "isfinite",
            ["double.IsInfinity(double)"] = "isinf",
            ["double.IsNaN(double)"] = "isnan",
            ["System.BitConverter.DoubleToInt64Bits(double)"] = "__double_as_longlong",
            ["System.BitConverter.Int64BitsToDouble(long)"] = "__longlong_as_double"
        };

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
            return syntax.ToString();
        }
        return FormatType(type, deepReadOnly, syntax.GetLocation());
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
                return CreateTypeNode(FormatType(type, false, node.GetLocation()), node);
        }
        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.NullLiteralExpression))
            return SyntaxFactory.IdentifierName("nullptr").WithTriviaFrom(node);
        if (!node.IsKind(SyntaxKind.NumericLiteralExpression))
            return base.VisitLiteralExpression(node);

        var replacement = TranslateNumericLiteral(node.Token.Text);
        if (replacement == node.Token.Text)
            return base.VisitLiteralExpression(node);
        var token = CreateNumericLiteralToken(node.Token, replacement);
        return node.WithToken(token);
    }

    public override SyntaxNode? VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.UncheckedKeyword))
            return Visit(node.Expression)?.WithTriviaFrom(node);
        return base.VisitCheckedExpression(node);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var fixedLocalArray = TranslateFixedLocalArray(node);
        if (fixedLocalArray is not null)
            return fixedLocalArray;

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

    public override SyntaxNode? VisitStackAllocArrayCreationExpression(
        StackAllocArrayCreationExpressionSyntax node)
    {
        ReportUnsupportedSyntax(node);
        return base.VisitStackAllocArrayCreationExpression(node);
    }

    public override SyntaxNode? VisitImplicitStackAllocArrayCreationExpression(
        ImplicitStackAllocArrayCreationExpressionSyntax node)
    {
        ReportUnsupportedSyntax(node);
        return base.VisitImplicitStackAllocArrayCreationExpression(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var method = semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
        if (method is null)
        {
            ReportUnsupportedCall(node, node.Expression.ToString());
            return base.VisitInvocationExpression(node);
        }

        if (method.ContainingType.ToDisplayString() == "CSharp2CUDA.Cuda")
            return TranslateCudaInvocation(node, method);

        var displayName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (MethodMappings.TryGetValue(displayName, out var mappedName))
            return ReplaceInvocationName(node, mappedName);

        if (method.ContainingType.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() ==
                "CSharp2CUDA.CudaTranslationUnitAttribute"))
        {
            return base.VisitInvocationExpression(node);
        }

        ReportUnsupportedCall(node, displayName);
        return base.VisitInvocationExpression(node);
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (TryTranslateDimension(node, out var translated))
            return translated.WithTriviaFrom(node);
        return base.VisitMemberAccessExpression(node);
    }

    private SyntaxNode TranslateCudaInvocation(InvocationExpressionSyntax node, IMethodSymbol method)
    {
        return method.Name switch
        {
            nameof(Cuda.SyncThreads) => ReplaceInvocationName(node, "__syncthreads"),
            nameof(Cuda.AtomicAdd) => TranslateAtomic(node, "atomicAdd"),
            nameof(Cuda.AtomicExchange) => TranslateAtomic(node, "atomicExch"),
            nameof(Cuda.Int) => UnwrapSingleArgument(node, parenthesize: true),
            nameof(Cuda.Bool) or nameof(Cuda.Unsigned) or nameof(Cuda.ReadOnly) =>
                UnwrapSingleArgument(node, parenthesize: false),
            nameof(Cuda.FloatingRemainder) => ReplaceInvocationName(node, "fmod"),
            nameof(Cuda.NearbyInteger) => ReplaceInvocationName(node, "nearbyint"),
            nameof(Cuda.SignBit) => ReplaceInvocationName(node, "signbit"),
            _ => ReportAndReturn(node, method.ToDisplayString())
        };
    }

    private SyntaxNode TranslateAtomic(InvocationExpressionSyntax node, string name)
    {
        var arguments = node.ArgumentList.Arguments;
        if (arguments.Count != 2 || !arguments[0].RefKindKeyword.IsKind(SyntaxKind.RefKeyword))
            return ReportAndReturn(node, node.Expression.ToString());

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

    private SyntaxNode UnwrapSingleArgument(
        InvocationExpressionSyntax node,
        bool parenthesize)
    {
        if (node.ArgumentList.Arguments.Count != 1)
            return ReportAndReturn(node, node.Expression.ToString());
        var expression = (ExpressionSyntax)Visit(node.ArgumentList.Arguments[0].Expression)!;
        if (!parenthesize)
            return expression.WithTriviaFrom(node);
        return SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia())
            .WithTriviaFrom(node);
    }

    private SyntaxNode ReplaceInvocationName(InvocationExpressionSyntax node, string name) =>
        node.WithExpression(SyntaxFactory.IdentifierName(name).WithTriviaFrom(node.Expression))
            .WithArgumentList((ArgumentListSyntax)Visit(node.ArgumentList)!);

    private bool TryTranslateDimension(
        MemberAccessExpressionSyntax node,
        out ExpressionSyntax translated)
    {
        translated = null!;
        if (node.Expression is not MemberAccessExpressionSyntax dimension ||
            dimension.Expression is not IdentifierNameSyntax cuda ||
            cuda.Identifier.ValueText != nameof(Cuda))
        {
            return false;
        }

        var target = dimension.Name.Identifier.ValueText switch
        {
            nameof(Cuda.ThreadIdx) => "threadIdx",
            nameof(Cuda.BlockIdx) => "blockIdx",
            nameof(Cuda.BlockDim) => "blockDim",
            nameof(Cuda.GridDim) => "gridDim",
            _ => null
        };
        var component = node.Name.Identifier.ValueText.ToLowerInvariant();
        if (target is null || component is not "x" and not "y" and not "z")
            return false;

        translated = SyntaxFactory.ParseExpression($"{target}.{component}");
        return true;
    }

    private bool IsCudaMethod(ExpressionSyntax? expression, string name)
    {
        if (expression is not InvocationExpressionSyntax invocation)
            return false;
        var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return method?.Name == name &&
            method.ContainingType.ToDisplayString() == "CSharp2CUDA.Cuda";
    }

    private StatementSyntax? TranslateFixedLocalArray(LocalDeclarationStatementSyntax node)
    {
        if (node.Declaration.Variables.Count != 1 ||
            node.Declaration.Type is IdentifierNameSyntax { Identifier.ValueText: "var" })
        {
            return null;
        }

        var variable = node.Declaration.Variables[0];
        var value = variable.Initializer?.Value;
        if (value is not StackAllocArrayCreationExpressionSyntax stackAllocation ||
            stackAllocation.Type is not ArrayTypeSyntax arrayType ||
            arrayType.RankSpecifiers is not [var rank] ||
            rank.Sizes is not [var size] ||
            size is OmittedArraySizeExpressionSyntax)
        {
            return null;
        }

        var local = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
        var elementType = semanticModel.GetTypeInfo(arrayType.ElementType).Type;
        var constant = semanticModel.GetConstantValue(size);
        if (local is null ||
            !TryGetFixedArrayElementType(local.Type, out var localElementType, out var readOnly) ||
            elementType is null ||
            !SymbolEqualityComparer.Default.Equals(localElementType, elementType) ||
            !constant.HasValue ||
            constant.Value is not int length ||
            length <= 0 ||
            (readOnly && stackAllocation.Initializer is null) ||
            (stackAllocation.Initializer is not null &&
                stackAllocation.Initializer.Expressions.Count != length))
        {
            return null;
        }

        var translatedSize = ((ExpressionSyntax)Visit(size)!)
            .WithoutTrivia()
            .ToFullString();
        var replacement = $"{(readOnly ? "const " : string.Empty)}" +
            $"{FormatType(elementType, false, arrayType.ElementType.GetLocation())} " +
            $"{variable.Identifier.ValueText}[{translatedSize}]";
        if (stackAllocation.Initializer is not null)
        {
            var initializer = (InitializerExpressionSyntax)Visit(stackAllocation.Initializer)!;
            var lineBreak = stackAllocation.Type.GetTrailingTrivia()
                .Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                ? "\n"
                : string.Empty;
            replacement += " =" + lineBreak + initializer.ToFullString();
        }
        replacement += ";";

        string marker;
        do
        {
            marker = $"__csharp2cuda_fixed_local_array_{fixedLocalArrayMarker++}";
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
        if (localType is IPointerTypeSymbol pointerType)
        {
            elementType = pointerType.PointedAtType;
            readOnly = false;
            return true;
        }

        if (localType is INamedTypeSymbol namedType && namedType.TypeArguments is [var argument])
        {
            var definition = namedType.OriginalDefinition.ToDisplayString();
            if (definition is "System.Span<T>" or "System.ReadOnlySpan<T>")
            {
                elementType = argument;
                readOnly = definition == "System.ReadOnlySpan<T>";
                return true;
            }
        }

        elementType = null!;
        readOnly = false;
        return false;
    }

    private string FormatType(ITypeSymbol type, bool deepReadOnly, Location location)
    {
        if (type.ToDisplayString() == "CSharp2CUDA.CudaInt32")
            return "int";
        if (type is IPointerTypeSymbol pointer)
        {
            var depth = 0;
            ITypeSymbol current = pointer;
            while (current is IPointerTypeSymbol currentPointer)
            {
                depth++;
                current = currentPointer.PointedAtType;
            }

            var baseType = FormatType(current, false, location);
            if (!deepReadOnly)
                return baseType + new string('*', depth);
            var result = "const " + baseType + "*";
            for (var index = 1; index < depth; index++)
                result += " const*";
            return result;
        }

        if (type.TypeKind == TypeKind.Enum)
            return type.Name;
        if (type.SpecialType != SpecialType.None)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Void => "void",
                SpecialType.System_Boolean => "bool",
                SpecialType.System_SByte => "signed char",
                SpecialType.System_Byte => "unsigned char",
                SpecialType.System_Int16 => "short",
                SpecialType.System_UInt16 => "unsigned short",
                SpecialType.System_Int32 => "int",
                SpecialType.System_UInt32 => "unsigned int",
                SpecialType.System_Int64 => "long long",
                SpecialType.System_UInt64 => "unsigned long long",
                SpecialType.System_Single => "float",
                SpecialType.System_Double => "double",
                SpecialType.System_Char => "char",
                _ => ReportUnsupportedType(type, location)
            };
        }
        if (type is INamedTypeSymbol named &&
            named.ContainingType?.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() ==
                "CSharp2CUDA.CudaTranslationUnitAttribute") == true)
        {
            return named.Name;
        }
        return ReportUnsupportedType(type, location);
    }

    private string ReportUnsupportedType(ITypeSymbol type, Location location)
    {
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedType,
            location,
            type.ToDisplayString()));
        return type.Name;
    }

    private SyntaxNode ReportAndReturn(InvocationExpressionSyntax node, string name)
    {
        ReportUnsupportedCall(node, name);
        return base.VisitInvocationExpression(node)!;
    }

    private void ReportUnsupportedCall(InvocationExpressionSyntax node, string name) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedCall,
            node.GetLocation(),
            name));

    private void ReportUnsupportedSyntax(SyntaxNode node) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedSyntax,
            node.GetLocation(),
            node.Kind().ToString()));

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

    private static string TranslateNumericLiteral(string text)
    {
        if (text.EndsWith("UL", StringComparison.OrdinalIgnoreCase))
            return text[..^2] + "ull";
        if (text.EndsWith('L'))
            return text[..^1] + "LL";
        if (text.EndsWith('l'))
            return text[..^1] + "ll";
        if (text.EndsWith('U') || text.EndsWith('u'))
            return text[..^1] + "u";
        if (text.EndsWith('F') || text.EndsWith('f'))
            return text[..^1] + "f";
        return text;
    }

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
            decimal value => SyntaxFactory.Literal(
                original.LeadingTrivia, text, value, original.TrailingTrivia),
            _ => throw new InvalidOperationException(
                $"Numeric literal '{original.Text}' has an unsupported value type.")
        };
}
