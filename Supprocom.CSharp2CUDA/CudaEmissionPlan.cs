using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Supprocom.CSharp2CUDA;

internal sealed class CudaEmissionPlan
{
    internal const string ExternalAttributeName = "Supprocom.CSharp2CUDA.CudaExternalAttribute";
    internal const string ConstantAttributeName = "Supprocom.CSharp2CUDA.CudaConstantAttribute";
    internal const string DeviceAttributeName = "Supprocom.CSharp2CUDA.CudaDeviceAttribute";
    internal const string GlobalAttributeName = "Supprocom.CSharp2CUDA.CudaGlobalAttribute";
    internal const string ReadOnlyAttributeName = "Supprocom.CSharp2CUDA.CudaReadOnlyAttribute";
    internal const string TranslationUnitAttributeName =
        "Supprocom.CSharp2CUDA.CudaTranslationUnitAttribute";

    private static readonly IReadOnlyDictionary<string, string> RuntimeMethodMappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Math.Abs(double)"] = "fabs",
            ["System.Math.Asin(double)"] = "asin",
            ["System.Math.Ceiling(double)"] = "ceil",
            ["System.Math.CopySign(double, double)"] = "copysign",
            ["System.Math.Floor(double)"] = "floor",
            ["System.Math.ILogB(double)"] = "ilogb",
            ["System.Math.Max(double, double)"] = "csharp2cuda_f64_maximum",
            ["System.Math.Min(double, double)"] = "csharp2cuda_f64_minimum",
            ["System.Math.ScaleB(double, int)"] = "ldexp",
            ["System.Math.Truncate(double)"] = "trunc",
            ["double.IsFinite(double)"] = "isfinite",
            ["double.IsInfinity(double)"] = "isinf",
            ["double.IsNaN(double)"] = "isnan",
            ["System.BitConverter.DoubleToInt64Bits(double)"] = "__double_as_longlong",
            ["System.BitConverter.Int64BitsToDouble(long)"] = "__longlong_as_double"
        };

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
        SyntaxKind.UnsafeKeyword
    ];

    private static readonly HashSet<SyntaxKind> MethodModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.StaticKeyword,
        SyntaxKind.UnsafeKeyword
    ];

    private static readonly HashSet<SyntaxKind> FieldModifiers =
    [
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.PublicKeyword,
        SyntaxKind.ProtectedKeyword
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
    private readonly IMethodSymbol? cudaLogMethod;
    private readonly Dictionary<ISymbol, string> identifierNames =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, CudaFunctionPlan> functionPlans =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, CudaStructPlan> structPlans =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<LocalDeclarationStatementSyntax> fixedLocalArrays = [];
    private readonly HashSet<LocalDeclarationStatementSyntax> recognizedStorageDeclarations = [];
    private readonly Dictionary<LocalDeclarationStatementSyntax, CudaStoragePlan>
        storageDeclarations = [];
    private readonly Dictionary<ILocalSymbol, CudaStoragePlan> dynamicSharedStorage =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, CudaConstantArrayPlan> constantArrayPlans =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> pureFunctions =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<BinaryExpressionSyntax, string> binaryHelpers = [];
    private readonly Dictionary<BinaryExpressionSyntax, CudaBinaryConversionPlan>
        binaryConversions = [];
    private readonly Dictionary<AssignmentExpressionSyntax, string> assignmentHelpers = [];
    private readonly Dictionary<PrefixUnaryExpressionSyntax, string> prefixHelpers = [];
    private readonly Dictionary<PostfixUnaryExpressionSyntax, string> postfixHelpers = [];
    private readonly Dictionary<CastExpressionSyntax, string> castHelpers = [];
    private readonly HashSet<string> reportedTypes = new(StringComparer.Ordinal);

    private CudaEmissionPlan(
        CSharpCompilation compilation,
        IReadOnlyList<ClassDeclarationSyntax> units,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        this.compilation = compilation;
        this.diagnostics = diagnostics;
        cudaLogMethod = ResolveCudaLogMethod(compilation);
        Units = units.Select(unit => new CudaUnitPlan(
            unit,
            compilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: true)))
            .ToImmutableArray();
    }

    public ImmutableArray<CudaUnitPlan> Units { get; }

    public ImmutableArray<CudaStructPlan> Structs { get; private set; } = [];

    public ImmutableArray<CudaFunctionPlan> Functions { get; private set; } = [];

    public ImmutableArray<CudaConstantArrayPlan> ConstantArrays { get; private set; } = [];

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
        functionPlans.TryGetValue(method, out function!);

    public bool TryGetStruct(INamedTypeSymbol type, out CudaStructPlan structure) =>
        structPlans.TryGetValue(type, out structure!);

    public bool IsFixedLocalArray(LocalDeclarationStatementSyntax declaration) =>
        fixedLocalArrays.Contains(declaration);

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

    public void PlanBinaryHelper(BinaryExpressionSyntax expression, string helper) =>
        binaryHelpers[expression] = helper;

    public bool TryGetBinaryHelper(BinaryExpressionSyntax expression, out string helper) =>
        binaryHelpers.TryGetValue(expression, out helper!);

    public void PlanBinaryConversions(
        BinaryExpressionSyntax expression,
        string? leftType,
        string? rightType) =>
        binaryConversions[expression] = new CudaBinaryConversionPlan(leftType, rightType);

    public bool TryGetBinaryConversions(
        BinaryExpressionSyntax expression,
        out CudaBinaryConversionPlan conversions) =>
        binaryConversions.TryGetValue(expression, out conversions!);

    public void PlanAssignmentHelper(AssignmentExpressionSyntax expression, string helper) =>
        assignmentHelpers[expression] = helper;

    public bool TryGetAssignmentHelper(
        AssignmentExpressionSyntax expression,
        out string helper) => assignmentHelpers.TryGetValue(expression, out helper!);

    public void PlanPrefixHelper(PrefixUnaryExpressionSyntax expression, string helper) =>
        prefixHelpers[expression] = helper;

    public bool TryGetPrefixHelper(
        PrefixUnaryExpressionSyntax expression,
        out string helper) => prefixHelpers.TryGetValue(expression, out helper!);

    public void PlanPostfixHelper(PostfixUnaryExpressionSyntax expression, string helper) =>
        postfixHelpers[expression] = helper;

    public bool TryGetPostfixHelper(
        PostfixUnaryExpressionSyntax expression,
        out string helper) => postfixHelpers.TryGetValue(expression, out helper!);

    public void PlanCastHelper(CastExpressionSyntax expression, string helper) =>
        castHelpers[expression] = helper;

    public bool TryGetCastHelper(CastExpressionSyntax expression, out string helper) =>
        castHelpers.TryGetValue(expression, out helper!);

    public CudaCallPlan? GetCallPlan(IMethodSymbol method)
    {
        if (functionPlans.TryGetValue(method, out var function))
            return new CudaCallPlan(CudaCallKind.PlannedFunction, function.EmittedName);

        if (cudaLogMethod is not null && SymbolEqualityComparer.Default.Equals(
                method.OriginalDefinition,
                cudaLogMethod))
        {
            return new CudaCallPlan(CudaCallKind.Direct, "log");
        }

        if (method.ContainingType.ToDisplayString() == "Supprocom.CSharp2CUDA.Cuda")
        {
            return method.Name switch
            {
                nameof(Cuda.SyncThreads) => new(CudaCallKind.Direct, "__syncthreads"),
                nameof(Cuda.ThreadFence) => new(CudaCallKind.Direct, "__threadfence"),
                nameof(Cuda.ThreadFenceSystem) =>
                    new(CudaCallKind.Direct, "__threadfence_system"),
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

        var displayName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return RuntimeMethodMappings.TryGetValue(displayName, out var mappedName)
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

    private static bool IsImpureIntrinsic(string name) => name is
        "__syncthreads" or "__threadfence" or "__threadfence_system" or
        "__syncwarp" or "__shfl_down_sync" or "__nanosleep";

    private static IMethodSymbol? ResolveCudaLogMethod(CSharpCompilation compilation)
    {
        var cudaType = compilation.GetTypeByMetadataName("Supprocom.CSharp2CUDA.Cuda");
        return cudaType?.GetMembers(nameof(Cuda.Log))
            .OfType<IMethodSymbol>()
            .SingleOrDefault(method =>
                method.IsStatic &&
                method.Arity == 0 &&
                method.ReturnType.SpecialType == SpecialType.System_Double &&
                method.Parameters is
                [
                    {
                        RefKind: RefKind.None,
                        Type.SpecialType: SpecialType.System_Double
                    }
                ]);
    }

    public bool TryGetDimensionReplacement(
        MemberAccessExpressionSyntax expression,
        out string replacement)
    {
        replacement = string.Empty;
        if (expression.Expression is not MemberAccessExpressionSyntax dimension)
            return false;

        var model = GetSemanticModel(expression);
        if (model.GetSymbolInfo(expression).Symbol is not IPropertySymbol component ||
            component.ContainingType.ToDisplayString() != "Supprocom.CSharp2CUDA.CudaDimension" ||
            component.Name is not nameof(CudaDimension.X) and
                not nameof(CudaDimension.Y) and
                not nameof(CudaDimension.Z) ||
            model.GetSymbolInfo(dimension).Symbol is not IPropertySymbol source ||
            source.ContainingType.ToDisplayString() != "Supprocom.CSharp2CUDA.Cuda")
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
        property.ContainingType.ToDisplayString() == "Supprocom.CSharp2CUDA.Cuda" &&
        property.Name is nameof(Cuda.ThreadIdx) or nameof(Cuda.BlockIdx) or
            nameof(Cuda.BlockDim) or nameof(Cuda.GridDim) ||
        property.ContainingType.ToDisplayString() == "Supprocom.CSharp2CUDA.CudaDimension" &&
        property.Name is nameof(CudaDimension.X) or nameof(CudaDimension.Y) or
            nameof(CudaDimension.Z);

    public string FormatType(ITypeSymbol type, bool deepReadOnly, Location location)
    {
        if (type.ToDisplayString() == "Supprocom.CSharp2CUDA.CudaInt32")
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
                _ => ReportUnsupportedType(type, location)
            };
        }

        if (type is INamedTypeSymbol named && structPlans.TryGetValue(named, out var structure))
            return structure.EmittedName;

        return ReportUnsupportedType(type, location);
    }

    public string FormatParameterType(CudaFunctionPlan function, int index)
    {
        var parameter = function.Symbol.Parameters[index];
        var syntax = function.Syntax.ParameterList.Parameters[index];
        var readOnly = HasAttribute(parameter, ReadOnlyAttributeName);
        var prefix = parameter.RefKind == RefKind.In ? "const " : string.Empty;
        var suffix = parameter.RefKind == RefKind.In ? "&" : string.Empty;
        return prefix + FormatType(parameter.Type, readOnly, syntax.Type!.GetLocation()) + suffix;
    }

    public static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

    public static AttributeData? GetAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

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
                        RegisterStruct(unit, structure, structures);
                        break;
                    case MethodDeclarationSyntax method:
                        RegisterFunction(unit, method, functions);
                        break;
                    case FieldDeclarationSyntax field:
                        RegisterConstantArray(unit, field, constants);
                        break;
                    default:
                        diagnostics.Add(Diagnostic.Create(
                            CudaDiagnostics.UnsupportedMember,
                            member.GetLocation(),
                            member.Kind().ToString()));
                        break;
                }
            }
        }

        Structs = OrderStructs(structures).ToImmutableArray();
        Functions = functions.ToImmutableArray();
        ConstantArrays = constants.ToImmutableArray();

        foreach (var structure in structures)
            ValidateStruct(structure);
        foreach (var function in functions)
            ValidateFunction(function);

        ValidateGlobalCollisions();
        ComputePureFunctions();

        foreach (var function in Functions.Where(static function => !function.IsExternal))
        {
            var validator = new CudaSyntaxValidator(this, function.Model, diagnostics);
            validator.Visit(function.Syntax.Body);
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
        CudaUnitPlan unit,
        StructDeclarationSyntax syntax,
        ICollection<CudaStructPlan> structures)
    {
        if (unit.Model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
            return;

        var externalAttribute = GetAttribute(symbol, ExternalAttributeName);
        if (externalAttribute is not null && GetNamedBoolean(
                externalAttribute,
                nameof(CudaExternalAttribute.IsPure),
                false))
        {
            ReportUnsupportedSyntax(syntax);
        }
        var name = RegisterIdentifier(symbol, syntax.Identifier.ValueText, syntax.Identifier.GetLocation());
        var structure = new CudaStructPlan(
            syntax,
            symbol,
            unit.Model,
            name,
            externalAttribute is not null);
        structures.Add(structure);
        structPlans[symbol] = structure;
    }

    private void RegisterFunction(
        CudaUnitPlan unit,
        MethodDeclarationSyntax syntax,
        ICollection<CudaFunctionPlan> functions)
    {
        if (unit.Model.GetDeclaredSymbol(syntax) is not IMethodSymbol symbol)
            return;

        var externalAttribute = GetAttribute(symbol, ExternalAttributeName);
        var external = externalAttribute is not null;
        var device = GetAttribute(symbol, DeviceAttributeName);
        var global = GetAttribute(symbol, GlobalAttributeName);
        var kind = device is not null
            ? CudaFunctionKind.Device
            : global is not null
                ? CudaFunctionKind.Global
                : CudaFunctionKind.External;
        var namingAttribute = device ?? global;
        var name = namingAttribute is null
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
            unit.Model,
            name,
            kind,
            externC,
            external,
            externalAttribute is not null && GetNamedBoolean(
                externalAttribute,
                nameof(CudaExternalAttribute.IsPure),
                false),
            device is not null,
            global is not null);
        functions.Add(function);
        functionPlans[symbol] = function;

        foreach (var parameter in symbol.Parameters)
        {
            var parameterSyntax = syntax.ParameterList.Parameters[parameter.Ordinal];
            RegisterIdentifier(
                parameter,
                parameterSyntax.Identifier.ValueText,
                parameterSyntax.Identifier.GetLocation());
        }

        if (!external && syntax.Body is not null)
        {
            foreach (var variable in syntax.Body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (unit.Model.GetDeclaredSymbol(variable) is ILocalSymbol local)
                {
                    RegisterIdentifier(
                        local,
                        variable.Identifier.ValueText,
                        variable.Identifier.GetLocation());
                }
            }

            foreach (var declaration in syntax.Body.DescendantNodes()
                         .OfType<LocalDeclarationStatementSyntax>())
            {
                if (IsValidFixedLocalArray(declaration, unit.Model))
                    fixedLocalArrays.Add(declaration);
                else
                    RegisterStorageDeclaration(declaration, function);
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
            method.ContainingType.ToDisplayString() != "Supprocom.CSharp2CUDA.Cuda" ||
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
                function.Syntax))
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
        if (syntax.Modifiers.Any(modifier => !StructModifiers.Contains(modifier.Kind())) ||
            syntax.TypeParameterList is not null ||
            syntax.BaseList is not null ||
            syntax.ConstraintClauses.Count != 0 ||
            syntax.ParameterList is not null ||
            !HasOnlyAttributes(syntax.AttributeLists, ExternalAttributeName))
        {
            ReportUnsupportedSyntax(syntax);
        }

        foreach (var member in syntax.Members)
        {
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
                field.AttributeLists.Count != 0)
            {
                ReportUnsupportedSyntax(field);
            }

            var variable = field.Declaration.Variables[0];
            if (structure.Model.GetDeclaredSymbol(variable) is not IFieldSymbol symbol)
                continue;
            RegisterIdentifier(symbol, variable.Identifier.ValueText, variable.Identifier.GetLocation());
            structure.Fields.Add(new CudaFieldPlan(field, variable, symbol));
            FormatType(symbol.Type, false, field.Declaration.Type.GetLocation());

            if (symbol.IsStatic || symbol.IsConst || symbol.IsReadOnly || symbol.IsVolatile ||
                variable.Initializer is not null)
            {
                ReportUnsupportedSyntax(field);
            }
        }
    }

    private void ValidateFunction(CudaFunctionPlan function)
    {
        var syntax = function.Syntax;
        if (!syntax.Modifiers.Any(SyntaxKind.StaticKeyword) ||
            syntax.Modifiers.Any(modifier => !MethodModifiers.Contains(modifier.Kind())) ||
            syntax.TypeParameterList is not null ||
            syntax.ConstraintClauses.Count != 0 ||
            syntax.ExplicitInterfaceSpecifier is not null ||
            !HasOnlyAttributes(
                syntax.AttributeLists,
                ExternalAttributeName,
                DeviceAttributeName,
                GlobalAttributeName))
        {
            ReportUnsupportedSyntax(syntax);
        }

        if (function.HasDeviceAttribute && function.HasGlobalAttribute)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ConflictingFunctionKinds,
                syntax.Identifier.GetLocation(),
                function.Symbol.Name));
        }
        else if (!function.IsExternal &&
            !function.HasDeviceAttribute &&
            !function.HasGlobalAttribute)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.MissingFunctionKind,
                syntax.Identifier.GetLocation(),
                function.Symbol.Name));
        }
        else if (function.IsExternal &&
            (function.HasDeviceAttribute || function.HasGlobalAttribute))
        {
            ReportUnsupportedSyntax(syntax);
        }

        if (!function.IsExternal &&
            (syntax.Body is null || syntax.ExpressionBody is not null))
        {
            ReportUnsupportedSyntax(syntax);
        }

        FormatType(function.Symbol.ReturnType, false, syntax.ReturnType.GetLocation());
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
            }
        }
    }

    private void ValidateParameter(CudaFunctionPlan function, IParameterSymbol parameter)
    {
        var syntax = function.Syntax.ParameterList.Parameters[parameter.Ordinal];
        var readOnly = HasAttribute(parameter, ReadOnlyAttributeName);
        if (!HasOnlyAttributes(syntax.AttributeLists, ReadOnlyAttributeName) ||
            syntax.Type is null ||
            syntax.Default is not null ||
            parameter.IsOptional ||
            parameter.IsParams ||
            parameter.IsThis ||
            syntax.Modifiers.Any(modifier =>
                modifier.Kind() is not SyntaxKind.InKeyword))
        {
            ReportUnsupportedSyntax(syntax);
        }

        if (readOnly && parameter.Type is not IPointerTypeSymbol)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidReadOnlyParameter,
                syntax.GetLocation(),
                syntax.Identifier.ValueText));
        }

        if ((parameter.RefKind == RefKind.In && parameter.Type is IPointerTypeSymbol) ||
            parameter.RefKind is not RefKind.None and not RefKind.In)
        {
            ReportUnsupportedSyntax(syntax);
        }

        FormatType(parameter.Type, readOnly, syntax.Type?.GetLocation() ?? syntax.GetLocation());
    }

    private void ValidateLocalDeclaration(
        LocalDeclarationStatementSyntax declaration,
        SemanticModel model)
    {
        if (declaration.Modifiers.Any(modifier =>
                !modifier.IsKind(SyntaxKind.ConstKeyword)) ||
            declaration.UsingKeyword != default ||
            declaration.AwaitKeyword != default)
        {
            ReportUnsupportedSyntax(declaration);
        }

        if (fixedLocalArrays.Contains(declaration) ||
            recognizedStorageDeclarations.Contains(declaration))
            return;

        var type = model.GetTypeInfo(declaration.Declaration.Type).Type;
        if (type is not null)
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

    private bool IsFunctionPure(CudaFunctionPlan function)
    {
        if (function.Syntax.Body is null)
            return false;
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
            return fixedLocalArrays.Contains(declaration);
        }
        return false;
    }

    private IEnumerable<CudaStructPlan> OrderStructs(IReadOnlyCollection<CudaStructPlan> structures)
    {
        var ordered = new List<CudaStructPlan>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var structure in structures)
            Visit(structure);
        return ordered;

        void Visit(CudaStructPlan structure)
        {
            if (!visited.Add(structure.Symbol))
                return;
            visiting.Add(structure.Symbol);
            foreach (var field in structure.Symbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic || field.Type is IPointerTypeSymbol ||
                    field.Type is not INamedTypeSymbol named ||
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

    private bool IsValidFixedLocalArray(
        LocalDeclarationStatementSyntax declaration,
        SemanticModel model)
    {
        if (declaration.Declaration.Variables.Count != 1 ||
            declaration.Declaration.Type is IdentifierNameSyntax { Identifier.ValueText: "var" })
        {
            return false;
        }

        var variable = declaration.Declaration.Variables[0];
        if (variable.Initializer?.Value is not StackAllocArrayCreationExpressionSyntax stack ||
            stack.Type is not ArrayTypeSyntax arrayType ||
            arrayType.RankSpecifiers is not [var rank] ||
            rank.Sizes is not [var size] ||
            size is OmittedArraySizeExpressionSyntax ||
            model.GetDeclaredSymbol(variable) is not ILocalSymbol local ||
            model.GetTypeInfo(arrayType.ElementType).Type is not { } elementType ||
            !TryGetFixedArrayElementType(local.Type, out var localElementType, out var readOnly) ||
            !SymbolEqualityComparer.Default.Equals(localElementType, elementType))
        {
            return false;
        }

        var constant = model.GetConstantValue(size);
        return constant is { HasValue: true, Value: int length } &&
            length > 0 &&
            (!readOnly || stack.Initializer is not null) &&
            (stack.Initializer is null || stack.Initializer.Expressions.Count == length);
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

        if (localType is INamedTypeSymbol named && named.TypeArguments is [var argument])
        {
            var definition = named.OriginalDefinition.ToDisplayString();
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
            if (symbol?.ContainingType.ToDisplayString() is not { } name ||
                !allowedNames.Contains(name))
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
}

internal sealed record CudaUnitPlan(
    ClassDeclarationSyntax Syntax,
    SemanticModel Model);

internal sealed class CudaStructPlan(
    StructDeclarationSyntax syntax,
    INamedTypeSymbol symbol,
    SemanticModel model,
    string emittedName,
    bool isExternal)
{
    public StructDeclarationSyntax Syntax { get; } = syntax;
    public INamedTypeSymbol Symbol { get; } = symbol;
    public SemanticModel Model { get; } = model;
    public string EmittedName { get; } = emittedName;
    public bool IsExternal { get; } = isExternal;
    public List<CudaFieldPlan> Fields { get; } = [];
}

internal sealed record CudaFieldPlan(
    FieldDeclarationSyntax Declaration,
    VariableDeclaratorSyntax Variable,
    IFieldSymbol Symbol);

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

internal enum CudaStorageKind
{
    SharedScalar,
    SharedArray,
    DynamicSharedBytes
}

internal sealed record CudaFunctionPlan(
    MethodDeclarationSyntax Syntax,
    IMethodSymbol Symbol,
    SemanticModel Model,
    string EmittedName,
    CudaFunctionKind Kind,
    bool ExternC,
    bool IsExternal,
    bool IsPureExternal,
    bool HasDeviceAttribute,
    bool HasGlobalAttribute);

internal enum CudaFunctionKind
{
    Device,
    Global,
    External
}

internal sealed record CudaCallPlan(CudaCallKind Kind, string Name);

internal sealed record CudaBinaryConversionPlan(string? LeftType, string? RightType);

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
    Unwrap
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
        "CSHARP2CUDA_INTEGER_SEMANTICS_0_1",
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
