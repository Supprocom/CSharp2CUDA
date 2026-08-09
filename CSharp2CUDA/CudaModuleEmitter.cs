using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharp2CUDA;

internal sealed class CudaModuleEmitter(
    SemanticModel semanticModel,
    ImmutableArray<Diagnostic>.Builder diagnostics,
    CudaTranspilationOptions options)
{
    private const string DeviceAttributeName = "CSharp2CUDA.CudaDeviceAttribute";
    private const string GlobalAttributeName = "CSharp2CUDA.CudaGlobalAttribute";
    private readonly CudaSyntaxTranslator translator = new(semanticModel, diagnostics);

    public string Emit(ClassDeclarationSyntax unit)
    {
        ValidateUnit(unit);
        var validator = new CudaSyntaxValidator(diagnostics);
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
            validator.Visit(member);
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
        output.Write(GetEmittedName(symbol, device ?? global!));
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

    private static string GetEmittedName(IMethodSymbol method, AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == nameof(CudaDeviceAttribute.Name) &&
                argument.Value.Value is string name && name.Length > 0)
            {
                return name;
            }
        }
        return method.Name;
    }
}
