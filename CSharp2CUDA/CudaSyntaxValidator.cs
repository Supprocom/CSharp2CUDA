using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CSharp2CUDA;

internal sealed class CudaSyntaxValidator(
    CudaEmissionPlan plan,
    SemanticModel semanticModel,
    ImmutableArray<Diagnostic>.Builder diagnostics) : CSharpSyntaxWalker
{
    private static readonly HashSet<SyntaxKind> SupportedKinds =
    [
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.AddExpression,
        SyntaxKind.AddressOfExpression,
        SyntaxKind.AndAssignmentExpression,
        SyntaxKind.Argument,
        SyntaxKind.ArgumentList,
        SyntaxKind.ArrayInitializerExpression,
        SyntaxKind.ArrayRankSpecifier,
        SyntaxKind.ArrayType,
        SyntaxKind.BitwiseAndExpression,
        SyntaxKind.BitwiseNotExpression,
        SyntaxKind.BitwiseOrExpression,
        SyntaxKind.Block,
        SyntaxKind.BracketedArgumentList,
        SyntaxKind.BreakStatement,
        SyntaxKind.CaseSwitchLabel,
        SyntaxKind.CastExpression,
        SyntaxKind.ConditionalExpression,
        SyntaxKind.ContinueStatement,
        SyntaxKind.DefaultSwitchLabel,
        SyntaxKind.DivideAssignmentExpression,
        SyntaxKind.DivideExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ElseClause,
        SyntaxKind.EmptyStatement,
        SyntaxKind.EqualsExpression,
        SyntaxKind.EqualsValueClause,
        SyntaxKind.ExclusiveOrAssignmentExpression,
        SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.ExpressionStatement,
        SyntaxKind.FalseLiteralExpression,
        SyntaxKind.ForStatement,
        SyntaxKind.GenericName,
        SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.IdentifierName,
        SyntaxKind.IfStatement,
        SyntaxKind.InvocationExpression,
        SyntaxKind.LeftShiftAssignmentExpression,
        SyntaxKind.LeftShiftExpression,
        SyntaxKind.LessThanExpression,
        SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.LocalDeclarationStatement,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.ModuloAssignmentExpression,
        SyntaxKind.ModuloExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.MultiplyExpression,
        SyntaxKind.NotEqualsExpression,
        SyntaxKind.NullLiteralExpression,
        SyntaxKind.NumericLiteralExpression,
        SyntaxKind.OrAssignmentExpression,
        SyntaxKind.ParenthesizedExpression,
        SyntaxKind.PointerIndirectionExpression,
        SyntaxKind.PointerMemberAccessExpression,
        SyntaxKind.PointerType,
        SyntaxKind.PostDecrementExpression,
        SyntaxKind.PostIncrementExpression,
        SyntaxKind.PreDecrementExpression,
        SyntaxKind.PreIncrementExpression,
        SyntaxKind.PredefinedType,
        SyntaxKind.ReturnStatement,
        SyntaxKind.RightShiftAssignmentExpression,
        SyntaxKind.RightShiftExpression,
        SyntaxKind.SimpleAssignmentExpression,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.StackAllocArrayCreationExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.SubtractExpression,
        SyntaxKind.SwitchSection,
        SyntaxKind.SwitchStatement,
        SyntaxKind.TrueLiteralExpression,
        SyntaxKind.TypeArgumentList,
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.UncheckedExpression,
        SyntaxKind.VariableDeclaration,
        SyntaxKind.VariableDeclarator,
        SyntaxKind.WhileStatement
    ];

    public override void DefaultVisit(SyntaxNode node)
    {
        if (!SupportedKinds.Contains(node.Kind()))
        {
            ReportUnsupportedSyntax(node);
            return;
        }
        base.DefaultVisit(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var method = semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
        var operation = semanticModel.GetOperation(node) as IInvocationOperation;
        var call = method is null ? null : plan.GetCallPlan(method);
        if (method is null || call is null ||
            operation is null ||
            operation.Arguments.Any(argument => argument.ArgumentKind == ArgumentKind.DefaultValue) ||
            node.ArgumentList.Arguments.Any(argument => argument.NameColon is not null))
        {
            ReportUnsupportedCall(node, method?.ToDisplayString(
                SymbolDisplayFormat.CSharpErrorMessageFormat) ?? node.Expression.ToString());
        }
        else
        {
            var arguments = node.ArgumentList.Arguments;
            if (call.Kind == CudaCallKind.InvalidAtomic)
            {
                var type = method.TypeArguments.FirstOrDefault()?.ToDisplayString() ?? "unknown";
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.InvalidAtomicType,
                    node.GetLocation(),
                    type,
                    method.Name));
            }
            else if (call.Kind is CudaCallKind.Atomic or CudaCallKind.SignedInt64Atomic)
            {
                var expectedCount = method.Name == nameof(Cuda.AtomicCompareExchange) ? 3 : 2;
                if (arguments.Count != expectedCount ||
                    !arguments[0].RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                    arguments.Skip(1).Any(argument => argument.RefKindKeyword != default))
                {
                    ReportUnsupportedCall(node, method.ToDisplayString());
                }
            }
            else if (call.Kind == CudaCallKind.Storage && !plan.IsStorageInvocation(node))
            {
                diagnostics.Add(Diagnostic.Create(
                    CudaDiagnostics.InvalidStorage,
                    node.GetLocation(),
                    method.Name));
            }
            else if (call.Kind == CudaCallKind.DynamicSharedView)
            {
                ValidateDynamicSharedView(node, method);
            }
            else if (arguments.Any(argument => argument.RefKindKeyword != default))
            {
                ReportUnsupportedCall(node, method.ToDisplayString());
            }

            if (method.Name == nameof(Cuda.SyncWarp) && arguments.Count == 1)
                ValidateWarpMask(arguments[0].Expression);
            else if (method.Name == nameof(Cuda.ShuffleDownSync))
            {
                ValidateWarpMask(arguments[0].Expression);
                ValidateWarpWidth(arguments[3].Expression);
            }

            if (!HasSafeArgumentOrder(arguments))
            {
                ReportUnsupportedCall(node, method.ToDisplayString());
            }
        }
        base.VisitInvocationExpression(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (!IsAuthorizedMember(node))
            ReportUnsupportedSyntax(node);
        base.VisitMemberAccessExpression(node);
    }

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var expressionType = semanticModel.GetTypeInfo(node.Expression).Type;
        if (expressionType is not IPointerTypeSymbol &&
            !IsFixedLocalArrayReference(node.Expression) &&
            !IsConstantArrayReference(node.Expression))
            ReportUnsupportedSyntax(node);
        base.VisitElementAccessExpression(node);
    }

    public override void VisitStackAllocArrayCreationExpression(
        StackAllocArrayCreationExpressionSyntax node)
    {
        var declaration = node.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (declaration is null || !plan.IsFixedLocalArray(declaration))
            ReportUnsupportedSyntax(node);
        base.VisitStackAllocArrayCreationExpression(node);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.NumericLiteralExpression) && node.Token.Value is decimal)
            ReportUnsupportedSyntax(node);
        base.VisitLiteralExpression(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.ValueText != "var" && !IsAuthorizedIdentifier(node))
            ReportUnsupportedSyntax(node);
        base.VisitIdentifierName(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        ValidateBinary(node);
        base.VisitBinaryExpression(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        ValidateAssignment(node);
        base.VisitAssignmentExpression(node);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        ValidatePrefix(node);
        base.VisitPrefixUnaryExpression(node);
    }

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        ValidatePostfix(node);
        base.VisitPostfixUnaryExpression(node);
    }

    public override void VisitCastExpression(CastExpressionSyntax node)
    {
        ValidateCast(node);
        base.VisitCastExpression(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        if (semanticModel.GetOperation(node) is not IConditionalOperation operation ||
            EffectiveType(operation.Condition.Type) != SpecialType.System_Boolean ||
            !HaveCompatibleConditionalTypes(
                semanticModel.GetTypeInfo(node.WhenTrue).Type,
                semanticModel.GetTypeInfo(node.WhenFalse).Type))
        {
            ReportUnsupportedSyntax(node);
        }
        base.VisitConditionalExpression(node);
    }

    private void ValidateBinary(BinaryExpressionSyntax node)
    {
        if (semanticModel.GetOperation(node) is not IBinaryOperation operation)
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        if (operation.OperatorMethod is { } operatorMethod)
        {
            if (operatorMethod.ContainingType.ToDisplayString() != "CSharp2CUDA.CudaInt32" ||
                operation.OperatorKind is not BinaryOperatorKind.Equals and
                    not BinaryOperatorKind.NotEquals ||
                !CanEvaluateInEitherOrder(node.Left, node.Right))
            {
                ReportUnsupportedSyntax(node);
            }
            return;
        }

        var result = EffectiveType(operation.Type);
        var left = EffectiveType(operation.LeftOperand.Type);
        var right = EffectiveType(operation.RightOperand.Type);
        var hasPointer = operation.LeftOperand.Type is IPointerTypeSymbol ||
            operation.RightOperand.Type is IPointerTypeSymbol;
        if (hasPointer)
        {
            string? pointerHelper = null;
            var supportedPointerOperation = operation.OperatorKind switch
            {
                BinaryOperatorKind.Add =>
                    operation.Type is IPointerTypeSymbol &&
                    (operation.LeftOperand.Type is IPointerTypeSymbol &&
                        right == SpecialType.System_Int32 ||
                     operation.RightOperand.Type is IPointerTypeSymbol &&
                        left == SpecialType.System_Int32),
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals =>
                    operation.LeftOperand.Type is IPointerTypeSymbol or null &&
                    operation.RightOperand.Type is IPointerTypeSymbol or null,
                _ => false
            };
            if (!supportedPointerOperation)
            {
                ReportUnsupportedSyntax(node);
                return;
            }
            if (!CanEvaluateInEitherOrder(node.Left, node.Right))
            {
                ReportUnsupportedSyntax(node);
                return;
            }
            if (operation.OperatorKind == BinaryOperatorKind.Add)
            {
                pointerHelper = operation.LeftOperand.Type is IPointerTypeSymbol
                    ? "csharp2cuda_pointer_add"
                    : "csharp2cuda_pointer_add_reverse";
                plan.PlanBinaryHelper(node, pointerHelper);
            }
            return;
        }

        string? helper = null;
        var supported = operation.OperatorKind switch
        {
            BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr =>
                result == SpecialType.System_Boolean &&
                left == SpecialType.System_Boolean &&
                right == SpecialType.System_Boolean,
            BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or
            BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
            BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual =>
                PlanComparison(node, operation),
            BinaryOperatorKind.Add => PlanArithmetic(result, "add", ref helper),
            BinaryOperatorKind.Subtract => PlanArithmetic(result, "sub", ref helper),
            BinaryOperatorKind.Multiply => PlanArithmetic(result, "mul", ref helper),
            BinaryOperatorKind.Divide => PlanDivision(result, "div", ref helper),
            BinaryOperatorKind.Remainder => PlanDivision(result, "rem", ref helper),
            BinaryOperatorKind.And => PlanBitwise(result, "and", ref helper),
            BinaryOperatorKind.Or => PlanBitwise(result, "or", ref helper),
            BinaryOperatorKind.ExclusiveOr => PlanBitwise(result, "xor", ref helper),
            BinaryOperatorKind.LeftShift => PlanShift(result, "shl", ref helper),
            BinaryOperatorKind.RightShift => PlanShift(result, "shr", ref helper),
            _ => false
        };

        if (!supported)
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        if (operation.OperatorKind is not BinaryOperatorKind.ConditionalAnd and
                not BinaryOperatorKind.ConditionalOr &&
            !CanEvaluateInEitherOrder(node.Left, node.Right))
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        if (helper is not null)
            plan.PlanBinaryHelper(node, helper);
    }

    private void ValidateAssignment(AssignmentExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            if (!CanEvaluateAssignmentInCppOrder(node.Left, node.Right))
                ReportUnsupportedSyntax(node);
            return;
        }
        if (semanticModel.GetOperation(node) is not ICompoundAssignmentOperation operation ||
            operation.OperatorMethod is not null)
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        var type = EffectiveType(operation.Target.Type);
        string? helper = null;
        var supported = operation.OperatorKind switch
        {
            BinaryOperatorKind.Add => PlanCompound(type, "add", ref helper),
            BinaryOperatorKind.Subtract => PlanCompound(type, "sub", ref helper),
            BinaryOperatorKind.Multiply => PlanCompound(type, "mul", ref helper),
            BinaryOperatorKind.Divide => PlanCompoundDivision(type, "div", ref helper),
            BinaryOperatorKind.Remainder => PlanCompoundDivision(type, "rem", ref helper),
            BinaryOperatorKind.And => PlanCompoundBitwise(type, "and", ref helper),
            BinaryOperatorKind.Or => PlanCompoundBitwise(type, "or", ref helper),
            BinaryOperatorKind.ExclusiveOr => PlanCompoundBitwise(type, "xor", ref helper),
            BinaryOperatorKind.LeftShift => PlanCompoundShift(type, "shl", ref helper),
            BinaryOperatorKind.RightShift => PlanCompoundShift(type, "shr", ref helper),
            _ => false
        };

        if (!supported || !CanEvaluateInEitherOrder(node.Left, node.Right))
        {
            ReportUnsupportedSyntax(node);
            return;
        }
        if (helper is not null)
            plan.PlanAssignmentHelper(node, helper);
    }

    private void ValidatePrefix(PrefixUnaryExpressionSyntax node)
    {
        if (semanticModel.GetOperation(node) is IIncrementOrDecrementOperation increment)
        {
            ValidateIncrement(node, increment, prefix: true);
            return;
        }

        if (semanticModel.GetOperation(node) is not IUnaryOperation operation)
            return;
        var type = EffectiveType(operation.Type);
        string? helper = null;
        var supported = operation.OperatorKind switch
        {
            UnaryOperatorKind.Minus => PlanUnary(type, "neg", ref helper),
            UnaryOperatorKind.Plus => IsArithmetic(type),
            UnaryOperatorKind.Not => type == SpecialType.System_Boolean,
            UnaryOperatorKind.BitwiseNegation => PlanBitwise(type, "not", ref helper),
            _ => false
        };
        if (!supported)
        {
            ReportUnsupportedSyntax(node);
            return;
        }
        if (helper is not null)
            plan.PlanPrefixHelper(node, helper);
    }

    private void ValidatePostfix(PostfixUnaryExpressionSyntax node)
    {
        if (semanticModel.GetOperation(node) is not IIncrementOrDecrementOperation increment)
        {
            ReportUnsupportedSyntax(node);
            return;
        }
        ValidateIncrement(node, increment, prefix: false);
    }

    private void ValidateIncrement(
        ExpressionSyntax node,
        IIncrementOrDecrementOperation operation,
        bool prefix)
    {
        var type = EffectiveType(operation.Target.Type);
        var operationName = operation.Kind == OperationKind.Increment ? "increment" : "decrement";
        var helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{(prefix ? "pre" : "post")}_{operationName}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{(prefix ? "pre" : "post")}_{operationName}",
            _ => null
        };
        var native = type is SpecialType.System_Byte or SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double;
        if (helper is null && !native)
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        if (helper is not null && node is PrefixUnaryExpressionSyntax prefixNode)
            plan.PlanPrefixHelper(prefixNode, helper);
        else if (helper is not null && node is PostfixUnaryExpressionSyntax postfixNode)
            plan.PlanPostfixHelper(postfixNode, helper);
    }

    private void ValidateCast(CastExpressionSyntax node)
    {
        var operation = semanticModel.GetOperation(node) as IConversionOperation;
        var source = operation?.Operand.Type ?? semanticModel.GetTypeInfo(node.Expression).Type;
        var target = operation?.Type ?? semanticModel.GetTypeInfo(node.Type).Type;
        if (source is null || target is null)
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        plan.FormatType(target, false, node.Type.GetLocation());
        var sourceType = EffectiveType(source);
        var targetType = EffectiveType(target);
        var supported = IsArithmetic(sourceType) && IsArithmetic(targetType) ||
            source is IPointerTypeSymbol && target is IPointerTypeSymbol ||
            source is IPointerTypeSymbol &&
                targetType is SpecialType.System_Int64 or SpecialType.System_UInt64 ||
            target is IPointerTypeSymbol &&
                sourceType is SpecialType.System_Int64 or SpecialType.System_UInt64;
        if (!supported || targetType is SpecialType.System_SByte or SpecialType.System_Int16 &&
            sourceType != targetType)
        {
            ReportUnsupportedSyntax(node);
            return;
        }

        if (targetType == SpecialType.System_Int32 &&
            sourceType is SpecialType.System_UInt32 or SpecialType.System_Int64 or
                SpecialType.System_UInt64)
        {
            plan.PlanCastHelper(node, "csharp2cuda_i32_from_bits");
        }
        else if (targetType == SpecialType.System_Int64 &&
            sourceType == SpecialType.System_UInt64)
        {
            plan.PlanCastHelper(node, "csharp2cuda_i64_from_bits");
        }
    }

    private bool IsAuthorizedMember(MemberAccessExpressionSyntax node)
    {
        if (plan.TryGetDimensionReplacement(node, out _))
            return true;
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is IFieldSymbol field)
        {
            if (field.IsStatic && plan.TryGetConstantArray(field, out _))
                return true;
            return !field.IsStatic &&
                field.ContainingType is { } containingType &&
                plan.TryGetStruct(containingType, out _) &&
                plan.TryGetIdentifier(field, out _);
        }
        if (symbol is IPropertySymbol property)
            return plan.IsDimensionProperty(property);
        if (symbol is IMethodSymbol method)
            return plan.GetCallPlan(method) is not null;
        if (symbol is INamespaceSymbol or INamedTypeSymbol)
        {
            var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();
            return invocation is not null &&
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol target &&
                plan.GetCallPlan(target) is not null;
        }
        return false;
    }

    private bool IsAuthorizedIdentifier(IdentifierNameSyntax node)
    {
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is ILocalSymbol or IParameterSymbol)
            return plan.TryGetIdentifier(symbol, out _);
        if (symbol is IFieldSymbol field)
        {
            if (field.IsStatic && plan.TryGetConstantArray(field, out _))
                return true;
            return !field.IsStatic &&
                field.ContainingType is { } containingType &&
                plan.TryGetStruct(containingType, out _) &&
                plan.TryGetIdentifier(field, out _);
        }
        if (symbol is IMethodSymbol method)
            return plan.GetCallPlan(method) is not null;
        if (symbol is IPropertySymbol property)
            return plan.IsDimensionProperty(property);
        if (symbol is INamedTypeSymbol type)
        {
            if (type.ToDisplayString() == "CSharp2CUDA.CudaInt32" ||
                plan.TryGetStruct(type, out _))
            {
                return true;
            }
            return IsAuthorizedQualifier(node);
        }
        if (symbol is INamespaceSymbol)
            return IsAuthorizedQualifier(node);
        return false;
    }

    private bool IsAuthorizedQualifier(IdentifierNameSyntax node)
    {
        var member = node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>()
            .LastOrDefault();
        if (member is not null && IsAuthorizedMember(member))
            return true;
        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
        return invocation is not null &&
            semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
            plan.GetCallPlan(method) is not null;
    }

    private bool IsFixedLocalArrayReference(ExpressionSyntax expression)
    {
        if (semanticModel.GetSymbolInfo(expression).Symbol is not ILocalSymbol local ||
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not
                VariableDeclaratorSyntax variable ||
            variable.Parent?.Parent is not LocalDeclarationStatementSyntax declaration)
        {
            return false;
        }
        return plan.IsFixedLocalArray(declaration);
    }

    private bool IsConstantArrayReference(ExpressionSyntax expression) =>
        semanticModel.GetSymbolInfo(expression).Symbol is IFieldSymbol field &&
        plan.TryGetConstantArray(field, out _);

    private void ValidateDynamicSharedView(
        InvocationExpressionSyntax node,
        IMethodSymbol method)
    {
        var elementType = method.TypeArguments[0];
        if (!CudaEmissionPlan.IsStorageElementType(elementType))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidStorageType,
                node.GetLocation(),
                elementType.ToDisplayString(),
                nameof(Cuda.DynamicSharedView)));
            return;
        }

        var storageExpression = node.ArgumentList.Arguments[0].Expression;
        var storageSymbol = semanticModel.GetSymbolInfo(storageExpression).Symbol;
        var requiredAlignment = CudaEmissionPlan.GetNaturalAlignment(elementType);
        if (!plan.TryGetDynamicSharedStorage(storageSymbol, out var storage) ||
            storage.Alignment < requiredAlignment)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidAlignment,
                storageExpression.GetLocation(),
                plan.TryGetDynamicSharedStorage(storageSymbol, out var candidate)
                    ? candidate.Alignment.ToString()
                    : "unplanned",
                nameof(Cuda.DynamicSharedView)));
        }
    }

    private void ValidateWarpMask(ExpressionSyntax expression)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant is not { HasValue: true, Value: uint value } || value == 0u)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidWarpMask,
                expression.GetLocation()));
        }
    }

    private void ValidateWarpWidth(ExpressionSyntax expression)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant is not { HasValue: true, Value: int value } ||
            value is not (1 or 2 or 4 or 8 or 16 or 32))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidWarpWidth,
                expression.GetLocation()));
        }
    }

    private bool HasSafeArgumentOrder(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        for (var first = 0; first < arguments.Count; first++)
        {
            for (var second = first + 1; second < arguments.Count; second++)
            {
                if (!CanEvaluateInEitherOrder(
                        arguments[first].Expression,
                        arguments[second].Expression))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private bool CanEvaluateAssignmentInCppOrder(
        ExpressionSyntax target,
        ExpressionSyntax value)
    {
        if (HasStorageConflict(target, value))
            return false;
        if (HasNoObservableEffectInTarget(target) && HasNoObservableEffect(value))
            return true;
        if (HasOnlyIsolatedLocalEffects(target) && HasNoObservableEffect(value))
            return true;
        if (HasIsolatedTargetEffects(target))
            return true;
        return HasInvariantTargetLocation(target) || IsInvariantValue(value);
    }

    private bool CanEvaluateInEitherOrder(ExpressionSyntax first, ExpressionSyntax second)
    {
        if (HasStorageConflict(first, second))
            return false;
        if (HasNoObservableEffect(first) && HasNoObservableEffect(second))
            return true;
        if (HasOnlyIsolatedLocalEffects(first) && HasNoObservableEffect(second) ||
            HasOnlyIsolatedLocalEffects(second) && HasNoObservableEffect(first))
        {
            return true;
        }
        return IsInvariantValue(first) || IsInvariantValue(second);
    }

    private bool HasNoObservableEffect(ExpressionSyntax expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is AssignmentExpressionSyntax or PrefixUnaryExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.PreIncrementExpression or
                        (int)SyntaxKind.PreDecrementExpression
                } or PostfixUnaryExpressionSyntax)
            {
                return false;
            }
            if (node is InvocationExpressionSyntax invocation)
            {
                var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method is null || !plan.IsPureCall(method))
                    return false;
            }
        }
        return true;
    }

    private bool HasOnlyIsolatedLocalEffects(ExpressionSyntax expression)
    {
        var hasEffect = false;
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method is null || !plan.IsPureCall(method))
                    return false;
            }

            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix => postfix.Operand,
                _ => null
            };
            if (target is null)
                continue;
            hasEffect = true;
            if (!IsIsolatedLocalTarget(target))
                return false;
        }
        return hasEffect;
    }

    private bool IsIsolatedLocalTarget(ExpressionSyntax target)
    {
        while (target is ParenthesizedExpressionSyntax parenthesized)
            target = parenthesized.Expression;
        var symbol = semanticModel.GetSymbolInfo(target).Symbol;
        return symbol switch
        {
            ILocalSymbol local => !IsStorageExposed(local),
            IParameterSymbol { RefKind: RefKind.None } parameter =>
                !IsStorageExposed(parameter),
            _ => false
        };
    }

    private bool HasIsolatedTargetEffects(ExpressionSyntax target)
    {
        if (!HasOnlyIsolatedLocalEffects(target))
            return false;
        return target switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                HasIsolatedTargetEffects(parenthesized.Expression),
            PrefixUnaryExpressionSyntax prefix when
                prefix.IsKind(SyntaxKind.PointerIndirectionExpression) =>
                IsInvariantValue(prefix.Operand),
            ElementAccessExpressionSyntax element =>
                IsInvariantValue(element.Expression) &&
                element.ArgumentList.Arguments.All(argument =>
                    IsInvariantValue(argument.Expression) ||
                    IsIsolatedIndexMutation(argument.Expression)),
            MemberAccessExpressionSyntax member when
                member.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
                HasIsolatedTargetEffects(member.Expression),
            MemberAccessExpressionSyntax member when
                member.IsKind(SyntaxKind.PointerMemberAccessExpression) =>
                IsInvariantValue(member.Expression),
            _ => false
        };
    }

    private bool IsIsolatedIndexMutation(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression switch
        {
            PrefixUnaryExpressionSyntax prefix when
                prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                prefix.IsKind(SyntaxKind.PreDecrementExpression) =>
                IsIsolatedLocalTarget(prefix.Operand),
            PostfixUnaryExpressionSyntax postfix =>
                IsIsolatedLocalTarget(postfix.Operand),
            _ => false
        };
    }

    private bool HasNoObservableEffectInTarget(ExpressionSyntax target)
    {
        return target switch
        {
            IdentifierNameSyntax => true,
            ParenthesizedExpressionSyntax parenthesized =>
                HasNoObservableEffectInTarget(parenthesized.Expression),
            PrefixUnaryExpressionSyntax prefix when
                prefix.IsKind(SyntaxKind.PointerIndirectionExpression) =>
                HasNoObservableEffect(prefix.Operand),
            ElementAccessExpressionSyntax element =>
                HasNoObservableEffect(element.Expression) &&
                element.ArgumentList.Arguments.All(argument =>
                    HasNoObservableEffect(argument.Expression)),
            MemberAccessExpressionSyntax member when
                member.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
                HasNoObservableEffectInTarget(member.Expression),
            MemberAccessExpressionSyntax member when
                member.IsKind(SyntaxKind.PointerMemberAccessExpression) =>
                HasNoObservableEffect(member.Expression),
            _ => HasNoObservableEffect(target)
        };
    }

    private bool HasInvariantTargetLocation(ExpressionSyntax target)
    {
        return target switch
        {
            IdentifierNameSyntax identifier =>
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol or
                    IParameterSymbol,
            ParenthesizedExpressionSyntax parenthesized =>
                HasInvariantTargetLocation(parenthesized.Expression),
            PrefixUnaryExpressionSyntax prefix when
                prefix.IsKind(SyntaxKind.PointerIndirectionExpression) =>
                IsInvariantValue(prefix.Operand),
            ElementAccessExpressionSyntax element =>
                IsInvariantValue(element.Expression) &&
                element.ArgumentList.Arguments.All(argument =>
                    IsInvariantValue(argument.Expression)),
            MemberAccessExpressionSyntax member when
                member.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
                HasInvariantTargetLocation(member.Expression),
            MemberAccessExpressionSyntax member when
                member.IsKind(SyntaxKind.PointerMemberAccessExpression) =>
                IsInvariantValue(member.Expression),
            _ => false
        };
    }

    private bool IsInvariantValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax identifier => IsInvariantIdentifier(identifier),
            ParenthesizedExpressionSyntax parenthesized =>
                IsInvariantValue(parenthesized.Expression),
            CheckedExpressionSyntax checkedExpression when
                checkedExpression.IsKind(SyntaxKind.UncheckedExpression) =>
                IsInvariantValue(checkedExpression.Expression),
            CastExpressionSyntax cast => IsInvariantValue(cast.Expression),
            PrefixUnaryExpressionSyntax prefix when
                prefix.IsKind(SyntaxKind.UnaryPlusExpression) ||
                prefix.IsKind(SyntaxKind.UnaryMinusExpression) ||
                prefix.IsKind(SyntaxKind.LogicalNotExpression) ||
                prefix.IsKind(SyntaxKind.BitwiseNotExpression) ||
                prefix.IsKind(SyntaxKind.AddressOfExpression) =>
                IsInvariantValue(prefix.Operand),
            BinaryExpressionSyntax binary when
                !binary.IsKind(SyntaxKind.DivideExpression) &&
                !binary.IsKind(SyntaxKind.ModuloExpression) =>
                IsInvariantValue(binary.Left) && IsInvariantValue(binary.Right),
            ConditionalExpressionSyntax conditional =>
                IsInvariantValue(conditional.Condition) &&
                IsInvariantValue(conditional.WhenTrue) &&
                IsInvariantValue(conditional.WhenFalse),
            MemberAccessExpressionSyntax member => IsInvariantMember(member),
            InvocationExpressionSyntax invocation => IsInvariantCall(invocation),
            _ => false
        };
    }

    private bool IsInvariantIdentifier(IdentifierNameSyntax identifier)
    {
        var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
        return symbol switch
        {
            ILocalSymbol local => !IsStorageExposed(local),
            IParameterSymbol { RefKind: RefKind.None } parameter =>
                !IsStorageExposed(parameter),
            IPropertySymbol property => plan.IsDimensionProperty(property),
            _ => false
        };
    }

    private bool IsInvariantMember(MemberAccessExpressionSyntax member)
    {
        if (plan.TryGetDimensionReplacement(member, out _))
            return true;
        return member.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
            semanticModel.GetSymbolInfo(member).Symbol is IFieldSymbol { IsStatic: false } &&
            IsInvariantValue(member.Expression);
    }

    private bool IsInvariantCall(InvocationExpressionSyntax invocation)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            !plan.IsPureCall(method) ||
            plan.GetCallPlan(method) is not { Kind: not CudaCallKind.PlannedFunction } ||
            method.Parameters.Any(parameter =>
                parameter.RefKind != RefKind.None ||
                parameter.Type is IPointerTypeSymbol))
        {
            return false;
        }
        return invocation.ArgumentList.Arguments.All(argument =>
            IsInvariantValue(argument.Expression));
    }

    private bool IsStorageExposed(ISymbol symbol)
    {
        var method = symbol.ContainingSymbol as IMethodSymbol;
        var syntax = method?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as
            MethodDeclarationSyntax;
        if (syntax is null)
            return true;

        foreach (var node in syntax.DescendantNodes())
        {
            if (node is PrefixUnaryExpressionSyntax prefix &&
                prefix.IsKind(SyntaxKind.AddressOfExpression) &&
                SymbolEqualityComparer.Default.Equals(
                    GetExposedStorageSymbol(prefix.Operand),
                    symbol))
            {
                return true;
            }
            if (node is ArgumentSyntax argument &&
                argument.RefKindKeyword != default &&
                SymbolEqualityComparer.Default.Equals(
                    GetExposedStorageSymbol(argument.Expression),
                    symbol))
            {
                return true;
            }
        }
        return false;
    }

    private bool HasStorageConflict(ExpressionSyntax first, ExpressionSyntax second)
    {
        var firstWrites = GetWrittenSymbols(first);
        var secondWrites = GetWrittenSymbols(second);
        return firstWrites.Overlaps(GetReferencedSymbols(second)) ||
            secondWrites.Overlaps(GetReferencedSymbols(first));
    }

    private HashSet<ISymbol> GetWrittenSymbols(ExpressionSyntax expression)
    {
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            ExpressionSyntax? target = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix => postfix.Operand,
                _ => null
            };
            if (target is not null && semanticModel.GetSymbolInfo(target).Symbol is { } symbol)
                symbols.Add(symbol);
        }
        return symbols;
    }

    private HashSet<ISymbol> GetReferencedSymbols(ExpressionSyntax expression)
    {
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var identifier in expression.DescendantNodesAndSelf()
                     .OfType<IdentifierNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol)
                symbols.Add(symbol);
        }
        return symbols;
    }

    private ISymbol? GetExposedStorageSymbol(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        if (expression is IdentifierNameSyntax)
            return semanticModel.GetSymbolInfo(expression).Symbol;
        if (expression is MemberAccessExpressionSyntax member &&
            member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            return GetExposedStorageSymbol(member.Expression);
        }
        return null;
    }

    private static bool PlanArithmetic(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}",
            _ => null
        };
        return helper is not null || type is SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or SpecialType.System_Single or
            SpecialType.System_Double;
    }

    private bool PlanComparison(BinaryExpressionSyntax node, IBinaryOperation operation)
    {
        if (!IsComparable(operation.LeftOperand.Type, operation.RightOperand.Type))
            return false;

        var sourceLeft = semanticModel.GetTypeInfo(node.Left).Type;
        var sourceRight = semanticModel.GetTypeInfo(node.Right).Type;
        var leftType = HaveSameEmittedType(sourceLeft, operation.LeftOperand.Type)
            ? null
            : plan.FormatType(
                operation.LeftOperand.Type!,
                false,
                node.Left.GetLocation());
        var rightType = HaveSameEmittedType(sourceRight, operation.RightOperand.Type)
            ? null
            : plan.FormatType(
                operation.RightOperand.Type!,
                false,
                node.Right.GetLocation());
        if (leftType is not null || rightType is not null)
            plan.PlanBinaryConversions(node, leftType, rightType);
        return true;
    }

    private static bool PlanDivision(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}",
            SpecialType.System_UInt32 => $"csharp2cuda_u32_{operation}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}",
            SpecialType.System_UInt64 => $"csharp2cuda_u64_{operation}",
            _ => null
        };
        return helper is not null ||
            operation == "div" && type is SpecialType.System_Single or SpecialType.System_Double;
    }

    private static bool PlanBitwise(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}",
            _ => null
        };
        return helper is not null ||
            type is SpecialType.System_UInt32 or SpecialType.System_UInt64;
    }

    private static bool PlanShift(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}",
            SpecialType.System_UInt32 => $"csharp2cuda_u32_{operation}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}",
            SpecialType.System_UInt64 => $"csharp2cuda_u64_{operation}",
            _ => null
        };
        return helper is not null;
    }

    private static bool PlanCompound(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}_assign",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}_assign",
            _ => null
        };
        return helper is not null || type is SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or SpecialType.System_Single or
            SpecialType.System_Double;
    }

    private static bool PlanCompoundDivision(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}_assign",
            SpecialType.System_UInt32 => $"csharp2cuda_u32_{operation}_assign",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}_assign",
            SpecialType.System_UInt64 => $"csharp2cuda_u64_{operation}_assign",
            _ => null
        };
        return helper is not null ||
            operation == "div" && type is SpecialType.System_Single or SpecialType.System_Double;
    }

    private static bool PlanCompoundBitwise(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}_assign",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}_assign",
            _ => null
        };
        return helper is not null ||
            type is SpecialType.System_UInt32 or SpecialType.System_UInt64;
    }

    private static bool PlanCompoundShift(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}_assign",
            SpecialType.System_UInt32 => $"csharp2cuda_u32_{operation}_assign",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}_assign",
            SpecialType.System_UInt64 => $"csharp2cuda_u64_{operation}_assign",
            _ => null
        };
        return helper is not null;
    }

    private static bool PlanUnary(
        SpecialType type,
        string operation,
        ref string? helper)
    {
        helper = type switch
        {
            SpecialType.System_Int32 => $"csharp2cuda_i32_{operation}",
            SpecialType.System_Int64 => $"csharp2cuda_i64_{operation}",
            _ => null
        };
        return helper is not null || type is SpecialType.System_Single or
            SpecialType.System_Double or SpecialType.System_UInt32 or
            SpecialType.System_UInt64;
    }

    private static bool IsComparable(ITypeSymbol? left, ITypeSymbol? right)
    {
        var leftType = EffectiveType(left);
        var rightType = EffectiveType(right);
        return leftType == rightType &&
            (IsArithmetic(leftType) || leftType == SpecialType.System_Boolean);
    }

    private bool HaveSameEmittedType(ITypeSymbol? left, ITypeSymbol? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (SymbolEqualityComparer.Default.Equals(left, right))
            return true;
        if (EffectiveType(left) is { } leftType && leftType != SpecialType.None &&
            EffectiveType(right) == leftType)
        {
            return true;
        }
        if (left is IPointerTypeSymbol leftPointer && right is IPointerTypeSymbol rightPointer)
        {
            return SymbolEqualityComparer.Default.Equals(
                leftPointer.PointedAtType,
                rightPointer.PointedAtType);
        }
        return false;
    }

    private bool HaveCompatibleConditionalTypes(ITypeSymbol? left, ITypeSymbol? right)
    {
        if (left is null && right is IPointerTypeSymbol ||
            right is null && left is IPointerTypeSymbol)
        {
            return true;
        }
        return HaveSameEmittedType(left, right);
    }

    private static bool IsArithmetic(SpecialType type) =>
        type is SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double;

    private static bool IsIntegral(SpecialType type) =>
        type is SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64;

    private static SpecialType EffectiveType(ITypeSymbol? type) =>
        type?.ToDisplayString() == "CSharp2CUDA.CudaInt32"
            ? SpecialType.System_Int32
            : type?.SpecialType ?? SpecialType.None;

    private void ReportUnsupportedCall(InvocationExpressionSyntax node, string name) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedCall,
            node.GetLocation(),
            name));

    private void ReportUnsupportedSyntax(SyntaxNode node) =>
        diagnostics.Add(Diagnostic.Create(
            CudaDiagnostics.UnsupportedSyntax,
            node.GetLocation(),
            node.Kind().ToString()));
}
