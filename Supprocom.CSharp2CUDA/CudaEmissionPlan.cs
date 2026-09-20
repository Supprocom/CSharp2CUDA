using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Supprocom.CSharp2CUDA.Compilation;
using Supprocom.CSharp2CUDA.Emission;
using Supprocom.CSharp2CUDA.Semantics;

namespace Supprocom.CSharp2CUDA;

internal sealed class CudaEmissionPlan
{
    public const int MaximumFixedLocalElementCount = 1024;

    internal const string ExternalAttributeName = "Supprocom.CSharp2CUDA.CudaExternalAttribute";
    internal const string ExternalDeviceAttributeName =
        "Supprocom.CSharp2CUDA.CudaExternalDeviceAttribute";
    internal const string InlineArrayAttributeName =
        "Supprocom.CSharp2CUDA.CudaInlineArrayAttribute";
    internal const string ConstantAttributeName = "Supprocom.CSharp2CUDA.CudaConstantAttribute";
    internal const string DeviceAttributeName = "Supprocom.CSharp2CUDA.CudaDeviceAttribute";
    internal const string GlobalAttributeName = "Supprocom.CSharp2CUDA.CudaGlobalAttribute";
    internal const string ReadOnlyAttributeName = "Supprocom.CSharp2CUDA.CudaReadOnlyAttribute";
    internal const string StructLayoutAttributeName =
        "System.Runtime.InteropServices.StructLayoutAttribute";
    internal const string FieldOffsetAttributeName =
        "System.Runtime.InteropServices.FieldOffsetAttribute";
    private static readonly HashSet<SyntaxKind> UnitModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.StaticKeyword,
        SyntaxKind.UnsafeKeyword
    ];

    private static readonly HashSet<SyntaxKind> StructModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.ReadOnlyKeyword,
        SyntaxKind.UnsafeKeyword
    ];

    private static readonly HashSet<SyntaxKind> MethodModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.ReadOnlyKeyword,
        SyntaxKind.StaticKeyword,
        SyntaxKind.UnsafeKeyword
    ];

    private static readonly HashSet<SyntaxKind> FieldModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.ReadOnlyKeyword
    ];

    private static readonly HashSet<SyntaxKind> ConstantFieldModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.ReadOnlyKeyword,
        SyntaxKind.StaticKeyword
    ];

    private static readonly HashSet<SpecialType> StorageElementTypes =
    [
        SpecialType.System_Int32,
        SpecialType.System_UInt32,
        SpecialType.System_Int64,
        SpecialType.System_UInt64,
        SpecialType.System_Double
    ];

    private readonly CSharpCompilation compilation;
    private readonly ImmutableArray<Diagnostic>.Builder diagnostics;
    private readonly CudaSymbolCatalog symbols;
    private readonly Dictionary<ISymbol, string> identifierNames =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, CudaFunctionPlan> functionPlans =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, CudaStructPlan> structPlans =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, CudaFieldPlan> fieldPlans =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IPropertySymbol, CudaPropertyPlan> propertyPlans =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<LocalDeclarationStatementSyntax, CudaFixedLocalArrayPlan>
        fixedLocalArrays = [];
    private readonly HashSet<LocalDeclarationStatementSyntax> recognizedStorageDeclarations = [];
    private readonly Dictionary<LocalDeclarationStatementSyntax, CudaStoragePlan>
        storageDeclarations = [];
    private readonly Dictionary<ILocalSymbol, CudaStoragePlan> dynamicSharedStorage =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, CudaConstantArrayPlan> constantArrayPlans =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> pureFunctions =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IParameterSymbol> writableViewParameters =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> reportedTypes = new(StringComparer.Ordinal);

    private CudaEmissionPlan(
        CSharpCompilation compilation,
        IReadOnlyList<ClassDeclarationSyntax> units,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        this.compilation = compilation;
        this.diagnostics = diagnostics;
        symbols = new CudaSymbolCatalog(compilation);
        Units = units.Select(unit => new CudaUnitPlan(
            unit,
            compilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: true)))
            .ToImmutableArray();
    }

    public ImmutableArray<CudaUnitPlan> Units { get; }

    public ImmutableArray<CudaStructPlan> Structs { get; private set; } = [];

    public ImmutableArray<CudaFunctionPlan> Functions { get; private set; } = [];

    public ImmutableArray<CudaConstantArrayPlan> ConstantArrays { get; private set; } = [];

    public bool UsesVolatileMappedMemory { get; private set; }

    public bool UsesGlobalTimer { get; private set; }

    public static CudaEmissionPlan Create(
        CSharpCompilation compilation,
        IReadOnlyList<ClassDeclarationSyntax> units,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var plan = new CudaEmissionPlan(compilation, units, diagnostics);
        plan.Build();
        return plan;
    }

    public SemanticModel GetSemanticModel(SyntaxNode node) =>
        compilation.GetSemanticModel(node.SyntaxTree, ignoreAccessibility: true);

    public string GetIdentifier(ISymbol symbol) =>
        identifierNames.TryGetValue(symbol, out var name) ? name : symbol.Name;

    public bool TryGetIdentifier(ISymbol? symbol, out string name)
    {
        if (symbol is not null && identifierNames.TryGetValue(symbol, out name!))
            return true;
        name = string.Empty;
        return false;
    }

    public bool TryGetFunction(IMethodSymbol method, out CudaFunctionPlan function) =>
        functionPlans.TryGetValue(NormalizeMethod(method), out function!);

    public IMethodSymbol ResolveMethod(IMethodSymbol method, CudaFunctionPlan caller)
    {
        method = NormalizeMethod(method);
        if (!method.IsGenericMethod)
            return method;

        var arguments = method.TypeArguments
            .Select(type => SubstituteType(type, caller))
            .ToArray();
        if (arguments.Any(static type => type.TypeKind == TypeKind.TypeParameter))
            return method;
        return method.OriginalDefinition.Construct(arguments);
    }

    public ITypeSymbol SubstituteType(ITypeSymbol type, CudaFunctionPlan function)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            for (var index = 0; index < function.DefinitionSymbol.TypeParameters.Length; index++)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        parameter,
                        function.DefinitionSymbol.TypeParameters[index]))
                {
                    return function.Symbol.TypeArguments[index];
                }
            }

            var definitionType = function.DefinitionSymbol.ContainingType;
            var constructedType = function.Symbol.ContainingType;
            for (var index = 0; index < definitionType.TypeParameters.Length; index++)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        parameter,
                        definitionType.TypeParameters[index]))
                {
                    return constructedType.TypeArguments[index];
                }
            }
            return type;
        }

        if (type is IArrayTypeSymbol array)
        {
            var element = SubstituteType(array.ElementType, function);
            return SymbolEqualityComparer.Default.Equals(element, array.ElementType)
                ? array
                : compilation.CreateArrayTypeSymbol(element, array.Rank);
        }

        if (type is IPointerTypeSymbol pointer)
        {
            var element = SubstituteType(pointer.PointedAtType, function);
            return SymbolEqualityComparer.Default.Equals(element, pointer.PointedAtType)
                ? pointer
                : compilation.CreatePointerTypeSymbol(element);
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            var arguments = named.TypeArguments
                .Select(argument => SubstituteType(argument, function))
                .ToArray();
            var changed = arguments.Where((argument, index) =>
                !SymbolEqualityComparer.Default.Equals(argument, named.TypeArguments[index])).Any();
            return changed ? named.OriginalDefinition.Construct(arguments) : named;
        }

        return type;
    }

    public bool TryGetStruct(INamedTypeSymbol type, out CudaStructPlan structure)
    {
        if (structPlans.TryGetValue(type, out structure!))
            return true;
        structure = structPlans.Values.FirstOrDefault(item =>
            SymbolEqualityComparer.Default.Equals(
                item.DefinitionSymbol,
                type.OriginalDefinition))!;
        return structure is not null;
    }

    public bool TryGetField(IFieldSymbol field, out CudaFieldPlan fieldPlan) =>
        fieldPlans.TryGetValue(field, out fieldPlan!);

    public bool TryGetProperty(IPropertySymbol property, out CudaPropertyPlan propertyPlan) =>
        propertyPlans.TryGetValue(property, out propertyPlan!);

    public bool TryGetFixedLocalArray(
        LocalDeclarationStatementSyntax declaration,
        out CudaFixedLocalArrayPlan fixedArray) =>
        fixedLocalArrays.TryGetValue(declaration, out fixedArray!);

    public bool IsRecognizedStorageDeclaration(LocalDeclarationStatementSyntax declaration) =>
        recognizedStorageDeclarations.Contains(declaration);

    public bool TryGetStorageDeclaration(
        LocalDeclarationStatementSyntax declaration,
        out CudaStoragePlan storage) => storageDeclarations.TryGetValue(declaration, out storage!);

    public bool IsStorageInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault() is
        { } declaration &&
        recognizedStorageDeclarations.Contains(declaration) &&
        declaration.Declaration.Variables.Count == 1 &&
        declaration.Declaration.Variables[0].Initializer?.Value is { } initializer &&
        initializer.SyntaxTree == invocation.SyntaxTree &&
        initializer.Span == invocation.Span;

    public bool TryGetDynamicSharedStorage(ISymbol? symbol, out CudaStoragePlan storage)
    {
        if (symbol is ILocalSymbol local && dynamicSharedStorage.TryGetValue(local, out storage!))
            return true;
        storage = null!;
        return false;
    }

    public bool TryGetConstantArray(IFieldSymbol field, out CudaConstantArrayPlan constant) =>
        constantArrayPlans.TryGetValue(field, out constant!);

    public static bool IsStorageElementType(ITypeSymbol type) =>
        StorageElementTypes.Contains(type.SpecialType);

    public static int GetNaturalAlignment(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 or SpecialType.System_UInt32 => 4,
        SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Double => 8,
        _ => 0
    };

    public CudaCallPlan? GetCallPlan(IMethodSymbol method)
    {
        method = NormalizeMethod(method);
        if (functionPlans.TryGetValue(method, out var function))
            return new CudaCallPlan(CudaCallKind.PlannedFunction, function.EmittedName);

        if (symbols.CudaLogMethod is not null && SymbolEqualityComparer.Default.Equals(
                method.OriginalDefinition,
                symbols.CudaLogMethod))
        {
            return new CudaCallPlan(CudaCallKind.Direct, "log");
        }

        if (symbols.IsCudaType(method.ContainingType))
        {
            return method.Name switch
            {
                nameof(Cuda.SyncThreads) => new(CudaCallKind.Direct, "__syncthreads"),
                nameof(Cuda.ThreadFence) => new(CudaCallKind.Direct, "__threadfence"),
                nameof(Cuda.ThreadFenceSystem) =>
                    new(CudaCallKind.Direct, "__threadfence_system"),
                nameof(Cuda.VolatileLoad) => GetVolatilePointerCallPlan(method, false),
                nameof(Cuda.VolatileLoadInt32) =>
                    GetVolatileMappedCallPlan("csharp2cuda_volatile_load_i32_bytes"),
                nameof(Cuda.VolatileLoadUInt64) =>
                    GetVolatileMappedCallPlan("csharp2cuda_volatile_load_u64_bytes"),
                nameof(Cuda.VolatileStore) => GetVolatilePointerCallPlan(method, true),
                nameof(Cuda.VolatileStoreInt32) =>
                    GetVolatileMappedCallPlan("csharp2cuda_volatile_store_i32_bytes"),
                nameof(Cuda.VolatileStoreUInt64) =>
                    GetVolatileMappedCallPlan("csharp2cuda_volatile_store_u64_bytes"),
                nameof(Cuda.GlobalTimer) => GetGlobalTimerCallPlan(),
                nameof(Cuda.SyncWarp) => new(CudaCallKind.Direct, "__syncwarp"),
                nameof(Cuda.ShuffleDownSync) =>
                    new(CudaCallKind.Direct, "__shfl_down_sync"),
                nameof(Cuda.NanoSleep) => new(CudaCallKind.Direct, "__nanosleep"),
                nameof(Cuda.Shared) or nameof(Cuda.SharedArray) or
                    nameof(Cuda.DynamicSharedBytes) =>
                    new(CudaCallKind.Storage, string.Empty),
                nameof(Cuda.DynamicSharedView) =>
                    new(CudaCallKind.DynamicSharedView, string.Empty),
                nameof(Cuda.AtomicAdd) or nameof(Cuda.AtomicExchange) or
                    nameof(Cuda.AtomicCompareExchange) or nameof(Cuda.AtomicXor) or
                    nameof(Cuda.AtomicMin) => GetAtomicCallPlan(method),
                nameof(Cuda.Int) => new(CudaCallKind.BooleanToInteger, string.Empty),
                nameof(Cuda.Bool) => new(CudaCallKind.IntegerToBoolean, string.Empty),
                nameof(Cuda.Unsigned) => new(CudaCallKind.SignedToUnsigned, string.Empty),
                nameof(Cuda.ReadOnly) => new(CudaCallKind.Unwrap, string.Empty),
                nameof(Cuda.Array) => GetArrayViewCallPlan(readOnly: false),
                nameof(Cuda.ReadOnlyArray) => GetArrayViewCallPlan(readOnly: true),
                nameof(Cuda.FloatingRemainder) => new(CudaCallKind.Direct, "fmod"),
                nameof(Cuda.NearbyInteger) => new(CudaCallKind.Direct, "nearbyint"),
                nameof(Cuda.SignBit) => new(CudaCallKind.Direct, "signbit"),
                nameof(Cuda.DoubleAddRoundNearest) => new(CudaCallKind.Direct, "__dadd_rn"),
                nameof(Cuda.DoubleSubtractRoundNearest) =>
                    new(CudaCallKind.Direct, "__dsub_rn"),
                nameof(Cuda.DoubleMultiplyRoundNearest) =>
                    new(CudaCallKind.Direct, "__dmul_rn"),
                nameof(Cuda.DoubleDivideRoundNearest) =>
                    new(CudaCallKind.Direct, "__ddiv_rn"),
                nameof(Cuda.Log1p) => new(CudaCallKind.Direct, "log1p"),
                nameof(Cuda.Sqrt) => new(CudaCallKind.Direct, "sqrt"),
                nameof(Cuda.Exp) => new(CudaCallKind.Direct, "exp"),
                nameof(Cuda.Pow) => new(CudaCallKind.Direct, "pow"),
                nameof(Cuda.NaN) => new(CudaCallKind.NaN, "nan"),
                _ => null
            };
        }

        if (symbols.IsSpanType(method.ContainingType, out _) &&
            method.Name == "Slice" &&
            method.Parameters.Length is 1 or 2 &&
            method.Parameters.All(static parameter =>
                parameter.Type.SpecialType == SpecialType.System_Int32))
        {
            UsesArrayViews = true;
            return new CudaCallPlan(CudaCallKind.SliceView, "slice");
        }
        if (symbols.IsSpanType(method.ContainingType, out var readOnlySpan))
        {
            UsesArrayViews = true;
            if (!readOnlySpan && method.Name == "Clear" && method.Parameters.IsEmpty)
                return new CudaCallPlan(CudaCallKind.ClearView, "clear");
            if (!readOnlySpan && method.Name == "Fill" && method.Parameters.Length == 1)
                return new CudaCallPlan(CudaCallKind.FillView, "fill");
            if (method.Name == "CopyTo" && method.Parameters.Length == 1)
                return new CudaCallPlan(CudaCallKind.CopyView, "copy_to");
            if (method.Name == "TryCopyTo" && method.Parameters.Length == 1)
                return new CudaCallPlan(CudaCallKind.TryCopyView, "try_copy_to");
        }
        if (symbols.IsMemoryExtensionsType(method.ContainingType) &&
            method.Name == "AsSpan" &&
            method.IsGenericMethod &&
            method.Parameters.Length is >= 1 and <= 3 &&
            method.Parameters[0].Type is IArrayTypeSymbol { Rank: 1 } &&
            method.Parameters.Skip(1).All(static parameter =>
                parameter.Type.SpecialType == SpecialType.System_Int32))
        {
            UsesArrayViews = true;
            return new CudaCallPlan(CudaCallKind.AsSpanView, "as_span");
        }

        return symbols.TryGetRuntimeMethod(method, out var mappedName)
            ? new CudaCallPlan(CudaCallKind.Direct, mappedName)
            : null;
    }

    public bool IsPureCall(IMethodSymbol method)
    {
        if (functionPlans.ContainsKey(method))
            return pureFunctions.Contains(method);
        var call = GetCallPlan(method);
        return call is not null &&
            (call.Kind is CudaCallKind.BooleanToInteger or
                CudaCallKind.IntegerToBoolean or
                CudaCallKind.SignedToUnsigned or
                CudaCallKind.Unwrap or
                CudaCallKind.DynamicSharedView or
                CudaCallKind.ArrayView or
                CudaCallKind.ReadOnlyArrayView or
                CudaCallKind.SliceView or
                CudaCallKind.AsSpanView or
                CudaCallKind.NaN ||
                call.Kind == CudaCallKind.Direct && !IsImpureIntrinsic(call.Name));
    }

    private static CudaCallPlan GetAtomicCallPlan(IMethodSymbol method)
    {
        if (method.IsGenericMethod)
            return new(CudaCallKind.InvalidAtomic, method.Name);

        var name = method.Name switch
        {
            nameof(Cuda.AtomicAdd) => "atomicAdd",
            nameof(Cuda.AtomicExchange) => "atomicExch",
            nameof(Cuda.AtomicCompareExchange) => "atomicCAS",
            nameof(Cuda.AtomicXor) => "atomicXor",
            nameof(Cuda.AtomicMin) => "atomicMin",
            _ => string.Empty
        };
        var type = method.Parameters[0].Type;
        return type.SpecialType == SpecialType.System_Int64 &&
            method.Name != nameof(Cuda.AtomicMin)
            ? new(CudaCallKind.SignedInt64Atomic, name)
            : new(CudaCallKind.Atomic, name);
    }

    private CudaCallPlan GetVolatilePointerCallPlan(IMethodSymbol method, bool store)
    {
        UsesVolatileMappedMemory = true;
        var pointer = (IPointerTypeSymbol)method.Parameters[0].Type;
        var suffix = pointer.PointedAtType.SpecialType == SpecialType.System_Int32
            ? "i32"
            : "u64";
        return new CudaCallPlan(
            CudaCallKind.Direct,
            $"csharp2cuda_volatile_{(store ? "store" : "load")}_{suffix}");
    }

    private CudaCallPlan GetVolatileMappedCallPlan(string name)
    {
        UsesVolatileMappedMemory = true;
        return new CudaCallPlan(CudaCallKind.Direct, name);
    }

    private CudaCallPlan GetGlobalTimerCallPlan()
    {
        UsesGlobalTimer = true;
        return new CudaCallPlan(CudaCallKind.Direct, "csharp2cuda_global_timer");
    }

    private CudaCallPlan GetArrayViewCallPlan(bool readOnly)
    {
        UsesArrayViews = true;
        return new CudaCallPlan(
            readOnly ? CudaCallKind.ReadOnlyArrayView : CudaCallKind.ArrayView,
            string.Empty);
    }

    private static bool IsImpureIntrinsic(string name) => name is
        "__syncthreads" or "__threadfence" or "__threadfence_system" or
        "__syncwarp" or "__shfl_down_sync" or "__nanosleep" or
        "csharp2cuda_volatile_load_i32" or "csharp2cuda_volatile_load_u64" or
        "csharp2cuda_volatile_load_i32_bytes" or
        "csharp2cuda_volatile_load_u64_bytes" or
        "csharp2cuda_volatile_store_i32" or "csharp2cuda_volatile_store_u64" or
        "csharp2cuda_volatile_store_i32_bytes" or
        "csharp2cuda_volatile_store_u64_bytes" or "csharp2cuda_global_timer";

    public bool TryGetDimensionReplacement(
        MemberAccessExpressionSyntax expression,
        out string replacement)
    {
        replacement = string.Empty;
        if (expression.Expression is not MemberAccessExpressionSyntax dimension)
            return false;

        var model = GetSemanticModel(expression);
        if (model.GetSymbolInfo(expression).Symbol is not IPropertySymbol component ||
            !symbols.IsCudaDimensionType(component.ContainingType) ||
            component.Name is not nameof(CudaDimension.X) and
                not nameof(CudaDimension.Y) and
                not nameof(CudaDimension.Z) ||
            model.GetSymbolInfo(dimension).Symbol is not IPropertySymbol source ||
            !symbols.IsCudaType(source.ContainingType))
        {
            return false;
        }

        var target = source.Name switch
        {
            nameof(Cuda.ThreadIdx) => "threadIdx",
            nameof(Cuda.BlockIdx) => "blockIdx",
            nameof(Cuda.BlockDim) => "blockDim",
            nameof(Cuda.GridDim) => "gridDim",
            _ => null
        };
        if (target is null)
            return false;

        replacement = $"{target}.{component.Name.ToLowerInvariant()}";
        return true;
    }

    public bool IsDimensionProperty(IPropertySymbol property) =>
        symbols.IsCudaType(property.ContainingType) &&
        property.Name is nameof(Cuda.ThreadIdx) or nameof(Cuda.BlockIdx) or
            nameof(Cuda.BlockDim) or nameof(Cuda.GridDim) ||
        symbols.IsCudaDimensionType(property.ContainingType) &&
        property.Name is nameof(CudaDimension.X) or nameof(CudaDimension.Y) or
            nameof(CudaDimension.Z);

    public string FormatType(ITypeSymbol type, bool deepReadOnly, Location location)
    {
        if (symbols.IsCudaInt32Type(type))
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

        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            UsesArrayViews = true;
            var viewName = deepReadOnly
                ? "csharp2cuda_readonly_array_view"
                : "csharp2cuda_array_view";
            return $"{viewName}<{FormatType(array.ElementType, false, location)}>";
        }

        if (type is INamedTypeSymbol view &&
            view.TypeArguments.Length == 1 &&
            symbols.IsSpanType(view, out var readOnlyView))
        {
            UsesArrayViews = true;
            var element = FormatType(view.TypeArguments[0], false, location);
            return readOnlyView
                ? $"csharp2cuda_readonly_array_view<{element}>"
                : $"csharp2cuda_array_view<{element}>";
        }

        if (type is INamedTypeSymbol { IsTupleType: true } tuple &&
            tuple.TupleElements.Length is >= 2 and <= 7)
        {
            UsesTupleTypes = true;
            var elements = tuple.TupleElements
                .Select(element => FormatType(element.Type, false, location));
            return $"csharp2cuda_tuple{tuple.TupleElements.Length}<" +
                string.Join(", ", elements) + ">";
        }

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
                SpecialType.System_Char => "unsigned short",
                SpecialType.System_Int32 => "int",
                SpecialType.System_UInt32 => "unsigned int",
                SpecialType.System_Int64 => "long long",
                SpecialType.System_UInt64 => "unsigned long long",
                SpecialType.System_Single => "float",
                SpecialType.System_Double => "double",
                _ => ReportUnsupportedType(type, location)
            };
        }

        if (type is INamedTypeSymbol named && structPlans.TryGetValue(named, out var structure))
            return structure.EmittedName;

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration &&
            enumeration.EnumUnderlyingType is not null)
        {
            return FormatType(enumeration.EnumUnderlyingType, false, location);
        }

        return ReportUnsupportedType(type, location);
    }

    public string FormatParameterType(CudaFunctionPlan function, int index)
    {
        var parameter = function.Symbol.Parameters[index];
        var syntax = function.Syntax.ParameterList.Parameters[index];
        var readOnly = HasAttribute(parameter, ReadOnlyAttributeName);
        if (IsArrayViewType(parameter.Type))
            readOnly = !IsViewParameterWritable(parameter);
        var prefix = parameter.RefKind == RefKind.In ? "const " : string.Empty;
        var suffix = parameter.RefKind == RefKind.None ? string.Empty : "*";
        return prefix + FormatType(
            parameter.Type,
            readOnly,
            syntax.Type?.GetLocation() ?? syntax.GetLocation()) + suffix;
    }

    public bool IsViewParameterWritable(IParameterSymbol parameter) =>
        writableViewParameters.Contains(parameter);

    public bool IsCudaType(ITypeSymbol? type) => symbols.IsCudaType(type);

    public bool IsCudaInt32Type(ITypeSymbol? type) => symbols.IsCudaInt32Type(type);

    public bool TryGetRuntimeField(IFieldSymbol field, out string? code) =>
        symbols.TryGetRuntimeField(field, out code);

    public bool IsSpanType(ITypeSymbol? type, out bool readOnly) =>
        symbols.IsSpanType(type, out readOnly);

    public bool RequiresAbiLayout(CudaStructPlan structure) =>
        IsKernelAbiType(structure.Symbol);

    public bool TryGetExplicitFieldLayout(
        IFieldSymbol field,
        out CudaStructPlan structure,
        out CudaFieldLayout layout)
    {
        if (fieldPlans.TryGetValue(field, out var fieldPlan) &&
            structPlans.TryGetValue(field.ContainingType, out structure!) &&
            structure.Layout is { IsExplicit: true } explicitLayout)
        {
            layout = explicitLayout.Fields.First(item =>
                SymbolEqualityComparer.Default.Equals(
                    item.Field.Symbol,
                    fieldPlan.Symbol));
            return true;
        }
        structure = null!;
        layout = null!;
        return false;
    }

    public bool IsArrayViewType(ITypeSymbol? type) =>
        type is IArrayTypeSymbol { Rank: 1 } ||
        symbols.IsSpanType(type, out _);

    public bool IsTupleType(ITypeSymbol? type) =>
        type is INamedTypeSymbol
        {
            IsTupleType: true,
            TupleElements.Length: >= 2 and <= 7
        };

    public bool HasAttribute(ISymbol symbol, string metadataName) =>
        GetAttribute(symbol, metadataName) is not null;

    public AttributeData? GetAttribute(ISymbol symbol, string metadataName) =>
        symbols.GetAttribute(symbol, metadataName);

    public bool UsesArrayViews { get; private set; }

    public bool UsesTupleTypes { get; private set; }

    private void Build()
    {
        var structures = new List<CudaStructPlan>();
        var functions = new List<CudaFunctionPlan>();
        var constants = new List<CudaConstantArrayPlan>();

        foreach (var unit in Units)
        {
            ValidateUnit(unit);
            foreach (var member in unit.Syntax.Members)
            {
                switch (member)
                {
                    case StructDeclarationSyntax structure:
                        RegisterStruct(unit.Model, structure, structures);
                        break;
                    case RecordDeclarationSyntax
                    {
                        ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword
                    } record:
                        RegisterStruct(unit.Model, record, structures);
                        break;
                    case MethodDeclarationSyntax method:
                        if (HasCudaFunctionContract(unit.Model, method))
                            RegisterFunction(
                                unit.Model,
                                CudaFunctionSource.Create(method),
                                functions,
                                isInferred: false);
                        break;
                    case FieldDeclarationSyntax field:
                        if (HasCudaConstantContract(unit.Model, field))
                            RegisterConstantArray(unit, field, constants);
                        break;
                }
            }
        }

        DiscoverReachableFunctions(functions);
        DiscoverReachableStructs(functions, structures);
        AnalyzeCaptures(functions);
        AnalyzeViewParameters(functions);

        foreach (var structure in structures)
            ValidateStruct(structure);
        ValidateStructLayouts(structures);

        Structs = OrderStructs(structures).ToImmutableArray();
        Functions = functions
            .OrderBy(static function => function.EmittedName, StringComparer.Ordinal)
            .ThenBy(
                static function => function.Symbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat),
                StringComparer.Ordinal)
            .ToImmutableArray();
        ConstantArrays = constants
            .OrderBy(static constant => constant.EmittedName, StringComparer.Ordinal)
            .ToImmutableArray();

        foreach (var function in Functions)
            ValidateFunction(function);

        ValidateGlobalCollisions();
        ComputePureFunctions();

        if (diagnostics.All(static diagnostic =>
                diagnostic.Severity != DiagnosticSeverity.Error))
        {
            foreach (var function in Functions.Where(static function => !function.IsExternal))
            {
                function.Body = new CudaOperationLowerer(this, function, diagnostics).Lower();
            }
        }
    }

    private void RegisterConstantArray(
        CudaUnitPlan unit,
        FieldDeclarationSyntax syntax,
        ICollection<CudaConstantArrayPlan> constants)
    {
        var hasConstantAttribute = syntax.Declaration.Variables
            .Select(variable => unit.Model.GetDeclaredSymbol(variable))
            .OfType<IFieldSymbol>()
            .Any(symbol => HasAttribute(symbol, ConstantAttributeName));
        if (!hasConstantAttribute)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.UnsupportedMember,
                syntax.GetLocation(),
                syntax.Kind().ToString()));
            return;
        }

        if (syntax.Declaration.Variables.Count != 1 ||
            syntax.Modifiers.Any(modifier => !ConstantFieldModifiers.Contains(modifier.Kind())) ||
            !syntax.Modifiers.Any(SyntaxKind.StaticKeyword) ||
            !syntax.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) ||
            !HasOnlyAttributes(syntax.AttributeLists, ConstantAttributeName))
        {
            ReportInvalidStorage(syntax, "device constant array");
            return;
        }

        var variable = syntax.Declaration.Variables[0];
        if (unit.Model.GetDeclaredSymbol(variable) is not IFieldSymbol symbol)
            return;
        var name = RegisterIdentifier(
            symbol,
            variable.Identifier.ValueText,
            variable.Identifier.GetLocation());

        if (symbol.Type is not IArrayTypeSymbol
            {
                Rank: 1,
                ElementType.SpecialType: SpecialType.System_Int32
            })
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStorageType,
                syntax.Declaration.Type.GetLocation(),
                symbol.Type.ToDisplayString(),
                "device constant array"));
            return;
        }

        if (!TryGetConstantValues(variable.Initializer?.Value, unit.Model, out var values))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidConstantInitializer,
                variable.Initializer?.GetLocation() ?? variable.GetLocation(),
                symbol.Name));
            return;
        }

        var constant = new CudaConstantArrayPlan(syntax, variable, symbol, name, values);
        constants.Add(constant);
        constantArrayPlans[symbol] = constant;
    }

    private void ValidateUnit(CudaUnitPlan unit)
    {
        var syntax = unit.Syntax;
        if (!syntax.Modifiers.Any(SyntaxKind.StaticKeyword) ||
            syntax.Modifiers.Any(modifier => !UnitModifiers.Contains(modifier.Kind())) ||
            syntax.TypeParameterList is not null ||
            syntax.BaseList is not null ||
            syntax.ConstraintClauses.Count != 0)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidTranslationUnit,
                syntax.Identifier.GetLocation(),
                syntax.Identifier.ValueText));
        }
    }

    private void RegisterStruct(
        SemanticModel model,
        TypeDeclarationSyntax syntax,
        ICollection<CudaStructPlan> structures,
        INamedTypeSymbol? constructedSymbol = null)
    {
        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol definitionSymbol)
            return;

        var symbol = constructedSymbol ?? definitionSymbol;
        if (structPlans.ContainsKey(symbol))
            return;

        var externalAttribute = GetAttribute(symbol, ExternalAttributeName);
        if (externalAttribute is not null && GetNamedBoolean(
                externalAttribute,
                nameof(CudaExternalAttribute.IsPure),
                false))
        {
            ReportUnsupportedSyntax(syntax);
        }
        var sourceName = symbol.IsGenericType
            ? CreateInferredTypeName(symbol)
            : syntax.Identifier.ValueText;
        var name = RegisterIdentifier(symbol, sourceName, syntax.Identifier.GetLocation());
        var structure = new CudaStructPlan(
            syntax,
            symbol,
            definitionSymbol,
            model,
            name,
            externalAttribute is not null);
        structures.Add(structure);
        structPlans[symbol] = structure;
        if (syntax is RecordDeclarationSyntax record)
            RegisterPositionalRecordProperties(structure, record);
    }

    private void DiscoverReachableStructs(
        IEnumerable<CudaFunctionPlan> functions,
        ICollection<CudaStructPlan> structures)
    {
        var queue = new Queue<ITypeSymbol>();
        foreach (var function in functions)
        {
            queue.Enqueue(function.Symbol.ReturnType);
            foreach (var parameter in function.Symbol.Parameters)
                queue.Enqueue(parameter.Type);

            var root = function.Syntax.Body is not null
                ? function.Model.GetOperation(function.Syntax.Body)
                : function.Syntax.ExpressionBodyExpression is not null
                    ? function.Model.GetOperation(function.Syntax.ExpressionBodyExpression)
                    : null;
            if (root is null)
                continue;
            foreach (var operation in EnumerateOperations(root))
            {
                if (operation.Type is not null)
                    queue.Enqueue(SubstituteType(operation.Type, function));
            }
        }

        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!visited.Add(type))
                continue;
            switch (type)
            {
                case IPointerTypeSymbol pointer:
                    queue.Enqueue(pointer.PointedAtType);
                    continue;
                case IArrayTypeSymbol array:
                    queue.Enqueue(array.ElementType);
                    continue;
                case INamedTypeSymbol named when named.IsGenericType:
                    foreach (var argument in named.TypeArguments)
                        queue.Enqueue(argument);
                    break;
            }

            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } structure ||
                structure.SpecialType != SpecialType.None ||
                symbols.IsCudaInt32Type(structure) ||
                symbols.IsCudaDimensionType(structure) ||
                structPlans.ContainsKey(structure))
            {
                continue;
            }

            var declaration = structure.DeclaringSyntaxReferences
                .OrderBy(static reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                .ThenBy(static reference => reference.Span.Start)
                .Select(static reference => reference.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(static candidate =>
                    candidate is StructDeclarationSyntax ||
                    candidate is RecordDeclarationSyntax
                    {
                        ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword
                    });
            if (declaration is null)
                continue;

            RegisterStruct(
                GetSemanticModel(declaration),
                declaration,
                structures,
                constructedSymbol: structure);
            foreach (var field in structure.GetMembers().OfType<IFieldSymbol>().Where(
                         static field => !field.IsStatic))
            {
                queue.Enqueue(field.Type);
            }
            foreach (var property in structure.GetMembers().OfType<IPropertySymbol>().Where(
                         static property => !property.IsStatic))
            {
                queue.Enqueue(property.Type);
            }
        }
    }

    private void RegisterFunction(
        SemanticModel model,
        CudaFunctionSource syntax,
        ICollection<CudaFunctionPlan> functions,
        bool isInferred,
        IMethodSymbol? constructedSymbol = null)
    {
        var definitionSymbol = model.GetDeclaredSymbol(syntax.Node) as IMethodSymbol ??
            constructedSymbol?.OriginalDefinition;
        if (definitionSymbol is null)
            return;
        definitionSymbol = NormalizeMethod(definitionSymbol);
        var symbol = NormalizeMethod(constructedSymbol ?? definitionSymbol);
        if (functionPlans.ContainsKey(symbol))
            return;

        var externalAttribute = GetAttribute(symbol, ExternalAttributeName);
        var externalDevice = GetAttribute(symbol, ExternalDeviceAttributeName);
        var external = externalAttribute is not null || externalDevice is not null;
        var device = GetAttribute(symbol, DeviceAttributeName);
        var global = GetAttribute(symbol, GlobalAttributeName);
        var kind = isInferred || device is not null || externalDevice is not null
            ? CudaFunctionKind.Device
            : global is not null
                ? CudaFunctionKind.Global
                : CudaFunctionKind.External;
        var namingAttribute = device ?? global ?? externalDevice;
        var name = isInferred
            ? CreateInferredFunctionName(symbol)
            : namingAttribute is null
            ? symbol.Name
            : GetNamedString(namingAttribute, nameof(CudaDeviceAttribute.Name)) ?? symbol.Name;
        var location = namingAttribute is null
            ? syntax.Identifier.GetLocation()
            : GetNameLocation(symbol, namingAttribute);
        name = RegisterIdentifier(symbol, name, location);
        var externC = global is not null &&
            GetNamedBoolean(global, nameof(CudaGlobalAttribute.ExternC), true);
        var function = new CudaFunctionPlan(
            syntax,
            symbol,
            definitionSymbol,
            model,
            name,
            kind,
            externC,
            external,
            (externalAttribute ?? externalDevice) is { } purityAttribute && GetNamedBoolean(
                purityAttribute,
                nameof(CudaExternalAttribute.IsPure),
                false),
            externalDevice is not null,
            device is not null,
            global is not null,
            externalDevice is not null,
            isInferred);
        functions.Add(function);
        functionPlans[symbol] = function;

        foreach (var parameter in symbol.Parameters)
        {
            var parameterSyntax = syntax.ParameterList.Parameters[parameter.Ordinal];
            var parameterName = RegisterIdentifier(
                parameter,
                parameterSyntax.Identifier.ValueText,
                parameterSyntax.Identifier.GetLocation());
            identifierNames[definitionSymbol.Parameters[parameter.Ordinal]] = parameterName;
        }

        if (!external && syntax.Body is not null)
        {
            foreach (var variable in syntax.Body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (model.GetDeclaredSymbol(variable) is ILocalSymbol local)
                {
                    RegisterIdentifier(
                        local,
                        variable.Identifier.ValueText,
                        variable.Identifier.GetLocation());
                }
            }

            foreach (var loop in syntax.Body.DescendantNodes().OfType<ForEachStatementSyntax>())
            {
                if (model.GetDeclaredSymbol(loop) is ILocalSymbol local)
                {
                    RegisterIdentifier(
                        local,
                        loop.Identifier.ValueText,
                        loop.Identifier.GetLocation());
                }
            }

            foreach (var declaration in syntax.Body.DescendantNodes()
                         .OfType<LocalDeclarationStatementSyntax>())
            {
                if (TryCreateFixedLocalArrayPlan(declaration, model, out var fixedArray))
                    fixedLocalArrays.Add(declaration, fixedArray);
                else
                    RegisterStorageDeclaration(declaration, function);
            }
        }

        if (!external)
        {
            foreach (var designation in syntax.Node.DescendantNodes()
                         .OfType<SingleVariableDesignationSyntax>())
            {
                if (model.GetDeclaredSymbol(designation) is ILocalSymbol local)
                {
                    RegisterIdentifier(
                        local,
                        designation.Identifier.ValueText,
                        designation.Identifier.GetLocation());
                }
            }
        }
    }

    private void RegisterStorageDeclaration(
        LocalDeclarationStatementSyntax declaration,
        CudaFunctionPlan function)
    {
        if (declaration.Declaration.Variables.Count != 1 ||
            declaration.Declaration.Variables[0].Initializer?.Value is not
                InvocationExpressionSyntax invocation ||
            function.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            !symbols.IsCudaType(method.ContainingType) ||
            method.Name is not (nameof(Cuda.Shared) or nameof(Cuda.SharedArray) or
                nameof(Cuda.DynamicSharedBytes)))
        {
            return;
        }

        recognizedStorageDeclarations.Add(declaration);
        var variable = declaration.Declaration.Variables[0];
        if (function.Model.GetDeclaredSymbol(variable) is not ILocalSymbol local)
            return;

        if (function.Kind != CudaFunctionKind.Global ||
            declaration.Modifiers.Count != 0 ||
            declaration.UsingKeyword != default ||
            declaration.AwaitKeyword != default)
        {
            ReportInvalidStorage(declaration, method.Name);
            return;
        }

        if (method.Name == nameof(Cuda.DynamicSharedBytes))
        {
            RegisterDynamicSharedBytes(declaration, invocation, local, function);
            return;
        }

        var elementType = method.TypeArguments.Single();
        if (!IsStorageElementType(elementType))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStorageType,
                invocation.GetLocation(),
                elementType.ToDisplayString(),
                method.Name));
            return;
        }

        if (method.Name == nameof(Cuda.Shared))
        {
            if (!SymbolEqualityComparer.Default.Equals(local.Type, elementType))
            {
                ReportInvalidStorage(declaration, method.Name);
                return;
            }
            storageDeclarations[declaration] = new CudaStoragePlan(
                declaration,
                local,
                CudaStorageKind.SharedScalar,
                elementType,
                0,
                0);
            return;
        }

        if (local.Type is not IPointerTypeSymbol pointer ||
            !SymbolEqualityComparer.Default.Equals(pointer.PointedAtType, elementType) ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            function.Model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression) is not
            { HasValue: true, Value: int length } ||
            length <= 0)
        {
            ReportInvalidStorage(declaration, method.Name);
            return;
        }

        storageDeclarations[declaration] = new CudaStoragePlan(
            declaration,
            local,
            CudaStorageKind.SharedArray,
            elementType,
            length,
            0);
    }

    private void RegisterDynamicSharedBytes(
        LocalDeclarationStatementSyntax declaration,
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        CudaFunctionPlan function)
    {
        if (local.Type is not IPointerTypeSymbol
            {
                PointedAtType.SpecialType: SpecialType.System_Byte
            } ||
            invocation.ArgumentList.Arguments.Count != 1)
        {
            ReportInvalidStorage(declaration, nameof(Cuda.DynamicSharedBytes));
            return;
        }

        var constant = function.Model.GetConstantValue(
            invocation.ArgumentList.Arguments[0].Expression);
        if (constant is not { HasValue: true, Value: int alignment } ||
            alignment is not (1 or 2 or 4 or 8 or 16))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidAlignment,
                invocation.ArgumentList.Arguments[0].GetLocation(),
                constant.HasValue ? constant.Value?.ToString() ?? "null" : "nonconstant",
                nameof(Cuda.DynamicSharedBytes)));
            return;
        }

        if (dynamicSharedStorage.Values.Any(storage =>
                storage.Declaration.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() ==
                function.Syntax.Node))
        {
            ReportInvalidStorage(declaration, nameof(Cuda.DynamicSharedBytes));
            return;
        }

        var storage = new CudaStoragePlan(
            declaration,
            local,
            CudaStorageKind.DynamicSharedBytes,
            ((IPointerTypeSymbol)local.Type).PointedAtType,
            0,
            alignment);
        storageDeclarations[declaration] = storage;
        dynamicSharedStorage[local] = storage;
    }

    private void ValidateStruct(CudaStructPlan structure)
    {
        var syntax = structure.Syntax;
        var invalidDeclarationShape = syntax switch
        {
            StructDeclarationSyntax { ParameterList: not null } => true,
            RecordDeclarationSyntax record =>
                !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ||
                record.ParameterList is null ||
                !record.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) ||
                record.TypeParameterList is not null,
            _ => false
        };
        if (!structure.IsExternal && !structure.Symbol.IsUnmanagedType)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStructureLayout,
                syntax.Identifier.GetLocation(),
                structure.Symbol.ToDisplayString(),
                "the structure contains managed storage"));
        }
        if (syntax.Modifiers.Any(modifier => !StructModifiers.Contains(modifier.Kind())) ||
            syntax.BaseList is not null ||
            invalidDeclarationShape ||
            !HasOnlyAttributes(
                syntax.AttributeLists,
                ExternalAttributeName,
                StructLayoutAttributeName))
        {
            ReportUnsupportedSyntax(syntax);
        }

        if (structure.Symbol.IsGenericType &&
            structure.Symbol.TypeArguments.Any(static type =>
                type.TypeKind is TypeKind.TypeParameter or TypeKind.Error ||
                !type.IsUnmanagedType))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidGenericConstruction,
                syntax.Identifier.GetLocation(),
                structure.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        foreach (var member in syntax.Members)
        {
            if (member is OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax)
                continue;

            if (member is BaseMethodDeclarationSyntax method &&
                structure.Model.GetDeclaredSymbol(method) is IMethodSymbol methodSymbol &&
                IsPlannedFunctionDefinition(methodSymbol))
            {
                continue;
            }

            if (member is ConversionOperatorDeclarationSyntax conversion &&
                structure.Model.GetDeclaredSymbol(conversion) is IMethodSymbol conversionSymbol &&
                IsPlannedFunctionDefinition(conversionSymbol))
            {
                continue;
            }

            if (member is PropertyDeclarationSyntax property &&
                TryRegisterAutoProperty(structure, property))
            {
                continue;
            }


            if (member is PropertyDeclarationSyntax sourceProperty &&
                structure.Model.GetDeclaredSymbol(sourceProperty) is IPropertySymbol propertySymbol &&
                IsPlannedProperty(propertySymbol))
            {
                continue;
            }

            if (member is IndexerDeclarationSyntax sourceIndexer &&
                structure.Model.GetDeclaredSymbol(sourceIndexer) is IPropertySymbol indexerSymbol &&
                IsPlannedProperty(indexerSymbol))
            {
                continue;
            }

            if (member is not FieldDeclarationSyntax field ||
                field.Declaration.Variables.Count != 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.UnsupportedMember,
                    member.GetLocation(),
                    member.Kind().ToString()));
                continue;
            }

            if (field.Modifiers.Any(modifier => !FieldModifiers.Contains(modifier.Kind())) ||
                !HasOnlyAttributes(
                    field.AttributeLists,
                    InlineArrayAttributeName,
                    FieldOffsetAttributeName))
            {
                ReportUnsupportedSyntax(field);
            }

            var variable = field.Declaration.Variables[0];
            if (structure.Model.GetDeclaredSymbol(variable) is not IFieldSymbol definitionField)
                continue;
            var symbol = structure.Symbol.GetMembers(definitionField.Name)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(
                    field.OriginalDefinition,
                    definitionField)) ?? definitionField;
            RegisterIdentifier(symbol, variable.Identifier.ValueText, variable.Identifier.GetLocation());
            identifierNames[definitionField] = GetIdentifier(symbol);
            var inlineArray = GetAttribute(symbol, InlineArrayAttributeName);
            var inlineArrayLength = 0;
            if (inlineArray is not null &&
                !structure.IsExternal &&
                symbol.Type is IPointerTypeSymbol pointer &&
                IsSupportedInlineArrayElement(pointer.PointedAtType) &&
                inlineArray.ConstructorArguments is
                [
                    {
                        Kind: TypedConstantKind.Primitive,
                        Value: int length
                    }
                ] &&
                length > 0)
            {
                inlineArrayLength = length;
                FormatType(pointer.PointedAtType, false, field.Declaration.Type.GetLocation());
            }
            else
            {
                if (inlineArray is null)
                {
                    FormatType(symbol.Type, false, field.Declaration.Type.GetLocation());
                }
                else
                {
                    diagnostics.Add(Diagnostic.Create(
                        CudaDiagnostics.InvalidInlineArray,
                        field.GetLocation(),
                        symbol.Name));
                }
            }

            var fieldPlan = new CudaFieldPlan(
                field,
                variable,
                symbol,
                inlineArrayLength);
            structure.Fields.Add(fieldPlan);
            fieldPlans[symbol] = fieldPlan;
            fieldPlans[definitionField] = fieldPlan;

            if (symbol.IsStatic || symbol.IsConst || symbol.IsVolatile ||
                variable.Initializer is not null)
            {
                ReportUnsupportedSyntax(field);
            }
        }
    }

    private bool IsPlannedFunctionDefinition(IMethodSymbol method)
    {
        method = NormalizeMethod(method);
        return functionPlans.Values.Any(function => SymbolEqualityComparer.Default.Equals(
            function.DefinitionSymbol,
            method));
    }

    private bool TryRegisterAutoProperty(
        CudaStructPlan structure,
        PropertyDeclarationSyntax property)
    {
        if (property.AccessorList is null ||
            property.ExpressionBody is not null ||
            property.AccessorList.Accessors.Any(static accessor =>
                accessor.Body is not null || accessor.ExpressionBody is not null) ||
            property.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword)) ||
            property.Initializer is not null ||
            property.AttributeLists.SelectMany(static list => list.Attributes).Any())
        {
            return false;
        }

        if (structure.Model.GetDeclaredSymbol(property) is not IPropertySymbol definitionProperty)
            return false;
        var symbol = structure.Symbol.GetMembers(definitionProperty.Name)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                candidate.OriginalDefinition,
                definitionProperty)) ?? definitionProperty;
        var name = RegisterIdentifier(
            symbol,
            property.Identifier.ValueText,
            property.Identifier.GetLocation());
        identifierNames[definitionProperty] = name;
        FormatType(symbol.Type, false, property.Type.GetLocation());
        var propertyPlan = new CudaPropertyPlan(property, property.Type, symbol);
        structure.Properties.Add(propertyPlan);
        propertyPlans[symbol] = propertyPlan;
        propertyPlans[definitionProperty] = propertyPlan;

        if (IsKernelAbiType(structure.Symbol))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStructureLayout,
                property.Identifier.GetLocation(),
                structure.Symbol.ToDisplayString(),
                "auto-properties cannot cross the kernel ABI"));
        }
        return true;
    }

    private void RegisterPositionalRecordProperties(
        CudaStructPlan structure,
        RecordDeclarationSyntax record)
    {
        if (record.ParameterList is null)
            return;
        foreach (var parameter in record.ParameterList.Parameters)
        {
            if (parameter.Type is null ||
                structure.Model.GetDeclaredSymbol(parameter) is not IParameterSymbol positional)
            {
                continue;
            }
            var definitionProperty = structure.DefinitionSymbol.GetMembers(positional.Name)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(static property => !property.IsStatic);
            if (definitionProperty is null)
                continue;
            var symbol = structure.Symbol.GetMembers(positional.Name)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(static property => !property.IsStatic) ?? definitionProperty;
            var name = RegisterIdentifier(
                symbol,
                parameter.Identifier.ValueText,
                parameter.Identifier.GetLocation());
            identifierNames[definitionProperty] = name;
            FormatType(symbol.Type, false, parameter.Type.GetLocation());
            var propertyPlan = new CudaPropertyPlan(parameter, parameter.Type, symbol);
            structure.Properties.Add(propertyPlan);
            propertyPlans[symbol] = propertyPlan;
            propertyPlans[definitionProperty] = propertyPlan;
        }
    }

    private bool IsPlannedProperty(IPropertySymbol property)
    {
        var accessors = new[] { property.GetMethod, property.SetMethod }
            .OfType<IMethodSymbol>()
            .Where(HasSourceBody)
            .ToArray();
        return accessors.Length > 0 && accessors.All(accessor =>
            functionPlans.ContainsKey(NormalizeMethod(accessor)));
    }

    private bool IsKernelAbiType(INamedTypeSymbol structure) => functionPlans.Values
        .Where(static function => function.Kind == CudaFunctionKind.Global)
        .SelectMany(static function => function.Symbol.Parameters)
        .Any(parameter => ContainsType(
            parameter.Type,
            structure,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)));

    private bool ContainsType(
        ITypeSymbol type,
        INamedTypeSymbol expected,
        HashSet<ITypeSymbol> visited)
    {
        while (type is IPointerTypeSymbol pointer)
            type = pointer.PointedAtType;
        if (SymbolEqualityComparer.Default.Equals(type, expected))
            return true;
        if (type is not INamedTypeSymbol named ||
            named.TypeKind != TypeKind.Struct ||
            !visited.Add(named))
        {
            return false;
        }
        if (!structPlans.TryGetValue(named, out var plan))
            return false;
        foreach (var field in plan.Fields)
        {
            var fieldType = field.InlineArrayLength > 0 &&
                field.Symbol.Type is IPointerTypeSymbol inlinePointer
                ? inlinePointer.PointedAtType
                : field.Symbol.Type;
            if (ContainsType(fieldType, expected, visited))
                return true;
        }
        return false;
    }

    private bool IsSupportedInlineArrayElement(ITypeSymbol elementType)
    {
        if (elementType.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Char or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double)
        {
            return true;
        }

        return elementType is INamedTypeSymbol named &&
            structPlans.TryGetValue(named, out var elementPlan) &&
            !elementPlan.IsExternal;
    }

    private void ValidateStructLayouts(IEnumerable<CudaStructPlan> structures)
    {
        var layoutEngine = new CudaLayoutEngine(symbols, structPlans);
        foreach (var structure in structures.Where(static item => !item.IsExternal))
        {
            if (structure.Properties.Count > 0 && !IsKernelAbiType(structure.Symbol))
                continue;
            if (layoutEngine.TryGetLayout(structure, out var layout, out var reason))
            {
                structure.Layout = layout;
                continue;
            }
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStructureLayout,
                structure.Syntax.Identifier.GetLocation(),
                structure.Symbol.ToDisplayString(),
                reason));
        }
    }

    private void ValidateFunction(CudaFunctionPlan function)
    {
        var syntax = function.Syntax;
        var validInstanceMethod = !function.Symbol.IsStatic &&
            function.Symbol.ContainingType.TypeKind == TypeKind.Struct &&
            !function.Symbol.IsVirtual &&
            !function.Symbol.IsOverride &&
            !function.Symbol.IsAbstract;
        if ((!syntax.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                !validInstanceMethod &&
                !function.IsLocalFunction &&
                !function.IsAnonymousFunction &&
                !function.IsConstructor) ||
            syntax.Modifiers.Any(modifier => !MethodModifiers.Contains(modifier.Kind())) ||
            syntax.ExplicitInterfaceSpecifier is not null ||
            (!function.IsInferred && !HasOnlyAttributes(
                syntax.AttributeLists,
                ExternalAttributeName,
                ExternalDeviceAttributeName,
                DeviceAttributeName,
                GlobalAttributeName)))
        {
            ReportUnsupportedSyntax(syntax.Node);
        }

        if (function.IsConstructor &&
            function.Syntax.Node is ConstructorDeclarationSyntax { Initializer: not null })
        {
            ReportUnsupportedSyntax(function.Syntax.Node);
        }

        if (function.Syntax.Node is OperatorDeclarationSyntax { CheckedKeyword.RawKind: not 0 } or
            ConversionOperatorDeclarationSyntax { CheckedKeyword.RawKind: not 0 } ||
            function.Symbol.Name is "op_True" or "op_False")
        {
            ReportUnsupportedSyntax(function.Syntax.Node);
        }

        if ((function.Symbol.IsGenericMethod &&
             function.Symbol.TypeArguments.Any(static type =>
                 type.TypeKind is TypeKind.TypeParameter or TypeKind.Error ||
                 !type.IsUnmanagedType)) ||
            (!function.Symbol.IsGenericMethod && syntax.TypeParameterList is not null))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidGenericConstruction,
                syntax.Identifier.GetLocation(),
                function.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        if (function.HasDeviceAttribute && function.HasGlobalAttribute)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ConflictingFunctionKinds,
                syntax.Identifier.GetLocation(),
                function.Symbol.Name));
        }
        else if (function.HasExternalDeviceAttribute &&
            (function.HasDeviceAttribute ||
             function.HasGlobalAttribute ||
             HasAttribute(function.Symbol, ExternalAttributeName)))
        {
            ReportUnsupportedSyntax(syntax.Node);
        }
        else if (!function.IsExternal &&
            !function.HasDeviceAttribute &&
            !function.HasGlobalAttribute &&
            !function.IsInferred)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.MissingFunctionKind,
                syntax.Identifier.GetLocation(),
                function.Symbol.Name));
        }
        else if (function.IsExternal && !function.HasExternalDeviceAttribute &&
            (function.HasDeviceAttribute || function.HasGlobalAttribute))
        {
            ReportUnsupportedSyntax(syntax.Node);
        }

        if (!function.IsExternal && syntax.Body is null && syntax.ExpressionBodyExpression is null)
        {
            ReportUnsupportedSyntax(syntax.Node);
        }

        FormatType(
            function.IsConstructor ? function.Symbol.ContainingType : function.Symbol.ReturnType,
            false,
            syntax.ReturnType?.GetLocation() ?? syntax.Identifier.GetLocation());
        if ((function.Kind == CudaFunctionKind.Global || function.IsExternal) &&
            (ContainsDeviceInternalAbiType(
                 function.Symbol.ReturnType,
                 new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ||
             function.IsExternal && IsArrayViewType(function.Symbol.ReturnType)))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidPortableAbiType,
                syntax.ReturnType?.GetLocation() ?? syntax.Identifier.GetLocation(),
                function.Symbol.ReturnType.ToDisplayString()));
        }
        foreach (var parameter in function.Symbol.Parameters)
            ValidateParameter(function, parameter);

        if (function.IsPureExternal)
        {
            foreach (var parameter in function.Symbol.Parameters.Where(parameter =>
                         parameter.Type is IPointerTypeSymbol &&
                         !HasAttribute(parameter, ReadOnlyAttributeName)))
            {
                ReportUnsupportedSyntax(
                    syntax.ParameterList.Parameters[parameter.Ordinal]);
            }
        }

        if (!function.IsExternal && syntax.Body is not null)
        {
            foreach (var declaration in syntax.Body.DescendantNodes()
                         .OfType<LocalDeclarationStatementSyntax>())
            {
                ValidateLocalDeclaration(declaration, function.Model);
                if (fixedLocalArrays.TryGetValue(declaration, out var fixedArray))
                    ValidateFixedLocalArrayEscape(function, fixedArray);
            }
        }
        if (!function.IsExternal)
        {
            ValidateArrayRangeEscapes(function);
            ValidateExpandedParamsEscapes(function);
        }
    }

    private void ValidateArrayRangeEscapes(CudaFunctionPlan function)
    {
        var root = GetFunctionOperation(function);
        if (root is null)
            return;
        var ranges = EnumerateOperations(root)
            .Where(IsArrayRangeOperation)
            .ToArray();
        foreach (var range in ranges)
        {
            var aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var referenceAliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            bool changed;
            do
            {
                changed = false;
                foreach (var operation in EnumerateOperations(root))
                {
                    switch (operation)
                    {
                        case IVariableDeclaratorOperation
                        {
                            Symbol: ILocalSymbol local,
                            Initializer.Value: { } value
                        }:
                            if (local.RefKind != RefKind.None &&
                                IsArrayRangeReferenceDerived(
                                    value,
                                    range,
                                    aliases,
                                    referenceAliases,
                                    function))
                            {
                                changed |= referenceAliases.Add(local);
                            }
                            else if (local.RefKind == RefKind.None &&
                                IsStorageHandleType(local.Type) &&
                                IsArrayRangeDerived(value, range, aliases, function))
                            {
                                changed |= aliases.Add(local);
                            }
                            break;
                        case ISimpleAssignmentOperation
                        {
                            Target: ILocalReferenceOperation target,
                            Value: { } value
                        }:
                            if (target.Local.RefKind != RefKind.None &&
                                IsArrayRangeReferenceDerived(
                                    value,
                                    range,
                                    aliases,
                                    referenceAliases,
                                    function))
                            {
                                changed |= referenceAliases.Add(target.Local);
                            }
                            else if (target.Local.RefKind == RefKind.None &&
                                IsStorageHandleType(target.Local.Type) &&
                                IsArrayRangeDerived(value, range, aliases, function))
                            {
                                changed |= aliases.Add(target.Local);
                            }
                            break;
                    }
                }
            }
            while (changed);

            if (function.Syntax.Body is null &&
                (IsStorageHandleType(function.Symbol.ReturnType) &&
                    IsArrayRangeDerived(root, range, aliases, function) ||
                 (function.Symbol.ReturnsByRef || function.Symbol.ReturnsByRefReadonly) &&
                    IsArrayRangeReferenceDerived(
                        root,
                        range,
                        aliases,
                        referenceAliases,
                        function)))
            {
                ReportArrayRangeEscape(range.Syntax);
                continue;
            }

            foreach (var operation in EnumerateOperations(root))
            {
                var escapes = operation switch
                {
                    IReturnOperation { ReturnedValue: { } returned }
                        when (function.Symbol.ReturnsByRef ||
                              function.Symbol.ReturnsByRefReadonly) &&
                             IsArrayRangeReferenceDerived(
                                 returned,
                                 range,
                                 aliases,
                                 referenceAliases,
                                 function) => true,
                    IReturnOperation { ReturnedValue: { } returned }
                        when IsArrayRangeDerived(returned, range, aliases, function) => true,
                    ISimpleAssignmentOperation assignment
                        when assignment.Target is IFieldReferenceOperation or
                                IPropertyReferenceOperation &&
                             IsArrayRangeDerived(
                                 assignment.Value,
                                 range,
                                 aliases,
                                 function) => true,
                    IConversionOperation conversion
                        when conversion.Type?.IsReferenceType == true &&
                             !IsArrayViewType(conversion.Type) &&
                             IsArrayRangeDerived(
                                 conversion.Operand,
                                 range,
                                 aliases,
                                 function) => true,
                    IArgumentOperation argument
                        when IsArrayRangeDerived(
                                 argument.Value,
                                 range,
                                 aliases,
                                 function) &&
                             ArgumentMayEscape(argument, function) => true,
                    _ => false
                };
                if (!escapes)
                    continue;
                ReportArrayRangeEscape(operation.Syntax);
                break;
            }
        }
    }

    private static bool IsArrayRangeOperation(IOperation operation) => operation switch
    {
        IArrayElementReferenceOperation
        {
            ArrayReference.Type: IArrayTypeSymbol { Rank: 1 },
            Indices: [IRangeOperation]
        } => true,
        IImplicitIndexerReferenceOperation
        {
            Instance.Type: IArrayTypeSymbol { Rank: 1 },
            Argument: IRangeOperation
        } => true,
        _ => false
    };

    private bool IsArrayRangeDerived(
        IOperation operation,
        IOperation range,
        ISet<ISymbol> aliases,
        CudaFunctionPlan function)
    {
        while (operation is IConversionOperation conversion &&
            IsStorageHandleType(conversion.Type))
        {
            operation = conversion.Operand;
        }
        if (ReferenceEquals(operation, range))
            return true;
        if (!IsStorageHandleType(operation.Type))
            return false;
        if (operation is ILocalReferenceOperation local)
            return aliases.Contains(local.Local);
        if (operation is IConditionalOperation conditional)
        {
            return IsArrayRangeDerived(conditional.WhenTrue, range, aliases, function) ||
                conditional.WhenFalse is not null &&
                IsArrayRangeDerived(conditional.WhenFalse, range, aliases, function);
        }
        if (operation is IInvocationOperation invocation)
        {
            if (invocation.Instance is not null &&
                IsArrayRangeDerived(invocation.Instance, range, aliases, function))
            {
                return true;
            }
            return invocation.Arguments.Any(argument =>
                IsArrayRangeDerived(argument.Value, range, aliases, function));
        }
        return false;
    }

    private bool IsArrayRangeReferenceDerived(
        IOperation operation,
        IOperation range,
        ISet<ISymbol> aliases,
        ISet<ISymbol> referenceAliases,
        CudaFunctionPlan function)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        if (operation is ILocalReferenceOperation local)
            return referenceAliases.Contains(local.Local);
        if (operation is IArrayElementReferenceOperation element)
            return IsArrayRangeDerived(element.ArrayReference, range, aliases, function);
        if (operation is IImplicitIndexerReferenceOperation indexer)
            return IsArrayRangeDerived(indexer.Instance, range, aliases, function);
        if (operation is IPropertyReferenceOperation
            {
                Property.IsIndexer: true,
                Instance: { } instance
            })
        {
            return IsArrayRangeDerived(instance, range, aliases, function);
        }
        if (operation is IFieldReferenceOperation { Instance: { } fieldInstance })
        {
            return IsArrayRangeReferenceDerived(
                fieldInstance,
                range,
                aliases,
                referenceAliases,
                function);
        }
        if (operation is IInvocationOperation invocation &&
            (invocation.TargetMethod.ReturnsByRef || invocation.TargetMethod.ReturnsByRefReadonly))
        {
            if (invocation.Instance is not null &&
                (IsArrayRangeReferenceDerived(
                     invocation.Instance,
                     range,
                     aliases,
                     referenceAliases,
                     function) ||
                 IsArrayRangeDerived(invocation.Instance, range, aliases, function)))
            {
                return true;
            }
            return invocation.Arguments.Any(argument =>
                IsArrayRangeReferenceDerived(
                    argument.Value,
                    range,
                    aliases,
                    referenceAliases,
                    function) ||
                IsArrayRangeDerived(argument.Value, range, aliases, function));
        }
        return false;
    }

    private void ReportArrayRangeEscape(SyntaxNode syntax) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedRangeOperation,
            syntax.GetLocation(),
            syntax.ToString()));

    private void ValidateExpandedParamsEscapes(CudaFunctionPlan function)
    {
        var root = GetFunctionOperation(function);
        if (root is null)
            return;
        var expandedArguments = EnumerateOperations(root)
            .OfType<IArgumentOperation>()
            .Where(static argument =>
                argument.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection)
            .Where(static argument => argument.Value switch
            {
                IArrayCreationOperation { Initializer.ElementValues.Length: > 0 } => true,
                ICollectionExpressionOperation { Elements.Length: > 0 } => true,
                _ => false
            })
            .ToArray();
        foreach (var expanded in expandedArguments)
        {
            var aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            bool changed;
            do
            {
                changed = false;
                foreach (var operation in EnumerateOperations(root))
                {
                    switch (operation)
                    {
                        case IVariableDeclaratorOperation
                        {
                            Symbol: ILocalSymbol local,
                            Initializer.Value: { } value
                        } when IsStorageHandleType(local.Type) &&
                            IsArrayRangeDerived(value, expanded.Value, aliases, function):
                            changed |= aliases.Add(local);
                            break;
                        case ISimpleAssignmentOperation
                        {
                            Target: ILocalReferenceOperation target,
                            Value: { } value
                        } when IsStorageHandleType(target.Local.Type) &&
                            IsArrayRangeDerived(value, expanded.Value, aliases, function):
                            changed |= aliases.Add(target.Local);
                            break;
                    }
                }
            }
            while (changed);

            if (function.Syntax.Body is null &&
                IsStorageHandleType(function.Symbol.ReturnType) &&
                IsArrayRangeDerived(
                    root,
                    expanded.Value,
                    aliases,
                    function))
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.LocalStorageEscape,
                    root.Syntax.GetLocation(),
                    expanded.Parameter?.Name ?? "params"));
                continue;
            }

            foreach (var operation in EnumerateOperations(root))
            {
                var escapes = operation switch
                {
                    IReturnOperation { ReturnedValue: { } returned }
                        when IsArrayRangeDerived(
                            returned,
                            expanded.Value,
                            aliases,
                            function) => true,
                    ISimpleAssignmentOperation assignment
                        when assignment.Target is IFieldReferenceOperation or
                                IPropertyReferenceOperation &&
                             IsArrayRangeDerived(
                                 assignment.Value,
                                 expanded.Value,
                                 aliases,
                                 function) => true,
                    IConversionOperation conversion
                        when conversion.Type?.IsReferenceType == true &&
                             !IsArrayViewType(conversion.Type) &&
                             IsArrayRangeDerived(
                                 conversion.Operand,
                                 expanded.Value,
                                 aliases,
                                 function) => true,
                    IArgumentOperation argument
                        when IsArrayRangeDerived(
                                 argument.Value,
                                 expanded.Value,
                                 aliases,
                                 function) &&
                             ExpandedArgumentMayEscape(argument, function) => true,
                    _ => false
                };
                if (!escapes)
                    continue;
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.LocalStorageEscape,
                    operation.Syntax.GetLocation(),
                    expanded.Parameter?.Name ?? "params"));
                break;
            }
        }
    }

    private bool ExpandedArgumentMayEscape(
        IArgumentOperation argument,
        CudaFunctionPlan caller)
    {
        if (argument.Parent is IInvocationOperation invocation)
        {
            var target = ResolveMethod(invocation.TargetMethod, caller);
            if (target.ReturnsByRef || target.ReturnsByRefReadonly)
                return true;
        }
        return ArgumentMayEscape(argument, caller);
    }

    private void ValidateFixedLocalArrayEscape(
        CudaFunctionPlan function,
        CudaFixedLocalArrayPlan fixedArray)
    {
        var root = GetFunctionOperation(function);
        if (root is null)
            return;
        var aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        {
            fixedArray.Symbol
        };
        var referenceAliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        DiscoverStorageAliases(function, root, aliases, referenceAliases);
        if (ValidateStorageOperations(
                function,
                root,
                fixedArray.Symbol,
                aliases,
                referenceAliases,
                allowUnknownExternalArguments: fixedArray.IsPointer))
        {
            return;
        }

        foreach (var closure in functionPlans.Values.Where(candidate =>
                     candidate.IsClosureFunction &&
                     candidate.Captures.Any(capture => aliases.Contains(capture.Symbol))))
        {
            var closureRoot = GetFunctionOperation(closure);
            if (closureRoot is null)
                continue;
            var closureAliases = new HashSet<ISymbol>(aliases, SymbolEqualityComparer.Default);
            var closureReferenceAliases = new HashSet<ISymbol>(
                referenceAliases,
                SymbolEqualityComparer.Default);
            DiscoverStorageAliases(
                closure,
                closureRoot,
                closureAliases,
                closureReferenceAliases);
            if (ValidateStorageOperations(
                    closure,
                    closureRoot,
                    fixedArray.Symbol,
                    closureAliases,
                    closureReferenceAliases,
                    allowUnknownExternalArguments: fixedArray.IsPointer))
            {
                return;
            }
        }
    }

    private void DiscoverStorageAliases(
        CudaFunctionPlan function,
        IOperation root,
        ISet<ISymbol> aliases,
        ISet<ISymbol> referenceAliases)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var operation in EnumerateOperations(root))
            {
                switch (operation)
                {
                    case IVariableDeclaratorOperation
                    {
                        Symbol: ILocalSymbol local,
                        Initializer.Value: { } value
                    }:
                        if (local.RefKind != RefKind.None &&
                            IsStorageReferenceDerived(
                                value,
                                function,
                                aliases,
                                referenceAliases) &&
                            referenceAliases.Add(local))
                        {
                            changed = true;
                        }
                        else if (local.RefKind == RefKind.None &&
                            IsStorageHandleType(local.Type) &&
                            IsDerivedStorageValue(value, function, aliases) &&
                            aliases.Add(local))
                        {
                            changed = true;
                        }
                        break;
                    case ISimpleAssignmentOperation
                    {
                        Target: ILocalReferenceOperation target,
                        Value: { } value
                    }:
                        if (target.Local.RefKind != RefKind.None &&
                            IsStorageReferenceDerived(
                                value,
                                function,
                                aliases,
                                referenceAliases) &&
                            referenceAliases.Add(target.Local))
                        {
                            changed = true;
                        }
                        else if (target.Local.RefKind == RefKind.None &&
                            IsStorageHandleType(target.Local.Type) &&
                            IsDerivedStorageValue(value, function, aliases) &&
                            aliases.Add(target.Local))
                        {
                            changed = true;
                        }
                        break;
                }
            }
        }
        while (changed);
    }

    private bool ValidateStorageOperations(
        CudaFunctionPlan function,
        IOperation root,
        ISymbol storage,
        ISet<ISymbol> aliases,
        ISet<ISymbol> referenceAliases,
        bool allowUnknownExternalArguments)
    {
        foreach (var operation in EnumerateOperations(root))
        {
            if (operation is IReturnOperation { ReturnedValue: { } returned })
            {
                if ((function.Symbol.ReturnsByRef || function.Symbol.ReturnsByRefReadonly) &&
                    IsStorageReferenceDerived(
                        returned,
                        function,
                        aliases,
                        referenceAliases))
                {
                    diagnostics.Add(Diagnostic.Create(
                        CudaDiagnostics.InvalidReferenceEscape,
                        operation.Syntax.GetLocation(),
                        operation.Syntax.ToString()));
                    return true;
                }
                if (IsDerivedStorageValue(returned, function, aliases))
                {
                    ReportLocalStorageEscape(operation.Syntax, storage);
                    return true;
                }
            }

            SyntaxNode? offending = operation switch
            {
                ISimpleAssignmentOperation assignment
                    when assignment.Target is IFieldReferenceOperation or
                            IPropertyReferenceOperation &&
                         IsDerivedStorageValue(assignment.Value, function, aliases) =>
                    operation.Syntax,
                IConversionOperation conversion
                    when IsDerivedStorageValue(conversion.Operand, function, aliases) &&
                         conversion.Type?.IsReferenceType == true &&
                         !IsArrayViewType(conversion.Type) => operation.Syntax,
                IBinaryOperation binary
                    when binary.OperatorKind is BinaryOperatorKind.Equals or
                            BinaryOperatorKind.NotEquals &&
                         (IsManagedArrayIdentity(binary.LeftOperand, function, aliases) ||
                          IsManagedArrayIdentity(binary.RightOperand, function, aliases)) =>
                    operation.Syntax,
                IArgumentOperation argument
                    when IsDerivedStorageValue(argument.Value, function, aliases) &&
                         ArgumentMayEscape(
                             argument,
                             function,
                             allowUnknownExternalArguments) => operation.Syntax,
                _ => null
            };
            if (offending is null)
                continue;
            ReportLocalStorageEscape(offending, storage);
            return true;
        }
        return false;
    }

    private void ReportLocalStorageEscape(SyntaxNode syntax, ISymbol storage) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.LocalStorageEscape,
            syntax.GetLocation(),
            storage.Name));

    private bool ArgumentMayEscape(
        IArgumentOperation argument,
        CudaFunctionPlan caller,
        bool allowUnknownExternal = false)
    {
        if (argument.Parent is not IInvocationOperation invocation ||
            argument.Parameter is null)
        {
            return true;
        }
        var target = ResolveMethod(invocation.TargetMethod, caller);
        var call = GetCallPlan(target);
        if (call is { Kind: not CudaCallKind.PlannedFunction })
        {
            return TryGetFunction(target, out var directFunction) &&
                directFunction.IsExternal &&
                !allowUnknownExternal;
        }
        if (!TryGetFunction(target, out var function))
            return !allowUnknownExternal;
        if (function.IsExternal)
            return !allowUnknownExternal;
        if (function.Symbol.ReturnsByRef || function.Symbol.ReturnsByRefReadonly)
            return false;
        var ordinal = argument.Parameter.Ordinal;
        if (invocation.TargetMethod.ReducedFrom is not null)
            ordinal++;
        if ((uint)ordinal >= (uint)function.Symbol.Parameters.Length)
            return true;
        return ParameterEscapes(
            function,
            function.Symbol.Parameters[ordinal],
            new HashSet<string>(StringComparer.Ordinal),
            allowUnknownExternal);
    }

    private bool ParameterEscapes(
        CudaFunctionPlan function,
        IParameterSymbol parameter,
        ISet<string> visited,
        bool allowUnknownExternal)
    {
        var key = function.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "#" + parameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!visited.Add(key))
            return false;
        var root = GetFunctionOperation(function);
        if (root is null)
            return true;
        var parameterAliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { parameter };
        var referenceAliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        DiscoverStorageAliases(function, root, parameterAliases, referenceAliases);
        foreach (var operation in EnumerateOperations(root))
        {
            if (operation is ISimpleAssignmentOperation assignment &&
                assignment.Target is IFieldReferenceOperation or IPropertyReferenceOperation &&
                IsDerivedStorageValue(assignment.Value, function, parameterAliases))
            {
                return true;
            }
            if (operation is IArgumentOperation argument &&
                IsDerivedStorageValue(argument.Value, function, parameterAliases) &&
                argument.Parent is IInvocationOperation invocation &&
                argument.Parameter is { } targetParameter)
            {
                var target = ResolveMethod(invocation.TargetMethod, function);
                if (GetCallPlan(target) is { Kind: not CudaCallKind.PlannedFunction })
                {
                    if (TryGetFunction(target, out var directFunction) &&
                        directFunction.IsExternal &&
                        !allowUnknownExternal)
                    {
                        return true;
                    }
                    continue;
                }
                if (!TryGetFunction(target, out var targetFunction))
                    return !allowUnknownExternal;
                if (targetFunction.IsExternal)
                    return !allowUnknownExternal;
                var ordinal = targetParameter.Ordinal +
                    (invocation.TargetMethod.ReducedFrom is not null ? 1 : 0);
                if ((uint)ordinal >= (uint)targetFunction.Symbol.Parameters.Length ||
                    ParameterEscapes(
                        targetFunction,
                        targetFunction.Symbol.Parameters[ordinal],
                        visited,
                        allowUnknownExternal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool IsDerivedStorageValue(
        IOperation operation,
        CudaFunctionPlan function,
        ISet<ISymbol> symbols)
    {
        while (operation is IConversionOperation conversion &&
            IsStorageHandleType(conversion.Type))
        {
            operation = conversion.Operand;
        }
        if (!IsStorageHandleType(operation.Type))
            return false;
        if (operation is ILocalReferenceOperation local)
            return symbols.Contains(local.Local);
        if (operation is IParameterReferenceOperation parameter)
            return symbols.Contains(parameter.Parameter);
        if (operation is IBinaryOperation binary && operation.Type is IPointerTypeSymbol)
        {
            return IsDerivedStorageValue(binary.LeftOperand, function, symbols) ||
                IsDerivedStorageValue(binary.RightOperand, function, symbols);
        }
        if (operation is IConditionalOperation conditional)
        {
            return IsDerivedStorageValue(conditional.WhenTrue, function, symbols) ||
                conditional.WhenFalse is not null &&
                IsDerivedStorageValue(conditional.WhenFalse, function, symbols);
        }
        if (operation is IInvocationOperation invocation)
        {
            var target = ResolveMethod(invocation.TargetMethod, function);
            if (TryGetFunction(target, out var targetFunction) &&
                targetFunction.Captures.Any(capture => symbols.Contains(capture.Symbol)))
            {
                return true;
            }
            if (invocation.Instance is not null &&
                IsDerivedStorageValue(invocation.Instance, function, symbols))
            {
                return true;
            }
            return invocation.Arguments.Any(argument =>
                IsDerivedStorageValue(argument.Value, function, symbols));
        }
        return false;
    }

    private bool IsStorageReferenceDerived(
        IOperation operation,
        CudaFunctionPlan function,
        ISet<ISymbol> aliases,
        ISet<ISymbol> referenceAliases)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        if (operation is ILocalReferenceOperation local)
            return referenceAliases.Contains(local.Local);
        if (operation is IArrayElementReferenceOperation element)
            return IsDerivedStorageValue(element.ArrayReference, function, aliases);
        if (operation is IImplicitIndexerReferenceOperation indexer)
            return IsDerivedStorageValue(indexer.Instance, function, aliases);
        if (operation is IPropertyReferenceOperation
            {
                Property.IsIndexer: true,
                Instance: { } instance
            })
        {
            return IsDerivedStorageValue(instance, function, aliases);
        }
        if (operation is IFieldReferenceOperation { Instance: { } fieldInstance })
        {
            return IsStorageReferenceDerived(
                fieldInstance,
                function,
                aliases,
                referenceAliases);
        }
        if (operation is IInvocationOperation invocation &&
            (invocation.TargetMethod.ReturnsByRef || invocation.TargetMethod.ReturnsByRefReadonly))
        {
            var target = ResolveMethod(invocation.TargetMethod, function);
            if (TryGetFunction(target, out var targetFunction) &&
                targetFunction.Captures.Any(capture => aliases.Contains(capture.Symbol)))
            {
                return true;
            }
            if (invocation.Instance is not null &&
                (IsStorageReferenceDerived(
                     invocation.Instance,
                     function,
                     aliases,
                     referenceAliases) ||
                 IsDerivedStorageValue(invocation.Instance, function, aliases)))
            {
                return true;
            }
            return invocation.Arguments.Any(argument =>
                IsDerivedStorageValue(argument.Value, function, aliases));
        }
        if (operation.Syntax.IsKind(SyntaxKind.PointerIndirectionExpression) ||
            operation.Syntax.IsKind(SyntaxKind.ElementAccessExpression))
        {
            return operation.ChildOperations.Any(child =>
                IsDerivedStorageValue(child, function, aliases));
        }
        return false;
    }

    private bool IsManagedArrayIdentity(
        IOperation operation,
        CudaFunctionPlan function,
        ISet<ISymbol> aliases)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation.Type is IArrayTypeSymbol &&
            IsDerivedStorageValue(operation, function, aliases);
    }

    private bool IsStorageHandleType(ITypeSymbol? type) =>
        type is IPointerTypeSymbol || IsArrayViewType(type);

    private void AnalyzeCaptures(IEnumerable<CudaFunctionPlan> functions)
    {
        var localFunctions = functions.Where(static function =>
            function.IsClosureFunction && !function.IsExternal).ToArray();
        var captures = localFunctions.ToDictionary(
            static function => function,
            static _ => new Dictionary<ISymbol, bool>(SymbolEqualityComparer.Default));

        foreach (var localFunction in localFunctions)
        {
            var root = GetFunctionOperation(localFunction);
            if (root is null)
                continue;
            foreach (var operation in EnumerateOperations(root))
            {
                ISymbol? symbol = operation switch
                {
                    ILocalReferenceOperation local => local.Local,
                    IParameterReferenceOperation parameter => parameter.Parameter,
                    _ => null
                };
                if (symbol is null ||
                    SymbolEqualityComparer.Default.Equals(
                        symbol.ContainingSymbol,
                        localFunction.Symbol))
                {
                    continue;
                }
                var byReference = RequiresReferenceCapture(operation);
                if (symbol is IParameterSymbol { RefKind: not RefKind.None } ||
                    symbol is ILocalSymbol { RefKind: not RefKind.None })
                {
                    byReference = true;
                }
                if (captures[localFunction].TryGetValue(symbol, out var existing))
                    captures[localFunction][symbol] = existing || byReference;
                else
                    captures[localFunction].Add(symbol, byReference);
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var caller in localFunctions)
            {
                foreach (var callSite in EnumerateCallSites(caller))
                {
                    var target = ResolveMethod(callSite.Target, caller);
                    if (!functionPlans.TryGetValue(target, out var callee) ||
                        !captures.TryGetValue(callee, out var calleeCaptures))
                    {
                        continue;
                    }
                    foreach (var capture in calleeCaptures)
                    {
                        if (SymbolEqualityComparer.Default.Equals(
                                capture.Key.ContainingSymbol,
                                caller.Symbol))
                        {
                            continue;
                        }
                        if (captures[caller].TryGetValue(capture.Key, out var existing))
                        {
                            if (!existing && capture.Value)
                            {
                                captures[caller][capture.Key] = true;
                                changed = true;
                            }
                        }
                        else
                        {
                            captures[caller].Add(capture.Key, capture.Value);
                            changed = true;
                        }
                    }
                }
            }
        }
        while (changed);

        foreach (var function in localFunctions)
        {
            var ordered = captures[function]
                .OrderBy(static capture =>
                    capture.Key.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
                .ThenBy(static capture => capture.Key.Name, StringComparer.Ordinal)
                .Select((capture, index) => new CudaCapturePlan(
                    capture.Key,
                    GetCaptureType(capture.Key),
                    $"csharp2cuda_capture_{NormalizeIdentifierPart(capture.Key.Name)}_{index}",
                    capture.Value,
                    capture.Key is IParameterSymbol { RefKind: RefKind.In } ||
                    capture.Key is ILocalSymbol { RefKind: RefKind.RefReadOnly }))
                .ToImmutableArray();
            function.Captures = ordered;
            foreach (var capture in ordered)
            {
                FormatType(
                    capture.Type,
                    false,
                    capture.Symbol.Locations.FirstOrDefault() ?? function.Syntax.GetLocation());
            }
        }
    }

    private static IOperation? GetFunctionOperation(CudaFunctionPlan function) =>
        function.Syntax.Body is not null
            ? function.Model.GetOperation(function.Syntax.Body)
            : function.Syntax.ExpressionBodyExpression is not null
                ? function.Model.GetOperation(function.Syntax.ExpressionBodyExpression)
                : null;

    private static ITypeSymbol GetCaptureType(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        _ => throw new InvalidOperationException($"Unsupported capture symbol '{symbol.Kind}'.")
    };

    private static bool RequiresReferenceCapture(IOperation reference)
    {
        for (var current = reference; current.Parent is { } parent; current = parent)
        {
            if (parent is ISimpleAssignmentOperation assignment &&
                ContainsOperation(assignment.Target, current) ||
                parent is ICompoundAssignmentOperation compound &&
                ContainsOperation(compound.Target, current) ||
                parent is IIncrementOrDecrementOperation increment &&
                ContainsOperation(increment.Target, current) ||
                parent is IArgumentOperation argument &&
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out ||
                parent is IAddressOfOperation ||
                parent is IInvocationOperation invocation &&
                ReferenceEquals(invocation.Instance, current) &&
                !invocation.TargetMethod.IsReadOnly)
            {
                return true;
            }
            if (parent is ILocalFunctionOperation)
                break;
        }
        return false;
    }

    private static bool ContainsOperation(IOperation root, IOperation candidate) =>
        ReferenceEquals(root, candidate) ||
        root.ChildOperations.Any(child => ContainsOperation(child, candidate));

    private void ValidateParameter(CudaFunctionPlan function, IParameterSymbol parameter)
    {
        var syntax = function.Syntax.ParameterList.Parameters[parameter.Ordinal];
        var readOnly = HasAttribute(parameter, ReadOnlyAttributeName);
        if (!HasOnlyAttributes(syntax.AttributeLists, ReadOnlyAttributeName) ||
            syntax.Type is null && !function.IsAnonymousFunction ||
            parameter.IsThis && !function.IsInferred ||
            syntax.Modifiers.Any(modifier =>
                modifier.Kind() is not SyntaxKind.InKeyword and
                    not SyntaxKind.RefKeyword and
                    not SyntaxKind.OutKeyword and
                    not SyntaxKind.ParamsKeyword and
                    not SyntaxKind.ScopedKeyword and
                    not SyntaxKind.ThisKeyword))
        {
            ReportUnsupportedSyntax(syntax);
        }

        var isAbiBoundary = function.Kind == CudaFunctionKind.Global || function.IsExternal;
        if (isAbiBoundary && ContainsDeviceInternalAbiType(
                parameter.Type,
                new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidPortableAbiType,
                syntax.GetLocation(),
                parameter.Type.ToDisplayString()));
        }
        else if (function.Kind == CudaFunctionKind.Global &&
            (parameter.RefKind != RefKind.None || IsArrayViewType(parameter.Type)))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidKernelParameter,
                syntax.GetLocation(),
                parameter.Name,
                parameter.Type.ToDisplayString()));
        }
        else if (function.IsExternal &&
            (parameter.RefKind != RefKind.None || IsArrayViewType(parameter.Type)))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidPortableAbiType,
                syntax.GetLocation(),
                parameter.Type.ToDisplayString()));
        }

        if (readOnly && parameter.Type is not IPointerTypeSymbol)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidReadOnlyParameter,
                syntax.GetLocation(),
                syntax.Identifier.ValueText));
        }

        if (parameter.RefKind != RefKind.None &&
            (IsArrayViewType(parameter.Type) || !parameter.Type.IsUnmanagedType))
        {
            ReportUnsupportedSyntax(syntax);
        }

        FormatType(parameter.Type, readOnly, syntax.Type?.GetLocation() ?? syntax.GetLocation());
    }

    private bool ContainsDeviceInternalAbiType(
        ITypeSymbol type,
        ISet<ITypeSymbol> visited)
    {
        while (type is IPointerTypeSymbol pointer)
            type = pointer.PointedAtType;
        if (type is IArrayTypeSymbol array)
            return ContainsDeviceInternalAbiType(array.ElementType, visited);
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
            return true;
        if (type is not INamedTypeSymbol named || !visited.Add(named))
            return false;
        if (TryGetStruct(named, out var structure) && structure.IsPositionalRecord)
            return true;
        if (!TryGetStruct(named, out structure))
            return false;
        return structure.Fields.Any(field => ContainsDeviceInternalAbiType(
                field.InlineArrayLength > 0 && field.Symbol.Type is IPointerTypeSymbol inline
                    ? inline.PointedAtType
                    : field.Symbol.Type,
                visited)) ||
            structure.Properties.Any(property => ContainsDeviceInternalAbiType(
                property.Symbol.Type,
                visited));
    }

    private void ValidateLocalDeclaration(
        LocalDeclarationStatementSyntax declaration,
        SemanticModel model)
    {
        if (declaration.Modifiers.Any(modifier =>
                modifier.Kind() is not SyntaxKind.ConstKeyword and
                    not SyntaxKind.ScopedKeyword) ||
            declaration.UsingKeyword != default ||
            declaration.AwaitKeyword != default)
        {
            ReportUnsupportedSyntax(declaration);
        }

        if (fixedLocalArrays.ContainsKey(declaration) ||
            recognizedStorageDeclarations.Contains(declaration))
            return;

        if (declaration.Declaration.Variables.Count == 1 &&
            declaration.Declaration.Variables[0].Initializer?.Value is
                InvocationExpressionSyntax invocation &&
            model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
            symbols.IsCudaType(method.ContainingType) &&
            method.Name == nameof(Cuda.DynamicSharedView) &&
            method.TypeArguments is [var elementType] &&
            !IsStorageElementType(elementType))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStorageType,
                invocation.GetLocation(),
                elementType.ToDisplayString(),
                nameof(Cuda.DynamicSharedView)));
            return;
        }

        var type = model.GetTypeInfo(declaration.Declaration.Type).Type;
        if (type is null)
            return;
        if (type.IsReferenceType && !IsArrayViewType(type))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ManagedAllocation,
                declaration.GetLocation(),
                declaration.Kind().ToString()));
            return;
        }
        FormatType(type, false, declaration.Declaration.Type.GetLocation());
    }

    private void ValidateGlobalCollisions()
    {
        var names = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var constant in ConstantArrays)
        {
            if (!names.TryAdd(constant.EmittedName, constant.Variable.Identifier.GetLocation()))
                ReportCollision(constant.EmittedName, constant.Variable.Identifier.GetLocation());
        }
        foreach (var structure in Structs)
        {
            if (!names.TryAdd(structure.EmittedName, structure.Syntax.Identifier.GetLocation()))
                ReportCollision(structure.EmittedName, structure.Syntax.Identifier.GetLocation());
        }

        var signatures = new Dictionary<string, Location>(StringComparer.Ordinal);
        var externCNames = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var function in Functions)
        {
            var parameterTypes = Enumerable.Range(0, function.Symbol.Parameters.Length)
                .Select(index => FormatParameterType(function, index));
            var signature = $"{function.EmittedName}({string.Join(",", parameterTypes)})";
            if (!signatures.TryAdd(signature, function.Syntax.Identifier.GetLocation()))
                ReportCollision(signature, function.Syntax.Identifier.GetLocation());

            if (function.Kind == CudaFunctionKind.Global && function.ExternC &&
                !externCNames.TryAdd(function.EmittedName, function.Syntax.Identifier.GetLocation()))
            {
                ReportCollision(function.EmittedName, function.Syntax.Identifier.GetLocation());
            }

            if (names.ContainsKey(function.EmittedName))
                ReportCollision(function.EmittedName, function.Syntax.Identifier.GetLocation());
        }
    }

    private void ComputePureFunctions()
    {
        foreach (var function in Functions.Where(static function =>
                     function.IsExternal && function.IsPureExternal))
        {
            pureFunctions.Add(function.Symbol);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var function in Functions.Where(static function => !function.IsExternal))
            {
                if (!pureFunctions.Contains(function.Symbol) && IsFunctionPure(function))
                    changed |= pureFunctions.Add(function.Symbol);
            }
        }
        while (changed);
    }

    private void AnalyzeViewParameters(IEnumerable<CudaFunctionPlan> functions)
    {
        foreach (var function in functions.Where(static item => !item.IsExternal))
        {
            var root = function.Syntax.Body is not null
                ? function.Model.GetOperation(function.Syntax.Body)
                : function.Syntax.ExpressionBodyExpression is not null
                    ? function.Model.GetOperation(function.Syntax.ExpressionBodyExpression)
                    : null;
            if (root is null)
                continue;

            foreach (var operation in EnumerateOperations(root))
            {
                IOperation? target = operation switch
                {
                    ISimpleAssignmentOperation assignment => assignment.Target,
                    ICompoundAssignmentOperation assignment => assignment.Target,
                    IIncrementOrDecrementOperation increment => increment.Target,
                    IArgumentOperation argument when argument.Parameter?.RefKind is
                        RefKind.Ref or RefKind.Out => argument.Value,
                    IReturnOperation returned when
                        (function.Symbol.ReturnsByRef ||
                         function.Symbol.ReturnsByRefReadonly) => returned.ReturnedValue,
                    _ => null
                };
                if (target is null)
                    continue;

                foreach (var parameter in function.Symbol.Parameters.Where(parameter =>
                             IsArrayViewType(parameter.Type) &&
                             IsRootedInParameter(target, parameter)))
                {
                    writableViewParameters.Add(parameter);
                }
            }
        }
    }

    private static bool IsRootedInParameter(IOperation operation, IParameterSymbol parameter)
    {
        if (operation is IParameterReferenceOperation reference)
            return SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
        return operation.ChildOperations.Any(child => IsRootedInParameter(child, parameter));
    }

    private bool IsFunctionPure(CudaFunctionPlan function)
    {
        if (function.Syntax.Body is null ||
            IsStorageHandleType(function.Symbol.ReturnType) ||
            function.Symbol.Parameters.Any(parameter =>
                parameter.RefKind != RefKind.None || IsStorageHandleType(parameter.Type)))
        {
            return false;
        }
        foreach (var node in function.Syntax.Body.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                if (function.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
                    !IsPureCall(method))
                {
                    return false;
                }
            }
            else if (node is AssignmentExpressionSyntax assignment &&
                !IsLocalMutationTarget(assignment.Left, function.Model))
            {
                return false;
            }
            else if (node is PrefixUnaryExpressionSyntax prefix &&
                (prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                 prefix.IsKind(SyntaxKind.PreDecrementExpression)))
            {
                var prefixNode = (PrefixUnaryExpressionSyntax)node;
                if (!IsLocalMutationTarget(prefixNode.Operand, function.Model))
                    return false;
            }
            else if (node is PostfixUnaryExpressionSyntax postfix &&
                !IsLocalMutationTarget(postfix.Operand, function.Model))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsLocalMutationTarget(ExpressionSyntax expression, SemanticModel model)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        if (expression is IdentifierNameSyntax)
        {
            return model.GetSymbolInfo(expression).Symbol is ILocalSymbol or
                IParameterSymbol { RefKind: RefKind.None };
        }
        if (expression is MemberAccessExpressionSyntax member &&
            member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            return IsLocalMutationTarget(member.Expression, model);
        }
        if (expression is ElementAccessExpressionSyntax element &&
            model.GetSymbolInfo(element.Expression).Symbol is ILocalSymbol local &&
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is
                VariableDeclaratorSyntax variable &&
            variable.Parent?.Parent is LocalDeclarationStatementSyntax declaration)
        {
            return fixedLocalArrays.ContainsKey(declaration);
        }
        return false;
    }

    private IEnumerable<CudaStructPlan> OrderStructs(IReadOnlyCollection<CudaStructPlan> structures)
    {
        var ordered = new List<CudaStructPlan>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var structure in structures.OrderBy(
                     static item => item.EmittedName,
                     StringComparer.Ordinal))
            Visit(structure);
        return ordered;

        void Visit(CudaStructPlan structure)
        {
            if (!visited.Add(structure.Symbol))
                return;
            visiting.Add(structure.Symbol);
            foreach (var field in structure.Fields.OrderBy(
                         static item => item.Symbol.Name,
                         StringComparer.Ordinal))
            {
                var fieldType = field.InlineArrayLength > 0
                    ? ((IPointerTypeSymbol)field.Symbol.Type).PointedAtType
                    : field.Symbol.Type;
                if (field.Symbol.IsStatic ||
                    fieldType is IPointerTypeSymbol ||
                    fieldType is not INamedTypeSymbol named ||
                    !structPlans.TryGetValue(named, out var dependency) ||
                    dependency.IsExternal)
                {
                    continue;
                }

                if (visiting.Contains(dependency.Symbol))
                {
                    ReportUnsupportedSyntax(structure.Syntax);
                    continue;
                }
                Visit(dependency);
            }
            visiting.Remove(structure.Symbol);
            ordered.Add(structure);
        }
    }

    private bool TryCreateFixedLocalArrayPlan(
        LocalDeclarationStatementSyntax declaration,
        SemanticModel model,
        out CudaFixedLocalArrayPlan plan)
    {
        plan = null!;
        if (declaration.Declaration.Variables.Count != 1 ||
            declaration.Declaration.Type is IdentifierNameSyntax { Identifier.ValueText: "var" })
        {
            return false;
        }

        var variable = declaration.Declaration.Variables[0];
        if (variable.Initializer?.Value is not { } initializer ||
            model.GetDeclaredSymbol(variable) is not ILocalSymbol local ||
            !TryGetFixedArrayElementType(local.Type, out var elementType, out var readOnly))
        {
            return false;
        }

        ExpressionSyntax? size = null;
        IEnumerable<ExpressionSyntax>? expressions = null;
        var zeroInitialize = false;
        var allowsEmpty = false;
        switch (initializer)
        {
            case StackAllocArrayCreationExpressionSyntax
            {
                Type: ArrayTypeSyntax
                {
                    ElementType: var stackElement,
                    RankSpecifiers: [{ Sizes: [var stackSize] }]
                },
                Initializer: var stackInitializer
            }:
                if (!SymbolEqualityComparer.Default.Equals(
                        model.GetTypeInfo(stackElement).Type,
                        elementType) ||
                    stackSize is OmittedArraySizeExpressionSyntax)
                {
                    return false;
                }
                size = stackSize;
                expressions = stackInitializer?.Expressions;
                if (readOnly && stackInitializer is null)
                    return false;
                break;
            case ArrayCreationExpressionSyntax
            {
                Type:
                {
                    ElementType: var arrayElement,
                    RankSpecifiers: [{ Sizes: [var arraySize] }]
                },
                Initializer: var arrayInitializer
            }:
                if (!SymbolEqualityComparer.Default.Equals(
                        model.GetTypeInfo(arrayElement).Type,
                        elementType))
                {
                    return false;
                }
                size = arraySize is OmittedArraySizeExpressionSyntax ? null : arraySize;
                expressions = arrayInitializer?.Expressions;
                zeroInitialize = arrayInitializer is null;
                allowsEmpty = arrayInitializer is not null;
                break;
            case ImplicitArrayCreationExpressionSyntax { Initializer: var implicitInitializer }:
                expressions = implicitInitializer.Expressions;
                allowsEmpty = true;
                break;
            case CollectionExpressionSyntax collection:
                if (collection.Elements.Any(static item => item is not ExpressionElementSyntax))
                    return false;
                expressions = collection.Elements
                    .Cast<ExpressionElementSyntax>()
                    .Select(static item => item.Expression);
                allowsEmpty = true;
                break;
            default:
                return false;
        }

        var initializerExpressions = expressions?.ToImmutableArray() ?? [];
        int length;
        if (size is not null)
        {
            if (model.GetConstantValue(size) is not { HasValue: true, Value: int value })
                return false;
            length = value;
        }
        else
        {
            length = initializerExpressions.Length;
        }
        if (length < 0 ||
            length == 0 && (!allowsEmpty || local.Type is IPointerTypeSymbol) ||
            (!initializerExpressions.IsEmpty && initializerExpressions.Length != length))
        {
            return false;
        }

        plan = new CudaFixedLocalArrayPlan(
            local,
            elementType,
            length,
            readOnly,
            local.Type is IPointerTypeSymbol,
            zeroInitialize,
            initializerExpressions);
        if (length > MaximumFixedLocalElementCount && !plan.IsPointer)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.FixedLocalStorageLimit,
                initializer.GetLocation(),
                local.Name,
                length,
                MaximumFixedLocalElementCount));
        }
        return true;
    }

    private bool TryGetFixedArrayElementType(
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

        if (localType is IArrayTypeSymbol { Rank: 1 } array)
        {
            elementType = array.ElementType;
            readOnly = false;
            return true;
        }

        if (localType is INamedTypeSymbol named &&
            named.TypeArguments is [var argument] &&
            symbols.IsSpanType(named, out readOnly))
        {
            elementType = argument;
            return true;
        }

        elementType = null!;
        readOnly = false;
        return false;
    }

    private static bool TryGetConstantValues(
        ExpressionSyntax? initializer,
        SemanticModel model,
        out ImmutableArray<int> values)
    {
        IEnumerable<ExpressionSyntax>? expressions;
        if (initializer is CollectionExpressionSyntax collection)
        {
            if (collection.Elements.Any(static element => element is not ExpressionElementSyntax))
            {
                values = [];
                return false;
            }
            expressions = collection.Elements
                .Cast<ExpressionElementSyntax>()
                .Select(static element => element.Expression);
        }
        else
        {
            expressions = initializer switch
            {
                ArrayCreationExpressionSyntax { Initializer: { } array } => array.Expressions,
                ImplicitArrayCreationExpressionSyntax { Initializer: { } array } =>
                    array.Expressions,
                _ => null
            };
        }
        if (expressions is null)
        {
            values = [];
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<int>();
        foreach (var expression in expressions)
        {
            var constant = model.GetConstantValue(expression);
            if (constant is not { HasValue: true, Value: int value })
            {
                values = [];
                return false;
            }
            builder.Add(value);
        }
        values = builder.ToImmutable();
        return values.Length > 0;
    }

    private string RegisterIdentifier(ISymbol symbol, string name, Location location)
    {
        if (!CudaIdentifier.IsValid(name))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidIdentifier,
                location,
                name));
            name = "csharp2cuda_invalid_identifier";
        }
        identifierNames[symbol] = name;
        return name;
    }

    private string ReportUnsupportedType(ITypeSymbol type, Location location)
    {
        var key = $"{location.SourceTree?.FilePath}|{location.SourceSpan}|{type.ToDisplayString()}";
        if (reportedTypes.Add(key))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.UnsupportedType,
                location,
                type.ToDisplayString()));
        }
        return "csharp2cuda_unsupported_type";
    }

    private void ReportUnsupportedSyntax(SyntaxNode syntax) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedSyntax,
            syntax.GetLocation(),
            syntax.Kind().ToString()));

    private void ReportInvalidStorage(SyntaxNode syntax, string name) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.InvalidStorage,
            syntax.GetLocation(),
            name));

    private void ReportCollision(string name, Location location) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.DeclarationCollision,
            location,
            name));

    private bool HasOnlyAttributes(
        SyntaxList<AttributeListSyntax> attributeLists,
        params string[] allowed)
    {
        var allowedNames = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var attribute in attributeLists.SelectMany(static list => list.Attributes))
        {
            var model = GetSemanticModel(attribute);
            var symbol = model.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
            if (symbol is null ||
                !allowedNames.Any(name => symbols.IsAttributeType(symbol.ContainingType, name)))
            {
                return false;
            }
        }
        return true;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
                return argument.Value.Value as string;
        }
        return null;
    }

    private static bool GetNamedBoolean(AttributeData attribute, string name, bool defaultValue)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
                return value;
        }
        return defaultValue;
    }

    private static Location GetNameLocation(IMethodSymbol method, AttributeData attribute)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax)
        {
            var argument = syntax.ArgumentList?.Arguments.FirstOrDefault(item =>
                item.NameEquals?.Name.Identifier.ValueText == nameof(CudaDeviceAttribute.Name));
            if (argument is not null)
                return argument.Expression.GetLocation();
        }
        return method.Locations.FirstOrDefault() ?? Location.None;
    }

    private bool HasCudaFunctionContract(
        SemanticModel model,
        MethodDeclarationSyntax syntax) =>
        model.GetDeclaredSymbol(syntax) is IMethodSymbol symbol &&
        (GetAttribute(symbol, ExternalAttributeName) is not null ||
         GetAttribute(symbol, ExternalDeviceAttributeName) is not null ||
         GetAttribute(symbol, DeviceAttributeName) is not null ||
         GetAttribute(symbol, GlobalAttributeName) is not null);

    private bool HasCudaConstantContract(
        SemanticModel model,
        FieldDeclarationSyntax syntax) =>
        syntax.Declaration.Variables
            .Select(variable => model.GetDeclaredSymbol(variable))
            .OfType<IFieldSymbol>()
            .Any(symbol => GetAttribute(symbol, ConstantAttributeName) is not null);

    private void DiscoverReachableFunctions(List<CudaFunctionPlan> functions)
    {
        var roots = functions.Where(static function =>
                !function.IsExternal &&
                (function.HasDeviceAttribute || function.HasGlobalAttribute))
            .ToArray();
        var queue = new Queue<CudaFunctionPlan>(roots);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var parents = new Dictionary<IMethodSymbol, CudaCallParent?>(
            SymbolEqualityComparer.Default);
        var edges = new Dictionary<IMethodSymbol, List<CudaCallEdge>>(
            SymbolEqualityComparer.Default);
        foreach (var root in roots)
            parents.TryAdd(root.Symbol, null);

        while (queue.Count > 0)
        {
            var function = queue.Dequeue();
            if (!visited.Add(function.Symbol) || function.IsExternal)
                continue;

            foreach (var invocation in EnumerateCallSites(function))
            {
                var target = ResolveMethod(invocation.Target, function);
                if (target.MethodKind == MethodKind.Constructor &&
                    (symbols.IsSpanType(target.ContainingType, out _) ||
                     target.ContainingType.IsReferenceType))
                {
                    continue;
                }
                if (GetCallPlan(target) is { Kind: not CudaCallKind.PlannedFunction })
                    continue;

                if (!functionPlans.TryGetValue(target, out var targetFunction))
                {
                    if (!TryGetSourceFunction(target, out var syntax, out var model))
                    {
                        if (target.MethodKind is MethodKind.UserDefinedOperator or
                            MethodKind.Conversion)
                        {
                            diagnostics.Add(Diagnostic.Create(
                                CudaDiagnostics.UnsupportedOperator,
                                invocation.Syntax.GetLocation(),
                                target.ToDisplayString(
                                    SymbolDisplayFormat.CSharpErrorMessageFormat)));
                            continue;
                        }
                        if (symbols.IsSpanType(target.ContainingType, out _) ||
                            symbols.IsMemoryExtensionsType(target.ContainingType))
                        {
                            diagnostics.Add(Diagnostic.Create(
                                CudaDiagnostics.UnsupportedRangeOperation,
                                invocation.Syntax.GetLocation(),
                                invocation.Syntax.ToString()));
                            continue;
                        }
                        var callPath = BuildCallPath(function.Symbol, target, parents);
                        diagnostics.Add(Diagnostic.Create(
                            CudaDiagnostics.MissingReachableBody,
                            invocation.Syntax.GetLocation(),
                            CreateReachabilityProperties(
                                function.Symbol,
                                target,
                                parents,
                                callPath,
                                "Move the unsupported call outside the CUDA kernel."),
                            target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                            callPath));
                        continue;
                    }

                    var containingType = syntax.Node.Ancestors().OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault();
                    if (containingType is not (ClassDeclarationSyntax or
                        StructDeclarationSyntax or RecordDeclarationSyntax))
                    {
                        var callPath = BuildCallPath(function.Symbol, target, parents);
                        diagnostics.Add(Diagnostic.Create(
                            CudaDiagnostics.UnsupportedReachableMethod,
                            syntax.Identifier.GetLocation(),
                            CreateReachabilityProperties(
                                function.Symbol,
                                target,
                                parents,
                                callPath,
                                "Move the method into a source class or structure."),
                            target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                            callPath,
                            "The method is not declared in a source class or structure."));
                        continue;
                    }

                    var hasContract = syntax.Node is MethodDeclarationSyntax methodSyntax &&
                        HasCudaFunctionContract(model, methodSyntax);
                    RegisterFunction(
                        model,
                        syntax,
                        functions,
                        isInferred: !hasContract,
                        constructedSymbol: target);
                    if (!functionPlans.TryGetValue(target, out targetFunction))
                        continue;
                }

                if (targetFunction.IsExternal)
                    continue;

                if (!edges.TryGetValue(function.Symbol, out var functionEdges))
                {
                    functionEdges = [];
                    edges.Add(function.Symbol, functionEdges);
                }
                functionEdges.Add(new CudaCallEdge(targetFunction.Symbol, invocation.Syntax.GetLocation()));
                if (parents.TryAdd(
                        targetFunction.Symbol,
                        new CudaCallParent(function.Symbol, invocation.Syntax.GetLocation())))
                {
                    queue.Enqueue(targetFunction);
                }
            }
        }

        ValidateNoRecursion(roots, edges);
    }

    private IEnumerable<CudaCallSite> EnumerateCallSites(
        CudaFunctionPlan function)
    {
        IOperation? operation = function.Syntax.Body is not null
            ? function.Model.GetOperation(function.Syntax.Body)
            : function.Syntax.ExpressionBodyExpression is not null
                ? function.Model.GetOperation(function.Syntax.ExpressionBodyExpression)
                : null;
        if (operation is null)
            return [];

        return EnumerateOperations(operation)
            .SelectMany(GetOperationCallSites)
            .OrderBy(static invocation => invocation.Syntax.SpanStart)
            .ToArray();
    }

    private IEnumerable<CudaCallSite> GetOperationCallSites(IOperation operation)
    {
        switch (operation)
        {
            case IDelegateCreationOperation creation
                when !IsDirectlyInvokedDelegate(creation):
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.ClosureEscape,
                    creation.Syntax.GetLocation(),
                    creation.Syntax.ToString()));
                break;
            case IInvocationOperation invocation
                when TryGetDirectAnonymousFunction(invocation.Instance, out var anonymous):
                yield return new CudaCallSite(anonymous.Symbol, invocation.Syntax);
                break;
            case IInvocationOperation invocation
                when invocation.TargetMethod.ContainingType.TypeKind == TypeKind.Delegate:
                break;
            case IInvocationOperation invocation
                when IsGeneratedPositionalRecordMember(invocation.TargetMethod):
                break;
            case IInvocationOperation invocation:
                yield return new CudaCallSite(invocation.TargetMethod, invocation.Syntax);
                break;
            case IObjectCreationOperation { Constructor: { } constructor }
                when IsGeneratedPositionalRecordMember(constructor):
                break;
            case IObjectCreationOperation { Constructor: { } constructor } creation:
                yield return new CudaCallSite(constructor, creation.Syntax);
                break;
            case IPropertyReferenceOperation property:
                if (property.Property.GetMethod is { } getter && HasSourceBody(getter))
                    yield return new CudaCallSite(getter, property.Syntax);
                if (property.Property.SetMethod is { } setter && HasSourceBody(setter))
                    yield return new CudaCallSite(setter, property.Syntax);
                break;
            case IBinaryOperation { OperatorMethod: { } method }
                when symbols.IsCudaInt32Type(method.ContainingType):
                break;
            case IBinaryOperation { OperatorMethod: { } method }
                when IsGeneratedPositionalRecordMember(method):
                break;
            case IBinaryOperation { OperatorMethod: { } method } binary:
                yield return new CudaCallSite(method, binary.Syntax);
                break;
            case IUnaryOperation { OperatorMethod: { } method }
                when symbols.IsCudaInt32Type(method.ContainingType):
                break;
            case IUnaryOperation { OperatorMethod: { } method } unary:
                yield return new CudaCallSite(method, unary.Syntax);
                break;
            case IConversionOperation { OperatorMethod: { } method } conversion
                when !symbols.IsIndexType(method.ContainingType) &&
                    !symbols.IsCudaInt32Type(method.ContainingType):
                yield return new CudaCallSite(method, conversion.Syntax);
                break;
            case ICompoundAssignmentOperation compound:
                if (compound.OperatorMethod is { } compoundOperator &&
                    !symbols.IsCudaInt32Type(compoundOperator.ContainingType))
                {
                    yield return new CudaCallSite(compoundOperator, compound.Syntax);
                }
                if (compound.InConversion.MethodSymbol is { } inConversion &&
                    !symbols.IsCudaInt32Type(inConversion.ContainingType))
                {
                    yield return new CudaCallSite(inConversion, compound.Syntax);
                }
                if (compound.OutConversion.MethodSymbol is { } outConversion &&
                    !symbols.IsCudaInt32Type(outConversion.ContainingType))
                {
                    yield return new CudaCallSite(outConversion, compound.Syntax);
                }
                break;
            case IIncrementOrDecrementOperation { OperatorMethod: { } method }
                when symbols.IsCudaInt32Type(method.ContainingType):
                break;
            case IIncrementOrDecrementOperation { OperatorMethod: { } method } increment:
                yield return new CudaCallSite(method, increment.Syntax);
                break;
        }
    }

    internal static bool IsGeneratedPositionalRecordMember(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is RecordDeclarationSyntax
            {
                ParameterList: not null,
                ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword
            } record &&
            record.Modifiers.Any(SyntaxKind.ReadOnlyKeyword));

    private static bool TryGetDirectAnonymousFunction(
        IOperation? operation,
        out IAnonymousFunctionOperation anonymous)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        if (operation is IDelegateCreationOperation { Target: IAnonymousFunctionOperation value })
        {
            anonymous = value;
            return true;
        }
        anonymous = null!;
        return false;
    }

    private static bool IsDirectlyInvokedDelegate(IDelegateCreationOperation creation)
    {
        IOperation current = creation;
        while (current.Parent is IConversionOperation conversion &&
            ReferenceEquals(conversion.Operand, current))
        {
            current = conversion;
        }
        return current.Parent is IInvocationOperation invocation &&
            ReferenceEquals(invocation.Instance, current);
    }

    private static bool HasSourceBody(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() switch
        {
            AccessorDeclarationSyntax accessor =>
                accessor.Body is not null || accessor.ExpressionBody is not null,
            PropertyDeclarationSyntax property => property.ExpressionBody is not null,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody is not null,
            _ => false
        });

    private static IEnumerable<IOperation> EnumerateOperations(IOperation operation)
    {
        yield return operation;
        if (operation is ILocalFunctionOperation or IAnonymousFunctionOperation)
            yield break;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in EnumerateOperations(child))
                yield return descendant;
        }
    }

    private bool TryGetSourceFunction(
        IMethodSymbol method,
        out CudaFunctionSource syntax,
        out SemanticModel model)
    {
        method = NormalizeMethod(method);
        foreach (var syntaxReference in method.OriginalDefinition.DeclaringSyntaxReferences
                     .OrderBy(static reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
                     .ThenBy(static reference => reference.Span.Start))
        {
            var node = syntaxReference.GetSyntax();
            var candidate = node switch
            {
                MethodDeclarationSyntax declaration => CudaFunctionSource.Create(declaration),
                LocalFunctionStatementSyntax declaration => CudaFunctionSource.Create(declaration),
                ConstructorDeclarationSyntax declaration => CudaFunctionSource.Create(declaration),
                OperatorDeclarationSyntax declaration => CudaFunctionSource.Create(declaration),
                ConversionOperatorDeclarationSyntax declaration =>
                    CudaFunctionSource.Create(declaration),
                AnonymousFunctionExpressionSyntax declaration => CudaFunctionSource.Create(
                    declaration,
                    method.OriginalDefinition),
                AccessorDeclarationSyntax declaration => CudaFunctionSource.Create(
                    declaration,
                    method.OriginalDefinition),
                PropertyDeclarationSyntax declaration when declaration.ExpressionBody is not null =>
                    CudaFunctionSource.Create(declaration, method.OriginalDefinition),
                IndexerDeclarationSyntax declaration when declaration.ExpressionBody is not null =>
                    CudaFunctionSource.Create(declaration, method.OriginalDefinition),
                _ => null
            };
            if (candidate is null ||
                candidate.Body is null && candidate.ExpressionBodyExpression is null)
                continue;
            syntax = candidate;
            model = GetSemanticModel(candidate.Node);
            return true;
        }

        syntax = null!;
        model = null!;
        return false;
    }

    private void ValidateNoRecursion(
        IReadOnlyList<CudaFunctionPlan> roots,
        IReadOnlyDictionary<IMethodSymbol, List<CudaCallEdge>> edges)
    {
        var states = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var stack = new List<IMethodSymbol>();
        foreach (var root in roots)
        {
            if (Visit(root.Symbol))
                return;
        }

        bool Visit(IMethodSymbol method)
        {
            states[method] = 1;
            stack.Add(method);
            if (edges.TryGetValue(method, out var calls))
            {
                foreach (var call in calls)
                {
                    if (!states.TryGetValue(call.Target, out var state))
                    {
                        if (Visit(call.Target))
                            return true;
                        continue;
                    }
                    if (state != 1)
                        continue;

                    var cycleStart = stack.FindIndex(item =>
                        SymbolEqualityComparer.Default.Equals(item, call.Target));
                    var cycle = stack.Skip(Math.Max(0, cycleStart))
                        .Append(call.Target)
                        .Select(FormatCallPathName)
                        .ToArray();
                    var callPath = string.Join(" -> ", cycle);
                    diagnostics.Add(Diagnostic.Create(
                        CudaDiagnostics.RecursiveCall,
                        call.Location,
                        ImmutableDictionary<string, string?>.Empty
                            .Add("RootSymbol", FormatCallPathName(stack[0]))
                            .Add("FailingSymbol", FormatCallPathName(call.Target))
                            .Add("CallPath", callPath)
                            .Add(
                                "SuggestedReplacement",
                                "Replace recursion with a bounded iterative algorithm."),
                        callPath));
                    return true;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            states[method] = 2;
            return false;
        }
    }

    private string BuildCallPath(
        IMethodSymbol caller,
        IMethodSymbol target,
        IReadOnlyDictionary<IMethodSymbol, CudaCallParent?> parents)
    {
        var path = new List<IMethodSymbol> { caller };
        var current = caller;
        while (parents.TryGetValue(current, out var parent) && parent is not null)
        {
            current = parent.Caller;
            path.Add(current);
        }
        path.Reverse();
        path.Add(target);
        return string.Join(" -> ", path.Select(FormatCallPathName));
    }

    private ImmutableDictionary<string, string?> CreateReachabilityProperties(
        IMethodSymbol caller,
        IMethodSymbol target,
        IReadOnlyDictionary<IMethodSymbol, CudaCallParent?> parents,
        string callPath,
        string suggestedReplacement)
    {
        var root = caller;
        while (parents.TryGetValue(root, out var parent) && parent is not null)
            root = parent.Caller;
        return ImmutableDictionary<string, string?>.Empty
            .Add("RootSymbol", FormatCallPathName(root))
            .Add("FailingSymbol", FormatCallPathName(target))
            .Add("CallPath", callPath)
            .Add("SuggestedReplacement", suggestedReplacement);
    }

    private string FormatCallPathName(IMethodSymbol method)
    {
        method = NormalizeMethod(method);
        if (functionPlans.TryGetValue(method, out var function) &&
            !function.IsInferred)
        {
            return function.EmittedName;
        }
        return method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        method = method.ReducedFrom ?? method;
        return method.PartialImplementationPart ?? method;
    }

    private static string CreateInferredFunctionName(IMethodSymbol method)
    {
        var identity = method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var readable = NormalizeIdentifierPart(
            $"{method.ContainingNamespace.ToDisplayString()}_{method.ContainingType.Name}_{method.Name}");
        var hash = 14695981039346656037UL;
        foreach (var value in Encoding.UTF8.GetBytes(identity))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return $"cs2cuda_{readable}_{hash:x16}";
    }

    private static string CreateInferredTypeName(INamedTypeSymbol type)
    {
        var identity = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var readable = NormalizeIdentifierPart(
            $"{type.ContainingNamespace.ToDisplayString()}_{type.Name}");
        var hash = 14695981039346656037UL;
        foreach (var value in Encoding.UTF8.GetBytes(identity))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return $"cs2cuda_{readable}_{hash:x16}";
    }

    private static string NormalizeIdentifierPart(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousUnderscore = false;
        foreach (var character in value)
        {
            var accepted = character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9';
            if (accepted)
            {
                builder.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }
        return builder.ToString().Trim('_');
    }
}

internal sealed record CudaUnitPlan(
    ClassDeclarationSyntax Syntax,
    SemanticModel Model);

internal sealed class CudaStructPlan(
    TypeDeclarationSyntax syntax,
    INamedTypeSymbol symbol,
    INamedTypeSymbol definitionSymbol,
    SemanticModel model,
    string emittedName,
    bool isExternal)
{
    public TypeDeclarationSyntax Syntax { get; } = syntax;
    public INamedTypeSymbol Symbol { get; } = symbol;
    public INamedTypeSymbol DefinitionSymbol { get; } = definitionSymbol;
    public SemanticModel Model { get; } = model;
    public string EmittedName { get; } = emittedName;
    public bool IsExternal { get; } = isExternal;
    public bool IsPositionalRecord => Syntax is RecordDeclarationSyntax { ParameterList: not null };
    public List<CudaFieldPlan> Fields { get; } = [];
    public List<CudaPropertyPlan> Properties { get; } = [];
    public CudaStructLayout? Layout { get; set; }
}

internal sealed record CudaFieldPlan(
    FieldDeclarationSyntax Declaration,
    VariableDeclaratorSyntax Variable,
    IFieldSymbol Symbol,
    int InlineArrayLength);

internal sealed record CudaPropertyPlan(
    SyntaxNode Declaration,
    TypeSyntax TypeSyntax,
    IPropertySymbol Symbol);

internal sealed record CudaConstantArrayPlan(
    FieldDeclarationSyntax Declaration,
    VariableDeclaratorSyntax Variable,
    IFieldSymbol Symbol,
    string EmittedName,
    ImmutableArray<int> Values);

internal sealed record CudaStoragePlan(
    LocalDeclarationStatementSyntax Declaration,
    ILocalSymbol Symbol,
    CudaStorageKind Kind,
    ITypeSymbol ElementType,
    int Length,
    int Alignment);

internal sealed record CudaFixedLocalArrayPlan(
    ILocalSymbol Symbol,
    ITypeSymbol ElementType,
    int Length,
    bool IsReadOnly,
    bool IsPointer,
    bool ZeroInitialize,
    ImmutableArray<ExpressionSyntax> Initializers);

internal enum CudaStorageKind
{
    SharedScalar,
    SharedArray,
    DynamicSharedBytes
}

internal sealed record CudaFunctionPlan(
    CudaFunctionSource Syntax,
    IMethodSymbol Symbol,
    IMethodSymbol DefinitionSymbol,
    SemanticModel Model,
    string EmittedName,
    CudaFunctionKind Kind,
    bool ExternC,
    bool IsExternal,
    bool IsPureExternal,
    bool EmitsDeclaration,
    bool HasDeviceAttribute,
    bool HasGlobalAttribute,
    bool HasExternalDeviceAttribute,
    bool IsInferred)
{
    public CudaFunctionBodyIr? Body { get; set; }
    public ImmutableArray<CudaCapturePlan> Captures { get; set; } = [];
    public bool HasInstance => !Syntax.IsConstructor &&
        !Syntax.IsLocalFunction &&
        !Syntax.IsAnonymousFunction &&
        !Symbol.IsStatic;
    public bool IsConstructor => Syntax.IsConstructor;
    public bool IsLocalFunction => Syntax.IsLocalFunction;
    public bool IsAnonymousFunction => Syntax.IsAnonymousFunction;
    public bool IsClosureFunction => IsLocalFunction || IsAnonymousFunction;
    public bool IsReadOnlyInstance => Symbol.IsReadOnly;

    public bool TryGetCapture(ISymbol symbol, out CudaCapturePlan capture)
    {
        foreach (var candidate in Captures)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.Symbol, symbol))
            {
                capture = candidate;
                return true;
            }
        }
        capture = null!;
        return false;
    }
}

internal sealed record CudaCapturePlan(
    ISymbol Symbol,
    ITypeSymbol Type,
    string EmittedName,
    bool ByReference,
    bool IsReadOnly);

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

internal sealed record CudaCallParent(IMethodSymbol Caller, Location Location);

internal sealed record CudaCallEdge(IMethodSymbol Target, Location Location);

internal sealed record CudaCallSite(IMethodSymbol Target, SyntaxNode Syntax);

internal enum CudaFunctionKind
{
    Device,
    Global,
    External
}

internal sealed record CudaCallPlan(CudaCallKind Kind, string Name);

internal enum CudaCallKind
{
    PlannedFunction,
    Direct,
    Atomic,
    SignedInt64Atomic,
    InvalidAtomic,
    Storage,
    DynamicSharedView,
    NaN,
    BooleanToInteger,
    IntegerToBoolean,
    SignedToUnsigned,
    Unwrap,
    ArrayView,
    ReadOnlyArrayView,
    SliceView,
    AsSpanView,
    ClearView,
    FillView,
    CopyView,
    TryCopyView
}

internal static class CudaIdentifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
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

    private static readonly HashSet<string> RuntimeIdentifiers = new(StringComparer.Ordinal)
    {
        "CSHARP2CUDA_GLOBAL_TIMER_0_1",
        "CSHARP2CUDA_INTEGER_SEMANTICS_0_1",
        "CSHARP2CUDA_VOLATILE_MAPPED_MEMORY_0_1",
        "asin",
        "atomicAdd",
        "atomicCAS",
        "atomicExch",
        "atomicMin",
        "atomicXor",
        "blockDim",
        "blockIdx",
        "ceil",
        "copysign",
        "fabs",
        "exp",
        "floor",
        "fmax",
        "fmin",
        "fmod",
        "gridDim",
        "ilogb",
        "isfinite",
        "isinf",
        "isnan",
        "ldexp",
        "log1p",
        "nan",
        "nearbyint",
        "pow",
        "signbit",
        "sqrt",
        "threadIdx",
        "trunc"
    };

    public static bool IsValid(string name)
    {
        if (name.Length == 0 ||
            !IsAsciiLetter(name[0]) ||
            Keywords.Contains(name) ||
            RuntimeIdentifiers.Contains(name) ||
            name.Contains("__", StringComparison.Ordinal) ||
            name.StartsWith("csharp2cuda_", StringComparison.Ordinal))
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
