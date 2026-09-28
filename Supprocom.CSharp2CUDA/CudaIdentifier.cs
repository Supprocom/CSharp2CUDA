using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Supprocom.CSharp2CUDA.Compilation;
using Supprocom.CSharp2CUDA.Emission;
using Supprocom.CSharp2CUDA.Semantics;

namespace Supprocom.CSharp2CUDA;

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
