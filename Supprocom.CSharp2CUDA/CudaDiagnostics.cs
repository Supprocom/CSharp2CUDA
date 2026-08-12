using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA;

internal static class CudaDiagnostics
{
    private const string Category = "Supprocom.CSharp2CUDA";

    public static readonly DiagnosticDescriptor MissingTranslationUnit = new(
        "CS2CUDA001",
        "Translation unit is missing",
        "The selected source does not contain a top-level class",
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

    public static readonly DiagnosticDescriptor InvalidStorage = new(
        "CS2CUDA013",
        "CUDA storage declaration is invalid",
        "CUDA storage declaration '{0}' is invalid",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidStorageType = new(
        "CS2CUDA014",
        "CUDA storage element type is invalid",
        "Type '{0}' is not valid for CUDA storage '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidAlignment = new(
        "CS2CUDA015",
        "CUDA storage alignment is invalid",
        "Alignment '{0}' is not valid for CUDA storage '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidConstantInitializer = new(
        "CS2CUDA016",
        "CUDA constant initializer is invalid",
        "CUDA constant array '{0}' requires a nonempty compile-time initializer",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidWarpMask = new(
        "CS2CUDA017",
        "CUDA warp mask is invalid",
        "CUDA warp mask must be a nonzero compile-time uint value",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidWarpWidth = new(
        "CS2CUDA018",
        "CUDA warp width is invalid",
        "CUDA warp width must be a compile-time power of two from 1 through 32",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidAtomicType = new(
        "CS2CUDA019",
        "CUDA atomic type is invalid",
        "Type '{0}' is not valid for CUDA atomic operation '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstantWrite = new(
        "CS2CUDA020",
        "CUDA constant storage is read-only",
        "CUDA constant array '{0}' is read-only",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidOutputPath = new(
        "CS2CUDA021",
        "CUDA output path is invalid",
        "CUDA output path '{0}' must be a relative .cu file path below the assembly output directory",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingOutputPaths = new(
        "CS2CUDA022",
        "CUDA output path is not unique",
        "CUDA output path '{0}' conflicts with output path '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BuildFailure = new(
        "CS2CUDA023",
        "CUDA build transpilation failed",
        "CUDA build transpilation failed: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidInlineArray = new(
        "CS2CUDA024",
        "CUDA inline array is invalid",
        "Field '{0}' must use a supported pointer type and a positive inline-array length",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        MissingTranslationUnit,
        InvalidTranslationUnit,
        UnsupportedMember,
        MissingFunctionKind,
        UnsupportedSyntax,
        UnsupportedCall,
        UnsupportedType,
        ConflictingFunctionKinds,
        InvalidReadOnlyParameter,
        InvalidIdentifier,
        DeclarationCollision,
        CheckedOverflowCompilation,
        InvalidStorage,
        InvalidStorageType,
        InvalidAlignment,
        InvalidConstantInitializer,
        InvalidWarpMask,
        InvalidWarpWidth,
        InvalidAtomicType,
        ConstantWrite,
        InvalidOutputPath,
        ConflictingOutputPaths,
        BuildFailure,
        InvalidInlineArray
    ];
}
