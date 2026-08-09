using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharp2CUDA;

internal sealed class CudaModuleEmitter
{
    private const string DeviceAttributeName = "CSharp2CUDA.CudaDeviceAttribute";
    private const string GlobalAttributeName = "CSharp2CUDA.CudaGlobalAttribute";
    private static readonly HashSet<string> CudaKeywords = new(StringComparer.Ordinal)
    {
        "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel",
        "atomic_commit", "atomic_noexcept", "auto", "bitand", "bitor", "bool",
        "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t",
        "class", "compl", "concept", "const", "consteval", "constexpr", "constinit",
        "const_cast", "continue", "co_await", "co_return", "co_yield", "decltype",
        "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
        "explicit", "export", "extern", "false", "final", "float", "for", "friend",
        "goto", "if", "import", "inline", "int", "long", "module", "mutable",
        "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator",
        "or", "or_eq", "override", "private", "protected", "public", "reflexpr",
        "register", "reinterpret_cast", "requires", "return", "short", "signed",
        "sizeof", "static", "static_assert", "static_cast", "struct", "switch",
        "synchronized", "template", "this", "thread_local", "throw", "transaction_safe",
        "transaction_safe_dynamic", "true", "try", "typedef", "typeid", "typename",
        "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t",
        "while", "xor", "xor_eq"
    };

    private readonly SemanticModel semanticModel;
    private readonly ImmutableArray<Diagnostic>.Builder diagnostics;
    private readonly CudaTranspilationOptions options;
    private readonly Dictionary<IMethodSymbol, string> functionNames;
    private readonly CudaSyntaxTranslator translator;

    public CudaModuleEmitter(
        SemanticModel semanticModel,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        CudaTranspilationOptions options,
        Dictionary<IMethodSymbol, string> functionNames)
    {
        this.semanticModel = semanticModel;
        this.diagnostics = diagnostics;
        this.options = options;
        this.functionNames = functionNames;
        translator = new CudaSyntaxTranslator(semanticModel, diagnostics, functionNames);
    }

    public string Emit(ClassDeclarationSyntax unit)
    {
        ValidateUnit(unit);
        var validator = new CudaSyntaxValidator(semanticModel, diagnostics);
        using var output = new StringWriter { NewLine = options.NewLine };
        var wroteMember = false;

        foreach (var member in unit.Members)
        {
            if (IsExternal(member))
                continue;
            string? translated = member switch
            {
                StructDeclarationSyntax structure => EmitStruct(structure),
                MethodDeclarationSyntax method => EmitMethod(method),
                _ => ReportUnsupportedMember(member)
            };

            if (translated is null)
                continue;
            if (member is MethodDeclarationSyntax methodDeclaration)
                validator.Visit(methodDeclaration.Body);
            if (wroteMember)
                output.Write(options.NewLine + options.NewLine);
            output.Write(translated);
            wroteMember = true;
        }

        return output.ToString();
    }

    private void ValidateUnit(ClassDeclarationSyntax unit)
    {
        var isStatic = unit.Modifiers.Any(SyntaxKind.StaticKeyword);
        if (!isStatic || unit.TypeParameterList is not null || unit.BaseList is not null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidTranslationUnit,
                unit.Identifier.GetLocation(),
                unit.Identifier.ValueText));
        }
    }

    private string EmitStruct(StructDeclarationSyntax structure)
    {
        using var output = new StringWriter { NewLine = options.NewLine };
        output.Write("struct ");
        output.Write(structure.Identifier.ValueText);
        output.WriteLine();
        output.WriteLine("{");
        foreach (var member in structure.Members)
        {
            if (member is not FieldDeclarationSyntax field ||
                field.Declaration.Variables.Count != 1)
            {
                ReportUnsupportedMember(member);
                continue;
            }

            var type = translator.TranslateType(field.Declaration.Type, deepReadOnly: false);
            var variable = field.Declaration.Variables[0];
            output.Write("    ");
            output.Write(type);
            output.Write(' ');
            output.Write(variable.Identifier.ValueText);
            if (variable.Initializer is not null)
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.UnsupportedSyntax,
                    variable.Initializer.GetLocation(),
                    variable.Initializer.Kind().ToString()));
            }
            output.WriteLine(";");
        }
        output.Write("};");
        return output.ToString();
    }

    private string? EmitMethod(MethodDeclarationSyntax method)
    {
        if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol)
            return null;

        var device = GetAttribute(symbol, DeviceAttributeName);
        var global = GetAttribute(symbol, GlobalAttributeName);
        if (device is not null && global is not null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ConflictingFunctionKinds,
                method.Identifier.GetLocation(),
                symbol.Name));
            return null;
        }
        if (device is null && global is null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.MissingFunctionKind,
                method.Identifier.GetLocation(),
                symbol.Name));
            return null;
        }
        if (method.Body is null || method.ExpressionBody is not null ||
            method.TypeParameterList is not null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.UnsupportedSyntax,
                method.GetLocation(),
                method.Kind().ToString()));
            return null;
        }

        using var output = new StringWriter { NewLine = options.NewLine };
        if (device is not null)
        {
            output.Write("__device__ ");
        }
        else
        {
            var externC = GetNamedBoolean(global!, nameof(CudaGlobalAttribute.ExternC), true);
            if (externC)
                output.Write("extern \"C\" ");
            output.Write("__global__ ");
        }

        output.Write(translator.TranslateType(method.ReturnType, deepReadOnly: false));
        output.Write(' ');
        output.Write(functionNames[symbol]);
        EmitParameters(output, method.ParameterList, symbol);
        output.WriteLine();
        output.Write(TranslateBody(method.Body));
        return output.ToString();
    }

    private void EmitParameters(
        TextWriter output,
        ParameterListSyntax parameters,
        IMethodSymbol method)
    {
        var parameterText = parameters.SyntaxTree.GetText().ToString(parameters.Span);
        var multiline = parameterText.Contains('\n') || parameterText.Contains('\r');
        output.Write('(');
        for (var index = 0; index < parameters.Parameters.Count; index++)
        {
            var parameter = parameters.Parameters[index];
            var symbol = method.Parameters[index];
            var readOnly = GetAttribute(symbol, "CSharp2CUDA.CudaReadOnlyAttribute") is not null;
            var readOnlyReference = symbol.RefKind == RefKind.In;
            if (readOnly && symbol.Type is not IPointerTypeSymbol)
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.InvalidReadOnlyParameter,
                    parameter.GetLocation(),
                    parameter.Identifier.ValueText));
            }
            if ((readOnlyReference && symbol.Type is IPointerTypeSymbol) ||
                symbol.RefKind is not RefKind.None and not RefKind.In)
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.UnsupportedSyntax,
                    parameter.GetLocation(),
                    parameter.Modifiers.ToString()));
            }

            if (multiline)
            {
                output.WriteLine();
                output.Write("    ");
            }
            if (readOnlyReference)
                output.Write("const ");
            output.Write(translator.TranslateType(parameter.Type!, readOnly));
            if (readOnlyReference)
                output.Write('&');
            output.Write(' ');
            output.Write(parameter.Identifier.ValueText);
            if (index + 1 < parameters.Parameters.Count)
            {
                output.Write(',');
                if (!multiline)
                    output.Write(' ');
            }
        }
        output.Write(')');
    }

    private string TranslateBody(BlockSyntax body)
    {
        var rewritten = (BlockSyntax)translator.Visit(body)!;
        var text = rewritten.WithoutLeadingTrivia().WithoutTrailingTrivia().ToFullString();
        text = translator.ExpandFixedLocalArrays(text);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("    ", StringComparison.Ordinal))
                lines[index] = lines[index][4..];
        }
        return string.Join(options.NewLine, lines);
    }

    private string? ReportUnsupportedMember(MemberDeclarationSyntax member)
    {
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedMember,
            member.GetLocation(),
            member.Kind().ToString()));
        return null;
    }

    private bool IsExternal(MemberDeclarationSyntax member)
    {
        var symbol = semanticModel.GetDeclaredSymbol(member);
        return symbol?.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() ==
            "CSharp2CUDA.CudaExternalAttribute") == true;
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static bool GetNamedBoolean(AttributeData attribute, string name, bool defaultValue)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
                return value;
        }
        return defaultValue;
    }

    public void RegisterFunctionNames(ClassDeclarationSyntax unit)
    {
        foreach (var method in unit.Members.OfType<MethodDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol)
                continue;
            var attribute = GetAttribute(symbol, DeviceAttributeName) ??
                GetAttribute(symbol, GlobalAttributeName);
            if (attribute is not null)
                functionNames[symbol] = GetEmittedName(symbol, attribute);
        }
    }

    private string GetEmittedName(IMethodSymbol method, AttributeData attribute)
    {
        var emittedName = method.Name;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == nameof(CudaDeviceAttribute.Name))
            {
                emittedName = argument.Value.Value as string ?? string.Empty;
                break;
            }
        }

        if (IsValidCudaIdentifier(emittedName))
            return emittedName;

        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.InvalidFunctionName,
            GetNameLocation(method, attribute),
            emittedName));
        return "csharp2cuda_invalid_function";
    }

    private static Location GetNameLocation(IMethodSymbol method, AttributeData attribute)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax)
        {
            var nameArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(argument =>
                argument.NameEquals?.Name.Identifier.ValueText ==
                nameof(CudaDeviceAttribute.Name));
            if (nameArgument is not null)
                return nameArgument.Expression.GetLocation();
        }

        return method.Locations.FirstOrDefault() ?? Location.None;
    }

    private static bool IsValidCudaIdentifier(string name)
    {
        if (name.Length == 0 ||
            !IsAsciiLetter(name[0]) ||
            CudaKeywords.Contains(name) ||
            name.Contains("__", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            var character = name[index];
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character != '_')
                return false;
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
