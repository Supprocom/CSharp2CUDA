using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Compilation;

namespace Supprocom.CSharp2CUDA.Emission;

internal sealed class CudaLayoutEngine(
    CudaSymbolCatalog symbols,
    IReadOnlyDictionary<INamedTypeSymbol, CudaStructPlan> structures)
{
    private readonly Dictionary<INamedTypeSymbol, CudaStructLayout> layouts =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> active = new(SymbolEqualityComparer.Default);

    public bool TryGetLayout(
        CudaStructPlan structure,
        out CudaStructLayout layout,
        out string reason)
    {
        if (layouts.TryGetValue(structure.Symbol, out layout!))
        {
            reason = string.Empty;
            return true;
        }
        if (!active.Add(structure.Symbol))
        {
            layout = null!;
            reason = "the structure layout is recursive";
            return false;
        }

        try
        {
            var attribute = symbols.GetStructLayoutAttribute(structure.Symbol);
            var layoutKind = attribute is
            { ConstructorArguments: [{ Value: int value }] }
                ? value
                : 0;
            if (layoutKind == 3)
            {
                layout = null!;
                reason = "automatic layout is not supported";
                return false;
            }
            if (layoutKind is not (0 or 2))
            {
                layout = null!;
                reason = $"layout kind {layoutKind} is not supported";
                return false;
            }

            var pack = GetNamedInt(attribute, "Pack");
            if (pack == 0)
                pack = 8;
            if (pack is not (1 or 2 or 4 or 8 or 16 or 32 or 64 or 128))
            {
                layout = null!;
                reason = $"pack value {pack} is invalid";
                return false;
            }

            var explicitLayout = layoutKind == 2;
            var offset = 0;
            var maximumEnd = 0;
            var structureAlignment = 1;
            var fields = ImmutableArray.CreateBuilder<CudaFieldLayout>();
            foreach (var field in structure.Fields)
            {
                var offsetAttribute = symbols.GetFieldOffsetAttribute(field.Symbol);
                if (!explicitLayout && offsetAttribute is not null)
                {
                    layout = null!;
                    reason = $"field '{field.Symbol.Name}' has an offset in sequential layout";
                    return false;
                }
                if (explicitLayout &&
                    offsetAttribute?.ConstructorArguments is not
                        [{ Value: int { } }])
                {
                    layout = null!;
                    reason = $"field '{field.Symbol.Name}' requires one nonnegative field offset";
                    return false;
                }

                var elementType = field.InlineArrayLength > 0
                    ? ((IPointerTypeSymbol)field.Symbol.Type).PointedAtType
                    : field.Symbol.Type;
                if (!TryGetTypeLayout(elementType, out var size, out var alignment, out reason))
                {
                    layout = null!;
                    reason = $"field '{field.Symbol.Name}' {reason}";
                    return false;
                }
                if (field.InlineArrayLength > 0)
                    size = checked(size * field.InlineArrayLength);
                alignment = Math.Min(alignment, pack);
                var fieldOffset = explicitLayout
                    ? (int)offsetAttribute!.ConstructorArguments[0].Value!
                    : Align(offset, alignment);
                if (fieldOffset < 0)
                {
                    layout = null!;
                    reason = $"field '{field.Symbol.Name}' has a negative field offset";
                    return false;
                }
                if (fieldOffset % alignment != 0)
                {
                    layout = null!;
                    reason = $"field '{field.Symbol.Name}' has an unaligned explicit offset";
                    return false;
                }
                fields.Add(new CudaFieldLayout(field, fieldOffset, size, alignment));
                var fieldEnd = checked(fieldOffset + size);
                maximumEnd = Math.Max(maximumEnd, fieldEnd);
                if (!explicitLayout)
                    offset = fieldEnd;
                structureAlignment = Math.Max(structureAlignment, alignment);
            }

            var declaredSize = GetNamedInt(attribute, "Size");
            var computedSize = Math.Max(1, Align(maximumEnd, structureAlignment));
            if (declaredSize > 0)
            {
                if (declaredSize < computedSize)
                {
                    layout = null!;
                    reason = $"declared size {declaredSize} is smaller than {computedSize}";
                    return false;
                }
                computedSize = Align(declaredSize, structureAlignment);
            }

            layout = new CudaStructLayout(
                computedSize,
                structureAlignment,
                pack,
                explicitLayout,
                fields.ToImmutable());
            layouts.Add(structure.Symbol, layout);
            reason = string.Empty;
            return true;
        }
        catch (OverflowException)
        {
            layout = null!;
            reason = "the computed layout exceeds supported size limits";
            return false;
        }
        finally
        {
            active.Remove(structure.Symbol);
        }
    }

    private bool TryGetTypeLayout(
        ITypeSymbol type,
        out int size,
        out int alignment,
        out string reason)
    {
        if (symbols.IsCudaInt32Type(type))
        {
            size = alignment = 4;
            reason = string.Empty;
            return true;
        }
        if (type is IPointerTypeSymbol)
        {
            size = alignment = 8;
            reason = string.Empty;
            return true;
        }
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration &&
            enumeration.EnumUnderlyingType is not null)
        {
            return TryGetTypeLayout(enumeration.EnumUnderlyingType, out size, out alignment, out reason);
        }

        (size, alignment) = type.SpecialType switch
        {
            SpecialType.System_Boolean or
            SpecialType.System_SByte or
            SpecialType.System_Byte => (1, 1),
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Char => (2, 2),
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Single => (4, 4),
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Double => (8, 8),
            _ => (0, 0)
        };
        if (size != 0)
        {
            reason = string.Empty;
            return true;
        }

        if (type is INamedTypeSymbol named &&
            structures.TryGetValue(named, out var nested) &&
            TryGetLayout(nested, out var nestedLayout, out reason))
        {
            size = nestedLayout.Size;
            alignment = nestedLayout.Alignment;
            return true;
        }

        size = alignment = 0;
        reason = "uses an unsupported field type";
        return false;
    }

    private static int GetNamedInt(AttributeData? attribute, string name)
    {
        if (attribute is null)
            return 0;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is int value)
                return value;
        }
        return 0;
    }

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}

internal sealed record CudaStructLayout(
    int Size,
    int Alignment,
    int Pack,
    bool IsExplicit,
    ImmutableArray<CudaFieldLayout> Fields);

internal sealed record CudaFieldLayout(
    CudaFieldPlan Field,
    int Offset,
    int Size,
    int Alignment);
