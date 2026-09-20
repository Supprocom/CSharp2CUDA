using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Supprocom.CSharp2CUDA.Semantics;

internal sealed class CudaOperationLowerer(
    CudaEmissionPlan plan,
    CudaFunctionPlan function,
    ImmutableArray<Diagnostic>.Builder diagnostics)
{
    private readonly SemanticModel semanticModel = function.Model;
    private readonly Dictionary<ISymbol, CudaViewMutability> viewMutabilities =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<SwitchStatementSyntax, string> patternSwitchBreakLabels = [];
    private int temporaryIndex;
    private int labelIndex;

    public CudaFunctionBodyIr? Lower()
    {
        if (function.IsConstructor && function.Syntax.Body is not null)
        {
            var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
            statements.Add(new CudaVariableDeclarationStatementIr(
                FormatType(
                    function.Symbol.ContainingType,
                    false,
                    function.Syntax.Identifier.GetLocation()),
                "csharp2cuda_constructed",
                new CudaValueIr(
                    "{}",
                    function.Symbol.ContainingType,
                    CudaEffectIr.None,
                    true,
                    function.Syntax.Identifier.GetLocation()),
                false,
                function.Syntax.Identifier.GetLocation()));
            statements.AddRange(LowerBlock(function.Syntax.Body).Statements);
            statements.Add(new CudaReturnStatementIr(
                ReferenceExpression(
                    "csharp2cuda_constructed",
                    function.Symbol.ContainingType,
                    function.Syntax.Identifier.GetLocation()),
                function.Syntax.Identifier.GetLocation()));
            return new CudaFunctionBodyIr(new CudaBlockStatementIr(
                statements.ToImmutable(),
                function.Syntax.GetLocation()));
        }

        if (function.Syntax.Body is not null)
            return new CudaFunctionBodyIr(LowerBlock(function.Syntax.Body));

        if (function.Syntax.ExpressionBodyExpression is { } expression)
        {
            var contextualConversion = FindContextualConversionOperation(
                semanticModel.GetOperation(function.Syntax.Node),
                expression);
            CudaStatementIr statement = function.Symbol.ReturnsVoid
                ? new CudaExpressionStatementIr(
                    LowerExpression(expression),
                    expression.GetLocation())
                : new CudaReturnStatementIr(
                    function.Symbol.ReturnsByRef || function.Symbol.ReturnsByRefReadonly
                        ? LowerReferenceExpression(expression)
                        : contextualConversion is null
                            ? LowerExpression(expression)
                            : LowerOperation(contextualConversion),
                    expression.GetLocation());
            return new CudaFunctionBodyIr(new CudaBlockStatementIr(
                [statement],
                function.Syntax.GetLocation()));
        }

        ReportUnsupported(function.Syntax.Node);
        return null;
    }

    private CudaBlockStatementIr LowerBlock(BlockSyntax syntax)
    {
        var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        foreach (var statement in syntax.Statements)
            statements.Add(LowerStatement(statement));
        return new CudaBlockStatementIr(statements.ToImmutable(), syntax.GetLocation());
    }

    private CudaStatementIr LowerStatement(StatementSyntax syntax) => syntax switch
    {
        BlockSyntax block => LowerBlock(block),
        LocalDeclarationStatementSyntax declaration => LowerLocalDeclaration(declaration),
        ExpressionStatementSyntax expression => new CudaExpressionStatementIr(
            LowerExpression(expression.Expression),
            expression.GetLocation()),
        IfStatementSyntax conditional => new CudaIfStatementIr(
            LowerExpression(conditional.Condition),
            LowerStatement(conditional.Statement),
            conditional.Else is null ? null : LowerStatement(conditional.Else.Statement),
            conditional.GetLocation()),
        WhileStatementSyntax loop => new CudaWhileStatementIr(
            LowerExpression(loop.Condition),
            LowerStatement(loop.Statement),
            loop.GetLocation()),
        DoStatementSyntax loop => new CudaDoWhileStatementIr(
            LowerStatement(loop.Statement),
            LowerExpression(loop.Condition),
            $"csharp2cuda_do_continue_{labelIndex++}",
            loop.GetLocation()),
        ForStatementSyntax loop => LowerFor(loop),
        ForEachStatementSyntax loop => LowerForEach(loop),
        SwitchStatementSyntax selection => LowerSwitch(selection),
        ReturnStatementSyntax returned => LowerReturn(returned),
        BreakStatementSyntax statement => LowerBreak(statement),
        ContinueStatementSyntax statement => new CudaContinueStatementIr(statement.GetLocation()),
        LocalFunctionStatementSyntax statement => new CudaEmptyStatementIr(
            statement.GetLocation()),
        EmptyStatementSyntax statement => new CudaEmptyStatementIr(statement.GetLocation()),
        _ => UnsupportedStatement(syntax)
    };

    private CudaReturnStatementIr LowerReturn(ReturnStatementSyntax syntax)
    {
        if (syntax.Expression is null)
            return new CudaReturnStatementIr(null, syntax.GetLocation());
        var returned = semanticModel.GetOperation(syntax) is IReturnOperation operation
            ? operation.ReturnedValue
            : null;
        var expression = function.Symbol.ReturnsByRef || function.Symbol.ReturnsByRefReadonly
            ? returned is null
                ? LowerReferenceExpression(syntax.Expression)
                : LowerReferenceOperation(returned)
            : returned is null
                ? LowerExpression(syntax.Expression)
                : LowerOperation(returned);
        return new CudaReturnStatementIr(expression, syntax.GetLocation());
    }

    private CudaStatementIr LowerLocalDeclaration(LocalDeclarationStatementSyntax syntax)
    {
        if (plan.TryGetStorageDeclaration(syntax, out var storage))
        {
            return new CudaStorageDeclarationStatementIr(
                FormatType(storage.ElementType, false, syntax.Declaration.Type.GetLocation()),
                plan.GetIdentifier(storage.Symbol),
                storage.Kind,
                storage.Length,
                storage.Alignment,
                syntax.GetLocation());
        }

        if (plan.TryGetFixedLocalArray(syntax, out var fixedArray))
            return LowerFixedArray(syntax, fixedArray);

        return LowerVariableDeclaration(
            syntax.Declaration,
            syntax.Modifiers,
            syntax.GetLocation());
    }

    private CudaStatementIr LowerVariableDeclaration(
        VariableDeclarationSyntax declaration,
        SyntaxTokenList modifiers,
        Location location)
    {
        var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        foreach (var variable in declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol local)
                continue;
            if (local.RefKind != RefKind.None)
            {
                if (variable.Initializer is null)
                {
                    ReportUnsupported(variable);
                    continue;
                }
                var reference = LowerReferenceExpression(variable.Initializer.Value);
                statements.AddRange(reference.Prefix);
                var typeName = FormatType(
                    local.Type,
                    false,
                    declaration.Type.GetLocation());
                if (local.RefKind == RefKind.RefReadOnly)
                    typeName = "const " + typeName;
                statements.Add(new CudaVariableDeclarationStatementIr(
                    typeName + "*",
                    plan.GetIdentifier(local),
                    reference.Value,
                    false,
                    variable.GetLocation()));
                continue;
            }
            CudaExpressionIr? initializer = null;
            if (variable.Initializer is not null)
            {
                var contextualConversion = FindContextualConversionOperation(
                    semanticModel.GetOperation(variable),
                    variable.Initializer.Value);
                initializer = contextualConversion is null
                    ? LowerExpression(variable.Initializer.Value)
                    : LowerOperation(contextualConversion);
            }
            if (initializer is not null)
                statements.AddRange(initializer.Prefix);
            var viewMutability = initializer?.Value.ViewMutability ?? CudaViewMutability.None;
            if (viewMutability != CudaViewMutability.None)
                viewMutabilities[local] = viewMutability;

            var deepReadOnly = variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                GetCallPlan(method)?.Kind == CudaCallKind.Unwrap;
            statements.Add(new CudaVariableDeclarationStatementIr(
                FormatType(
                    local.Type,
                    deepReadOnly || viewMutability == CudaViewMutability.ReadOnly,
                    declaration.Type.GetLocation()),
                plan.GetIdentifier(local),
                initializer?.Value,
                modifiers.Any(SyntaxKind.ConstKeyword),
                variable.GetLocation()));
        }
        return new CudaStatementGroupIr(statements.ToImmutable(), location);
    }

    private CudaStatementIr LowerFixedArray(
        LocalDeclarationStatementSyntax syntax,
        CudaFixedLocalArrayPlan fixedArray)
    {
        var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        if (fixedArray.Length == 0)
        {
            var elementName = FormatType(
                fixedArray.ElementType,
                false,
                syntax.Declaration.Type.GetLocation());
            var viewName = fixedArray.IsReadOnly
                ? "csharp2cuda_readonly_array_view"
                : "csharp2cuda_array_view";
            var mutability = fixedArray.IsReadOnly
                ? CudaViewMutability.ReadOnly
                : CudaViewMutability.Writable;
            viewMutabilities[fixedArray.Symbol] = mutability;
            statements.Add(new CudaVariableDeclarationStatementIr(
                $"{viewName}<{elementName}>",
                plan.GetIdentifier(fixedArray.Symbol),
                new CudaValueIr(
                    $"{viewName}<{elementName}>(nullptr, 0, false)",
                    fixedArray.Symbol.Type,
                    CudaEffectIr.None,
                    false,
                    syntax.GetLocation())
                {
                    ViewMutability = mutability
                },
                false,
                syntax.GetLocation()));
            return new CudaStatementGroupIr(statements.ToImmutable(), syntax.GetLocation());
        }
        var initializers = ImmutableArray.CreateBuilder<CudaValueIr>();
        var declarationOperation = semanticModel.GetOperation(
            syntax.Declaration.Variables[0]);
        foreach (var expression in fixedArray.Initializers)
        {
            var contextualConversion = FindContextualConversionOperation(
                declarationOperation,
                expression);
            var lowered = contextualConversion is null
                ? LowerExpression(expression)
                : LowerOperation(contextualConversion);
            statements.AddRange(lowered.Prefix);
            initializers.Add(lowered.Value);
        }
        var bindingKind = fixedArray.IsPointer
            ? CudaFixedArrayBindingKind.Pointer
            : fixedArray.IsReadOnly
                ? CudaFixedArrayBindingKind.ReadOnlyView
                : CudaFixedArrayBindingKind.WritableView;
        viewMutabilities[fixedArray.Symbol] = fixedArray.IsReadOnly
            ? CudaViewMutability.ReadOnly
            : CudaViewMutability.Writable;
        statements.Add(new CudaFixedArrayDeclarationStatementIr(
            FormatType(fixedArray.ElementType, false, syntax.Declaration.Type.GetLocation()),
            NewTemporaryName() + "_storage",
            fixedArray.Length,
            fixedArray.IsReadOnly,
            initializers.ToImmutable(),
            syntax.GetLocation())
        {
            BindingKind = bindingKind,
            BindingName = plan.GetIdentifier(fixedArray.Symbol),
            ZeroInitialize = fixedArray.ZeroInitialize
        });
        return new CudaStatementGroupIr(statements.ToImmutable(), syntax.GetLocation());
    }

    private CudaForStatementIr LowerFor(ForStatementSyntax syntax)
    {
        var initializers = ImmutableArray.CreateBuilder<CudaStatementIr>();
        if (syntax.Declaration is not null)
            initializers.Add(LowerVariableDeclaration(
                syntax.Declaration,
                default,
                syntax.Declaration.GetLocation()));
        foreach (var initializer in syntax.Initializers)
        {
            initializers.Add(new CudaExpressionStatementIr(
                LowerExpression(initializer),
                initializer.GetLocation()));
        }

        var incrementors = syntax.Incrementors
            .Select(LowerExpression)
            .ToImmutableArray();
        return new CudaForStatementIr(
            initializers.ToImmutable(),
            syntax.Condition is null ? null : LowerExpression(syntax.Condition),
            incrementors,
            LowerStatement(syntax.Statement),
            $"csharp2cuda_for_continue_{labelIndex++}",
            syntax.GetLocation());
    }

    private CudaForStatementIr LowerForEach(ForEachStatementSyntax syntax)
    {
        if (semanticModel.GetDeclaredSymbol(syntax) is not ILocalSymbol iteration ||
            !plan.IsArrayViewType(semanticModel.GetTypeInfo(syntax.Expression).Type))
        {
            ReportUnsupported(syntax);
            return new CudaForStatementIr(
                [],
                null,
                [],
                new CudaEmptyStatementIr(syntax.GetLocation()),
                $"csharp2cuda_for_continue_{labelIndex++}",
                syntax.GetLocation());
        }

        var collection = LowerExpression(syntax.Expression);
        var initializers = ImmutableArray.CreateBuilder<CudaStatementIr>();
        initializers.AddRange(collection.Prefix);
        var collectionName = NewTemporaryName();
        initializers.Add(new CudaVariableDeclarationStatementIr(
            FormatType(
                collection.Value.Type!,
                collection.Value.ViewMutability == CudaViewMutability.ReadOnly,
                syntax.Expression.GetLocation()),
            collectionName,
            collection.Value,
            false,
            syntax.Expression.GetLocation()));
        var indexName = NewTemporaryName();
        var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        var boolType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
        initializers.Add(new CudaVariableDeclarationStatementIr(
            "int",
            indexName,
            new CudaValueIr("0", intType, CudaEffectIr.None, true, syntax.GetLocation()),
            false,
            syntax.GetLocation()));

        var isReference = iteration.RefKind != RefKind.None || syntax.Type is RefTypeSyntax;
        var isReadOnlyReference = iteration.RefKind == RefKind.RefReadOnly ||
            syntax.Type is RefTypeSyntax { ReadOnlyKeyword.RawKind: not 0 };
        if (isReference && !isReadOnlyReference &&
            collection.Value.ViewMutability == CudaViewMutability.ReadOnly)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ViewEscape,
                syntax.Type.GetLocation(),
                iteration.Name));
        }
        var iterationTypeName = FormatType(
            iteration.Type,
            false,
            syntax.Type.GetLocation());
        if (isReference && isReadOnlyReference)
            iterationTypeName = "const " + iterationTypeName;
        if (isReference)
            iterationTypeName += "*";
        var iterationValueType = isReference
            ? semanticModel.Compilation.CreatePointerTypeSymbol(iteration.Type)
            : iteration.Type;
        var iterationCode = isReference
            ? $"&(({collectionName})[{indexName}])"
            : $"({collectionName})[{indexName}]";
        var iterationDeclaration = new CudaVariableDeclarationStatementIr(
            iterationTypeName,
            plan.GetIdentifier(iteration),
            new CudaValueIr(
                iterationCode,
                iterationValueType,
                CudaEffectIr.Read,
                false,
                syntax.GetLocation()),
            false,
            syntax.GetLocation());
        var loweredBody = LowerStatement(syntax.Statement);
        var body = new CudaStatementGroupIr(
            [iterationDeclaration, loweredBody],
            syntax.Statement.GetLocation());
        var condition = ValueExpression(new CudaValueIr(
            $"({indexName} < {collectionName}.get_length())",
            boolType,
            CudaEffectIr.None,
            false,
            syntax.Expression.GetLocation()));
        var increment = ValueExpression(new CudaValueIr(
            $"++{indexName}",
            intType,
            CudaEffectIr.Write,
            false,
            syntax.GetLocation()));
        return new CudaForStatementIr(
            initializers.ToImmutable(),
            condition,
            [increment],
            body,
            $"csharp2cuda_for_continue_{labelIndex++}",
            syntax.GetLocation());
    }

    private CudaStatementIr LowerSwitch(SwitchStatementSyntax syntax)
    {
        if (syntax.Sections.SelectMany(static section => section.Labels)
            .Any(static label => label is CasePatternSwitchLabelSyntax) &&
            semanticModel.GetOperation(syntax) is ISwitchOperation operation)
        {
            return LowerPatternSwitch(syntax, operation);
        }

        var sections = ImmutableArray.CreateBuilder<CudaSwitchSectionIr>();
        foreach (var section in syntax.Sections)
        {
            var labels = ImmutableArray.CreateBuilder<string?>();
            foreach (var label in section.Labels)
            {
                if (label is DefaultSwitchLabelSyntax)
                {
                    labels.Add(null);
                    continue;
                }
                var value = ((CaseSwitchLabelSyntax)label).Value;
                var constant = semanticModel.GetConstantValue(value);
                if (!constant.HasValue || constant.Value is null)
                {
                    ReportUnsupported(value);
                    labels.Add("0");
                    continue;
                }
                labels.Add(FormatConstant(constant.Value, semanticModel.GetTypeInfo(value).ConvertedType));
            }
            sections.Add(new CudaSwitchSectionIr(
                labels.ToImmutable(),
                section.Statements.Select(LowerStatement).ToImmutableArray(),
                section.GetLocation()));
        }
        return new CudaSwitchStatementIr(
            LowerExpression(syntax.Expression),
            sections.ToImmutable(),
            syntax.GetLocation());
    }

    private CudaExpressionIr LowerExpression(ExpressionSyntax syntax)
    {
        if (syntax is ParenthesizedExpressionSyntax parenthesized)
            return LowerExpression(parenthesized.Expression);

        if (syntax is StackAllocArrayCreationExpressionSyntax)
        {
            ReportUnsupported(syntax);
            return InvalidExpression(syntax.GetLocation());
        }

        if (syntax is PrefixUnaryExpressionSyntax)
        {
            var constant = semanticModel.GetConstantValue(syntax);
            if (constant.HasValue)
            {
                return ValueExpression(new CudaValueIr(
                    FormatConstant(constant.Value, semanticModel.GetTypeInfo(syntax).Type),
                    semanticModel.GetTypeInfo(syntax).Type,
                    CudaEffectIr.None,
                    true,
                    syntax.GetLocation()));
            }
        }

        var operation = semanticModel.GetOperation(syntax);
        if (operation is null)
        {
            var constant = semanticModel.GetConstantValue(syntax);
            if (constant.HasValue)
            {
                return ValueExpression(new CudaValueIr(
                    FormatConstant(constant.Value, semanticModel.GetTypeInfo(syntax).Type),
                    semanticModel.GetTypeInfo(syntax).Type,
                    CudaEffectIr.None,
                    true,
                    syntax.GetLocation()));
            }
            ReportUnsupported(syntax);
            return InvalidExpression(syntax.GetLocation());
        }
        return LowerOperation(operation);
    }

    private CudaExpressionIr LowerReferenceExpression(ExpressionSyntax syntax)
    {
        if (syntax is RefExpressionSyntax reference)
            syntax = reference.Expression;
        var operation = semanticModel.GetOperation(syntax);
        if (operation is null)
        {
            ReportUnsupported(syntax);
            return InvalidExpression(syntax.GetLocation());
        }
        return LowerReferenceOperation(operation);
    }

    private CudaExpressionIr LowerReferenceOperation(IOperation operation)
    {
        if (operation is IInvocationOperation invocation &&
            (invocation.TargetMethod.ReturnsByRef ||
             invocation.TargetMethod.ReturnsByRefReadonly))
        {
            return LowerInvocation(invocation, preserveReference: true);
        }
        if (!IsAddressable(operation))
            return UnsupportedExpression(operation);
        var place = LowerPlace(operation);
        var pointerType = semanticModel.Compilation.CreatePointerTypeSymbol(place.Type);
        return new CudaExpressionIr(
            place.Prefix,
            new CudaValueIr(
                place.AddressCode,
                pointerType,
                place.Effects,
                false,
                operation.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerOperation(IOperation operation)
    {
        if (IsPointerIndirection(operation))
            return LowerIndirection(operation);
        if (IsPointerElement(operation))
            return LowerPointerElement(operation);
        return operation switch
        {
            IParenthesizedOperation parenthesized => LowerOperation(parenthesized.Operand),
            ILiteralOperation literal => ValueExpression(new CudaValueIr(
                FormatConstant(literal.ConstantValue.HasValue ? literal.ConstantValue.Value : null, literal.Type),
                literal.Type,
                CudaEffectIr.None,
                true,
                literal.Syntax.GetLocation())),
            IDefaultValueOperation value => LowerDefaultValue(value),
            ILocalReferenceOperation local => LowerLocalReference(local),
            IParameterReferenceOperation parameter => LowerParameterReference(parameter),
            IFieldReferenceOperation field => LowerField(field),
            IPropertyReferenceOperation property => LowerProperty(property),
            IArrayElementReferenceOperation element => LowerElement(element),
            IImplicitIndexerReferenceOperation indexer => LowerImplicitIndexer(indexer),
            IAddressOfOperation address => LowerAddress(address),
            IInvocationOperation invocation => LowerInvocation(invocation),
            IArrayCreationOperation array => ManagedAllocationExpression(array),
            IObjectCreationOperation creation => LowerObjectCreation(creation),
            IAnonymousObjectCreationOperation anonymous => ManagedAllocationExpression(anonymous),
            IDelegateCreationOperation creation => ManagedAllocationExpression(creation),
            ITupleOperation tuple => LowerTuple(tuple),
            ITupleBinaryOperation tuple => LowerTupleBinary(tuple),
            IConversionOperation conversion => LowerConversion(conversion),
            IBinaryOperation binary => LowerBinary(binary),
            IUnaryOperation unary => LowerUnary(unary),
            ISimpleAssignmentOperation assignment => LowerSimpleAssignment(assignment),
            IDeconstructionAssignmentOperation assignment => LowerDeconstruction(assignment),
            ICompoundAssignmentOperation assignment => LowerCompoundAssignment(assignment),
            IIncrementOrDecrementOperation increment => LowerIncrement(increment),
            IConditionalOperation conditional => LowerConditional(conditional),
            IIsPatternOperation pattern => LowerIsPattern(pattern),
            ISwitchExpressionOperation selection => LowerSwitchExpression(selection),
            IArgumentOperation argument => LowerOperation(argument.Value),
            IInstanceReferenceOperation instance => LowerInstance(instance),
            _ when operation.Kind == OperationKind.None => LowerNoneOperation(operation),
            _ => UnsupportedExpression(operation)
        };
    }

    private CudaExpressionIr LowerDefaultValue(IDefaultValueOperation value)
    {
        if (value.Type is null)
            return UnsupportedExpression(value);
        if (value.Type is IArrayTypeSymbol { Rank: 1 } array)
        {
            var element = FormatType(array.ElementType, false, value.Syntax.GetLocation());
            return ValueExpression(new CudaValueIr(
                $"csharp2cuda_array_view<{element}>(nullptr, 0, true)",
                value.Type,
                CudaEffectIr.None,
                false,
                value.Syntax.GetLocation())
            {
                ViewMutability = CudaViewMutability.Writable
            });
        }
        if (value.Type is INamedTypeSymbol view && plan.IsSpanType(view, out var readOnly))
        {
            var element = FormatType(view.TypeArguments[0], false, value.Syntax.GetLocation());
            var viewName = readOnly
                ? "csharp2cuda_readonly_array_view"
                : "csharp2cuda_array_view";
            return ValueExpression(new CudaValueIr(
                $"{viewName}<{element}>(nullptr, 0, false)",
                value.Type,
                CudaEffectIr.None,
                false,
                value.Syntax.GetLocation())
            {
                ViewMutability = readOnly
                    ? CudaViewMutability.ReadOnly
                    : CudaViewMutability.Writable
            });
        }
        var code = value.Type switch
        {
            IPointerTypeSymbol => "nullptr",
            { SpecialType: SpecialType.System_Boolean } => "false",
            { SpecialType: SpecialType.System_Single } => "0.0f",
            { SpecialType: SpecialType.System_Double } => "0.0",
            { SpecialType: not SpecialType.None } => "0",
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => "0",
            _ => $"{FormatType(value.Type, false, value.Syntax.GetLocation())}{{}}"
        };
        return ValueExpression(new CudaValueIr(
            code,
            value.Type,
            CudaEffectIr.None,
            true,
            value.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerNoneOperation(IOperation operation)
    {
        var children = operation.ChildOperations.ToArray();
        if (children.Length == 1)
            return LowerOperation(children[0]);

        if (operation.Syntax is ParenthesizedExpressionSyntax parenthesized)
            return LowerExpression(parenthesized.Expression);

        if (operation.Syntax is PrefixUnaryExpressionSyntax unary)
        {
            var constant = semanticModel.GetConstantValue(unary);
            if (constant.HasValue)
            {
                return ValueExpression(new CudaValueIr(
                    FormatConstant(constant.Value, semanticModel.GetTypeInfo(unary).Type),
                    semanticModel.GetTypeInfo(unary).Type,
                    CudaEffectIr.None,
                    true,
                    unary.GetLocation()));
            }
        }

        if (operation.Syntax is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            return symbol switch
            {
                ILocalSymbol local => ReferenceExpression(
                    plan.GetIdentifier(local),
                    local.Type,
                    identifier.GetLocation()),
                IParameterSymbol parameter => ReferenceExpression(
                    plan.GetIdentifier(parameter),
                    parameter.Type,
                    identifier.GetLocation()),
                IFieldSymbol field when plan.TryGetIdentifier(field, out var name) => ReferenceExpression(
                    name,
                    field.Type,
                    identifier.GetLocation()),
                _ => UnsupportedExpression(operation)
            };
        }

        return UnsupportedExpression(operation);
    }

    private CudaExpressionIr LowerField(IFieldReferenceOperation field)
    {
        if (field.Field.HasConstantValue)
        {
            if (field.Field.Type.TypeKind != TypeKind.Enum &&
                !field.Field.Locations.Any(static location => location.IsInSource) &&
                !plan.TryGetRuntimeField(field.Field, out _))
            {
                ReportUnsupported(field.Syntax);
                return InvalidExpression(field.Syntax.GetLocation());
            }
            return ValueExpression(new CudaValueIr(
                FormatConstant(field.Field.ConstantValue, field.Field.Type),
                field.Field.Type,
                CudaEffectIr.None,
                true,
                field.Syntax.GetLocation()));
        }
        if (plan.TryGetRuntimeField(field.Field, out var runtimeFieldCode) &&
            runtimeFieldCode is not null)
        {
            return ReferenceExpression(
                runtimeFieldCode,
                field.Field.Type,
                field.Syntax.GetLocation());
        }
        if (plan.TryGetConstantArray(field.Field, out var constant))
        {
            return ReferenceExpression(
                constant.EmittedName,
                field.Field.Type,
                field.Syntax.GetLocation(),
                CudaViewMutability.ReadOnly);
        }
        if (TryGetTupleFieldName(field.Field, out var tupleFieldName))
        {
            if (field.Instance is null)
                return UnsupportedExpression(field);
            var tuplePrefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
            var instance = Materialize(LowerOperation(field.Instance), tuplePrefix);
            return new CudaExpressionIr(
                tuplePrefix.ToImmutable(),
                new CudaValueIr(
                    $"({instance.Code}).{tupleFieldName}",
                    field.Type,
                    CudaEffectIr.Read,
                    false,
                    field.Syntax.GetLocation()));
        }
        if (!plan.TryGetIdentifier(field.Field, out var name))
        {
            ReportUnsupported(field.Syntax);
            return InvalidExpression(field.Syntax.GetLocation());
        }
        if (plan.TryGetExplicitFieldLayout(field.Field, out _, out _))
        {
            var place = LowerExplicitFieldPlace(field);
            return new CudaExpressionIr(
                place.Prefix,
                new CudaValueIr(
                    place.AccessCode,
                    field.Type,
                    CudaEffectIr.Read,
                    false,
                    field.Syntax.GetLocation()));
        }
        var fieldInstance = field.Instance;
        if (fieldInstance is null)
            return ReferenceExpression(name, field.Type, field.Syntax.GetLocation());

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        string code;
        if (IsPointerMemberAccess(field))
        {
            var instance = Materialize(LowerOperation(fieldInstance), prefix);
            code = $"({instance.Code})->{name}";
        }
        else if (IsPointerIndirection(fieldInstance))
        {
            var instance = LowerOperation(fieldInstance.ChildOperations.Single());
            var saved = Materialize(instance, prefix);
            code = $"({saved.Code})->{name}";
        }
        else if (IsAddressable(fieldInstance))
        {
            var instance = LowerPlace(fieldInstance);
            prefix.AddRange(instance.Prefix);
            code = $"({instance.AccessCode}).{name}";
        }
        else
        {
            var instance = LowerOperation(fieldInstance);
            var saved = Materialize(instance, prefix);
            code = $"({saved.Code}).{name}";
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                field.Type,
                CudaEffectIr.Read,
                false,
                field.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerParameterReference(IParameterReferenceOperation parameter)
    {
        var captured = function.TryGetCapture(parameter.Parameter, out var capture);
        var name = captured ? capture.EmittedName : plan.GetIdentifier(parameter.Parameter);
        var code = captured
            ? capture.ByReference ? $"*({name})" : name
            : parameter.Parameter.RefKind == RefKind.None ? name : $"*({name})";
        return ReferenceExpression(
            code,
            parameter.Type,
            parameter.Syntax.GetLocation(),
            GetViewMutability(parameter.Parameter, parameter.Type));
    }

    private CudaExpressionIr LowerLocalReference(ILocalReferenceOperation local)
    {
        var captured = function.TryGetCapture(local.Local, out var capture);
        var name = captured ? capture.EmittedName : plan.GetIdentifier(local.Local);
        var indirect = captured
            ? capture.ByReference
            : local.Local.RefKind != RefKind.None;
        return ReferenceExpression(
            indirect ? $"*({name})" : name,
            local.Type,
            local.Syntax.GetLocation(),
            GetViewMutability(local.Local, local.Type));
    }

    private CudaExpressionIr LowerProperty(IPropertyReferenceOperation property)
    {
        if (property.Syntax is MemberAccessExpressionSyntax member &&
            plan.TryGetDimensionReplacement(member, out var replacement))
        {
            return ReferenceExpression(replacement, property.Type, property.Syntax.GetLocation());
        }
        if (plan.TryGetProperty(property.Property, out var propertyPlan))
        {
            if (property.Instance is null)
                return UnsupportedExpression(property);
            var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
            var instance = Materialize(LowerOperation(property.Instance), prefix);
            var name = plan.GetIdentifier(propertyPlan.Symbol);
            return new CudaExpressionIr(
                prefix.ToImmutable(),
                new CudaValueIr(
                    $"({instance.Code}).{name}",
                    property.Type,
                    CudaEffectIr.Read,
                    false,
                property.Syntax.GetLocation()));
        }
        if (property.Property.GetMethod is { } getter &&
            plan.GetCallPlan(plan.ResolveMethod(getter, function)) is
            { Kind: CudaCallKind.PlannedFunction } getterCall)
        {
            return LowerPropertyGetter(property, getter, getterCall);
        }
        if (property.Property.IsIndexer &&
            property.Instance is not null &&
            property.Arguments.Length == 1)
        {
            var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
            var target = Materialize(LowerOperation(property.Instance), prefix);
            var index = Materialize(LowerOperation(property.Arguments[0].Value), prefix);
            return new CudaExpressionIr(
                prefix.ToImmutable(),
                new CudaValueIr(
                    $"({target.Code})[{index.Code}]",
                    property.Type,
                    CudaEffectIr.Read,
                    false,
                    property.Syntax.GetLocation()));
        }
        if (property.Instance is not null &&
            (property.Property.Name is "Length" or "IsEmpty") &&
            plan.IsArrayViewType(property.Instance.Type))
        {
            var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
            var instance = Materialize(LowerOperation(property.Instance), prefix);
            var code = property.Property.Name == "Length"
                ? $"({instance.Code}).get_length()"
                : $"({instance.Code}).is_empty()";
            return new CudaExpressionIr(
                prefix.ToImmutable(),
                new CudaValueIr(
                    code,
                    property.Type,
                    CudaEffectIr.Read,
                    false,
                    property.Syntax.GetLocation()));
        }
        ReportUnsupported(property.Syntax);
        return InvalidExpression(property.Syntax.GetLocation());
    }

    private CudaExpressionIr LowerElement(IArrayElementReferenceOperation element)
    {
        if (element.Indices.Length != 1)
        {
            ReportUnsupported(element.Syntax);
            return InvalidExpression(element.Syntax.GetLocation());
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var targetExpression = LowerOperation(element.ArrayReference);
        prefix.AddRange(targetExpression.Prefix);
        var target = targetExpression.Value;
        if (target.Type is IPointerTypeSymbol)
            target = MaterializeValue(target, prefix);
        if (element.Indices[0] is IRangeOperation range)
            return LowerRangeAccess(target, range, element.Type!, prefix, element.Syntax.GetLocation());
        var index = LowerIndexArgument(element.Indices[0], target, prefix);
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"({target.Code})[{index.Code}]",
                element.Type,
                CudaEffectIr.Read,
                false,
                element.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerImplicitIndexer(IImplicitIndexerReferenceOperation indexer)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var target = Materialize(LowerOperation(indexer.Instance), prefix);
        if (indexer.Argument is IRangeOperation range)
            return LowerRangeAccess(target, range, indexer.Type!, prefix, indexer.Syntax.GetLocation());
        var argument = LowerIndexArgument(indexer.Argument, target, prefix);
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"({target.Code})[{argument.Code}]",
                indexer.Type,
                CudaEffectIr.Read,
                false,
                indexer.Syntax.GetLocation())
            {
                ViewMutability = target.ViewMutability
            });
    }

    private CudaExpressionIr LowerRangeAccess(
        CudaValueIr target,
        IRangeOperation range,
        ITypeSymbol resultType,
        ImmutableArray<CudaStatementIr>.Builder prefix,
        Location location)
    {
        target = MaterializeForReuse(target, prefix);
        CudaValueIr? startOperand = null;
        var startFromEnd = false;
        if (range.LeftOperand is not null)
        {
            startOperand = LowerRangeEndpointOperand(
                range.LeftOperand,
                prefix,
                out startFromEnd);
        }
        CudaValueIr? endOperand = null;
        var endFromEnd = false;
        if (range.RightOperand is not null)
        {
            endOperand = LowerRangeEndpointOperand(
                range.RightOperand,
                prefix,
                out endFromEnd);
        }
        var length = MaterializeValue(new CudaValueIr(
            $"({target.Code}).get_length()",
            semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32),
            CudaEffectIr.Read,
            false,
            range.Syntax.GetLocation()), prefix);
        var start = startOperand is null
            ? new CudaValueIr(
                "0",
                length.Type,
                CudaEffectIr.None,
                true,
                range.Syntax.GetLocation())
            : ResolveRangeEndpoint(startOperand, startFromEnd, length);
        var end = endOperand is null
            ? length
            : ResolveRangeEndpoint(endOperand, endFromEnd, length);
        start = MaterializeForReuse(start, prefix);
        end = MaterializeValue(end, prefix);
        var sliceCode =
            $"({target.Code}).slice({start.Code}, ({end.Code}) - ({start.Code}))";
        if (target.Type is IArrayTypeSymbol { Rank: 1 } array &&
            resultType is IArrayTypeSymbol)
        {
            var elementName = FormatType(array.ElementType, false, location);
            var storageName = NewTemporaryName() + "_range_storage";
            prefix.Add(new CudaFixedArrayDeclarationStatementIr(
                elementName,
                storageName,
                CudaEmissionPlan.MaximumFixedLocalElementCount,
                false,
                [],
                location));
            sliceCode =
                $"csharp2cuda_copy_array({sliceCode}, {storageName}, " +
                $"{CudaEmissionPlan.MaximumFixedLocalElementCount})";
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                sliceCode,
                resultType,
                CudaEffectIr.Read,
                false,
                location)
            {
                ViewMutability = target.ViewMutability
            });
    }

    private CudaValueIr LowerRangeEndpointOperand(
        IOperation endpoint,
        ImmutableArray<CudaStatementIr>.Builder prefix,
        out bool fromEnd)
    {
        fromEnd = false;
        if (endpoint is IConversionOperation conversion &&
            conversion.Type?.Name == nameof(Index) &&
            conversion.Type.ContainingNamespace?.ToDisplayString() == "System")
        {
            return Materialize(LowerOperation(conversion.Operand), prefix);
        }
        if (endpoint is IUnaryOperation { OperatorKind: UnaryOperatorKind.Hat } fromEndOperation)
        {
            fromEnd = true;
            return Materialize(LowerOperation(fromEndOperation.Operand), prefix);
        }
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedRangeOperation,
            endpoint.Syntax.GetLocation(),
            endpoint.Syntax.ToString()));
        return new CudaValueIr(
            "csharp2cuda_invalid",
            semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32),
            CudaEffectIr.None,
            true,
            endpoint.Syntax.GetLocation());
    }

    private static CudaValueIr ResolveRangeEndpoint(
        CudaValueIr operand,
        bool fromEnd,
        CudaValueIr length) => fromEnd
        ? operand with
        {
            Code = $"csharp2cuda_index_from_end({length.Code}, {operand.Code})",
            Type = length.Type,
            Effects = CudaEffectIr.Trap,
            IsSimple = false
        }
        : operand;

    private CudaValueIr LowerIndexArgument(
        IOperation argument,
        CudaValueIr target,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        if (argument is IUnaryOperation { OperatorKind: UnaryOperatorKind.Hat } fromEnd)
        {
            var value = Materialize(LowerOperation(fromEnd.Operand), prefix);
            var length = MaterializeValue(new CudaValueIr(
                $"({target.Code}).get_length()",
                semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32),
                CudaEffectIr.Read,
                false,
                argument.Syntax.GetLocation()), prefix);
            return new CudaValueIr(
                $"csharp2cuda_index_from_end({length.Code}, {value.Code})",
                length.Type,
                CudaEffectIr.Trap,
                false,
                argument.Syntax.GetLocation());
        }
        if (argument is IConversionOperation conversion &&
            conversion.Type?.Name == nameof(Index) &&
            conversion.Type.ContainingNamespace?.ToDisplayString() == "System")
        {
            return Materialize(LowerOperation(conversion.Operand), prefix);
        }
        return Materialize(LowerOperation(argument), prefix);
    }

    private CudaExpressionIr LowerPointerElement(IOperation element)
    {
        var children = element.ChildOperations.ToArray();
        CudaExpressionIr targetExpression;
        CudaExpressionIr indexExpression;
        if (children.Length == 2)
        {
            targetExpression = LowerOperation(children[0]);
            indexExpression = LowerOperation(children[1]);
        }
        else if (element.Syntax is ElementAccessExpressionSyntax syntax &&
            syntax.ArgumentList.Arguments.Count == 1)
        {
            return LowerPointerElement(syntax);
        }
        else
        {
            ReportUnsupported(element.Syntax);
            return InvalidExpression(element.Syntax.GetLocation());
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var target = Materialize(targetExpression, prefix);
        var index = Materialize(indexExpression, prefix);
        var elementType = element.Type ?? semanticModel.GetTypeInfo(element.Syntax).Type;
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"({target.Code})[{index.Code}]",
                elementType,
                CudaEffectIr.Read,
                false,
                element.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerPointerElement(ElementAccessExpressionSyntax syntax)
    {
        if (syntax.ArgumentList.Arguments.Count != 1)
        {
            ReportUnsupported(syntax);
            return InvalidExpression(syntax.GetLocation());
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var target = Materialize(LowerExpression(syntax.Expression), prefix);
        var index = Materialize(
            LowerExpression(syntax.ArgumentList.Arguments[0].Expression),
            prefix);
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"({target.Code})[{index.Code}]",
                semanticModel.GetTypeInfo(syntax).Type,
                CudaEffectIr.Read,
                false,
                syntax.GetLocation()));
    }

    private CudaExpressionIr LowerIndirection(IOperation indirection)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var pointer = Materialize(
            LowerOperation(indirection.ChildOperations.Single()),
            prefix);
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"*({pointer.Code})",
                indirection.Type,
                CudaEffectIr.Read,
                false,
                indirection.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerAddress(IAddressOfOperation address)
    {
        var place = LowerPlace(address.Reference);
        return new CudaExpressionIr(
            place.Prefix,
            new CudaValueIr(
                place.AddressCode,
                address.Type,
                place.Effects,
                false,
                address.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerInvocation(
        IInvocationOperation invocation,
        bool preserveReference = false)
    {
        var reducedFrom = invocation.TargetMethod.ReducedFrom;
        var sourceTarget = TryGetDirectAnonymousFunction(invocation.Instance, out var anonymous)
            ? anonymous.Symbol
            : invocation.TargetMethod;
        var targetMethod = plan.ResolveMethod(sourceTarget, function);
        if (CudaEmissionPlan.IsGeneratedPositionalRecordMember(targetMethod))
        {
            if (targetMethod.Name == nameof(object.Equals) &&
                invocation.Instance is not null &&
                invocation.Arguments.Length == 1)
            {
                return LowerRecordEquality(
                    invocation.Instance,
                    invocation.Arguments[0].Value,
                    negate: false,
                    invocation.Type,
                    invocation.Syntax.GetLocation());
            }
            if (targetMethod.Name == "Deconstruct" && invocation.Instance is not null)
                return LowerRecordDeconstruct(invocation, targetMethod);
        }
        var call = plan.GetCallPlan(targetMethod);
        if (call is null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.UnsupportedCall,
                invocation.Syntax.GetLocation(),
                invocation.TargetMethod.ToDisplayString(
                    SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return InvalidExpression(invocation.Syntax.GetLocation());
        }

        ValidateIntrinsicInvocation(invocation, targetMethod, call);

        if (call.Kind is CudaCallKind.Atomic or CudaCallKind.SignedInt64Atomic)
            return LowerAtomic(invocation, call);
        if (call.Kind == CudaCallKind.DynamicSharedView)
            return LowerDynamicSharedView(invocation);
        if (call.Kind is CudaCallKind.ArrayView or CudaCallKind.ReadOnlyArrayView)
            return LowerArrayView(invocation, call.Kind == CudaCallKind.ReadOnlyArrayView);
        if (call.Kind == CudaCallKind.SliceView)
            return LowerSliceView(invocation);
        if (call.Kind == CudaCallKind.AsSpanView)
            return LowerAsSpanView(invocation);
        if (call.Kind is CudaCallKind.ClearView or CudaCallKind.FillView or
            CudaCallKind.CopyView or CudaCallKind.TryCopyView)
        {
            return LowerViewOperation(invocation, call);
        }
        if (call.Kind == CudaCallKind.NaN)
        {
            return ValueExpression(new CudaValueIr(
                "nan(\"\")",
                invocation.Type,
                CudaEffectIr.None,
                false,
                invocation.Syntax.GetLocation()));
        }
        if (call.Kind is CudaCallKind.BooleanToInteger or
            CudaCallKind.IntegerToBoolean or
            CudaCallKind.SignedToUnsigned or
            CudaCallKind.Unwrap)
        {
            return LowerConversionIntrinsic(invocation, call.Kind);
        }
        if (call.Kind == CudaCallKind.InvalidAtomic)
        {
            var type = invocation.Arguments.FirstOrDefault()?.Value.Type?.ToDisplayString() ??
                targetMethod.TypeArguments.FirstOrDefault()?.ToDisplayString() ?? "unknown";
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidAtomicType,
                invocation.Syntax.GetLocation(),
                type,
                targetMethod.Name));
            return InvalidExpression(invocation.Syntax.GetLocation());
        }
        if (call.Kind == CudaCallKind.Storage)
        {
            ReportUnsupported(invocation.Syntax);
            return InvalidExpression(invocation.Syntax.GetLocation());
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var hasTargetFunction = plan.TryGetFunction(targetMethod, out var targetFunction);
        var hasInstance = hasTargetFunction
            ? targetFunction.HasInstance
            : !targetMethod.IsStatic;
        var isReducedExtension = reducedFrom is not null;
        var captureCount = hasTargetFunction ? targetFunction.Captures.Length : 0;
        var arguments = new string?[
            targetMethod.Parameters.Length + (hasInstance ? 1 : 0) + captureCount];
        var argumentOffset = hasInstance ? 1 : 0;
        if (hasInstance)
        {
            if (invocation.Instance is null)
                return UnsupportedExpression(invocation);
            var instance = LowerPlace(invocation.Instance);
            arguments[0] = MaterializeAddress(instance, prefix).Code;
        }
        else if (isReducedExtension)
        {
            if (invocation.Instance is null)
                return UnsupportedExpression(invocation);
            arguments[0] = LowerCallArgument(
                invocation.Instance,
                targetMethod.Parameters[0],
                prefix);
        }
        var explicitArguments = invocation.Arguments
            .Where(static item => item.ArgumentKind != ArgumentKind.DefaultValue)
            .OrderBy(static item => item.Syntax.SpanStart);
        foreach (var argument in explicitArguments)
        {
            var ordinal = argument.Parameter?.Ordinal ?? 0;
            if (isReducedExtension)
                ordinal++;
            var targetParameter = targetMethod.Parameters[ordinal];
            if (argument.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection)
            {
                arguments[argumentOffset + ordinal] = LowerExpandedParamsArgument(
                    argument,
                    targetParameter,
                    prefix);
                continue;
            }
            if (targetParameter.RefKind == RefKind.None)
            {
                if (plan.IsArrayViewType(targetParameter.Type) && IsNullConstant(argument.Value))
                {
                    arguments[argumentOffset + ordinal] = CreateNullView(
                        targetParameter,
                        argument.Syntax.GetLocation());
                    continue;
                }
                var lowered = LowerOperation(argument.Value);
                if (plan.IsViewParameterWritable(targetParameter) &&
                    lowered.Value.ViewMutability == CudaViewMutability.ReadOnly)
                {
                    diagnostics.Add(Diagnostic.Create(
                        CudaDiagnostics.ViewEscape,
                        argument.Syntax.GetLocation(),
                        targetParameter.Name));
                }
                arguments[argumentOffset + ordinal] = Materialize(lowered, prefix).Code;
            }
            else
            {
                arguments[argumentOffset + ordinal] = LowerCallArgument(
                    argument.Value,
                    targetParameter,
                    prefix);
            }
        }
        foreach (var argument in invocation.Arguments.Where(
            static item => item.ArgumentKind == ArgumentKind.DefaultValue))
        {
            if (argument.Parameter is null)
                continue;
            var ordinal = argument.Parameter.Ordinal + (isReducedExtension ? 1 : 0);
            var targetParameter = targetMethod.Parameters[ordinal];
            arguments[argumentOffset + ordinal] = targetParameter.RefKind == RefKind.None
                ? Materialize(LowerOperation(argument.Value), prefix).Code
                : LowerCallArgument(argument.Value, targetParameter, prefix);
        }
        if (hasTargetFunction)
        {
            for (var index = 0; index < targetFunction.Captures.Length; index++)
            {
                arguments[argumentOffset + targetMethod.Parameters.Length + index] =
                    LowerCaptureArgument(targetFunction.Captures[index], prefix);
            }
        }
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] ??= "csharp2cuda_invalid";
        var effects = plan.IsPureCall(targetMethod)
            ? CudaEffectIr.None
            : CudaEffectIr.Call;
        var callCode = $"{call.Name}({string.Join(", ", arguments)})";
        var resultType = ResolveType(invocation.Type);
        if (targetMethod.ReturnsByRef || targetMethod.ReturnsByRefReadonly)
        {
            if (preserveReference)
            {
                resultType = semanticModel.Compilation.CreatePointerTypeSymbol(
                    targetMethod.ReturnType);
            }
            else
            {
                callCode = $"*({callCode})";
            }
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                callCode,
                resultType,
                effects,
                false,
                invocation.Syntax.GetLocation()));
    }

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

    private string LowerCaptureArgument(
        CudaCapturePlan capture,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        if (function.TryGetCapture(capture.Symbol, out var forwarded))
        {
            if (capture.ByReference)
                return forwarded.ByReference
                    ? forwarded.EmittedName
                    : $"&({forwarded.EmittedName})";
            var code = forwarded.ByReference
                ? $"*({forwarded.EmittedName})"
                : forwarded.EmittedName;
            return MaterializeValue(new CudaValueIr(
                code,
                capture.Type,
                CudaEffectIr.Read,
                true,
                capture.Symbol.Locations.FirstOrDefault() ?? function.Syntax.GetLocation()), prefix).Code;
        }

        var name = plan.GetIdentifier(capture.Symbol);
        var sourceIsReference = capture.Symbol switch
        {
            IParameterSymbol parameter => parameter.RefKind != RefKind.None,
            ILocalSymbol local => local.RefKind != RefKind.None,
            _ => false
        };
        if (capture.ByReference)
            return sourceIsReference ? name : $"&({name})";
        var valueCode = sourceIsReference ? $"*({name})" : name;
        return MaterializeValue(new CudaValueIr(
            valueCode,
            capture.Type,
            CudaEffectIr.Read,
            true,
            capture.Symbol.Locations.FirstOrDefault() ?? function.Syntax.GetLocation()), prefix).Code;
    }

    private string LowerExpandedParamsArgument(
        IArgumentOperation argument,
        IParameterSymbol parameter,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        ImmutableArray<IOperation> elements;
        if (argument.Value is IArrayCreationOperation
            {
                Initializer: { } initializer
            })
        {
            elements = initializer.ElementValues;
        }
        else if (argument.Value is ICollectionExpressionOperation collection)
        {
            elements = collection.Elements;
        }
        else
        {
            ReportUnsupported(argument.Syntax);
            return "csharp2cuda_invalid";
        }

        ITypeSymbol elementType;
        if (parameter.Type is IArrayTypeSymbol { Rank: 1 } array)
        {
            elementType = ResolveType(array.ElementType)!;
        }
        else if (ResolveType(parameter.Type) is INamedTypeSymbol named &&
            plan.IsSpanType(named, out _))
        {
            elementType = ResolveType(named.TypeArguments[0])!;
        }
        else
        {
            ReportUnsupported(argument.Syntax);
            return "csharp2cuda_invalid";
        }

        var readOnly = !plan.IsViewParameterWritable(parameter);
        var elementName = FormatType(elementType, false, argument.Syntax.GetLocation());
        var viewName = readOnly
            ? "csharp2cuda_readonly_array_view"
            : "csharp2cuda_array_view";
        if (elements.IsEmpty)
            return $"{viewName}<{elementName}>(nullptr, 0, false)";
        if (elements.Length > CudaEmissionPlan.MaximumFixedLocalElementCount)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.FixedLocalStorageLimit,
                argument.Syntax.GetLocation(),
                parameter.Name,
                elements.Length,
                CudaEmissionPlan.MaximumFixedLocalElementCount));
            return "csharp2cuda_invalid";
        }

        var initializers = ImmutableArray.CreateBuilder<CudaValueIr>();
        foreach (var element in elements)
            initializers.Add(Materialize(LowerOperation(element), prefix));
        var storageName = NewTemporaryName() + "_params";
        prefix.Add(new CudaFixedArrayDeclarationStatementIr(
            elementName,
            storageName,
            elements.Length,
            readOnly,
            initializers.ToImmutable(),
            argument.Syntax.GetLocation()));
        return $"{viewName}<{elementName}>({storageName}, {elements.Length}, false)";
    }

    private string CreateNullView(IParameterSymbol parameter, Location location)
    {
        var elementType = parameter.Type switch
        {
            IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
            INamedTypeSymbol named when plan.IsSpanType(named, out _) => named.TypeArguments[0],
            _ => null
        };
        if (elementType is null)
            return "csharp2cuda_invalid";
        var viewName = plan.IsViewParameterWritable(parameter)
            ? "csharp2cuda_array_view"
            : "csharp2cuda_readonly_array_view";
        return $"{viewName}<{FormatType(elementType, false, location)}>(nullptr, 0, true)";
    }

    private static bool IsNullConstant(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: null } ||
        operation is IConversionOperation conversion && IsNullConstant(conversion.Operand);

    private string LowerCallArgument(
        IOperation operation,
        IParameterSymbol parameter,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        if (parameter.RefKind == RefKind.None)
            return Materialize(LowerOperation(operation), prefix).Code;

        if (IsAddressable(operation))
            return MaterializeAddress(LowerPlace(operation), prefix).Code;

        if (parameter.RefKind != RefKind.In)
        {
            ReportUnsupported(operation.Syntax);
            return "nullptr";
        }

        var value = Materialize(LowerOperation(operation), prefix);
        var name = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(parameter.Type, false, operation.Syntax.GetLocation()),
            name,
            value,
            false,
            operation.Syntax.GetLocation()));
        return $"&({name})";
    }

    private CudaExpressionIr LowerAtomic(IInvocationOperation invocation, CudaCallPlan call)
    {
        var ordered = invocation.Arguments.OrderBy(static item => item.Syntax.SpanStart).ToArray();
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var location = LowerPlace(ordered[0].Value);
        RejectWrite(location, ordered[0].Value);
        prefix.AddRange(location.Prefix);
        var address = location.AddressCode;
        if (call.Kind == CudaCallKind.SignedInt64Atomic)
            address = $"(unsigned long long*)({address})";
        var arguments = new List<string> { address };
        for (var index = 1; index < ordered.Length; index++)
        {
            var value = Materialize(LowerOperation(ordered[index].Value), prefix);
            arguments.Add(call.Kind == CudaCallKind.SignedInt64Atomic
                ? $"(unsigned long long)({value.Code})"
                : value.Code);
        }
        var code = $"{call.Name}({string.Join(", ", arguments)})";
        if (call.Kind == CudaCallKind.SignedInt64Atomic)
            code = $"csharp2cuda_i64_from_bits({code})";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                invocation.Type,
                CudaEffectIr.Atomic | CudaEffectIr.Read | CudaEffectIr.Write,
                false,
                invocation.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerDynamicSharedView(IInvocationOperation invocation)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var arguments = invocation.Arguments.OrderBy(static item => item.Syntax.SpanStart).ToArray();
        var storage = Materialize(LowerOperation(arguments[0].Value), prefix);
        var offset = Materialize(LowerOperation(arguments[1].Value), prefix);
        var elementType = plan.ResolveMethod(invocation.TargetMethod, function).TypeArguments[0];
        var pointerType = FormatType(elementType, false, invocation.Syntax.GetLocation()) + "*";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"(({pointerType})({storage.Code}) + ({offset.Code}))",
                invocation.Type,
                CudaEffectIr.None,
                false,
                invocation.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerArrayView(
        IInvocationOperation invocation,
        bool readOnly)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var arguments = invocation.Arguments
            .OrderBy(static item => item.Parameter?.Ordinal ?? int.MaxValue)
            .ToArray();
        var address = Materialize(LowerOperation(arguments[0].Value), prefix);
        var length = Materialize(LowerOperation(arguments[1].Value), prefix);
        var elementType = plan.ResolveMethod(invocation.TargetMethod, function).TypeArguments[0];
        var elementName = FormatType(
            elementType,
            false,
            invocation.Syntax.GetLocation());
        var viewName = readOnly
            ? "csharp2cuda_readonly_array_view"
            : "csharp2cuda_array_view";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"{viewName}<{elementName}>({address.Code}, {length.Code}, ({address.Code} == nullptr))",
                invocation.Type,
                CudaEffectIr.None,
                false,
                invocation.Syntax.GetLocation())
            {
                ViewMutability = readOnly
                    ? CudaViewMutability.ReadOnly
                    : CudaViewMutability.Writable
            });
    }

    private CudaExpressionIr LowerObjectCreation(IObjectCreationOperation creation)
    {
        if (!plan.IsArrayViewType(creation.Type))
        {
            if (creation.Type?.IsReferenceType == true)
                return ManagedAllocationExpression(creation);
            if (creation.Type is INamedTypeSymbol structure &&
                creation.Constructor is { } constructor)
            {
                if (plan.TryGetStruct(structure, out var recordPlan) &&
                    recordPlan.IsPositionalRecord &&
                    CudaEmissionPlan.IsGeneratedPositionalRecordMember(constructor))
                {
                    if (creation.Initializer is not null)
                        return UnsupportedExpression(creation);
                    var recordPrefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
                    var recordArguments = new CudaValueIr?[constructor.Parameters.Length];
                    foreach (var argument in creation.Arguments
                                 .OrderBy(static item => item.Syntax.SpanStart))
                    {
                        if (argument.Parameter is null)
                            continue;
                        recordArguments[argument.Parameter.Ordinal] = Materialize(
                            LowerOperation(argument.Value),
                            recordPrefix);
                    }
                    var recordType = FormatType(
                        structure,
                        false,
                        creation.Syntax.GetLocation());
                    var recordName = NewTemporaryName();
                    recordPrefix.Add(new CudaVariableDeclarationStatementIr(
                        recordType,
                        recordName,
                        new CudaValueIr(
                            "{}",
                            structure,
                            CudaEffectIr.None,
                            true,
                            creation.Syntax.GetLocation()),
                        false,
                        creation.Syntax.GetLocation()));
                    var components = recordPlan.Properties
                        .Where(static property => property.Declaration is ParameterSyntax)
                        .ToArray();
                    if (components.Length != recordArguments.Length)
                        return UnsupportedExpression(creation);
                    for (var index = 0; index < recordArguments.Length; index++)
                    {
                        var argument = recordArguments[index] ?? new CudaValueIr(
                            "csharp2cuda_invalid",
                            constructor.Parameters[index].Type,
                            CudaEffectIr.None,
                            true,
                            creation.Syntax.GetLocation());
                        recordPrefix.Add(CreateAssignmentStatement(
                            $"({recordName}).{plan.GetIdentifier(components[index].Symbol)}",
                            argument,
                            creation.Syntax.GetLocation()));
                    }
                    return new CudaExpressionIr(
                        recordPrefix.ToImmutable(),
                        new CudaValueIr(
                            recordName,
                            structure,
                            CudaEffectIr.None,
                            true,
                            creation.Syntax.GetLocation()));
                }
                if (constructor.IsImplicitlyDeclared && creation.Arguments.IsEmpty)
                {
                    var typeName = FormatType(
                        structure,
                        false,
                        creation.Syntax.GetLocation());
                    return ValueExpression(new CudaValueIr(
                        $"{typeName}{{}}",
                        structure,
                        CudaEffectIr.None,
                        false,
                        creation.Syntax.GetLocation()));
                }

                var target = plan.ResolveMethod(constructor, function);
                if (plan.GetCallPlan(target) is not { Kind: CudaCallKind.PlannedFunction } call)
                    return UnsupportedExpression(creation);
                if (creation.Initializer is not null)
                    return UnsupportedExpression(creation);

                var constructorPrefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
                var arguments = new string?[target.Parameters.Length];
                foreach (var argument in creation.Arguments
                             .OrderBy(static item => item.Syntax.SpanStart))
                {
                    if (argument.Parameter is null)
                        continue;
                    arguments[argument.Parameter.Ordinal] = LowerCallArgument(
                        argument.Value,
                        target.Parameters[argument.Parameter.Ordinal],
                        constructorPrefix);
                }
                for (var index = 0; index < arguments.Length; index++)
                    arguments[index] ??= "csharp2cuda_invalid";
                return new CudaExpressionIr(
                    constructorPrefix.ToImmutable(),
                    new CudaValueIr(
                        $"{call.Name}({string.Join(", ", arguments)})",
                        structure,
                        CudaEffectIr.Call,
                        false,
                        creation.Syntax.GetLocation()));
            }
            return UnsupportedExpression(creation);
        }
        if (
            creation.Type is not INamedTypeSymbol named ||
            !plan.IsSpanType(named, out var readOnly) ||
            creation.Arguments.Length != 2)
        {
            return UnsupportedExpression(creation);
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var ordered = creation.Arguments
            .OrderBy(static argument => argument.Syntax.SpanStart)
            .ToArray();
        var address = Materialize(LowerOperation(ordered[0].Value), prefix);
        var length = Materialize(LowerOperation(ordered[1].Value), prefix);
        var elementName = FormatType(
            named.TypeArguments[0],
            false,
            creation.Syntax.GetLocation());
        var viewName = readOnly
            ? "csharp2cuda_readonly_array_view"
            : "csharp2cuda_array_view";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"{viewName}<{elementName}>({address.Code}, {length.Code})",
                creation.Type,
                CudaEffectIr.None,
                false,
                creation.Syntax.GetLocation())
            {
                ViewMutability = readOnly
                    ? CudaViewMutability.ReadOnly
                    : CudaViewMutability.Writable
            });
    }

    private CudaExpressionIr LowerSliceView(IInvocationOperation invocation)
    {
        if (invocation.Instance is null)
            return UnsupportedExpression(invocation);

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var instance = Materialize(LowerOperation(invocation.Instance), prefix);
        var arguments = invocation.Arguments
            .OrderBy(static argument => argument.Syntax.SpanStart)
            .Select(argument => Materialize(LowerOperation(argument.Value), prefix).Code)
            .ToArray();
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"({instance.Code}).slice({string.Join(", ", arguments)})",
                invocation.Type,
                CudaEffectIr.None,
                false,
                invocation.Syntax.GetLocation())
            {
                ViewMutability = instance.ViewMutability
            });
    }

    private CudaExpressionIr LowerAsSpanView(IInvocationOperation invocation)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var values = new List<IOperation>();
        if (invocation.TargetMethod.ReducedFrom is not null)
        {
            if (invocation.Instance is null)
                return UnsupportedExpression(invocation);
            values.Add(invocation.Instance);
        }
        values.AddRange(invocation.Arguments
            .Where(static argument => argument.ArgumentKind != ArgumentKind.DefaultValue)
            .OrderBy(static argument => argument.Syntax.SpanStart)
            .Select(static argument => argument.Value));
        if (values.Count is < 1 or > 3)
            return UnsupportedExpression(invocation);

        var source = Materialize(LowerOperation(values[0]), prefix);
        var code = $"({source.Code}).as_span()";
        for (var index = 1; index < values.Count; index++)
        {
            var argument = Materialize(LowerOperation(values[index]), prefix);
            code += index == 1 ? $".slice({argument.Code}" : $", {argument.Code}";
        }
        if (values.Count > 1)
            code += ")";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                invocation.Type,
                CudaEffectIr.None,
                false,
                invocation.Syntax.GetLocation())
            {
                ViewMutability = CudaViewMutability.Writable
            });
    }

    private CudaExpressionIr LowerViewOperation(
        IInvocationOperation invocation,
        CudaCallPlan call)
    {
        if (invocation.Instance is null)
            return UnsupportedExpression(invocation);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var instance = Materialize(LowerOperation(invocation.Instance), prefix);
        if (call.Kind is (CudaCallKind.ClearView or CudaCallKind.FillView) &&
            instance.ViewMutability == CudaViewMutability.ReadOnly)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ViewEscape,
                invocation.Syntax.GetLocation(),
                invocation.TargetMethod.Name));
        }
        var arguments = invocation.Arguments
            .OrderBy(static argument => argument.Syntax.SpanStart)
            .Select(argument => Materialize(LowerOperation(argument.Value), prefix).Code)
            .ToArray();
        var effects = call.Kind is CudaCallKind.ClearView or CudaCallKind.FillView or
            CudaCallKind.CopyView or CudaCallKind.TryCopyView
            ? CudaEffectIr.Read | CudaEffectIr.Write
            : CudaEffectIr.Read;
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"({instance.Code}).{call.Name}({string.Join(", ", arguments)})",
                invocation.Type,
                effects,
                false,
                invocation.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerConversionIntrinsic(
        IInvocationOperation invocation,
        CudaCallKind kind)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var argument = Materialize(LowerOperation(invocation.Arguments[0].Value), prefix);
        var code = kind switch
        {
            CudaCallKind.BooleanToInteger => $"(({argument.Code}) ? 1 : 0)",
            CudaCallKind.IntegerToBoolean => $"(({argument.Code}) != 0)",
            CudaCallKind.SignedToUnsigned => $"((unsigned long long)({argument.Code}))",
            CudaCallKind.Unwrap => argument.Code,
            _ => throw new InvalidOperationException($"Unknown conversion intrinsic '{kind}'.")
        };
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                invocation.Type,
                CudaEffectIr.None,
                false,
                invocation.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerConversion(IConversionOperation conversion)
    {
        if (conversion.IsChecked)
        {
            ReportUnsupported(conversion.Syntax);
            return InvalidExpression(conversion.Syntax.GetLocation());
        }
        if (TryGetUserDefinedCall(conversion.OperatorMethod, out var conversionTarget, out var conversionCall))
        {
            return LowerUserDefinedCall(
                conversionTarget,
                conversionCall,
                [conversion.Operand],
                conversion.Type,
                conversion.Syntax.GetLocation());
        }
        var operand = LowerOperation(conversion.Operand);
        if (conversion.Conversion.IsIdentity ||
            plan.IsCudaInt32Type(conversion.OperatorMethod?.ContainingType))
        {
            return operand with
            {
                Value = operand.Value with { Type = conversion.Type }
            };
        }

        if (conversion.Operand.Type is INamedTypeSymbol sourceTuple &&
            conversion.Type is INamedTypeSymbol targetTuple &&
            plan.IsTupleType(sourceTuple) &&
            plan.IsTupleType(targetTuple))
        {
            return LowerTupleConversion(
                conversion,
                operand,
                sourceTuple,
                targetTuple);
        }

        if (plan.IsArrayViewType(conversion.Operand.Type) &&
            plan.IsArrayViewType(conversion.Type))
        {
            var sourceElement = GetViewElementType(conversion.Operand.Type);
            var targetElement = GetViewElementType(conversion.Type);
            if (sourceElement is null || targetElement is null ||
                !SymbolEqualityComparer.Default.Equals(sourceElement, targetElement))
            {
                ReportUnsupported(conversion.Syntax);
                return InvalidExpression(conversion.Syntax.GetLocation());
            }

            var targetReadOnly = conversion.Type is INamedTypeSymbol targetView &&
                plan.IsSpanType(targetView, out var readOnly) && readOnly;
            if (!targetReadOnly && operand.Value.ViewMutability == CudaViewMutability.ReadOnly)
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.ViewEscape,
                    conversion.Syntax.GetLocation(),
                    conversion.Syntax.ToString()));
                return InvalidExpression(conversion.Syntax.GetLocation());
            }
            return operand with
            {
                Value = operand.Value with
                {
                    Type = conversion.Type,
                    ViewMutability = targetReadOnly
                        ? CudaViewMutability.ReadOnly
                        : operand.Value.ViewMutability
                }
            };
        }

        if (conversion.Operand.Type?.IsValueType == true &&
                conversion.Type?.IsReferenceType == true ||
            conversion.Operand.Type?.IsReferenceType == true &&
                conversion.Type?.IsValueType == true)
        {
            return ManagedAllocationExpression(conversion);
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var value = Materialize(operand, prefix);
        string code;
        if (conversion.Type is null)
        {
            ReportUnsupported(conversion.Syntax);
            code = value.Code;
        }
        else if (!TryPlanConversion(conversion, out var conversionHelper))
        {
            ReportUnsupported(conversion.Syntax);
            code = value.Code;
        }
        else if (conversionHelper is not null)
        {
            code = $"{conversionHelper}({value.Code})";
        }
        else
        {
            var typeName = FormatType(
                conversion.Type,
                false,
                conversion.Syntax.GetLocation());
            code = $"(({typeName})({value.Code}))";
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                conversion.Type,
                CudaEffectIr.None,
                false,
                conversion.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerTupleConversion(
        IConversionOperation conversion,
        CudaExpressionIr operand,
        INamedTypeSymbol sourceTuple,
        INamedTypeSymbol targetTuple)
    {
        if (sourceTuple.TupleElements.Length != targetTuple.TupleElements.Length)
            return UnsupportedExpression(conversion);

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var source = Materialize(operand, prefix);
        var convertedElements = new string[sourceTuple.TupleElements.Length];
        for (var index = 0; index < convertedElements.Length; index++)
        {
            var converted = LowerValueConversion(
                new CudaValueIr(
                    $"({source.Code}).item{index + 1}",
                    sourceTuple.TupleElements[index].Type,
                    CudaEffectIr.Read,
                    false,
                    conversion.Syntax.GetLocation()),
                sourceTuple.TupleElements[index].Type,
                targetTuple.TupleElements[index].Type,
                conversion.Syntax.GetLocation());
            if (converted is null)
            {
                ReportUnsupported(conversion.Syntax);
                return InvalidExpression(conversion.Syntax.GetLocation());
            }
            convertedElements[index] = MaterializeValue(converted, prefix).Code;
        }

        var targetName = FormatType(targetTuple, false, conversion.Syntax.GetLocation());
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"{targetName}{{{string.Join(", ", convertedElements)}}}",
                targetTuple,
                CudaEffectIr.None,
                false,
                conversion.Syntax.GetLocation()));
    }

    private CudaValueIr? LowerValueConversion(
        CudaValueIr value,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        Location location)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            return value with { Type = targetType };

        if (sourceType is INamedTypeSymbol sourceTuple &&
            targetType is INamedTypeSymbol targetTuple &&
            plan.IsTupleType(sourceTuple) &&
            plan.IsTupleType(targetTuple) &&
            sourceTuple.TupleElements.Length == targetTuple.TupleElements.Length)
        {
            var elements = new string[sourceTuple.TupleElements.Length];
            for (var index = 0; index < elements.Length; index++)
            {
                var element = LowerValueConversion(
                    value with
                    {
                        Code = $"({value.Code}).item{index + 1}",
                        Type = sourceTuple.TupleElements[index].Type,
                        IsSimple = false
                    },
                    sourceTuple.TupleElements[index].Type,
                    targetTuple.TupleElements[index].Type,
                    location);
                if (element is null)
                    return null;
                elements[index] = element.Code;
            }
            return new CudaValueIr(
                $"{FormatType(targetTuple, false, location)}{{{string.Join(", ", elements)}}}",
                targetTuple,
                CudaEffectIr.None,
                false,
                location);
        }

        var classified = ((CSharpCompilation)semanticModel.Compilation)
            .ClassifyConversion(sourceType, targetType);
        if (classified.MethodSymbol is { } method &&
            TryGetUserDefinedCall(method, out _, out var call))
        {
            return new CudaValueIr(
                $"{call.Name}({value.Code})",
                targetType,
                CudaEffectIr.Call,
                false,
                location);
        }
        if (!classified.Exists ||
            !TryPlanConversion(sourceType, targetType, out var helper))
        {
            return null;
        }
        var code = helper is null
            ? $"(({FormatType(targetType, false, location)})({value.Code}))"
            : $"{helper}({value.Code})";
        return new CudaValueIr(
            code,
            targetType,
            CudaEffectIr.None,
            false,
            location);
    }

    private CudaExpressionIr LowerBinary(IBinaryOperation binary)
    {
        if (binary.IsChecked)
        {
            ReportUnsupported(binary.Syntax);
            return InvalidExpression(binary.Syntax.GetLocation());
        }
        if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals &&
            TryLowerViewNullComparison(binary, out var viewNullComparison))
        {
            return viewNullComparison;
        }
        if (binary.OperatorMethod is { } recordOperator &&
            CudaEmissionPlan.IsGeneratedPositionalRecordMember(recordOperator) &&
            binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
        {
            return LowerRecordEquality(
                binary.LeftOperand,
                binary.RightOperand,
                binary.OperatorKind == BinaryOperatorKind.NotEquals,
                binary.Type,
                binary.Syntax.GetLocation());
        }
        if (TryGetUserDefinedCall(binary.OperatorMethod, out var binaryTarget, out var binaryCall))
        {
            return LowerUserDefinedCall(
                binaryTarget,
                binaryCall,
                [binary.LeftOperand, binary.RightOperand],
                binary.Type,
                binary.Syntax.GetLocation());
        }
        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or
            BinaryOperatorKind.ConditionalOr)
        {
            return LowerShortCircuit(binary);
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var left = Materialize(LowerOperation(binary.LeftOperand), prefix);
        var right = Materialize(LowerOperation(binary.RightOperand), prefix);
        if (!TryPlanBinary(binary, out var helper))
        {
            ReportUnsupported(binary.Syntax);
            return InvalidExpression(binary.Syntax.GetLocation());
        }

        string code;
        if (helper is not null)
            code = $"{helper}({left.Code}, {right.Code})";
        else
        {
            code = $"(({left.Code}) {GetBinaryOperator(binary.OperatorKind)} ({right.Code}))";
        }
        var effects = binary.OperatorKind is BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder
            ? CudaEffectIr.Trap
            : CudaEffectIr.None;
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                binary.Type,
                effects,
                false,
                binary.Syntax.GetLocation()));
    }

    private bool TryLowerViewNullComparison(
        IBinaryOperation binary,
        out CudaExpressionIr result)
    {
        IOperation? viewOperation = null;
        var left = UnwrapViewReferenceConversion(binary.LeftOperand);
        var right = UnwrapViewReferenceConversion(binary.RightOperand);
        if (plan.IsArrayViewType(left.Type) && IsNullConstant(right))
            viewOperation = left;
        else if (plan.IsArrayViewType(right.Type) && IsNullConstant(left))
            viewOperation = right;
        if (viewOperation is null)
        {
            result = null!;
            return false;
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var view = Materialize(LowerOperation(viewOperation), prefix);
        var code = $"({view.Code}).is_null";
        if (binary.OperatorKind == BinaryOperatorKind.NotEquals)
            code = $"!({code})";
        result = new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                binary.Type,
                CudaEffectIr.Read,
                false,
                binary.Syntax.GetLocation()));
        return true;
    }

    private IOperation UnwrapViewReferenceConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion &&
            plan.IsArrayViewType(conversion.Operand.Type))
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    private CudaExpressionIr LowerShortCircuit(IBinaryOperation binary)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var left = Materialize(LowerOperation(binary.LeftOperand), prefix);
        var resultName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            "bool",
            resultName,
            null,
            false,
            binary.Syntax.GetLocation()));
        var right = LowerOperation(binary.RightOperand);
        var rightStatements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        rightStatements.AddRange(right.Prefix);
        rightStatements.Add(CreateAssignmentStatement(resultName, right.Value, binary.Syntax.GetLocation()));
        var fallback = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ? "false" : "true";
        var fallbackStatement = CreateAssignmentStatement(
            resultName,
            new CudaValueIr(
                fallback,
                binary.Type,
                CudaEffectIr.None,
                true,
                binary.Syntax.GetLocation()),
            binary.Syntax.GetLocation());
        var condition = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
            ? left
            : left with { Code = $"!({left.Code})" };
        prefix.Add(new CudaIfStatementIr(
            ValueExpression(condition),
            new CudaStatementGroupIr(rightStatements.ToImmutable(), binary.Syntax.GetLocation()),
            fallbackStatement,
            binary.Syntax.GetLocation()));
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                resultName,
                binary.Type,
                CudaEffectIr.None,
                true,
                binary.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerUnary(IUnaryOperation unary)
    {
        if (unary.IsChecked)
        {
            ReportUnsupported(unary.Syntax);
            return InvalidExpression(unary.Syntax.GetLocation());
        }
        if (TryGetUserDefinedCall(unary.OperatorMethod, out var unaryTarget, out var unaryCall))
        {
            return LowerUserDefinedCall(
                unaryTarget,
                unaryCall,
                [unary.Operand],
                unary.Type,
                unary.Syntax.GetLocation());
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var operand = Materialize(LowerOperation(unary.Operand), prefix);
        if (!TryPlanUnary(unary, out var helper))
        {
            ReportUnsupported(unary.Syntax);
            return InvalidExpression(unary.Syntax.GetLocation());
        }
        string code;
        if (helper is not null)
            code = $"{helper}({operand.Code})";
        else
        {
            code = $"({GetUnaryOperator(unary.OperatorKind)}({operand.Code}))";
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                unary.Type,
                CudaEffectIr.None,
                false,
                unary.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerSimpleAssignment(ISimpleAssignmentOperation assignment)
    {
        if (assignment.IsRef &&
            assignment.Target is ILocalReferenceOperation { Local.RefKind: not RefKind.None } local)
        {
            var reference = LowerReferenceOperation(assignment.Value);
            var name = plan.GetIdentifier(local.Local);
            return new CudaExpressionIr(
                reference.Prefix,
                new CudaValueIr(
                    $"*({name} = {reference.Value.Code})",
                    assignment.Type,
                    CudaEffectIr.Write,
                    false,
                    assignment.Syntax.GetLocation()));
        }
        if (assignment.Target is IDiscardOperation)
            return LowerOperation(assignment.Value);

        if (assignment.Target is IPropertyReferenceOperation property &&
            !plan.TryGetProperty(property.Property, out _) &&
            property.Property.SetMethod is { } setter &&
            plan.GetCallPlan(plan.ResolveMethod(setter, function)) is
            { Kind: CudaCallKind.PlannedFunction } setterCall)
        {
            return LowerPropertySetter(property, assignment.Value, setter, setterCall);
        }

        var place = LowerPlace(assignment.Target);
        RejectWrite(place, assignment.Target);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var address = MaterializeAddress(place, prefix);
        var value = Materialize(LowerOperation(assignment.Value), prefix);
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"(*({address.Code}) = {value.Code})",
                assignment.Type,
                CudaEffectIr.Write,
                false,
                assignment.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerTuple(ITupleOperation tuple)
    {
        if (!plan.IsTupleType(tuple.Type))
            return UnsupportedExpression(tuple);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var elements = tuple.Elements
            .Select(element => Materialize(LowerOperation(element), prefix).Code)
            .ToArray();
        var typeName = FormatType(tuple.Type!, false, tuple.Syntax.GetLocation());
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"{typeName}{{{string.Join(", ", elements)}}}",
                tuple.Type,
                CudaEffectIr.None,
                false,
                tuple.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerTupleBinary(ITupleBinaryOperation tuple)
    {
        if (tuple.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals) ||
            tuple.LeftOperand.Type is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            return UnsupportedExpression(tuple);
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var left = LowerOperation(tuple.LeftOperand);
        prefix.AddRange(left.Prefix);
        var leftName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(tuple.LeftOperand.Type!, false, tuple.LeftOperand.Syntax.GetLocation()),
            leftName,
            left.Value,
            false,
            tuple.LeftOperand.Syntax.GetLocation()));
        var right = LowerOperation(tuple.RightOperand);
        prefix.AddRange(right.Prefix);
        var rightName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(tuple.RightOperand.Type!, false, tuple.RightOperand.Syntax.GetLocation()),
            rightName,
            right.Value,
            false,
            tuple.RightOperand.Syntax.GetLocation()));
        var equality = BuildTupleEquality(
            tupleType,
            leftName,
            rightName,
            useEqualsSemantics: false);
        if (equality is null)
            return UnsupportedExpression(tuple);
        if (tuple.OperatorKind == BinaryOperatorKind.NotEquals)
            equality = $"!({equality})";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                equality,
                tuple.Type,
                CudaEffectIr.None,
                false,
                tuple.Syntax.GetLocation()));
    }

    private string? BuildTupleEquality(
        INamedTypeSymbol tuple,
        string left,
        string right,
        bool useEqualsSemantics)
    {
        var comparisons = new string[tuple.TupleElements.Length];
        for (var index = 0; index < comparisons.Length; index++)
        {
            var elementType = tuple.TupleElements[index].Type;
            var leftElement = $"({left}).item{index + 1}";
            var rightElement = $"({right}).item{index + 1}";
            var comparison = BuildValueEquality(
                elementType,
                leftElement,
                rightElement,
                useEqualsSemantics);
            if (comparison is null)
                return null;
            comparisons[index] = comparison;
        }
        return $"({string.Join(" && ", comparisons)})";
    }

    private CudaExpressionIr LowerRecordEquality(
        IOperation leftOperation,
        IOperation rightOperation,
        bool negate,
        ITypeSymbol? resultType,
        Location location)
    {
        if (leftOperation.Type is not INamedTypeSymbol recordType ||
            !plan.TryGetStruct(recordType, out var record) ||
            !record.IsPositionalRecord)
        {
            return UnsupportedExpression(leftOperation);
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var left = LowerOperation(leftOperation);
        prefix.AddRange(left.Prefix);
        var leftName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(recordType, false, leftOperation.Syntax.GetLocation()),
            leftName,
            left.Value,
            false,
            leftOperation.Syntax.GetLocation()));
        var right = LowerOperation(rightOperation);
        prefix.AddRange(right.Prefix);
        var rightName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(recordType, false, rightOperation.Syntax.GetLocation()),
            rightName,
            right.Value,
            false,
            rightOperation.Syntax.GetLocation()));
        var equality = BuildRecordEquality(record, leftName, rightName);
        if (equality is null)
            return UnsupportedExpression(leftOperation);
        if (negate)
            equality = $"!({equality})";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                equality,
                resultType,
                CudaEffectIr.None,
                false,
                location));
    }

    private string? BuildRecordEquality(CudaStructPlan record, string left, string right)
    {
        var comparisons = new List<string>(record.Fields.Count + record.Properties.Count);
        foreach (var field in record.Fields)
        {
            var comparison = BuildValueEquality(
                field.Symbol.Type,
                $"({left}).{plan.GetIdentifier(field.Symbol)}",
                $"({right}).{plan.GetIdentifier(field.Symbol)}",
                useEqualsSemantics: true);
            if (comparison is null)
                return null;
            comparisons.Add(comparison);
        }
        foreach (var property in record.Properties)
        {
            var name = plan.GetIdentifier(property.Symbol);
            var comparison = BuildValueEquality(
                property.Symbol.Type,
                $"({left}).{name}",
                $"({right}).{name}",
                useEqualsSemantics: true);
            if (comparison is null)
                return null;
            comparisons.Add(comparison);
        }
        return comparisons.Count == 0
            ? "true"
            : $"({string.Join(" && ", comparisons)})";
    }

    private string? BuildValueEquality(
        ITypeSymbol type,
        string left,
        string right,
        bool useEqualsSemantics)
    {
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
            return BuildTupleEquality(tuple, left, right, useEqualsSemantics);
        if (type is INamedTypeSymbol named &&
            plan.TryGetStruct(named, out var record) &&
            record.IsPositionalRecord)
        {
            return BuildRecordEquality(record, left, right);
        }
        if (type.SpecialType is SpecialType.System_Boolean or
            SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Char or SpecialType.System_Int32 or
            SpecialType.System_UInt32 or SpecialType.System_Int64 or
            SpecialType.System_UInt64 ||
            type.TypeKind == TypeKind.Enum ||
            type is IPointerTypeSymbol)
        {
            return $"(({left}) == ({right}))";
        }
        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double)
        {
            return useEqualsSemantics
                ? $"((({left}) == ({right})) || (isnan({left}) && isnan({right})))"
                : $"(({left}) == ({right}))";
        }
        return null;
    }

    private CudaExpressionIr LowerRecordDeconstruct(
        IInvocationOperation invocation,
        IMethodSymbol target)
    {
        if (invocation.Instance?.Type is not INamedTypeSymbol recordType ||
            !plan.TryGetStruct(recordType, out var record) ||
            !record.IsPositionalRecord)
        {
            return UnsupportedExpression(invocation);
        }
        var components = record.Properties
            .Where(static property => property.Declaration is ParameterSyntax)
            .ToArray();
        if (components.Length != target.Parameters.Length ||
            invocation.Arguments.Length != components.Length)
        {
            return UnsupportedExpression(invocation);
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var instance = LowerOperation(invocation.Instance);
        prefix.AddRange(instance.Prefix);
        var instanceName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(recordType, false, invocation.Instance.Syntax.GetLocation()),
            instanceName,
            instance.Value,
            false,
            invocation.Instance.Syntax.GetLocation()));
        foreach (var argument in invocation.Arguments.OrderBy(static item => item.Syntax.SpanStart))
        {
            if (argument.Parameter is null)
                continue;
            var place = LowerPlace(argument.Value);
            RejectWrite(place, argument.Value);
            prefix.AddRange(place.Prefix);
            var component = components[argument.Parameter.Ordinal];
            prefix.Add(CreateAssignmentStatement(
                place.AccessCode,
                new CudaValueIr(
                    $"({instanceName}).{plan.GetIdentifier(component.Symbol)}",
                    component.Symbol.Type,
                    CudaEffectIr.Read,
                    false,
                    argument.Syntax.GetLocation()),
                argument.Syntax.GetLocation()));
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                "((void)0)",
                invocation.Type,
                CudaEffectIr.None,
                true,
                invocation.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerDeconstruction(IDeconstructionAssignmentOperation assignment)
    {
        var isRecord = assignment.Value.Type is INamedTypeSymbol recordType &&
            plan.TryGetStruct(recordType, out var record) &&
            record.IsPositionalRecord;
        if (!plan.IsTupleType(assignment.Value.Type) && !isRecord)
            return UnsupportedExpression(assignment);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var value = LowerOperation(assignment.Value);
        prefix.AddRange(value.Prefix);
        var valueName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(assignment.Value.Type!, false, assignment.Value.Syntax.GetLocation()),
            valueName,
            value.Value,
            false,
            assignment.Value.Syntax.GetLocation()));
        var declarations = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        CollectDeconstructionDeclarations(assignment.Target, declarations);
        if (!LowerDeconstructionTarget(
                assignment.Target,
                valueName,
                assignment.Value.Type!,
                declarations,
                prefix))
        {
            return InvalidExpression(assignment.Syntax.GetLocation());
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                valueName,
                assignment.Type,
                CudaEffectIr.None,
                true,
                assignment.Syntax.GetLocation()));
    }

    private static void CollectDeconstructionDeclarations(
        IOperation operation,
        ISet<ILocalSymbol> declarations,
        bool insideDeclaration = false)
    {
        insideDeclaration |= operation is IDeclarationExpressionOperation;
        if (insideDeclaration && operation is ILocalReferenceOperation local)
            declarations.Add(local.Local);
        foreach (var child in operation.ChildOperations)
            CollectDeconstructionDeclarations(child, declarations, insideDeclaration);
    }

    private bool LowerDeconstructionTarget(
        IOperation target,
        string source,
        ITypeSymbol sourceType,
        ISet<ILocalSymbol> declarations,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        if (target is IDeclarationExpressionOperation declaration)
            return LowerDeconstructionTarget(
                declaration.ChildOperations.Single(),
                source,
                sourceType,
                declarations,
                prefix);
        if (target is IDiscardOperation)
            return true;
        if (target is ITupleOperation tuple &&
            sourceType is INamedTypeSymbol { IsTupleType: true } tupleType &&
            tuple.Elements.Length == tupleType.TupleElements.Length)
        {
            for (var index = 0; index < tuple.Elements.Length; index++)
            {
                if (!LowerDeconstructionTarget(
                        tuple.Elements[index],
                        $"({source}).item{index + 1}",
                        tupleType.TupleElements[index].Type,
                        declarations,
                        prefix))
                {
                    return false;
                }
            }
            return true;
        }
        if (target is ITupleOperation recordTarget &&
            sourceType is INamedTypeSymbol recordType &&
            plan.TryGetStruct(recordType, out var record) &&
            record.IsPositionalRecord)
        {
            var components = record.Properties
                .Where(static property => property.Declaration is ParameterSyntax)
                .ToArray();
            if (recordTarget.Elements.Length != components.Length)
            {
                ReportUnsupported(target.Syntax);
                return false;
            }
            for (var index = 0; index < recordTarget.Elements.Length; index++)
            {
                var component = components[index];
                if (!LowerDeconstructionTarget(
                        recordTarget.Elements[index],
                        $"({source}).{plan.GetIdentifier(component.Symbol)}",
                        component.Symbol.Type,
                        declarations,
                        prefix))
                {
                    return false;
                }
            }
            return true;
        }

        var value = new CudaValueIr(
            source,
            sourceType,
            CudaEffectIr.Read,
            false,
            target.Syntax.GetLocation());
        if (target is ILocalReferenceOperation local && declarations.Contains(local.Local))
        {
            prefix.Add(new CudaVariableDeclarationStatementIr(
                FormatType(local.Type!, false, local.Syntax.GetLocation()),
                plan.GetIdentifier(local.Local),
                value,
                false,
                local.Syntax.GetLocation()));
            return true;
        }
        if (!IsAddressable(target))
        {
            ReportUnsupported(target.Syntax);
            return false;
        }
        var place = LowerPlace(target);
        RejectWrite(place, target);
        prefix.AddRange(place.Prefix);
        prefix.Add(CreateAssignmentStatement(place.AccessCode, value, target.Syntax.GetLocation()));
        return true;
    }

    private CudaExpressionIr LowerPropertyGetter(
        IPropertyReferenceOperation property,
        IMethodSymbol getter,
        CudaCallPlan call)
    {
        if (property.Instance is null)
            return UnsupportedExpression(property);
        var target = plan.ResolveMethod(getter, function);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var arguments = new string?[target.Parameters.Length + 1];
        arguments[0] = LowerInstanceAddress(property.Instance, prefix);
        foreach (var argument in property.Arguments.OrderBy(static item => item.Syntax.SpanStart))
        {
            if (argument.Parameter is null)
                continue;
            arguments[argument.Parameter.Ordinal + 1] = LowerCallArgument(
                argument.Value,
                target.Parameters[argument.Parameter.Ordinal],
                prefix);
        }
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] ??= "csharp2cuda_invalid";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"{call.Name}({string.Join(", ", arguments)})",
                property.Type,
                CudaEffectIr.Call,
                false,
                property.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerPropertySetter(
        IPropertyReferenceOperation property,
        IOperation assignedValue,
        IMethodSymbol setter,
        CudaCallPlan call)
    {
        if (property.Instance is null)
            return UnsupportedExpression(property);
        var target = plan.ResolveMethod(setter, function);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var arguments = new string?[target.Parameters.Length + 1];
        arguments[0] = LowerInstanceAddress(property.Instance, prefix);
        foreach (var argument in property.Arguments.OrderBy(static item => item.Syntax.SpanStart))
        {
            if (argument.Parameter is null)
                continue;
            arguments[argument.Parameter.Ordinal + 1] = LowerCallArgument(
                argument.Value,
                target.Parameters[argument.Parameter.Ordinal],
                prefix);
        }
        var value = Materialize(LowerOperation(assignedValue), prefix);
        arguments[^1] = value.Code;
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] ??= "csharp2cuda_invalid";
        prefix.Add(new CudaExpressionStatementIr(
            ValueExpression(new CudaValueIr(
                $"{call.Name}({string.Join(", ", arguments)})",
                semanticModel.Compilation.GetSpecialType(SpecialType.System_Void),
                CudaEffectIr.Call | CudaEffectIr.Write,
                false,
                property.Syntax.GetLocation())),
            property.Syntax.GetLocation()));
        return new CudaExpressionIr(prefix.ToImmutable(), value);
    }

    private string LowerInstanceAddress(
        IOperation instance,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        if (IsAddressable(instance))
            return MaterializeAddress(LowerPlace(instance), prefix).Code;

        var value = Materialize(LowerOperation(instance), prefix);
        var name = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(value.Type!, false, instance.Syntax.GetLocation()),
            name,
            value,
            false,
            instance.Syntax.GetLocation()));
        return $"&({name})";
    }

    private CudaExpressionIr LowerCompoundAssignment(ICompoundAssignmentOperation assignment)
    {
        if (assignment.IsChecked)
        {
            ReportUnsupported(assignment.Syntax);
            return InvalidExpression(assignment.Syntax.GetLocation());
        }
        if (assignment.Target is IPropertyReferenceOperation property &&
            !IsArrayViewIndexer(property) &&
            !plan.TryGetProperty(property.Property, out _))
        {
            return LowerPropertyCompoundAssignment(property, assignment);
        }

        var place = LowerPlace(assignment.Target);
        RejectWrite(place, assignment.Target);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var address = MaterializeAddress(place, prefix);
        var current = MaterializeValue(
            new CudaValueIr(
                $"*({address.Code})",
                assignment.Target.Type,
                CudaEffectIr.Read,
                false,
                assignment.Target.Syntax.GetLocation()),
            prefix);
        current = ApplyUserDefinedCompoundConversion(
            current,
            assignment.InConversion,
            prefix,
            assignment.Syntax.GetLocation());
        var value = Materialize(LowerOperation(assignment.Value), prefix);
        if (TryGetUserDefinedCall(
                assignment.OperatorMethod,
                out var compoundTarget,
                out var compoundCall))
        {
            var operatorResult = MaterializeValue(new CudaValueIr(
                $"{compoundCall.Name}({current.Code}, {value.Code})",
                compoundTarget.ReturnType,
                CudaEffectIr.Call,
                false,
                assignment.Syntax.GetLocation()), prefix);
            operatorResult = ApplyUserDefinedCompoundConversion(
                operatorResult,
                assignment.OutConversion,
                prefix,
                assignment.Syntax.GetLocation());
            return new CudaExpressionIr(
                prefix.ToImmutable(),
                new CudaValueIr(
                    $"(*({address.Code}) = {operatorResult.Code})",
                    assignment.Type,
                    CudaEffectIr.Write,
                    false,
                    assignment.Syntax.GetLocation()));
        }
        if (!TryPlanCompound(assignment, out var helper))
        {
            ReportUnsupported(assignment.Syntax);
            return InvalidExpression(assignment.Syntax.GetLocation());
        }
        string result;
        if (helper is not null)
            result = $"{helper}({current.Code}, {value.Code})";
        else
        {
            result = $"(({current.Code}) {GetBinaryOperator(assignment.OperatorKind)} ({value.Code}))";
        }
        result = ApplyCompoundResultConversion(result, assignment.Target.Type);
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"(*({address.Code}) = {result})",
                assignment.Type,
                CudaEffectIr.Read | CudaEffectIr.Write,
                false,
                assignment.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerIncrement(IIncrementOrDecrementOperation increment)
    {
        if (increment.IsChecked)
        {
            ReportUnsupported(increment.Syntax);
            return InvalidExpression(increment.Syntax.GetLocation());
        }
        if (increment.Target is IPropertyReferenceOperation property &&
            !IsArrayViewIndexer(property) &&
            !plan.TryGetProperty(property.Property, out _))
        {
            return LowerPropertyIncrement(property, increment);
        }

        var place = LowerPlace(increment.Target);
        RejectWrite(place, increment.Target);
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var address = MaterializeAddress(place, prefix);
        if (TryGetUserDefinedCall(
                increment.OperatorMethod,
                out var incrementTarget,
                out var incrementCall))
        {
            var current = MaterializeValue(new CudaValueIr(
                $"*({address.Code})",
                increment.Target.Type,
                CudaEffectIr.Read,
                false,
                increment.Target.Syntax.GetLocation()), prefix);
            var updated = MaterializeValue(new CudaValueIr(
                $"{incrementCall.Name}({current.Code})",
                incrementTarget.ReturnType,
                CudaEffectIr.Call,
                false,
                increment.Syntax.GetLocation()), prefix);
            prefix.Add(CreateAssignmentStatement(
                $"*({address.Code})",
                updated,
                increment.Syntax.GetLocation()));
            return new CudaExpressionIr(
                prefix.ToImmutable(),
                increment.IsPostfix ? current : updated);
        }
        if (!TryPlanIncrement(increment, out var helper))
        {
            ReportUnsupported(increment.Syntax);
            return InvalidExpression(increment.Syntax.GetLocation());
        }
        var code = helper is not null
            ? $"{helper}(*({address.Code}))"
            : increment.IsPostfix
                ? $"(*({address.Code})){(increment.Kind == OperationKind.Increment ? "++" : "--")}"
                : $"{(increment.Kind == OperationKind.Increment ? "++" : "--")}(*({address.Code}))";
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                code,
                increment.Type,
                CudaEffectIr.Read | CudaEffectIr.Write,
                false,
                increment.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerPropertyCompoundAssignment(
        IPropertyReferenceOperation property,
        ICompoundAssignmentOperation assignment)
    {
        if (!TryPreparePropertyMutation(
                property,
                out var getterCall,
                out var setterCall,
                out var getterArguments,
                out var setterArguments,
                out var prefix))
        {
            return UnsupportedExpression(assignment);
        }

        var current = Materialize(
            ValueExpression(new CudaValueIr(
                $"{getterCall.Name}({string.Join(", ", getterArguments)})",
                property.Type,
                CudaEffectIr.Call | CudaEffectIr.Read,
                false,
                property.Syntax.GetLocation())),
            prefix);
        current = ApplyUserDefinedCompoundConversion(
            current,
            assignment.InConversion,
            prefix,
            assignment.Syntax.GetLocation());
        var value = Materialize(LowerOperation(assignment.Value), prefix);
        if (TryGetUserDefinedCall(
                assignment.OperatorMethod,
                out var compoundTarget,
                out var compoundCall))
        {
            var updated = MaterializeValue(new CudaValueIr(
                $"{compoundCall.Name}({current.Code}, {value.Code})",
                compoundTarget.ReturnType,
                CudaEffectIr.Call,
                false,
                assignment.Syntax.GetLocation()), prefix);
            updated = ApplyUserDefinedCompoundConversion(
                updated,
                assignment.OutConversion,
                prefix,
                assignment.Syntax.GetLocation());
            setterArguments.Add(updated.Code);
            prefix.Add(CreateCallStatement(
                setterCall,
                setterArguments,
                property.Syntax.GetLocation()));
            return new CudaExpressionIr(prefix.ToImmutable(), updated);
        }
        if (!TryPlanCompound(assignment, out var helper))
        {
            ReportUnsupported(assignment.Syntax);
            return InvalidExpression(assignment.Syntax.GetLocation());
        }
        var resultCode = helper is not null
            ? $"{helper}({current.Code}, {value.Code})"
            : $"(({current.Code}) {GetBinaryOperator(assignment.OperatorKind)} ({value.Code}))";
        resultCode = ApplyCompoundResultConversion(resultCode, assignment.Target.Type);
        var result = MaterializeValue(new CudaValueIr(
            resultCode,
            assignment.Type,
            CudaEffectIr.None,
            false,
            assignment.Syntax.GetLocation()), prefix);
        setterArguments.Add(result.Code);
        prefix.Add(CreateCallStatement(
            setterCall,
            setterArguments,
            property.Syntax.GetLocation()));
        return new CudaExpressionIr(prefix.ToImmutable(), result);
    }

    private CudaExpressionIr LowerPropertyIncrement(
        IPropertyReferenceOperation property,
        IIncrementOrDecrementOperation increment)
    {
        if (!TryPreparePropertyMutation(
                property,
                out var getterCall,
                out var setterCall,
                out var getterArguments,
                out var setterArguments,
                out var prefix))
        {
            ReportUnsupported(increment.Syntax);
            return InvalidExpression(increment.Syntax.GetLocation());
        }

        var currentName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(property.Type!, false, property.Syntax.GetLocation()),
            currentName,
            new CudaValueIr(
                $"{getterCall.Name}({string.Join(", ", getterArguments)})",
                property.Type,
                CudaEffectIr.Call | CudaEffectIr.Read,
                false,
                property.Syntax.GetLocation()),
            false,
            property.Syntax.GetLocation()));
        if (TryGetUserDefinedCall(
                increment.OperatorMethod,
                out var incrementTarget,
                out var incrementCall))
        {
            var updated = MaterializeValue(new CudaValueIr(
                $"{incrementCall.Name}({currentName})",
                incrementTarget.ReturnType,
                CudaEffectIr.Call,
                false,
                increment.Syntax.GetLocation()), prefix);
            setterArguments.Add(updated.Code);
            prefix.Add(CreateCallStatement(
                setterCall,
                setterArguments,
                property.Syntax.GetLocation()));
            return new CudaExpressionIr(
                prefix.ToImmutable(),
                increment.IsPostfix
                    ? ReferenceExpression(
                        currentName,
                        property.Type,
                        increment.Syntax.GetLocation()).Value
                    : updated);
        }
        if (!TryPlanIncrement(increment, out var helper))
        {
            ReportUnsupported(increment.Syntax);
            return InvalidExpression(increment.Syntax.GetLocation());
        }
        var resultCode = helper is not null
            ? $"{helper}({currentName})"
            : increment.IsPostfix
                ? $"({currentName}){(increment.Kind == OperationKind.Increment ? "++" : "--")}"
                : $"{(increment.Kind == OperationKind.Increment ? "++" : "--")}({currentName})";
        var result = MaterializeValue(new CudaValueIr(
            resultCode,
            increment.Type,
            CudaEffectIr.Read | CudaEffectIr.Write,
            false,
            increment.Syntax.GetLocation()), prefix);
        setterArguments.Add(currentName);
        prefix.Add(CreateCallStatement(
            setterCall,
            setterArguments,
            property.Syntax.GetLocation()));
        return new CudaExpressionIr(prefix.ToImmutable(), result);
    }

    private bool TryGetUserDefinedCall(
        IMethodSymbol? method,
        out IMethodSymbol target,
        out CudaCallPlan call)
    {
        target = null!;
        call = null!;
        if (method is null || plan.IsCudaInt32Type(method.ContainingType))
            return false;
        target = plan.ResolveMethod(method, function);
        if (plan.GetCallPlan(target) is not { Kind: CudaCallKind.PlannedFunction } planned)
            return false;
        call = planned;
        return true;
    }

    private CudaValueIr ApplyUserDefinedCompoundConversion(
        CudaValueIr value,
        CommonConversion conversion,
        ImmutableArray<CudaStatementIr>.Builder prefix,
        Location location)
    {
        if (!conversion.IsUserDefined ||
            !TryGetUserDefinedCall(
                conversion.MethodSymbol,
                out var conversionTarget,
                out var conversionCall))
        {
            return value;
        }
        return MaterializeValue(new CudaValueIr(
            $"{conversionCall.Name}({value.Code})",
            conversionTarget.ReturnType,
            CudaEffectIr.Call,
            false,
            location), prefix);
    }

    private CudaExpressionIr LowerUserDefinedCall(
        IMethodSymbol target,
        CudaCallPlan call,
        IReadOnlyList<IOperation> operands,
        ITypeSymbol? resultType,
        Location location)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var arguments = new string[target.Parameters.Length];
        if (operands.Count != arguments.Length)
        {
            ReportUnsupported(operands[0].Syntax);
            return InvalidExpression(location);
        }
        for (var index = 0; index < operands.Count; index++)
        {
            arguments[index] = target.Parameters[index].RefKind == RefKind.None
                ? Materialize(LowerOperation(operands[index]), prefix).Code
                : LowerCallArgument(operands[index], target.Parameters[index], prefix);
        }
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                $"{call.Name}({string.Join(", ", arguments)})",
                ResolveType(resultType),
                plan.IsPureCall(target) ? CudaEffectIr.None : CudaEffectIr.Call,
                false,
                location));
    }

    private bool TryPreparePropertyMutation(
        IPropertyReferenceOperation property,
        out CudaCallPlan getterCall,
        out CudaCallPlan setterCall,
        out List<string> getterArguments,
        out List<string> setterArguments,
        out ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        getterCall = null!;
        setterCall = null!;
        getterArguments = [];
        setterArguments = [];
        prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        if (property.Instance is null ||
            property.Property.GetMethod is not { } getter ||
            property.Property.SetMethod is not { } setter)
        {
            return false;
        }

        var targetGetter = plan.ResolveMethod(getter, function);
        var targetSetter = plan.ResolveMethod(setter, function);
        if (plan.GetCallPlan(targetGetter) is not
            { Kind: CudaCallKind.PlannedFunction } resolvedGetter ||
            plan.GetCallPlan(targetSetter) is not
            { Kind: CudaCallKind.PlannedFunction } resolvedSetter)
        {
            return false;
        }

        getterCall = resolvedGetter;
        setterCall = resolvedSetter;
        var receiver = LowerInstanceAddress(property.Instance, prefix);
        var indexes = new string?[targetGetter.Parameters.Length];
        foreach (var argument in property.Arguments.OrderBy(static item => item.Syntax.SpanStart))
        {
            if (argument.Parameter is null)
                continue;
            indexes[argument.Parameter.Ordinal] = LowerCallArgument(
                argument.Value,
                targetGetter.Parameters[argument.Parameter.Ordinal],
                prefix);
        }
        getterArguments.Add(receiver);
        setterArguments.Add(receiver);
        for (var index = 0; index < indexes.Length; index++)
        {
            var value = indexes[index] ?? "csharp2cuda_invalid";
            getterArguments.Add(value);
            setterArguments.Add(value);
        }
        return true;
    }

    private static CudaExpressionStatementIr CreateCallStatement(
        CudaCallPlan call,
        IEnumerable<string> arguments,
        Location location) => new(
            ValueExpression(new CudaValueIr(
                $"{call.Name}({string.Join(", ", arguments)})",
                null,
                CudaEffectIr.Call | CudaEffectIr.Write,
                false,
                location)),
            location);

    private CudaExpressionIr LowerConditional(IConditionalOperation conditional)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var condition = Materialize(LowerOperation(conditional.Condition), prefix);
        if (conditional.Type is null)
        {
            ReportUnsupported(conditional.Syntax);
            return InvalidExpression(conditional.Syntax.GetLocation());
        }
        var resultName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(conditional.Type, false, conditional.Syntax.GetLocation()),
            resultName,
            null,
            false,
            conditional.Syntax.GetLocation()));
        var whenTrue = CreateConditionalAssignment(
            resultName,
            LowerOperation(conditional.WhenTrue),
            conditional.Syntax.GetLocation());
        var whenFalse = CreateConditionalAssignment(
            resultName,
            LowerOperation(conditional.WhenFalse!),
            conditional.Syntax.GetLocation());
        prefix.Add(new CudaIfStatementIr(
            ValueExpression(condition),
            whenTrue,
            whenFalse,
            conditional.Syntax.GetLocation()));
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                resultName,
                conditional.Type,
                CudaEffectIr.None,
                true,
                conditional.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerSwitchExpression(ISwitchExpressionOperation selection)
    {
        if (selection.Type is null)
            return UnsupportedExpression(selection);

        if (selection.Arms.Any(static arm =>
                arm.Guard is not null ||
                arm.Pattern is not (IDiscardPatternOperation or IConstantPatternOperation)))
        {
            return LowerPatternSwitchExpression(selection);
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var value = Materialize(LowerOperation(selection.Value), prefix);
        var resultName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(selection.Type, false, selection.Syntax.GetLocation()),
            resultName,
            null,
            false,
            selection.Syntax.GetLocation()));

        var sections = ImmutableArray.CreateBuilder<CudaSwitchSectionIr>();
        var hasDefault = false;
        foreach (var arm in selection.Arms)
        {
            if (arm.Guard is not null)
            {
                ReportUnsupported(arm.Guard.Syntax);
                continue;
            }

            string? label;
            if (arm.Pattern is IDiscardPatternOperation)
            {
                label = null;
                hasDefault = true;
            }
            else if (arm.Pattern is IConstantPatternOperation constant &&
                constant.Value.ConstantValue.HasValue)
            {
                label = FormatConstant(
                    constant.Value.ConstantValue.Value,
                    constant.Value.Type);
            }
            else
            {
                ReportUnsupported(arm.Pattern.Syntax);
                continue;
            }

            var lowered = LowerOperation(arm.Value);
            var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
            statements.AddRange(lowered.Prefix);
            statements.Add(CreateAssignmentStatement(
                resultName,
                lowered.Value,
                arm.Value.Syntax.GetLocation()));
            statements.Add(new CudaBreakStatementIr(arm.Value.Syntax.GetLocation()));
            sections.Add(new CudaSwitchSectionIr(
                [label],
                statements.ToImmutable(),
                arm.Syntax.GetLocation()));
        }

        if (!hasDefault)
        {
            sections.Add(new CudaSwitchSectionIr(
                [null],
                [new CudaTrapStatementIr(selection.Syntax.GetLocation())],
                selection.Syntax.GetLocation()));
        }
        prefix.Add(new CudaSwitchStatementIr(
            ValueExpression(value),
            sections.ToImmutable(),
            selection.Syntax.GetLocation()));
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                resultName,
                selection.Type,
                CudaEffectIr.None,
                true,
                selection.Syntax.GetLocation()));
    }

    private CudaExpressionIr LowerIsPattern(IIsPatternOperation operation)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var input = LowerOperation(operation.Value);
        prefix.AddRange(input.Prefix);
        var inputName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(operation.Value.Type!, false, operation.Value.Syntax.GetLocation()),
            inputName,
            input.Value,
            false,
            operation.Value.Syntax.GetLocation()));
        var inputValue = new CudaValueIr(
            inputName,
            operation.Value.Type,
            CudaEffectIr.Read,
            true,
            operation.Value.Syntax.GetLocation());
        if (!TryLowerPattern(operation.Pattern, inputValue, out var condition, out var bindings))
            return InvalidExpression(operation.Syntax.GetLocation());
        prefix.AddRange(condition.Prefix);
        prefix.AddRange(bindings);
        return new CudaExpressionIr(prefix.ToImmutable(), condition.Value);
    }

    private CudaStatementIr LowerPatternSwitch(
        SwitchStatementSyntax syntax,
        ISwitchOperation operation)
    {
        var breakLabel = $"csharp2cuda_switch_break_{labelIndex++}";
        patternSwitchBreakLabels.Add(syntax, breakLabel);
        var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var input = LowerOperation(operation.Value);
        statements.AddRange(input.Prefix);
        var inputName = NewTemporaryName();
        statements.Add(new CudaVariableDeclarationStatementIr(
            FormatType(operation.Value.Type!, false, syntax.Expression.GetLocation()),
            inputName,
            input.Value,
            false,
            syntax.Expression.GetLocation()));
        var boolType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
        var matchedName = NewTemporaryName();
        statements.Add(new CudaVariableDeclarationStatementIr(
            "bool",
            matchedName,
            new CudaValueIr("false", boolType, CudaEffectIr.None, true, syntax.GetLocation()),
            false,
            syntax.GetLocation()));
        var inputValue = new CudaValueIr(
            inputName,
            operation.Value.Type,
            CudaEffectIr.Read,
            true,
            syntax.Expression.GetLocation());
        var deferredDefaultArms = ImmutableArray.CreateBuilder<CudaStatementIr>();

        for (var sectionIndex = 0; sectionIndex < operation.Cases.Length; sectionIndex++)
        {
            var switchCase = operation.Cases[sectionIndex];
            var sectionSyntax = syntax.Sections[sectionIndex];
            foreach (var clause in switchCase.Clauses)
            {
                if (!TryLowerCaseClause(
                        clause,
                        inputValue,
                        out var condition,
                        out var bindings,
                        out var guard))
                {
                    continue;
                }

                var body = sectionSyntax.Statements
                    .Select(LowerStatement)
                    .ToImmutableArray();
                var arm = CreatePatternArm(
                    matchedName,
                    condition,
                    bindings,
                    guard,
                    body,
                    clause.Syntax.GetLocation());
                if (clause is IDefaultCaseClauseOperation)
                    deferredDefaultArms.Add(arm);
                else
                    statements.Add(arm);
            }
        }
        statements.AddRange(deferredDefaultArms);
        patternSwitchBreakLabels.Remove(syntax);
        statements.Add(new CudaLabelStatementIr(breakLabel, syntax.GetLocation()));
        return new CudaStatementGroupIr(statements.ToImmutable(), syntax.GetLocation());
    }

    private CudaStatementIr LowerBreak(BreakStatementSyntax syntax)
    {
        var target = syntax.Ancestors().FirstOrDefault(static ancestor => ancestor is
            SwitchStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
            WhileStatementSyntax or DoStatementSyntax);
        return target is SwitchStatementSyntax selection &&
            patternSwitchBreakLabels.TryGetValue(selection, out var label)
                ? new CudaGotoStatementIr(label, syntax.GetLocation())
                : new CudaBreakStatementIr(syntax.GetLocation());
    }

    private CudaExpressionIr LowerPatternSwitchExpression(ISwitchExpressionOperation selection)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var input = LowerOperation(selection.Value);
        prefix.AddRange(input.Prefix);
        var inputName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(selection.Value.Type!, false, selection.Value.Syntax.GetLocation()),
            inputName,
            input.Value,
            false,
            selection.Value.Syntax.GetLocation()));
        var resultName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(selection.Type!, false, selection.Syntax.GetLocation()),
            resultName,
            null,
            false,
            selection.Syntax.GetLocation()));
        var boolType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
        var matchedName = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            "bool",
            matchedName,
            new CudaValueIr("false", boolType, CudaEffectIr.None, true, selection.Syntax.GetLocation()),
            false,
            selection.Syntax.GetLocation()));
        var inputValue = new CudaValueIr(
            inputName,
            selection.Value.Type,
            CudaEffectIr.Read,
            true,
            selection.Value.Syntax.GetLocation());

        foreach (var arm in selection.Arms)
        {
            if (!TryLowerPattern(arm.Pattern, inputValue, out var condition, out var bindings))
                continue;
            var value = LowerOperation(arm.Value);
            var body = ImmutableArray.CreateBuilder<CudaStatementIr>();
            body.AddRange(value.Prefix);
            body.Add(CreateAssignmentStatement(
                resultName,
                value.Value,
                arm.Value.Syntax.GetLocation()));
            prefix.Add(CreatePatternArm(
                matchedName,
                condition,
                bindings,
                arm.Guard is null ? null : LowerOperation(arm.Guard),
                body.ToImmutable(),
                arm.Syntax.GetLocation()));
        }
        prefix.Add(new CudaIfStatementIr(
            ValueExpression(new CudaValueIr(
                $"!({matchedName})",
                boolType,
                CudaEffectIr.Read,
                false,
                selection.Syntax.GetLocation())),
            new CudaTrapStatementIr(selection.Syntax.GetLocation()),
            null,
            selection.Syntax.GetLocation()));
        return new CudaExpressionIr(
            prefix.ToImmutable(),
            new CudaValueIr(
                resultName,
                selection.Type,
                CudaEffectIr.None,
                true,
                selection.Syntax.GetLocation()));
    }

    private bool TryLowerCaseClause(
        ICaseClauseOperation clause,
        CudaValueIr input,
        out CudaExpressionIr condition,
        out ImmutableArray<CudaStatementIr> bindings,
        out CudaExpressionIr? guard)
    {
        guard = null;
        if (clause is IDefaultCaseClauseOperation)
        {
            condition = ValueExpression(new CudaValueIr(
                "true",
                semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean),
                CudaEffectIr.None,
                true,
                clause.Syntax.GetLocation()));
            bindings = [];
            return true;
        }
        if (clause is IPatternCaseClauseOperation pattern)
        {
            if (!TryLowerPattern(pattern.Pattern, input, out condition, out bindings))
                return false;
            guard = pattern.Guard is null ? null : LowerOperation(pattern.Guard);
            return true;
        }
        if (clause is ISingleValueCaseClauseOperation single)
        {
            var constant = LowerOperation(single.Value);
            condition = new CudaExpressionIr(
                constant.Prefix,
                new CudaValueIr(
                    $"(({input.Code}) == ({constant.Value.Code}))",
                    semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean),
                    CudaEffectIr.None,
                    false,
                    clause.Syntax.GetLocation()));
            bindings = [];
            return true;
        }
        ReportUnsupported(clause.Syntax);
        condition = InvalidExpression(clause.Syntax.GetLocation());
        bindings = [];
        return false;
    }

    private CudaStatementIr CreatePatternArm(
        string matchedName,
        CudaExpressionIr condition,
        ImmutableArray<CudaStatementIr> bindings,
        CudaExpressionIr? guard,
        ImmutableArray<CudaStatementIr> body,
        Location location)
    {
        var boolType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
        var armStatements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        armStatements.AddRange(bindings);
        var selected = ImmutableArray.CreateBuilder<CudaStatementIr>();
        selected.Add(CreateAssignmentStatement(
            matchedName,
            new CudaValueIr("true", boolType, CudaEffectIr.None, true, location),
            location));
        selected.AddRange(body);
        if (guard is null)
        {
            armStatements.AddRange(selected);
        }
        else
        {
            armStatements.Add(new CudaIfStatementIr(
                guard,
                new CudaStatementGroupIr(selected.ToImmutable(), location),
                null,
                location));
        }
        return new CudaIfStatementIr(
            condition with
            {
                Value = condition.Value with
                {
                    Code = $"(!({matchedName}) && ({condition.Value.Code}))",
                    IsSimple = false
                }
            },
            new CudaStatementGroupIr(armStatements.ToImmutable(), location),
            null,
            location);
    }

    private bool TryLowerPattern(
        IPatternOperation pattern,
        CudaValueIr input,
        out CudaExpressionIr condition,
        out ImmutableArray<CudaStatementIr> bindings)
    {
        var boolType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
        switch (pattern)
        {
            case IDiscardPatternOperation:
                condition = ValueExpression(new CudaValueIr(
                    "true", boolType, CudaEffectIr.None, true, pattern.Syntax.GetLocation()));
                bindings = [];
                return true;
            case IConstantPatternOperation constant:
                {
                    if (plan.IsArrayViewType(input.Type) && IsNullConstant(constant.Value))
                    {
                        condition = ValueExpression(new CudaValueIr(
                            $"({input.Code}).is_null",
                            boolType,
                            CudaEffectIr.Read,
                            false,
                            pattern.Syntax.GetLocation()));
                        bindings = [];
                        return true;
                    }
                    var value = LowerOperation(constant.Value);
                    condition = new CudaExpressionIr(
                        value.Prefix,
                        new CudaValueIr(
                            $"(({input.Code}) == ({value.Value.Code}))",
                            boolType,
                            CudaEffectIr.None,
                            false,
                            pattern.Syntax.GetLocation()));
                    bindings = [];
                    return true;
                }
            case IRelationalPatternOperation relational:
                {
                    var value = LowerOperation(relational.Value);
                    var operation = relational.OperatorKind switch
                    {
                        BinaryOperatorKind.LessThan => "<",
                        BinaryOperatorKind.LessThanOrEqual => "<=",
                        BinaryOperatorKind.GreaterThan => ">",
                        BinaryOperatorKind.GreaterThanOrEqual => ">=",
                        _ => null
                    };
                    if (operation is null)
                        break;
                    condition = new CudaExpressionIr(
                        value.Prefix,
                        new CudaValueIr(
                            $"(({input.Code}) {operation} ({value.Value.Code}))",
                            boolType,
                            CudaEffectIr.None,
                            false,
                            pattern.Syntax.GetLocation()));
                    bindings = [];
                    return true;
                }
            case IBinaryPatternOperation binary:
                {
                    if (!TryLowerPattern(binary.LeftPattern, input, out var left, out var leftBindings) ||
                        !TryLowerPattern(binary.RightPattern, input, out var right, out var rightBindings) ||
                        binary.OperatorKind == BinaryOperatorKind.Or &&
                        (!leftBindings.IsEmpty || !rightBindings.IsEmpty))
                    {
                        break;
                    }
                    var prefixes = ImmutableArray.CreateBuilder<CudaStatementIr>();
                    prefixes.AddRange(left.Prefix);
                    prefixes.AddRange(right.Prefix);
                    var operation = binary.OperatorKind == BinaryOperatorKind.And ? "&&" : "||";
                    condition = new CudaExpressionIr(
                        prefixes.ToImmutable(),
                        new CudaValueIr(
                            $"(({left.Value.Code}) {operation} ({right.Value.Code}))",
                            boolType,
                            CudaEffectIr.None,
                            false,
                            pattern.Syntax.GetLocation()));
                    bindings = binary.OperatorKind == BinaryOperatorKind.And
                        ? leftBindings.AddRange(rightBindings)
                        : [];
                    return true;
                }
            case INegatedPatternOperation negated:
                if (TryLowerPattern(negated.Pattern, input, out var inner, out var innerBindings) &&
                    innerBindings.IsEmpty)
                {
                    condition = inner with
                    {
                        Value = inner.Value with
                        {
                            Code = $"!({inner.Value.Code})",
                            IsSimple = false
                        }
                    };
                    bindings = [];
                    return true;
                }
                break;
            case IDeclarationPatternOperation declaration
                when declaration.Syntax is VarPatternSyntax &&
                    declaration.DeclaredSymbol is ILocalSymbol local:
                bindings =
                [
                    new CudaVariableDeclarationStatementIr(
                        FormatType(
                            local.Type,
                            false,
                            declaration.Syntax.GetLocation()),
                        plan.GetIdentifier(local),
                        input,
                        false,
                        declaration.Syntax.GetLocation())
                ];
                condition = ValueExpression(new CudaValueIr(
                    "true", boolType, CudaEffectIr.None, true, pattern.Syntax.GetLocation()));
                return true;
        }

        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedPattern,
            pattern.Syntax.GetLocation(),
            pattern.Syntax.ToString()));
        condition = InvalidExpression(pattern.Syntax.GetLocation());
        bindings = [];
        return false;
    }

    private CudaStatementIr CreateConditionalAssignment(
        string target,
        CudaExpressionIr source,
        Location location)
    {
        var statements = ImmutableArray.CreateBuilder<CudaStatementIr>();
        statements.AddRange(source.Prefix);
        statements.Add(CreateAssignmentStatement(target, source.Value, location));
        return new CudaStatementGroupIr(statements.ToImmutable(), location);
    }

    private static CudaExpressionStatementIr CreateAssignmentStatement(
        string target,
        CudaValueIr source,
        Location location) => new(
            ValueExpression(source with
            {
                Code = $"{target} = {source.Code}",
                Effects = CudaEffectIr.Write,
                IsSimple = false
            }),
            location);

    private CudaExpressionIr LowerInstance(IInstanceReferenceOperation instance)
    {
        if (function.IsConstructor)
        {
            return ReferenceExpression(
                "csharp2cuda_constructed",
                function.Symbol.ContainingType,
                instance.Syntax.GetLocation());
        }
        if (!function.HasInstance)
        {
            ReportUnsupported(instance.Syntax);
            return InvalidExpression(instance.Syntax.GetLocation());
        }
        return ValueExpression(new CudaValueIr(
            "*(csharp2cuda_this)",
            function.Symbol.ContainingType,
            CudaEffectIr.Read,
            true,
            instance.Syntax.GetLocation()));
    }

    private CudaPlaceIr LowerPlace(IOperation operation)
    {
        while (operation is IConversionOperation { Conversion.IsIdentity: true } conversion)
            operation = conversion.Operand;
        if (IsPointerIndirection(operation))
        {
            var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
            var address = Materialize(
                LowerOperation(operation.ChildOperations.Single()),
                prefix);
            return new CudaPlaceIr(
                prefix.ToImmutable(),
                $"*({address.Code})",
                address.Code,
                operation.Type!,
                CudaEffectIr.Read,
                operation.Syntax.GetLocation());
        }
        if (IsPointerElement(operation))
            return LowerPointerElementPlace(operation);
        switch (operation)
        {
            case ILocalReferenceOperation local:
                {
                    var captured = function.TryGetCapture(local.Local, out var capture);
                    var name = captured ? capture.EmittedName : plan.GetIdentifier(local.Local);
                    var indirect = captured
                        ? capture.ByReference
                        : local.Local.RefKind != RefKind.None;
                    var access = indirect ? $"*({name})" : name;
                    return new CudaPlaceIr(
                        [],
                        access,
                        indirect ? name : $"&({name})",
                        local.Type!,
                        CudaEffectIr.Read,
                        local.Syntax.GetLocation());
                }
            case IParameterReferenceOperation parameter:
                {
                    var captured = function.TryGetCapture(parameter.Parameter, out var capture);
                    var name = captured
                        ? capture.EmittedName
                        : plan.GetIdentifier(parameter.Parameter);
                    var indirect = captured
                        ? capture.ByReference
                        : parameter.Parameter.RefKind != RefKind.None;
                    return new CudaPlaceIr(
                        [],
                        indirect ? $"*({name})" : name,
                        indirect ? name : $"&({name})",
                        parameter.Type!,
                        CudaEffectIr.Read,
                        parameter.Syntax.GetLocation());
                }
            case IInstanceReferenceOperation instance when function.HasInstance:
                return new CudaPlaceIr(
                    [],
                    "*(csharp2cuda_this)",
                    "csharp2cuda_this",
                    function.Symbol.ContainingType,
                    CudaEffectIr.Read,
                    instance.Syntax.GetLocation());
            case IInstanceReferenceOperation instance when function.IsConstructor:
                return new CudaPlaceIr(
                    [],
                    "csharp2cuda_constructed",
                    "&(csharp2cuda_constructed)",
                    function.Symbol.ContainingType,
                    CudaEffectIr.Read,
                    instance.Syntax.GetLocation());
            case IFieldReferenceOperation field:
                return LowerFieldPlace(field);
            case IArrayElementReferenceOperation element:
                return LowerElementPlace(element);
            case IImplicitIndexerReferenceOperation indexer when
                indexer.Argument is not IRangeOperation:
                return LowerImplicitIndexerPlace(indexer);
            case IPropertyReferenceOperation property when
                plan.TryGetProperty(property.Property, out var propertyPlan) &&
                property.Instance is not null:
                {
                    var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
                    var instance = Materialize(LowerOperation(property.Instance), prefix);
                    var name = plan.GetIdentifier(propertyPlan.Symbol);
                    var access = $"({instance.Code}).{name}";
                    return new CudaPlaceIr(
                        prefix.ToImmutable(),
                        access,
                        $"&({access})",
                        property.Type!,
                        CudaEffectIr.Read,
                        property.Syntax.GetLocation());
                }
            case IPropertyReferenceOperation property when
                property.Property.IsIndexer &&
                property.Instance is not null &&
                property.Arguments.Length == 1:
                return LowerIndexerPlace(property);
            default:
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.UnsupportedSyntax,
                    operation.Syntax.GetLocation(),
                    operation.Syntax.Kind().ToString()));
                return new CudaPlaceIr(
                    [],
                    "csharp2cuda_invalid",
                    "nullptr",
                    operation.Type ?? compilationIntType(),
                    CudaEffectIr.None,
                    operation.Syntax.GetLocation());
        }

        ITypeSymbol compilationIntType() =>
            semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
    }

    private CudaPlaceIr LowerFieldPlace(IFieldReferenceOperation field)
    {
        if (TryGetTupleFieldName(field.Field, out var tupleFieldName))
        {
            if (field.Instance is null)
            {
                ReportUnsupported(field.Syntax);
                return new CudaPlaceIr(
                    [],
                    "csharp2cuda_invalid",
                    "nullptr",
                    field.Type!,
                    CudaEffectIr.None,
                    field.Syntax.GetLocation());
            }
            var tuplePrefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
            string tupleAccess;
            if (IsAddressable(field.Instance))
            {
                var instance = LowerPlace(field.Instance);
                tuplePrefix.AddRange(instance.Prefix);
                tupleAccess = $"({instance.AccessCode}).{tupleFieldName}";
            }
            else
            {
                var instance = Materialize(LowerOperation(field.Instance), tuplePrefix);
                tupleAccess = $"({instance.Code}).{tupleFieldName}";
            }
            return new CudaPlaceIr(
                tuplePrefix.ToImmutable(),
                tupleAccess,
                $"&({tupleAccess})",
                field.Type!,
                CudaEffectIr.Read,
                field.Syntax.GetLocation());
        }
        if (!plan.TryGetIdentifier(field.Field, out var name))
        {
            ReportUnsupported(field.Syntax);
            name = "csharp2cuda_invalid";
        }
        if (plan.TryGetExplicitFieldLayout(field.Field, out _, out _))
            return LowerExplicitFieldPlace(field);
        var fieldInstance = field.Instance;
        if (fieldInstance is null)
        {
            return new CudaPlaceIr(
                [],
                name,
                $"&({name})",
                field.Type!,
                CudaEffectIr.Read,
                field.Syntax.GetLocation());
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        string access;
        if (IsPointerMemberAccess(field))
        {
            var instance = Materialize(LowerOperation(fieldInstance), prefix);
            access = $"({instance.Code})->{name}";
        }
        else if (IsPointerIndirection(fieldInstance))
        {
            var instance = Materialize(
                LowerOperation(fieldInstance.ChildOperations.Single()),
                prefix);
            access = $"({instance.Code})->{name}";
        }
        else if (IsAddressable(fieldInstance))
        {
            var instance = LowerPlace(fieldInstance);
            prefix.AddRange(instance.Prefix);
            access = $"({instance.AccessCode}).{name}";
        }
        else
        {
            var instance = Materialize(LowerOperation(fieldInstance), prefix);
            access = $"({instance.Code}).{name}";
        }
        return new CudaPlaceIr(
            prefix.ToImmutable(),
            access,
            $"&({access})",
            field.Type!,
            CudaEffectIr.Read,
            field.Syntax.GetLocation());
    }

    private static bool TryGetTupleFieldName(IFieldSymbol field, out string name)
    {
        name = string.Empty;
        if (!field.ContainingType.IsTupleType)
            return false;
        var canonicalName = field.CorrespondingTupleField?.Name ?? field.Name;
        if (!canonicalName.StartsWith("Item", StringComparison.Ordinal) ||
            !int.TryParse(
                canonicalName.AsSpan("Item".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index) ||
            index is < 1 or > 7)
        {
            return false;
        }
        name = $"item{index}";
        return true;
    }

    private CudaPlaceIr LowerExplicitFieldPlace(IFieldReferenceOperation field)
    {
        if (field.Instance is null ||
            !plan.TryGetExplicitFieldLayout(field.Field, out _, out var layout) ||
            !plan.TryGetField(field.Field, out var fieldPlan))
        {
            ReportUnsupported(field.Syntax);
            return new CudaPlaceIr(
                [],
                "csharp2cuda_invalid",
                "nullptr",
                field.Type!,
                CudaEffectIr.None,
                field.Syntax.GetLocation());
        }

        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var instanceAddress = LowerInstanceAddress(field.Instance, prefix);
        var byteAddress =
            $"((unsigned char*)({instanceAddress}) + {layout.Offset.ToString(CultureInfo.InvariantCulture)})";
        if (fieldPlan.InlineArrayLength > 0)
        {
            var elementType = ((IPointerTypeSymbol)fieldPlan.Symbol.Type).PointedAtType;
            var pointerType = FormatType(elementType, false, field.Syntax.GetLocation()) + "*";
            var address = $"(({pointerType})({byteAddress}))";
            return new CudaPlaceIr(
                prefix.ToImmutable(),
                address,
                address,
                field.Type!,
                CudaEffectIr.Read,
                field.Syntax.GetLocation());
        }

        var typeName = FormatType(field.Type!, false, field.Syntax.GetLocation());
        var typedAddress = $"(({typeName}*)({byteAddress}))";
        return new CudaPlaceIr(
            prefix.ToImmutable(),
            $"*({typedAddress})",
            typedAddress,
            field.Type!,
            CudaEffectIr.Read,
            field.Syntax.GetLocation());
    }

    private CudaPlaceIr LowerElementPlace(IArrayElementReferenceOperation element)
    {
        if (element.Indices.Length != 1)
        {
            ReportUnsupported(element.Syntax);
            return new CudaPlaceIr(
                [],
                "csharp2cuda_invalid",
                "nullptr",
                element.Type!,
                CudaEffectIr.None,
                element.Syntax.GetLocation());
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var targetExpression = LowerOperation(element.ArrayReference);
        prefix.AddRange(targetExpression.Prefix);
        var target = targetExpression.Value;
        if (target.Type is IPointerTypeSymbol)
            target = MaterializeValue(target, prefix);
        var index = LowerIndexArgument(element.Indices[0], target, prefix);
        var access = $"({target.Code})[{index.Code}]";
        return new CudaPlaceIr(
            prefix.ToImmutable(),
            access,
            $"&({access})",
            element.Type!,
            CudaEffectIr.Read,
            element.Syntax.GetLocation())
        {
            ViewMutability = target.ViewMutability
        };
    }

    private CudaPlaceIr LowerImplicitIndexerPlace(IImplicitIndexerReferenceOperation indexer)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var target = Materialize(LowerOperation(indexer.Instance), prefix);
        var argument = LowerIndexArgument(indexer.Argument, target, prefix);
        var access = $"({target.Code})[{argument.Code}]";
        return new CudaPlaceIr(
            prefix.ToImmutable(),
            access,
            $"&({access})",
            indexer.Type!,
            CudaEffectIr.Read,
            indexer.Syntax.GetLocation())
        {
            ViewMutability = target.ViewMutability
        };
    }

    private CudaPlaceIr LowerIndexerPlace(IPropertyReferenceOperation property)
    {
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var target = Materialize(LowerOperation(property.Instance!), prefix);
        var index = Materialize(LowerOperation(property.Arguments[0].Value), prefix);
        var access = $"({target.Code})[{index.Code}]";
        return new CudaPlaceIr(
            prefix.ToImmutable(),
            access,
            $"&({access})",
            property.Type!,
            CudaEffectIr.Read,
            property.Syntax.GetLocation())
        {
            ViewMutability = target.ViewMutability
        };
    }

    private bool IsArrayViewIndexer(IPropertyReferenceOperation property) =>
        property.Property.IsIndexer &&
        property.Instance is not null &&
        property.Arguments.Length == 1 &&
        plan.IsArrayViewType(property.Instance.Type);

    private CudaPlaceIr LowerPointerElementPlace(IOperation element)
    {
        var children = element.ChildOperations.ToArray();
        CudaExpressionIr targetExpression;
        CudaExpressionIr indexExpression;
        if (children.Length == 2)
        {
            targetExpression = LowerOperation(children[0]);
            indexExpression = LowerOperation(children[1]);
        }
        else if (element.Syntax is ElementAccessExpressionSyntax syntax &&
            syntax.ArgumentList.Arguments.Count == 1)
        {
            targetExpression = LowerExpression(syntax.Expression);
            indexExpression = LowerExpression(syntax.ArgumentList.Arguments[0].Expression);
        }
        else
        {
            ReportUnsupported(element.Syntax);
            return new CudaPlaceIr(
                [],
                "csharp2cuda_invalid",
                "nullptr",
                semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32),
                CudaEffectIr.None,
                element.Syntax.GetLocation());
        }
        var prefix = ImmutableArray.CreateBuilder<CudaStatementIr>();
        var target = Materialize(targetExpression, prefix);
        var index = Materialize(indexExpression, prefix);
        var access = $"({target.Code})[{index.Code}]";
        var elementType = element.Type ?? semanticModel.GetTypeInfo(element.Syntax).Type ??
            semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        return new CudaPlaceIr(
            prefix.ToImmutable(),
            access,
            $"&({access})",
            elementType,
            CudaEffectIr.Read,
            element.Syntax.GetLocation());
    }

    private CudaValueIr Materialize(
        CudaExpressionIr expression,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        prefix.AddRange(expression.Prefix);
        return MaterializeValue(expression.Value, prefix);
    }

    private CudaValueIr MaterializeValue(
        CudaValueIr value,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        var resolvedType = ResolveType(value.Type);
        if (resolvedType is not null && !SymbolEqualityComparer.Default.Equals(resolvedType, value.Type))
            value = value with { Type = resolvedType };
        if (value.Type is null || value.Type.SpecialType == SpecialType.System_Void)
            return value;
        if (value.Effects == CudaEffectIr.None ||
            (value.IsSimple && value.Effects == CudaEffectIr.Read))
        {
            return value;
        }
        var name = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(
                value.Type,
                value.ViewMutability == CudaViewMutability.ReadOnly,
                value.Location),
            name,
            value,
            false,
            value.Location));
        return new CudaValueIr(
            name,
            value.Type,
            CudaEffectIr.None,
            true,
            value.Location)
        {
            ViewMutability = value.ViewMutability
        };
    }

    private CudaValueIr MaterializeForReuse(
        CudaValueIr value,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        var resolvedType = ResolveType(value.Type);
        if (resolvedType is not null &&
            !SymbolEqualityComparer.Default.Equals(resolvedType, value.Type))
        {
            value = value with { Type = resolvedType };
        }
        if (value.Type is null ||
            value.Type.SpecialType == SpecialType.System_Void ||
            value.IsSimple)
        {
            return value;
        }
        var name = NewTemporaryName();
        prefix.Add(new CudaVariableDeclarationStatementIr(
            FormatType(
                value.Type,
                value.ViewMutability == CudaViewMutability.ReadOnly,
                value.Location),
            name,
            value,
            false,
            value.Location));
        return new CudaValueIr(
            name,
            value.Type,
            CudaEffectIr.None,
            true,
            value.Location)
        {
            ViewMutability = value.ViewMutability
        };
    }

    private CudaValueIr MaterializeAddress(
        CudaPlaceIr place,
        ImmutableArray<CudaStatementIr>.Builder prefix)
    {
        prefix.AddRange(place.Prefix);
        var pointerType = semanticModel.Compilation.CreatePointerTypeSymbol(place.Type);
        var value = new CudaValueIr(
            place.AddressCode,
            pointerType,
            place.Effects,
            false,
            place.Location);
        return MaterializeValue(value, prefix);
    }

    private static CudaValueIr ApplyConversion(CudaValueIr value, string? typeName) =>
        typeName is null
            ? value
            : value with
            {
                Code = $"(({typeName})({value.Code}))",
                IsSimple = false
            };

    private static CudaExpressionIr ValueExpression(CudaValueIr value) => new([], value);

    private static CudaExpressionIr ReferenceExpression(
        string name,
        ITypeSymbol? type,
        Location location,
        CudaViewMutability viewMutability = CudaViewMutability.None) => ValueExpression(
            new CudaValueIr(
                name,
                type,
                CudaEffectIr.Read,
                true,
                location)
            {
                ViewMutability = viewMutability
            });

    private CudaViewMutability GetViewMutability(ISymbol symbol, ITypeSymbol? type)
    {
        if (!plan.IsArrayViewType(type))
            return CudaViewMutability.None;
        if (viewMutabilities.TryGetValue(symbol, out var mutability))
            return mutability;
        if (symbol is IParameterSymbol parameter)
        {
            return plan.IsViewParameterWritable(parameter)
                ? CudaViewMutability.Writable
                : CudaViewMutability.ReadOnly;
        }
        return plan.IsSpanType(type, out var readOnly) && readOnly
            ? CudaViewMutability.ReadOnly
            : CudaViewMutability.Writable;
    }

    private void RejectWrite(CudaPlaceIr place, IOperation target)
    {
        if (target is ILocalReferenceOperation local &&
            (local.Local.RefKind == RefKind.RefReadOnly ||
             function.TryGetCapture(local.Local, out var localCapture) &&
             localCapture.IsReadOnly) ||
            target is IParameterReferenceOperation parameter &&
            (parameter.Parameter.RefKind == RefKind.In ||
             function.TryGetCapture(parameter.Parameter, out var parameterCapture) &&
             parameterCapture.IsReadOnly))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidReferenceEscape,
                target.Syntax.GetLocation(),
                target.Syntax.ToString()));
            return;
        }
        var constant = EnumerateOperationTree(target)
            .OfType<IFieldReferenceOperation>()
            .Select(reference => plan.TryGetConstantArray(reference.Field, out var value)
                ? value
                : null)
            .FirstOrDefault(static value => value is not null);
        if (constant is not null)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ConstantWrite,
                target.Syntax.GetLocation(),
                constant.Symbol.Name));
            return;
        }

        if (target is IFieldReferenceOperation field &&
            plan.TryGetField(field.Field, out var fieldPlan) &&
            fieldPlan.InlineArrayLength > 0)
        {
            ReportUnsupported(target.Syntax);
            return;
        }

        if (place.ViewMutability != CudaViewMutability.ReadOnly)
            return;
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.ViewEscape,
            place.Location,
            place.AccessCode));
    }

    private static IEnumerable<IOperation> EnumerateOperationTree(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in EnumerateOperationTree(child))
                yield return descendant;
        }
    }

    private static IConversionOperation? FindContextualConversionOperation(
        IOperation? root,
        ExpressionSyntax expression)
    {
        if (root is null)
            return null;
        return EnumerateOperationTree(root).OfType<IConversionOperation>()
            .FirstOrDefault(operation =>
            !operation.Conversion.IsIdentity &&
            operation.Operand is not (IObjectCreationOperation or IDefaultValueOperation) &&
            expression is not StackAllocArrayCreationExpressionSyntax &&
            operation.Syntax.RawKind == expression.RawKind &&
            operation.Syntax.SyntaxTree == expression.SyntaxTree &&
            operation.Syntax.Span == expression.Span);
    }

    private CudaStatementIr UnsupportedStatement(StatementSyntax syntax)
    {
        ReportUnsupported(syntax);
        return new CudaEmptyStatementIr(syntax.GetLocation());
    }

    private CudaExpressionIr UnsupportedExpression(IOperation operation)
    {
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedSyntax,
            operation.Syntax.GetLocation(),
            operation.Syntax.Kind().ToString()));
        return InvalidExpression(operation.Syntax.GetLocation());
    }

    private CudaExpressionIr ManagedAllocationExpression(IOperation operation)
    {
        if (operation is IDelegateCreationOperation)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ClosureEscape,
                operation.Syntax.GetLocation(),
                operation.Syntax.ToString()));
            return InvalidExpression(operation.Syntax.GetLocation());
        }
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.ManagedAllocation,
            operation.Syntax.GetLocation(),
            operation.Syntax.Kind().ToString()));
        return InvalidExpression(operation.Syntax.GetLocation());
    }

    private CudaExpressionIr InvalidExpression(Location location) => ValueExpression(
        new CudaValueIr(
            "csharp2cuda_invalid",
            semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32),
            CudaEffectIr.None,
            true,
            location));

    private void ReportUnsupported(SyntaxNode syntax) => diagnostics.Add(Diagnostic.Create(
        CudaDiagnostics.UnsupportedSyntax,
        syntax.GetLocation(),
        syntax.Kind().ToString()));

    private string NewTemporaryName() => $"csharp2cuda_temp_{temporaryIndex++}";

    private void ValidateIntrinsicInvocation(
        IInvocationOperation invocation,
        IMethodSymbol targetMethod,
        CudaCallPlan call)
    {
        if (!plan.IsCudaType(targetMethod.ContainingType))
            return;

        if (targetMethod.Name == nameof(Cuda.SyncWarp) && invocation.Arguments.Length == 1)
        {
            ValidateWarpMask(invocation.Arguments[0].Value);
        }
        else if (targetMethod.Name == nameof(Cuda.ShuffleDownSync) &&
            invocation.Arguments.Length == 4)
        {
            ValidateWarpMask(invocation.Arguments[0].Value);
            ValidateWarpWidth(invocation.Arguments[3].Value);
        }

        if (call.Kind != CudaCallKind.DynamicSharedView)
            return;
        var elementType = targetMethod.TypeArguments[0];
        if (!CudaEmissionPlan.IsStorageElementType(elementType))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStorageType,
                invocation.Syntax.GetLocation(),
                elementType.ToDisplayString(),
                nameof(Cuda.DynamicSharedView)));
            return;
        }

        var storageOperation = invocation.Arguments[0].Value;
        var storageSymbol = storageOperation switch
        {
            ILocalReferenceOperation local => (ISymbol)local.Local,
            IConversionOperation { Operand: ILocalReferenceOperation local } => local.Local,
            _ => null
        };
        var requiredAlignment = CudaEmissionPlan.GetNaturalAlignment(elementType);
        if (storageSymbol is null ||
            !plan.TryGetDynamicSharedStorage(storageSymbol, out var storage) ||
            storage.Alignment < requiredAlignment)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidAlignment,
                storageOperation.Syntax.GetLocation(),
                storageSymbol is not null &&
                    plan.TryGetDynamicSharedStorage(storageSymbol, out var candidate)
                    ? candidate.Alignment.ToString(CultureInfo.InvariantCulture)
                    : "unplanned",
                nameof(Cuda.DynamicSharedView)));
        }
    }

    private void ValidateWarpMask(IOperation operation)
    {
        if (!operation.ConstantValue.HasValue ||
            operation.ConstantValue.Value is not uint value || value == 0u)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidWarpMask,
                operation.Syntax.GetLocation()));
        }
    }

    private void ValidateWarpWidth(IOperation operation)
    {
        if (!operation.ConstantValue.HasValue ||
            operation.ConstantValue.Value is not int value ||
            value is not (1 or 2 or 4 or 8 or 16 or 32))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidWarpWidth,
                operation.Syntax.GetLocation()));
        }
    }

    private bool TryPlanBinary(IBinaryOperation operation, out string? helper)
    {
        helper = null;
        if (operation.OperatorMethod is { } operatorMethod &&
            !plan.IsCudaInt32Type(operatorMethod.ContainingType))
        {
            return false;
        }

        var leftType = ResolveType(operation.LeftOperand.Type);
        var rightType = ResolveType(operation.RightOperand.Type);
        var result = EffectiveType(operation.Type);
        var left = EffectiveType(leftType);
        var right = EffectiveType(rightType);
        var hasPointer = leftType is IPointerTypeSymbol || rightType is IPointerTypeSymbol;
        if (hasPointer)
        {
            if (operation.OperatorKind == BinaryOperatorKind.Add &&
                ResolveType(operation.Type) is IPointerTypeSymbol &&
                (leftType is IPointerTypeSymbol && right == SpecialType.System_Int32 ||
                 rightType is IPointerTypeSymbol && left == SpecialType.System_Int32))
            {
                helper = leftType is IPointerTypeSymbol
                    ? "csharp2cuda_pointer_add"
                    : "csharp2cuda_pointer_add_reverse";
                return true;
            }
            return operation.OperatorKind is BinaryOperatorKind.Equals or
                    BinaryOperatorKind.NotEquals &&
                (leftType is IPointerTypeSymbol || leftType is null) &&
                (rightType is IPointerTypeSymbol || rightType is null);
        }

        if (operation.OperatorKind is BinaryOperatorKind.ConditionalAnd or
            BinaryOperatorKind.ConditionalOr)
        {
            return result == SpecialType.System_Boolean &&
                left == SpecialType.System_Boolean &&
                right == SpecialType.System_Boolean;
        }

        if (operation.OperatorKind is BinaryOperatorKind.Equals or
            BinaryOperatorKind.NotEquals or
            BinaryOperatorKind.LessThan or
            BinaryOperatorKind.LessThanOrEqual or
            BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.GreaterThanOrEqual)
        {
            return left == right &&
                (IsArithmetic(left) || left == SpecialType.System_Boolean);
        }

        var operationName = operation.OperatorKind switch
        {
            BinaryOperatorKind.Add => "add",
            BinaryOperatorKind.Subtract => "sub",
            BinaryOperatorKind.Multiply => "mul",
            BinaryOperatorKind.Divide => "div",
            BinaryOperatorKind.Remainder => "rem",
            BinaryOperatorKind.And => "and",
            BinaryOperatorKind.Or => "or",
            BinaryOperatorKind.ExclusiveOr => "xor",
            BinaryOperatorKind.LeftShift => "shl",
            BinaryOperatorKind.RightShift => "shr",
            BinaryOperatorKind.UnsignedRightShift => "ushr",
            _ => null
        };
        if (operationName is null)
            return false;

        var isDivision = operation.OperatorKind is BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder;
        var isBitwise = operation.OperatorKind is BinaryOperatorKind.And or
            BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr;
        var isShift = operation.OperatorKind is BinaryOperatorKind.LeftShift or
            BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift;
        helper = result switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operationName}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operationName}",
            SpecialType.System_UInt32 when isDivision || isShift =>
                $"csharp2cuda_u32_{(operationName == "ushr" ? "shr" : operationName)}",
            SpecialType.System_UInt64 when isDivision || isShift =>
                $"csharp2cuda_u64_{(operationName == "ushr" ? "shr" : operationName)}",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Add =>
                "__fadd_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Subtract =>
                "__fsub_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Multiply =>
                "__fmul_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Divide =>
                "__fdiv_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Remainder =>
                "fmodf",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Add =>
                "__dadd_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Subtract =>
                "__dsub_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Multiply =>
                "__dmul_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Divide =>
                "__ddiv_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Remainder =>
                "fmod",
            _ => null
        };

        if (helper is not null)
            return true;
        if (result == SpecialType.System_Boolean)
            return isBitwise;
        if (result is SpecialType.System_UInt32 or SpecialType.System_UInt64)
            return !isDivision && !isShift;
        if (result is SpecialType.System_Single or SpecialType.System_Double)
            return false;
        return false;
    }

    private bool TryPlanUnary(IUnaryOperation operation, out string? helper)
    {
        helper = null;
        if (operation.OperatorMethod is not null)
            return false;
        var type = EffectiveType(operation.Type);
        helper = operation.OperatorKind switch
        {
            UnaryOperatorKind.Minus when type == SpecialType.System_Int32 =>
                "csharp2cuda_i32_neg",
            UnaryOperatorKind.Minus when type == SpecialType.System_Int64 =>
                "csharp2cuda_i64_neg",
            UnaryOperatorKind.BitwiseNegation when type == SpecialType.System_Int32 =>
                "csharp2cuda_i32_not",
            UnaryOperatorKind.BitwiseNegation when type == SpecialType.System_Int64 =>
                "csharp2cuda_i64_not",
            _ => null
        };
        return helper is not null || operation.OperatorKind switch
        {
            UnaryOperatorKind.Plus => IsArithmetic(type),
            UnaryOperatorKind.Minus => type is SpecialType.System_Single or SpecialType.System_Double or
                SpecialType.System_UInt32 or SpecialType.System_UInt64,
            UnaryOperatorKind.Not => type == SpecialType.System_Boolean,
            UnaryOperatorKind.BitwiseNegation => type is SpecialType.System_UInt32 or
                SpecialType.System_UInt64,
            _ => false
        };
    }

    private bool TryPlanCompound(
        ICompoundAssignmentOperation operation,
        out string? helper)
    {
        helper = null;
        if (operation.OperatorMethod is not null)
            return false;
        var type = EffectiveType(operation.Target.Type);
        var name = operation.OperatorKind switch
        {
            BinaryOperatorKind.Add => "add",
            BinaryOperatorKind.Subtract => "sub",
            BinaryOperatorKind.Multiply => "mul",
            BinaryOperatorKind.Divide => "div",
            BinaryOperatorKind.Remainder => "rem",
            BinaryOperatorKind.And => "and",
            BinaryOperatorKind.Or => "or",
            BinaryOperatorKind.ExclusiveOr => "xor",
            BinaryOperatorKind.LeftShift => "shl",
            BinaryOperatorKind.RightShift => "shr",
            BinaryOperatorKind.UnsignedRightShift => "ushr",
            _ => null
        };
        if (name is null)
            return false;
        var isDivision = operation.OperatorKind is BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder;
        var isShift = operation.OperatorKind is BinaryOperatorKind.LeftShift or
            BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift;
        var valueType = EffectiveType(operation.Value.Type);
        if (type is SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char)
        {
            if (valueType is not (SpecialType.System_SByte or SpecialType.System_Byte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Char or SpecialType.System_Int32))
            {
                return false;
            }
            helper = $"csharp2cuda_i32_{name}";
            return true;
        }
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{name}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{name}",
            SpecialType.System_UInt32 when isDivision || isShift =>
                $"csharp2cuda_u32_{(name == "ushr" ? "shr" : name)}",
            SpecialType.System_UInt64 when isDivision || isShift =>
                $"csharp2cuda_u64_{(name == "ushr" ? "shr" : name)}",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Add =>
                "__fadd_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Subtract =>
                "__fsub_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Multiply =>
                "__fmul_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Divide =>
                "__fdiv_rn",
            SpecialType.System_Single when operation.OperatorKind == BinaryOperatorKind.Remainder =>
                "fmodf",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Add =>
                "__dadd_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Subtract =>
                "__dsub_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Multiply =>
                "__dmul_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Divide =>
                "__ddiv_rn",
            SpecialType.System_Double when operation.OperatorKind == BinaryOperatorKind.Remainder =>
                "fmod",
            _ => null
        };
        if (helper is not null)
            return true;
        if (type is SpecialType.System_UInt32 or SpecialType.System_UInt64)
            return !isDivision && !isShift;
        return false;
    }

    private string ApplyCompoundResultConversion(string code, ITypeSymbol? targetType)
    {
        return EffectiveType(targetType) switch
        {
            SpecialType.System_SByte => $"csharp2cuda_i8_from_bits({code})",
            SpecialType.System_Int16 => $"csharp2cuda_i16_from_bits({code})",
            SpecialType.System_Byte => $"((unsigned char)({code}))",
            SpecialType.System_UInt16 or SpecialType.System_Char =>
                $"((unsigned short)({code}))",
            _ => code
        };
    }

    private bool TryPlanIncrement(
        IIncrementOrDecrementOperation operation,
        out string? helper)
    {
        helper = null;
        var type = EffectiveType(operation.Target.Type);
        var operationName = operation.Kind == OperationKind.Increment
            ? "increment"
            : "decrement";
        var position = operation.IsPostfix ? "post" : "pre";
        helper = type switch
        {
            SpecialType.System_SByte => $"csharp2cuda_i8_{position}_{operationName}",
            SpecialType.System_Int16 => $"csharp2cuda_i16_{position}_{operationName}",
            SpecialType.System_Int32 => $"csharp2cuda_i32_{position}_{operationName}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{position}_{operationName}",
            _ => null
        };
        return helper is not null || type is SpecialType.System_Byte or
            SpecialType.System_UInt16 or SpecialType.System_Char or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or SpecialType.System_Single or
            SpecialType.System_Double;
    }

    private bool TryPlanConversion(IConversionOperation operation, out string? helper)
    {
        var sourceType = ResolveType(operation.Operand.Type);
        var targetType = ResolveType(operation.Type);
        if (targetType is null)
        {
            helper = null;
            return false;
        }
        if (sourceType is null)
        {
            helper = null;
            return targetType is IPointerTypeSymbol;
        }
        return TryPlanConversion(sourceType, targetType, out helper);
    }

    private bool TryPlanConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        out string? helper)
    {
        helper = null;
        sourceType = ResolveType(sourceType)!;
        targetType = ResolveType(targetType)!;
        var source = EffectiveType(sourceType);
        var target = EffectiveType(targetType);
        var supported = IsArithmetic(source) && IsArithmetic(target) ||
            sourceType is IPointerTypeSymbol && targetType is IPointerTypeSymbol ||
            sourceType is IPointerTypeSymbol &&
                target is SpecialType.System_Int64 or SpecialType.System_UInt64 ||
            targetType is IPointerTypeSymbol &&
                source is SpecialType.System_Int64 or SpecialType.System_UInt64;
        if (!supported)
            return false;
        if (source is SpecialType.System_Single or SpecialType.System_Double &&
            IsIntegral(target))
        {
            helper = target switch
            {
                SpecialType.System_SByte => "csharp2cuda_f64_to_i8",
                SpecialType.System_Byte => "csharp2cuda_f64_to_u8",
                SpecialType.System_Int16 => "csharp2cuda_f64_to_i16",
                SpecialType.System_UInt16 or SpecialType.System_Char =>
                    "csharp2cuda_f64_to_u16",
                SpecialType.System_Int32 => "csharp2cuda_f64_to_i32",
                SpecialType.System_UInt32 => "csharp2cuda_f64_to_u32",
                SpecialType.System_Int64 => "csharp2cuda_f64_to_i64",
                SpecialType.System_UInt64 => "csharp2cuda_f64_to_u64",
                _ => null
            };
            return helper is not null;
        }
        if (target == SpecialType.System_SByte && source != target)
        {
            helper = "csharp2cuda_i8_from_bits";
        }
        else if (target == SpecialType.System_Int16 && source != target)
        {
            helper = "csharp2cuda_i16_from_bits";
        }
        else if (target == SpecialType.System_Int32 &&
            source is SpecialType.System_UInt32 or SpecialType.System_Int64 or
                SpecialType.System_UInt64)
        {
            helper = "csharp2cuda_i32_from_bits";
        }
        else if (target == SpecialType.System_Int64 && source == SpecialType.System_UInt64)
        {
            helper = "csharp2cuda_i64_from_bits";
        }
        return true;
    }

    private SpecialType EffectiveType(ITypeSymbol? type)
    {
        type = ResolveType(type);
        return type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration
            ? enumeration.EnumUnderlyingType?.SpecialType ?? SpecialType.None
            : plan.IsCudaInt32Type(type)
                ? SpecialType.System_Int32
                : type?.SpecialType ?? SpecialType.None;
    }

    private ITypeSymbol? GetViewElementType(ITypeSymbol? type) => type switch
    {
        IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
        INamedTypeSymbol named when plan.IsSpanType(named, out _) => named.TypeArguments[0],
        _ => null
    };

    private static bool IsArithmetic(SpecialType type) => type is
        SpecialType.System_SByte or SpecialType.System_Byte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double;

    private static bool IsIntegral(SpecialType type) => type is
        SpecialType.System_SByte or SpecialType.System_Byte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64;

    private static string GetBinaryOperator(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Add => "+",
        BinaryOperatorKind.Subtract => "-",
        BinaryOperatorKind.Multiply => "*",
        BinaryOperatorKind.Divide => "/",
        BinaryOperatorKind.Remainder => "%",
        BinaryOperatorKind.LeftShift => "<<",
        BinaryOperatorKind.RightShift => ">>",
        BinaryOperatorKind.UnsignedRightShift => ">>",
        BinaryOperatorKind.And => "&",
        BinaryOperatorKind.Or => "|",
        BinaryOperatorKind.ExclusiveOr => "^",
        BinaryOperatorKind.Equals => "==",
        BinaryOperatorKind.NotEquals => "!=",
        BinaryOperatorKind.LessThan => "<",
        BinaryOperatorKind.LessThanOrEqual => "<=",
        BinaryOperatorKind.GreaterThan => ">",
        BinaryOperatorKind.GreaterThanOrEqual => ">=",
        _ => throw new InvalidOperationException($"Unknown binary operator '{kind}'.")
    };

    private static string GetCompoundOperator(BinaryOperatorKind kind) =>
        GetBinaryOperator(kind) + "=";

    private static string GetUnaryOperator(UnaryOperatorKind kind) => kind switch
    {
        UnaryOperatorKind.Plus => "+",
        UnaryOperatorKind.Minus => "-",
        UnaryOperatorKind.Not => "!",
        UnaryOperatorKind.BitwiseNegation => "~",
        _ => throw new InvalidOperationException($"Unknown unary operator '{kind}'.")
    };

    private static string FormatConstant(object? value, ITypeSymbol? type)
    {
        if (value is null)
            return "nullptr";
        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number when number == int.MinValue => "(-2147483647 - 1)",
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture) + "u",
            long number when number == long.MinValue => "(-9223372036854775807LL - 1LL)",
            long number => number.ToString(CultureInfo.InvariantCulture) + "LL",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "ull",
            float number => FormatFloating(number),
            double number => FormatFloating(number),
            char character => ((ushort)character).ToString(CultureInfo.InvariantCulture),
            _ when type?.TypeKind == TypeKind.Enum => Convert.ToInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                $"Constant type '{value.GetType().FullName}' does not have a CUDA format.")
        };
    }

    private static string FormatFloating(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        if (!float.IsFinite(value) || bits == 0x80000000u)
            return $"__int_as_float(csharp2cuda_i32_from_bits(0x{bits:x8}u))";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e'))
            text += ".0";
        return text + "f";
    }

    private static string FormatFloating(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        if (!double.IsFinite(value) || bits == 0x8000000000000000ul)
        {
            return $"__longlong_as_double(csharp2cuda_i64_from_bits(0x{bits:x16}ull))";
        }
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e'))
            text += ".0";
        return text;
    }

    private static bool IsPointerIndirection(IOperation operation) =>
        operation.Syntax.IsKind(SyntaxKind.PointerIndirectionExpression);

    private static bool IsPointerElement(IOperation operation) =>
        operation.Kind == OperationKind.None &&
        operation.Syntax.IsKind(SyntaxKind.ElementAccessExpression);

    private static bool IsPointerMemberAccess(IFieldReferenceOperation field) =>
        field.Syntax.IsKind(SyntaxKind.PointerMemberAccessExpression);

    private static bool IsAddressable(IOperation operation)
    {
        while (operation is IConversionOperation { Conversion.IsIdentity: true } conversion)
            operation = conversion.Operand;
        return operation is ILocalReferenceOperation or
            IParameterReferenceOperation or
            IFieldReferenceOperation or
            IArrayElementReferenceOperation or
            IImplicitIndexerReferenceOperation or
            IPropertyReferenceOperation { Property.IsIndexer: true } ||
            IsPointerIndirection(operation) ||
            IsPointerElement(operation);
    }

    private CudaCallPlan? GetCallPlan(IMethodSymbol method) =>
        plan.GetCallPlan(plan.ResolveMethod(method, function));

    private ITypeSymbol? ResolveType(ITypeSymbol? type) =>
        type is null ? null : plan.SubstituteType(type, function);

    private string FormatType(ITypeSymbol type, bool deepReadOnly, Location location) =>
        plan.FormatType(plan.SubstituteType(type, function), deepReadOnly, location);
}
