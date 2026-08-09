using Microsoft.CodeAnalysis;

namespace CSharp2CUDA;

internal static class CudaDiagnostics
{
    private const string Category = "CSharp2CUDA";

    public static readonly DiagnosticDescriptor MissingTranslationUnit = new(
        "CS2CUDA001",
        "Translation unit is missing",
        "The compilation does not contain a type with CudaTranslationUnitAttribute",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidTranslationUnit = new(
        "CS2CUDA002",
        "Translation unit is invalid",
        "Translation unit '{0}' must be a static class without type parameters",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMember = new(
        "CS2CUDA003",
        "Member is not supported",
        "Translation unit member '{0}' does not have a CUDA translation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingFunctionKind = new(
        "CS2CUDA004",
        "CUDA function kind is missing",
        "Method '{0}' must have CudaDeviceAttribute or CudaGlobalAttribute",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedSyntax = new(
        "CS2CUDA005",
        "Syntax is not supported",
        "C# syntax '{0}' does not have a CUDA translation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedCall = new(
        "CS2CUDA006",
        "Method call is not supported",
        "Method call '{0}' does not have a CUDA translation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "CS2CUDA007",
        "Type is not supported",
        "Type '{0}' does not have a CUDA translation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingFunctionKinds = new(
        "CS2CUDA008",
        "CUDA function kind is not unique",
        "Method '{0}' cannot have both CudaDeviceAttribute and CudaGlobalAttribute",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidReadOnlyParameter = new(
        "CS2CUDA009",
        "Read-only parameter is invalid",
        "Parameter '{0}' can use CudaReadOnlyAttribute only with a pointer type",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidIdentifier = new(
        "CS2CUDA010",
        "CUDA identifier is invalid",
        "CUDA identifier '{0}' is not valid",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DeclarationCollision = new(
        "CS2CUDA011",
        "CUDA declaration is not unique",
        "CUDA declaration '{0}' conflicts with another declaration",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CheckedOverflowCompilation = new(
        "CS2CUDA012",
        "Checked integer overflow is not supported",
        "The compilation option for checked integer overflow does not have a CUDA translation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
