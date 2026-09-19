using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Supprocom.CSharp2CUDA.Tool;

internal static class CudaAdapterGenerator
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static CudaAdapterSource Generate(
        CSharpCompilation compilation,
        CudaMethodSelection method,
        CudaAdapterMapping mapping,
        string cudaOutputPath)
    {
        if (method.Symbol.IsGenericMethod ||
            method.Symbol.ContainingType.IsGenericType)
        {
            throw new ToolException(
                "CS2CUDA108",
                $"Method '{method.CanonicalName}' must be a closed nongeneric method.");
        }
        return mapping switch
        {
            CudaAdapterMapping.Elementwise => GenerateElementwise(method, cudaOutputPath),
            CudaAdapterMapping.SingleThread => GenerateSingleThread(
                compilation,
                method,
                cudaOutputPath),
            _ => throw new InvalidOperationException("The CUDA mapping is not valid.")
        };
    }

    public static CudaTranspilationResult Transpile(
        CSharpCompilation compilation,
        CudaAdapterSource adapter,
        string sourceRoot,
        string adapterPath)
    {
        var parseOptions = compilation.SyntaxTrees
            .Select(static tree => tree.Options)
            .OfType<CSharpParseOptions>()
            .FirstOrDefault() ?? new CSharpParseOptions(LanguageVersion.CSharp14);
        var tree = CSharpSyntaxTree.ParseText(
            SourceText.From(adapter.Source, Encoding.UTF8),
            parseOptions,
            adapterPath);
        var candidate = compilation.AddSyntaxTrees(tree);
        return CudaTranspiler.Transpile(
            candidate,
            new CudaTranspilationOptions
            {
                SourceRoot = sourceRoot,
                TranspileAttributedClassesOnly = true
            });
    }

    private static CudaAdapterSource GenerateElementwise(
        CudaMethodSelection method,
        string outputPath)
    {
        if (method.Symbol.ReturnsVoid || !IsScalar(method.Symbol.ReturnType))
        {
            throw new ToolException(
                "CS2CUDA109",
                "The elementwise mapping requires an unmanaged scalar result.");
        }
        if (method.Symbol.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None || !IsScalar(parameter.Type)))
        {
            throw new ToolException(
                "CS2CUDA109",
                "The elementwise mapping requires unmanaged scalar input parameters.");
        }

        var parameters = new List<string>();
        var arguments = new List<string>();
        for (var index = 0; index < method.Symbol.Parameters.Length; index++)
        {
            var parameter = method.Symbol.Parameters[index];
            var typeName = FormatType(parameter.Type);
            var name = $"cs2cuda_input_{index.ToString(CultureInfo.InvariantCulture)}";
            parameters.Add($"[CudaReadOnly] {typeName}* {name}");
            arguments.Add($"{name}[cs2cuda_index]");
        }
        parameters.Add($"{FormatType(method.Symbol.ReturnType)}* cs2cuda_output");
        parameters.Add("int cs2cuda_length");

        var call = CreateCall(method, arguments);
        var body = $"""
                int cs2cuda_index = Cuda.BlockIdx.X * Cuda.BlockDim.X + Cuda.ThreadIdx.X;
                if ((uint)cs2cuda_index >= (uint)cs2cuda_length)
                    return;
                cs2cuda_output[cs2cuda_index] = {call};
            """;
        return CreateSource(method, outputPath, "elementwise", parameters, body);
    }

    private static CudaAdapterSource GenerateSingleThread(
        CSharpCompilation compilation,
        CudaMethodSelection method,
        string outputPath)
    {
        if (!method.Symbol.ReturnsVoid && !IsScalar(method.Symbol.ReturnType))
        {
            throw new ToolException(
                "CS2CUDA110",
                "The single-thread mapping requires void or an unmanaged scalar result.");
        }

        var span = compilation.GetTypeByMetadataName("System.Span`1");
        var readOnlySpan = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
        var parameters = new List<string>();
        var arguments = new List<string>();
        for (var index = 0; index < method.Symbol.Parameters.Length; index++)
        {
            var parameter = method.Symbol.Parameters[index];
            if (parameter.RefKind != RefKind.None)
            {
                throw new ToolException(
                    "CS2CUDA110",
                    "The single-thread mapping does not support ref, in, or out parameters.");
            }

            var pointerName = $"cs2cuda_data_{index.ToString(CultureInfo.InvariantCulture)}";
            var lengthName = $"cs2cuda_length_{index.ToString(CultureInfo.InvariantCulture)}";
            if (TryGetViewElement(parameter.Type, span, readOnlySpan, out var element, out var readOnly))
            {
                var typeName = FormatType(element);
                parameters.Add($"{(readOnly ? "[CudaReadOnly] " : string.Empty)}{typeName}* {pointerName}");
                parameters.Add($"int {lengthName}");
                arguments.Add(readOnly
                    ? $"Cuda.ReadOnlyArray<{typeName}>({pointerName}, {lengthName})"
                    : $"Cuda.Array<{typeName}>({pointerName}, {lengthName})");
                continue;
            }
            if (!IsScalar(parameter.Type))
            {
                throw new ToolException(
                    "CS2CUDA110",
                    $"Parameter '{parameter.Name}' is not a supported scalar, array, or span.");
            }

            var name = $"cs2cuda_value_{index.ToString(CultureInfo.InvariantCulture)}";
            parameters.Add($"{FormatType(parameter.Type)} {name}");
            arguments.Add(name);
        }
        if (!method.Symbol.ReturnsVoid)
            parameters.Add($"{FormatType(method.Symbol.ReturnType)}* cs2cuda_output");

        var call = CreateCall(method, arguments);
        var invocation = method.Symbol.ReturnsVoid
            ? call + ";"
            : "*cs2cuda_output = " + call + ";";
        var body = $"""
                if (Cuda.BlockIdx.X != 0 || Cuda.ThreadIdx.X != 0)
                    return;
                {invocation}
            """;
        return CreateSource(method, outputPath, "single_thread", parameters, body);
    }

    private static CudaAdapterSource CreateSource(
        CudaMethodSelection method,
        string outputPath,
        string mappingName,
        IReadOnlyList<string> parameters,
        string body)
    {
        var hash = ComputeHash(method.CanonicalName + "|" + mappingName);
        var kernelName = $"cs2cuda_{NormalizeIdentifier(method.Symbol.ContainingType.Name)}_{NormalizeIdentifier(method.Symbol.Name)}_{mappingName}_{hash:x8}";
        var parameterText = string.Join(",\n        ", parameters);
        var outputLiteral = SymbolDisplay.FormatLiteral(outputPath, quote: true);
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            namespace Supprocom.CSharp2CUDA.Generated;

            [TranspileToCUDA({{outputLiteral}})]
            internal static unsafe class CudaAdapter
            {
                [CudaGlobal(Name = "{{kernelName}}")]
                private static void Run(
                    {{parameterText}})
                {
            {{Indent(body, 4)}}
                }
            }
            """;
        return new CudaAdapterSource(source, kernelName, outputPath);
    }

    private static string CreateCall(
        CudaMethodSelection method,
        IReadOnlyCollection<string> arguments) =>
        $"{method.Symbol.ContainingType.ToDisplayString(TypeFormat)}.{method.Symbol.Name}({string.Join(", ", arguments)})";

    private static bool TryGetViewElement(
        ITypeSymbol type,
        INamedTypeSymbol? span,
        INamedTypeSymbol? readOnlySpan,
        out ITypeSymbol element,
        out bool readOnly)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            element = array.ElementType;
            readOnly = false;
            return element.IsUnmanagedType;
        }
        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, span) ||
             SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readOnlySpan)))
        {
            element = named.TypeArguments[0];
            readOnly = SymbolEqualityComparer.Default.Equals(
                named.OriginalDefinition,
                readOnlySpan);
            return element.IsUnmanagedType;
        }
        element = null!;
        readOnly = false;
        return false;
    }

    private static bool IsScalar(ITypeSymbol type) =>
        type.IsUnmanagedType &&
        !type.IsRefLikeType &&
        type is not IPointerTypeSymbol and not IFunctionPointerTypeSymbol &&
        type.SpecialType != SpecialType.System_Void;

    private static string FormatType(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    private static string NormalizeIdentifier(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            result.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        return result.Length == 0 ? "method" : result.ToString();
    }

    private static uint ComputeHash(string value)
    {
        var hash = 2166136261U;
        foreach (var item in Encoding.UTF8.GetBytes(value))
        {
            hash ^= item;
            hash *= 16777619U;
        }
        return hash;
    }

    private static string Indent(string text, int spaces)
    {
        var prefix = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => prefix + line));
    }
}

internal sealed record CudaAdapterSource(
    string Source,
    string KernelName,
    string CudaOutputPath);
