using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Tool;

internal static class CudaMethodSelector
{
    public static CudaMethodSelection Select(
        Microsoft.CodeAnalysis.Compilation compilation,
        string requestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
        var normalizedRequest = Normalize(requestedName);
        var candidates = EnumerateTypes(compilation.Assembly.GlobalNamespace)
            .SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
            .Where(static method => method.MethodKind == MethodKind.Ordinary &&
                method.DeclaringSyntaxReferences.Length > 0)
            .Select(static method => method.PartialImplementationPart ?? method)
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            .Select(method => new CudaMethodSelection(method, Format(method)))
            .Where(candidate => IsMatch(candidate, normalizedRequest))
            .OrderBy(static candidate => candidate.CanonicalName, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new ToolException(
                "CS2CUDA105",
                $"Method '{requestedName}' was not found in source.");
        }
        if (candidates.Length > 1)
        {
            throw new ToolException(
                "CS2CUDA106",
                $"Method '{requestedName}' is ambiguous. Specify its parameter types.");
        }

        var result = candidates[0];
        if (!result.Symbol.IsStatic)
        {
            throw new ToolException(
                "CS2CUDA107",
                $"Method '{result.CanonicalName}' must be static.");
        }
        if (result.Symbol.DeclaredAccessibility is not
            (Accessibility.Public or Accessibility.Internal))
        {
            throw new ToolException(
                "CS2CUDA107",
                $"Method '{result.CanonicalName}' must be public or internal.");
        }
        if (!IsAccessible(result.Symbol.ContainingType))
        {
            throw new ToolException(
                "CS2CUDA107",
                $"The containing type for '{result.CanonicalName}' must be public or internal.");
        }
        return result;
    }

    public static string Format(IMethodSymbol method) =>
        method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static bool IsMatch(CudaMethodSelection candidate, string request)
    {
        var canonical = Normalize(candidate.CanonicalName);
        if (string.Equals(canonical, request, StringComparison.Ordinal))
            return true;
        if (!request.Contains('('))
        {
            return canonical.EndsWith('.' + request, StringComparison.Ordinal) ||
                string.Equals(candidate.Symbol.Name, request, StringComparison.Ordinal);
        }
        return canonical.EndsWith('.' + request, StringComparison.Ordinal);
    }

    private static bool IsAccessible(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not
                (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            foreach (var item in EnumerateType(type))
                yield return item;
        }
        foreach (var child in root.GetNamespaceMembers())
        {
            foreach (var item in EnumerateTypes(child))
                yield return item;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateType(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var child in type.GetTypeMembers())
        {
            foreach (var item in EnumerateType(child))
                yield return item;
        }
    }

    private static string Normalize(string value) => new(
        value.Replace("global::", string.Empty, StringComparison.Ordinal)
            .Where(static character => !char.IsWhiteSpace(character))
            .ToArray());
}

internal sealed record CudaMethodSelection(
    IMethodSymbol Symbol,
    string CanonicalName);
