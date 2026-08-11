using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Supprocom.CSharp2CUDA;

internal sealed class CudaModuleEmitter(
    CudaEmissionPlan plan,
    ImmutableArray<Diagnostic>.Builder diagnostics,
    CudaTranspilationOptions options)
{
    public string Emit()
    {
        var sections = new List<string>();
        var structures = plan.Structs.Where(static structure => !structure.IsExternal).ToArray();
        var functions = plan.Functions.Where(static function => !function.IsExternal).ToArray();

        if (functions.Length > 0)
            sections.Add(NormalizeNewLines(IntegerSemantics));
        foreach (var constant in plan.ConstantArrays)
            sections.Add(EmitConstantArray(constant));
        if (structures.Length > 0)
            sections.Add(EmitStructForwardDeclarations(structures));
        foreach (var structure in structures)
            sections.Add(EmitStruct(structure));
        if (functions.Length > 0)
            sections.Add(EmitFunctionPrototypes(functions));
        foreach (var function in functions)
            sections.Add(EmitFunction(function));

        return string.Join(options.NewLine + options.NewLine, sections);
    }

    private string EmitConstantArray(CudaConstantArrayPlan constant)
    {
        using var output = CreateWriter();
        output.Write("__device__ __constant__ int ");
        output.Write(constant.EmittedName);
        output.Write('[');
        output.Write(constant.Values.Length.ToString(CultureInfo.InvariantCulture));
        output.Write("] = { ");
        for (var index = 0; index < constant.Values.Length; index++)
        {
            if (index > 0)
                output.Write(", ");
            var value = constant.Values[index];
            output.Write(value == int.MinValue
                ? "(-2147483647 - 1)"
                : value.ToString(CultureInfo.InvariantCulture));
        }
        output.Write(" };");
        return output.ToString();
    }

    private string EmitStructForwardDeclarations(IEnumerable<CudaStructPlan> structures)
    {
        using var output = CreateWriter();
        var first = true;
        foreach (var structure in structures)
        {
            if (!first)
                output.WriteLine();
            output.Write("struct ");
            output.Write(structure.EmittedName);
            output.Write(';');
            first = false;
        }
        return output.ToString();
    }

    private string EmitStruct(CudaStructPlan structure)
    {
        using var output = CreateWriter();
        output.Write("struct ");
        output.Write(structure.EmittedName);
        output.WriteLine();
        output.WriteLine("{");
        foreach (var field in structure.Fields)
        {
            output.Write("    ");
            output.Write(plan.FormatType(
                field.Symbol.Type,
                false,
                field.Declaration.Declaration.Type.GetLocation()));
            output.Write(' ');
            output.Write(plan.GetIdentifier(field.Symbol));
            output.WriteLine(";");
        }
        output.Write("};");
        return output.ToString();
    }

    private string EmitFunctionPrototypes(IEnumerable<CudaFunctionPlan> functions)
    {
        using var output = CreateWriter();
        var first = true;
        foreach (var function in functions)
        {
            if (!first)
                output.WriteLine();
            EmitFunctionHeader(output, function);
            output.Write(';');
            first = false;
        }
        return output.ToString();
    }

    private string EmitFunction(CudaFunctionPlan function)
    {
        using var output = CreateWriter();
        EmitFunctionHeader(output, function);
        output.WriteLine();
        output.Write(TranslateBody(function));
        return output.ToString();
    }

    private void EmitFunctionHeader(TextWriter output, CudaFunctionPlan function)
    {
        if (function.Kind == CudaFunctionKind.Device)
        {
            output.Write("__device__ ");
        }
        else
        {
            if (function.ExternC)
                output.Write("extern \"C\" ");
            output.Write("__global__ ");
        }

        output.Write(plan.FormatType(
            function.Symbol.ReturnType,
            false,
            function.Syntax.ReturnType.GetLocation()));
        output.Write(' ');
        output.Write(function.EmittedName);
        EmitParameters(output, function);
    }

    private void EmitParameters(TextWriter output, CudaFunctionPlan function)
    {
        var parameters = function.Syntax.ParameterList;
        var parameterText = parameters.SyntaxTree.GetText().ToString(parameters.Span);
        var multiline = parameterText.Contains('\n') || parameterText.Contains('\r');
        output.Write('(');
        for (var index = 0; index < parameters.Parameters.Count; index++)
        {
            if (multiline)
            {
                output.WriteLine();
                output.Write("    ");
            }
            output.Write(plan.FormatParameterType(function, index));
            output.Write(' ');
            output.Write(plan.GetIdentifier(function.Symbol.Parameters[index]));
            if (index + 1 < parameters.Parameters.Count)
            {
                output.Write(',');
                if (!multiline)
                    output.Write(' ');
            }
        }
        output.Write(')');
    }

    private string TranslateBody(CudaFunctionPlan function)
    {
        var translator = new CudaSyntaxTranslator(plan, function.Model, diagnostics);
        var rewritten = (BlockSyntax)translator.Visit(function.Syntax.Body)!;
        var text = rewritten.WithoutLeadingTrivia().WithoutTrailingTrivia().ToFullString();
        text = translator.ExpandFixedLocalArrays(text);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("    ", StringComparison.Ordinal))
                lines[index] = lines[index][4..];
        }
        return string.Join(options.NewLine, lines);
    }

    private StringWriter CreateWriter() => new() { NewLine = options.NewLine };

    private string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", options.NewLine, StringComparison.Ordinal);

    private const string IntegerSemantics = """
        #ifndef CSHARP2CUDA_INTEGER_SEMANTICS_0_1
        #define CSHARP2CUDA_INTEGER_SEMANTICS_0_1
        static_assert(sizeof(int) == 4, "CSharp2CUDA requires a 32-bit CUDA int.");
        static_assert(sizeof(long long) == 8, "CSharp2CUDA requires a 64-bit CUDA long long.");

        static __device__ __forceinline__ int csharp2cuda_i32_from_bits(unsigned int bits)
        {
            return bits <= 0x7fffffffu ? (int)bits : -1 - (int)(~bits);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_from_bits(unsigned long long bits)
        {
            return bits <= 0x7fffffffffffffffull ? (long long)bits : -1LL - (long long)(~bits);
        }

        template <typename T>
        static __device__ __forceinline__ T* csharp2cuda_pointer_add(T* pointer, int offset)
        {
            unsigned long long address = (unsigned long long)pointer;
            unsigned long long displacement =
                (unsigned long long)(long long)offset * (unsigned long long)sizeof(T);
            return (T*)(address + displacement);
        }

        template <typename T>
        static __device__ __forceinline__ T* csharp2cuda_pointer_add_reverse(int offset, T* pointer)
        {
            return csharp2cuda_pointer_add(pointer, offset);
        }

        static __device__ __forceinline__ double csharp2cuda_f64_maximum(double left, double right)
        {
            if (left != right)
            {
                if (!isnan(left))
                    return right < left ? left : right;
                return left;
            }
            return signbit(right) ? left : right;
        }

        static __device__ __forceinline__ double csharp2cuda_f64_minimum(double left, double right)
        {
            if (left != right)
            {
                if (!isnan(left))
                    return left < right ? left : right;
                return left;
            }
            return signbit(left) ? left : right;
        }

        static __device__ __forceinline__ int csharp2cuda_i32_add(int left, int right)
        {
            return csharp2cuda_i32_from_bits((unsigned int)left + (unsigned int)right);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_sub(int left, int right)
        {
            return csharp2cuda_i32_from_bits((unsigned int)left - (unsigned int)right);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_mul(int left, int right)
        {
            return csharp2cuda_i32_from_bits((unsigned int)left * (unsigned int)right);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_div(int left, int right)
        {
            if (right == 0 || (left == (-2147483647 - 1) && right == -1))
            {
                __trap();
                return 0;
            }
            return left / right;
        }

        static __device__ __forceinline__ int csharp2cuda_i32_rem(int left, int right)
        {
            if (right == 0)
            {
                __trap();
                return 0;
            }
            if (left == (-2147483647 - 1) && right == -1)
                return 0;
            return left % right;
        }

        static __device__ __forceinline__ int csharp2cuda_i32_and(int left, int right)
        {
            return csharp2cuda_i32_from_bits((unsigned int)left & (unsigned int)right);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_or(int left, int right)
        {
            return csharp2cuda_i32_from_bits((unsigned int)left | (unsigned int)right);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_xor(int left, int right)
        {
            return csharp2cuda_i32_from_bits((unsigned int)left ^ (unsigned int)right);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_not(int value)
        {
            return csharp2cuda_i32_from_bits(~(unsigned int)value);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_neg(int value)
        {
            return csharp2cuda_i32_from_bits(0u - (unsigned int)value);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_shl(int value, int count)
        {
            unsigned int shift = (unsigned int)count & 31u;
            return csharp2cuda_i32_from_bits((unsigned int)value << shift);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_shr(int value, int count)
        {
            unsigned int shift = (unsigned int)count & 31u;
            if (shift == 0u)
                return value;
            unsigned int bits = (unsigned int)value >> shift;
            if (value < 0)
                bits |= ~0u << (32u - shift);
            return csharp2cuda_i32_from_bits(bits);
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_div(unsigned int left, unsigned int right)
        {
            if (right == 0u)
            {
                __trap();
                return 0u;
            }
            return left / right;
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_rem(unsigned int left, unsigned int right)
        {
            if (right == 0u)
            {
                __trap();
                return 0u;
            }
            return left % right;
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_shl(unsigned int value, int count)
        {
            return value << ((unsigned int)count & 31u);
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_shr(unsigned int value, int count)
        {
            return value >> ((unsigned int)count & 31u);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_add(long long left, long long right)
        {
            return csharp2cuda_i64_from_bits((unsigned long long)left + (unsigned long long)right);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_sub(long long left, long long right)
        {
            return csharp2cuda_i64_from_bits((unsigned long long)left - (unsigned long long)right);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_mul(long long left, long long right)
        {
            return csharp2cuda_i64_from_bits((unsigned long long)left * (unsigned long long)right);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_div(long long left, long long right)
        {
            if (right == 0LL ||
                (left == (-9223372036854775807LL - 1LL) && right == -1LL))
            {
                __trap();
                return 0LL;
            }
            return left / right;
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_rem(long long left, long long right)
        {
            if (right == 0LL)
            {
                __trap();
                return 0LL;
            }
            if (left == (-9223372036854775807LL - 1LL) && right == -1LL)
                return 0LL;
            return left % right;
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_and(long long left, long long right)
        {
            return csharp2cuda_i64_from_bits((unsigned long long)left & (unsigned long long)right);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_or(long long left, long long right)
        {
            return csharp2cuda_i64_from_bits((unsigned long long)left | (unsigned long long)right);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_xor(long long left, long long right)
        {
            return csharp2cuda_i64_from_bits((unsigned long long)left ^ (unsigned long long)right);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_not(long long value)
        {
            return csharp2cuda_i64_from_bits(~(unsigned long long)value);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_neg(long long value)
        {
            return csharp2cuda_i64_from_bits(0ull - (unsigned long long)value);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_shl(long long value, int count)
        {
            unsigned int shift = (unsigned int)count & 63u;
            return csharp2cuda_i64_from_bits((unsigned long long)value << shift);
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_shr(long long value, int count)
        {
            unsigned int shift = (unsigned int)count & 63u;
            if (shift == 0u)
                return value;
            unsigned long long bits = (unsigned long long)value >> shift;
            if (value < 0LL)
                bits |= ~0ull << (64u - shift);
            return csharp2cuda_i64_from_bits(bits);
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_div(unsigned long long left, unsigned long long right)
        {
            if (right == 0ull)
            {
                __trap();
                return 0ull;
            }
            return left / right;
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_rem(unsigned long long left, unsigned long long right)
        {
            if (right == 0ull)
            {
                __trap();
                return 0ull;
            }
            return left % right;
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shl(unsigned long long value, int count)
        {
            return value << ((unsigned int)count & 63u);
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shr(unsigned long long value, int count)
        {
            return value >> ((unsigned int)count & 63u);
        }

        static __device__ __forceinline__ int csharp2cuda_i32_add_assign(int& target, int value) { return target = csharp2cuda_i32_add(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_sub_assign(int& target, int value) { return target = csharp2cuda_i32_sub(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_mul_assign(int& target, int value) { return target = csharp2cuda_i32_mul(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_div_assign(int& target, int value) { return target = csharp2cuda_i32_div(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_rem_assign(int& target, int value) { return target = csharp2cuda_i32_rem(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_and_assign(int& target, int value) { return target = csharp2cuda_i32_and(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_or_assign(int& target, int value) { return target = csharp2cuda_i32_or(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_xor_assign(int& target, int value) { return target = csharp2cuda_i32_xor(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_shl_assign(int& target, int value) { return target = csharp2cuda_i32_shl(target, value); }
        static __device__ __forceinline__ int csharp2cuda_i32_shr_assign(int& target, int value) { return target = csharp2cuda_i32_shr(target, value); }

        static __device__ __forceinline__ long long csharp2cuda_i64_add_assign(long long& target, long long value) { return target = csharp2cuda_i64_add(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_sub_assign(long long& target, long long value) { return target = csharp2cuda_i64_sub(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_mul_assign(long long& target, long long value) { return target = csharp2cuda_i64_mul(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_div_assign(long long& target, long long value) { return target = csharp2cuda_i64_div(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_rem_assign(long long& target, long long value) { return target = csharp2cuda_i64_rem(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_and_assign(long long& target, long long value) { return target = csharp2cuda_i64_and(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_or_assign(long long& target, long long value) { return target = csharp2cuda_i64_or(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_xor_assign(long long& target, long long value) { return target = csharp2cuda_i64_xor(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_shl_assign(long long& target, int value) { return target = csharp2cuda_i64_shl(target, value); }
        static __device__ __forceinline__ long long csharp2cuda_i64_shr_assign(long long& target, int value) { return target = csharp2cuda_i64_shr(target, value); }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_div_assign(unsigned int& target, unsigned int value) { return target = csharp2cuda_u32_div(target, value); }
        static __device__ __forceinline__ unsigned int csharp2cuda_u32_rem_assign(unsigned int& target, unsigned int value) { return target = csharp2cuda_u32_rem(target, value); }
        static __device__ __forceinline__ unsigned int csharp2cuda_u32_shl_assign(unsigned int& target, int value) { return target = csharp2cuda_u32_shl(target, value); }
        static __device__ __forceinline__ unsigned int csharp2cuda_u32_shr_assign(unsigned int& target, int value) { return target = csharp2cuda_u32_shr(target, value); }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_div_assign(unsigned long long& target, unsigned long long value) { return target = csharp2cuda_u64_div(target, value); }
        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_rem_assign(unsigned long long& target, unsigned long long value) { return target = csharp2cuda_u64_rem(target, value); }
        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shl_assign(unsigned long long& target, int value) { return target = csharp2cuda_u64_shl(target, value); }
        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shr_assign(unsigned long long& target, int value) { return target = csharp2cuda_u64_shr(target, value); }

        static __device__ __forceinline__ int csharp2cuda_i32_pre_increment(int& target) { return target = csharp2cuda_i32_add(target, 1); }
        static __device__ __forceinline__ int csharp2cuda_i32_post_increment(int& target) { int result = target; target = csharp2cuda_i32_add(target, 1); return result; }
        static __device__ __forceinline__ int csharp2cuda_i32_pre_decrement(int& target) { return target = csharp2cuda_i32_sub(target, 1); }
        static __device__ __forceinline__ int csharp2cuda_i32_post_decrement(int& target) { int result = target; target = csharp2cuda_i32_sub(target, 1); return result; }
        static __device__ __forceinline__ long long csharp2cuda_i64_pre_increment(long long& target) { return target = csharp2cuda_i64_add(target, 1LL); }
        static __device__ __forceinline__ long long csharp2cuda_i64_post_increment(long long& target) { long long result = target; target = csharp2cuda_i64_add(target, 1LL); return result; }
        static __device__ __forceinline__ long long csharp2cuda_i64_pre_decrement(long long& target) { return target = csharp2cuda_i64_sub(target, 1LL); }
        static __device__ __forceinline__ long long csharp2cuda_i64_post_decrement(long long& target) { long long result = target; target = csharp2cuda_i64_sub(target, 1LL); return result; }
        #endif
        """;
}
