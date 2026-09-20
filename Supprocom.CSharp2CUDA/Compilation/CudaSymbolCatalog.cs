using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Supprocom.CSharp2CUDA.Compilation;

internal sealed class CudaSymbolCatalog
{
    private const string PackageAssemblyName = "Supprocom.CSharp2CUDA";
    private readonly Dictionary<IMethodSymbol, string> runtimeMethods =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, string?> runtimeFields =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, INamedTypeSymbol> packageAttributes =
        new(StringComparer.Ordinal);

    public CudaSymbolCatalog(CSharpCompilation compilation)
    {
        CudaType = ResolvePackageType(compilation, "Supprocom.CSharp2CUDA.Cuda");
        CudaDimensionType = ResolvePackageType(
            compilation,
            "Supprocom.CSharp2CUDA.CudaDimension");
        CudaInt32Type = ResolvePackageType(compilation, "Supprocom.CSharp2CUDA.CudaInt32");
        SpanType = compilation.GetTypeByMetadataName("System.Span`1");
        ReadOnlySpanType = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
        IndexType = compilation.GetTypeByMetadataName("System.Index");
        MemoryExtensionsType = compilation.GetTypeByMetadataName("System.MemoryExtensions");
        StructLayoutAttributeType = compilation.GetTypeByMetadataName(
            "System.Runtime.InteropServices.StructLayoutAttribute");
        FieldOffsetAttributeType = compilation.GetTypeByMetadataName(
            "System.Runtime.InteropServices.FieldOffsetAttribute");
        CudaLogMethod = ResolveMethod(
            CudaType,
            nameof(Cuda.Log),
            SpecialType.System_Double);

        AddPackageAttribute(compilation, CudaEmissionPlan.ExternalAttributeName);
        AddPackageAttribute(compilation, CudaEmissionPlan.ExternalDeviceAttributeName);
        AddPackageAttribute(compilation, CudaEmissionPlan.InlineArrayAttributeName);
        AddPackageAttribute(compilation, CudaEmissionPlan.ConstantAttributeName);
        AddPackageAttribute(compilation, CudaEmissionPlan.DeviceAttributeName);
        AddPackageAttribute(compilation, CudaEmissionPlan.GlobalAttributeName);
        AddPackageAttribute(compilation, CudaEmissionPlan.ReadOnlyAttributeName);

        AddRuntime(compilation, "System.Math", "Abs", "fabs", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Acos", "acos", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Acosh", "acosh", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Asin", "asin", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Asinh", "asinh", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Atan", "atan", SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "Atan2",
            "atan2",
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Atanh", "atanh", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Cbrt", "cbrt", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Ceiling", "ceil", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Cos", "cos", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Cosh", "cosh", SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "Clamp",
            "csharp2cuda_f64_clamp",
            SpecialType.System_Double,
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "CopySign",
            "copysign",
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Floor", "floor", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Exp", "exp", SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "FusedMultiplyAdd",
            "fma",
            SpecialType.System_Double,
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "IEEERemainder",
            "remainder",
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "ILogB", "ilogb", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Log", "log", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Log10", "log10", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Log2", "log2", SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "Max",
            "csharp2cuda_f64_maximum",
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "Pow",
            "pow",
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Round", "nearbyint", SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "Min",
            "csharp2cuda_f64_minimum",
            SpecialType.System_Double,
            SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Math",
            "ScaleB",
            "ldexp",
            SpecialType.System_Double,
            SpecialType.System_Int32);
        AddRuntime(compilation, "System.Math", "Truncate", "trunc", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Sign", "csharp2cuda_f64_sign", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Sin", "sin", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Sinh", "sinh", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Sqrt", "sqrt", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Tan", "tan", SpecialType.System_Double);
        AddRuntime(compilation, "System.Math", "Tanh", "tanh", SpecialType.System_Double);

        AddRuntime(compilation, "System.MathF", "Abs", "fabsf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Acos", "acosf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Acosh", "acoshf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Asin", "asinf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Asinh", "asinhf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Atan", "atanf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Atan2", "atan2f", SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Atanh", "atanhf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Cbrt", "cbrtf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Ceiling", "ceilf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Clamp", "csharp2cuda_f32_clamp", SpecialType.System_Single, SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "CopySign", "copysignf", SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Cos", "cosf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Cosh", "coshf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Exp", "expf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Floor", "floorf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "FusedMultiplyAdd", "fmaf", SpecialType.System_Single, SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "IEEERemainder", "remainderf", SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "ILogB", "ilogbf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Log", "logf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Log10", "log10f", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Log2", "log2f", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Max", "csharp2cuda_f32_maximum", SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Min", "csharp2cuda_f32_minimum", SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Pow", "powf", SpecialType.System_Single, SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Round", "nearbyintf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "ScaleB", "ldexpf", SpecialType.System_Single, SpecialType.System_Int32);
        AddRuntime(compilation, "System.MathF", "Sign", "csharp2cuda_f32_sign", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Sin", "sinf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Sinh", "sinhf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Sqrt", "sqrtf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Tan", "tanf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Tanh", "tanhf", SpecialType.System_Single);
        AddRuntime(compilation, "System.MathF", "Truncate", "truncf", SpecialType.System_Single);

        foreach (var type in new[]
        {
            SpecialType.System_SByte,
            SpecialType.System_Int16,
            SpecialType.System_Int32,
            SpecialType.System_Int64
        })
        {
            AddRuntime(compilation, "System.Math", "Abs", "csharp2cuda_integral_abs", type);
            AddRuntime(compilation, "System.Math", "Sign", "csharp2cuda_integral_sign", type);
        }
        foreach (var type in new[]
        {
            SpecialType.System_SByte,
            SpecialType.System_Byte,
            SpecialType.System_Int16,
            SpecialType.System_UInt16,
            SpecialType.System_Int32,
            SpecialType.System_UInt32,
            SpecialType.System_Int64,
            SpecialType.System_UInt64
        })
        {
            AddRuntime(compilation, "System.Math", "Min", "csharp2cuda_integral_minimum", type, type);
            AddRuntime(compilation, "System.Math", "Max", "csharp2cuda_integral_maximum", type, type);
            AddRuntime(
                compilation,
                "System.Math",
                "Clamp",
                "csharp2cuda_integral_clamp",
                type,
                type,
                type);
        }

        AddRuntime(compilation, "System.Double", "IsFinite", "isfinite", SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.Double",
            "IsInfinity",
            "isinf",
            SpecialType.System_Double);
        AddRuntime(compilation, "System.Double", "IsNaN", "isnan", SpecialType.System_Double);
        AddRuntime(compilation, "System.Single", "IsFinite", "isfinite", SpecialType.System_Single);
        AddRuntime(compilation, "System.Single", "IsInfinity", "isinf", SpecialType.System_Single);
        AddRuntime(compilation, "System.Single", "IsNaN", "isnan", SpecialType.System_Single);
        AddRuntime(
            compilation,
            "System.BitConverter",
            "DoubleToInt64Bits",
            "__double_as_longlong",
            SpecialType.System_Double);
        AddRuntime(
            compilation,
            "System.BitConverter",
            "Int64BitsToDouble",
            "__longlong_as_double",
            SpecialType.System_Int64);
        AddRuntime(
            compilation,
            "System.BitConverter",
            "SingleToInt32Bits",
            "__float_as_int",
            SpecialType.System_Single);
        AddRuntime(
            compilation,
            "System.BitConverter",
            "Int32BitsToSingle",
            "__int_as_float",
            SpecialType.System_Int32);

        AddRuntime(compilation, "System.Numerics.BitOperations", "LeadingZeroCount", "__clz", SpecialType.System_UInt32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "LeadingZeroCount", "__clzll", SpecialType.System_UInt64);
        AddRuntime(compilation, "System.Numerics.BitOperations", "PopCount", "__popc", SpecialType.System_UInt32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "PopCount", "__popcll", SpecialType.System_UInt64);
        AddRuntime(compilation, "System.Numerics.BitOperations", "TrailingZeroCount", "csharp2cuda_u32_trailing_zero_count", SpecialType.System_UInt32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "TrailingZeroCount", "csharp2cuda_u64_trailing_zero_count", SpecialType.System_UInt64);
        AddRuntime(compilation, "System.Numerics.BitOperations", "RotateLeft", "csharp2cuda_u32_rotate_left", SpecialType.System_UInt32, SpecialType.System_Int32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "RotateLeft", "csharp2cuda_u64_rotate_left", SpecialType.System_UInt64, SpecialType.System_Int32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "RotateRight", "csharp2cuda_u32_rotate_right", SpecialType.System_UInt32, SpecialType.System_Int32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "RotateRight", "csharp2cuda_u64_rotate_right", SpecialType.System_UInt64, SpecialType.System_Int32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "Log2", "csharp2cuda_u32_log2", SpecialType.System_UInt32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "Log2", "csharp2cuda_u64_log2", SpecialType.System_UInt64);
        AddRuntime(compilation, "System.Numerics.BitOperations", "IsPow2", "csharp2cuda_is_pow2", SpecialType.System_Int32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "IsPow2", "csharp2cuda_is_pow2", SpecialType.System_Int64);
        AddRuntime(compilation, "System.Numerics.BitOperations", "IsPow2", "csharp2cuda_is_pow2", SpecialType.System_UInt32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "IsPow2", "csharp2cuda_is_pow2", SpecialType.System_UInt64);
        AddRuntime(compilation, "System.Numerics.BitOperations", "RoundUpToPowerOf2", "csharp2cuda_u32_round_up_to_power_of_two", SpecialType.System_UInt32);
        AddRuntime(compilation, "System.Numerics.BitOperations", "RoundUpToPowerOf2", "csharp2cuda_u64_round_up_to_power_of_two", SpecialType.System_UInt64);

        AddRuntimeField(compilation, "System.Math", "E");
        AddRuntimeField(compilation, "System.Math", "PI");
        AddRuntimeField(compilation, "System.Math", "Tau");
        AddRuntimeField(compilation, "System.MathF", "E");
        AddRuntimeField(compilation, "System.MathF", "PI");
        AddRuntimeField(compilation, "System.MathF", "Tau");
        foreach (var name in new[]
        {
            "Epsilon", "MaxValue", "MinValue", "NaN", "NegativeInfinity",
            "NegativeZero", "PositiveInfinity"
        })
        {
            AddRuntimeField(compilation, "System.Double", name);
            AddRuntimeField(compilation, "System.Single", name);
        }
        foreach (var typeName in new[]
        {
            "System.SByte", "System.Byte", "System.Int16", "System.UInt16",
            "System.Char", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64"
        })
        {
            AddRuntimeField(compilation, typeName, "MinValue");
            AddRuntimeField(compilation, typeName, "MaxValue");
        }
    }

    public INamedTypeSymbol? CudaType { get; }

    public INamedTypeSymbol? CudaDimensionType { get; }

    public INamedTypeSymbol? CudaInt32Type { get; }

    public INamedTypeSymbol? SpanType { get; }

    public INamedTypeSymbol? ReadOnlySpanType { get; }

    public INamedTypeSymbol? IndexType { get; }

    public INamedTypeSymbol? MemoryExtensionsType { get; }

    public INamedTypeSymbol? StructLayoutAttributeType { get; }

    public INamedTypeSymbol? FieldOffsetAttributeType { get; }

    public IMethodSymbol? CudaLogMethod { get; }

    public bool IsCudaType(ITypeSymbol? type) => IsType(type, CudaType);

    public bool IsCudaDimensionType(ITypeSymbol? type) =>
        IsType(type, CudaDimensionType);

    public bool IsCudaInt32Type(ITypeSymbol? type) => IsType(type, CudaInt32Type);

    public bool IsSpanType(ITypeSymbol? type, out bool readOnly)
    {
        readOnly = false;
        if (type is not INamedTypeSymbol named)
            return false;
        if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, SpanType))
            return true;
        readOnly = SymbolEqualityComparer.Default.Equals(
            named.OriginalDefinition,
            ReadOnlySpanType);
        return readOnly;
    }

    public bool IsMemoryExtensionsType(ITypeSymbol? type) =>
        IsType(type, MemoryExtensionsType);

    public bool IsIndexType(ITypeSymbol? type) => IsType(type, IndexType);

    public bool TryGetRuntimeMethod(IMethodSymbol method, out string name) =>
        runtimeMethods.TryGetValue(method.OriginalDefinition, out name!);

    public bool TryGetRuntimeField(IFieldSymbol field, out string? code) =>
        runtimeFields.TryGetValue(field.OriginalDefinition, out code);

    public AttributeData? GetAttribute(ISymbol symbol, string metadataName)
    {
        if (!packageAttributes.TryGetValue(metadataName, out var attributeType))
            return null;
        return symbol.GetAttributes().FirstOrDefault(attribute =>
            IsType(attribute.AttributeClass, attributeType));
    }

    public bool IsAttributeType(ITypeSymbol? type, string metadataName) =>
        packageAttributes.TryGetValue(metadataName, out var attributeType)
            ? IsType(type, attributeType)
            : metadataName == "System.Runtime.InteropServices.StructLayoutAttribute"
                ? IsType(type, StructLayoutAttributeType)
                : metadataName == "System.Runtime.InteropServices.FieldOffsetAttribute" &&
                    IsType(type, FieldOffsetAttributeType);

    public AttributeData? GetStructLayoutAttribute(INamedTypeSymbol type) =>
        GetExactAttribute(type, StructLayoutAttributeType);

    public AttributeData? GetFieldOffsetAttribute(IFieldSymbol field) =>
        GetExactAttribute(field, FieldOffsetAttributeType);

    private void AddRuntime(
        CSharpCompilation compilation,
        string metadataType,
        string methodName,
        string emittedName,
        params SpecialType[] parameterTypes)
    {
        var type = compilation.GetTypeByMetadataName(metadataType);
        var method = ResolveMethod(type, methodName, parameterTypes);
        if (method is not null)
            runtimeMethods[method.OriginalDefinition] = emittedName;
    }

    private void AddPackageAttribute(CSharpCompilation compilation, string metadataName)
    {
        if (ResolvePackageType(compilation, metadataName) is { } type)
            packageAttributes.Add(metadataName, type);
    }

    private void AddRuntimeField(
        CSharpCompilation compilation,
        string metadataType,
        string fieldName,
        string? emittedCode = null)
    {
        var field = compilation.GetTypeByMetadataName(metadataType)?
            .GetMembers(fieldName)
            .OfType<IFieldSymbol>()
            .SingleOrDefault(static candidate => candidate.IsStatic);
        if (field is not null && (field.HasConstantValue || emittedCode is not null))
            runtimeFields[field.OriginalDefinition] = emittedCode;
    }

    private static IMethodSymbol? ResolveMethod(
        INamedTypeSymbol? type,
        string name,
        params SpecialType[] parameterTypes) => type?.GetMembers(name)
        .OfType<IMethodSymbol>()
        .SingleOrDefault(method =>
            method.IsStatic &&
            method.Arity == 0 &&
            method.Parameters.Length == parameterTypes.Length &&
            method.Parameters.Select(static parameter => parameter.Type.SpecialType)
                .SequenceEqual(parameterTypes));

    private static INamedTypeSymbol? ResolvePackageType(
        CSharpCompilation compilation,
        string metadataName)
    {
        var packageAssembly = compilation.SourceModule.ReferencedAssemblySymbols
            .FirstOrDefault(static assembly =>
                assembly.Identity.Name == PackageAssemblyName);
        return packageAssembly?.GetTypeByMetadataName(metadataName);
    }

    private static bool IsType(ITypeSymbol? candidate, INamedTypeSymbol? expected) =>
        candidate is INamedTypeSymbol named &&
        expected is not null &&
        SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expected);

    private static AttributeData? GetExactAttribute(
        ISymbol symbol,
        INamedTypeSymbol? expected) => expected is null
        ? null
        : symbol.GetAttributes().FirstOrDefault(attribute =>
            IsType(attribute.AttributeClass, expected));
}
