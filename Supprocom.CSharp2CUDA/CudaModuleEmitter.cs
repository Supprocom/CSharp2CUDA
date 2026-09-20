using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Emission;

namespace Supprocom.CSharp2CUDA;

internal sealed class CudaModuleEmitter(
    CudaEmissionPlan plan,
    CudaTranspilationOptions options)
{
    private readonly CudaSourcePathMapper sourceMapper = new(options.SourceRoot);

    public CudaModuleEmission Emit()
    {
        var sections = new List<CudaModuleSection>();
        var structures = plan.Structs.Where(static structure => !structure.IsExternal).ToArray();
        var functions = plan.Functions.Where(static function => !function.IsExternal).ToArray();
        var prototypes = plan.Functions.Where(static function =>
            !function.IsExternal || function.EmitsDeclaration).ToArray();

        if (functions.Length > 0)
            sections.Add(CudaModuleSection.Raw(NormalizeNewLines(IntegerSemantics)));
        if (plan.UsesTupleTypes)
            sections.Add(CudaModuleSection.Raw(NormalizeNewLines(TupleTypes)));
        if (plan.UsesArrayViews)
            sections.Add(CudaModuleSection.Raw(NormalizeNewLines(ArrayViewTypes)));
        if (plan.UsesVolatileMappedMemory)
            sections.Add(CudaModuleSection.Raw(NormalizeNewLines(VolatileMappedMemoryIntrinsics)));
        if (plan.UsesGlobalTimer)
            sections.Add(CudaModuleSection.Raw(NormalizeNewLines(GlobalTimerIntrinsic)));
        if (structures.Any(plan.RequiresAbiLayout))
            sections.Add(CudaModuleSection.Raw("#include <stddef.h>"));
        foreach (var constant in plan.ConstantArrays)
        {
            sections.Add(CreateMappedSection(
                EmitConstantArray(constant),
                constant.Declaration.GetLocation()));
        }
        if (structures.Length > 0)
        {
            sections.Add(CudaModuleSection.Raw(
                EmitStructForwardDeclarations(structures)));
        }
        foreach (var structure in structures)
        {
            sections.Add(CreateMappedSection(
                EmitStruct(structure),
                structure.Syntax.GetLocation()));
        }
        foreach (var function in prototypes)
            sections.Add(EmitFunctionPrototype(function));
        foreach (var function in functions)
            sections.Add(EmitFunction(function));

        var source = string.Join(
            options.NewLine + options.NewLine,
            sections.Select(static section => section.Source));
        var sourceMap = CombineSourceMaps(sections);
        var entryPoints = plan.Functions
            .Where(static function => function.Kind == CudaFunctionKind.Global)
            .Select(static function => new CudaEntryPoint(
                function.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                function.EmittedName))
            .ToImmutableArray();
        return new CudaModuleEmission(source, entryPoints, sourceMap);
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
        if (structure.Layout is { Pack: not 8 } packedLayout)
        {
            output.Write("#pragma pack(push, ");
            output.Write(packedLayout.Pack.ToString(CultureInfo.InvariantCulture));
            output.WriteLine(")");
        }
        output.Write("struct ");
        if (structure.Layout is { IsExplicit: true } explicitLayout)
        {
            output.Write("alignas(");
            output.Write(explicitLayout.Alignment.ToString(CultureInfo.InvariantCulture));
            output.Write(") ");
        }
        output.Write(structure.EmittedName);
        output.WriteLine();
        output.WriteLine("{");
        if (structure.Layout is { IsExplicit: true } rawLayout)
        {
            output.Write("    unsigned char csharp2cuda_storage[");
            output.Write(rawLayout.Size.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("];");
        }
        else
        {
            foreach (var field in structure.Fields)
            {
                output.Write("    ");
                var fieldType = field.InlineArrayLength > 0
                    ? ((IPointerTypeSymbol)field.Symbol.Type).PointedAtType
                    : field.Symbol.Type;
                output.Write(plan.FormatType(
                    fieldType,
                    false,
                    field.Declaration.Declaration.Type.GetLocation()));
                output.Write(' ');
                output.Write(plan.GetIdentifier(field.Symbol));
                if (field.InlineArrayLength > 0)
                {
                    output.Write('[');
                    output.Write(field.InlineArrayLength.ToString(CultureInfo.InvariantCulture));
                    output.Write(']');
                }
                output.WriteLine(";");
            }
        }
        foreach (var property in structure.Properties)
        {
            output.Write("    ");
            output.Write(plan.FormatType(
                property.Symbol.Type,
                false,
                property.TypeSyntax.GetLocation()));
            output.Write(' ');
            output.Write(plan.GetIdentifier(property.Symbol));
            output.WriteLine(";");
        }
        output.Write("};");
        if (structure.Layout is { Pack: not 8 })
        {
            output.WriteLine();
            output.Write("#pragma pack(pop)");
        }
        if (structure.Layout is { } layout && plan.RequiresAbiLayout(structure))
        {
            output.WriteLine();
            output.Write("static_assert(sizeof(");
            output.Write(structure.EmittedName);
            output.Write(") == ");
            output.Write(layout.Size.ToString(CultureInfo.InvariantCulture));
            output.WriteLine(", \"CUDA structure size mismatch\");");
            output.Write("static_assert(alignof(");
            output.Write(structure.EmittedName);
            output.Write(") == ");
            output.Write(layout.Alignment.ToString(CultureInfo.InvariantCulture));
            output.WriteLine(", \"CUDA structure alignment mismatch\");");
            foreach (var field in layout.Fields)
            {
                if (!layout.IsExplicit)
                {
                    output.Write("static_assert(offsetof(");
                    output.Write(structure.EmittedName);
                    output.Write(", ");
                    output.Write(plan.GetIdentifier(field.Field.Symbol));
                    output.Write(") == ");
                    output.Write(field.Offset.ToString(CultureInfo.InvariantCulture));
                    output.WriteLine(", \"CUDA structure field offset mismatch\");");
                }
            }
        }
        return output.ToString();
    }

    private CudaModuleSection EmitFunctionPrototype(CudaFunctionPlan function)
    {
        using var output = CreateWriter();
        EmitFunctionHeader(output, function);
        output.Write(';');
        return CreateMappedSection(output.ToString(), function.Syntax.GetLocation());
    }

    private CudaModuleSection EmitFunction(CudaFunctionPlan function)
    {
        using var output = CreateWriter();
        EmitFunctionHeader(output, function);
        output.WriteLine();
        var header = CreateMappedSection(
            output.ToString(),
            function.Syntax.GetLocation());
        var body = TranslateBody(function);
        var sourceMap = ImmutableArray.CreateBuilder<CudaSourceMapEntry>();
        sourceMap.AddRange(header.SourceMap);
        var bodyLineOffset = CountLinesBeforeNextText(header.Source);
        foreach (var entry in body.SourceMap)
        {
            sourceMap.Add(entry with
            {
                GeneratedLine = entry.GeneratedLine + bodyLineOffset
            });
        }
        return new CudaModuleSection(
            header.Source + body.Source,
            sourceMap.ToImmutable());
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

        var returnType = plan.FormatType(
            function.IsConstructor
                ? function.Symbol.ContainingType
                : function.Symbol.ReturnType,
            false,
            function.Syntax.ReturnType?.GetLocation() ??
                function.Syntax.Identifier.GetLocation());
        if (!function.IsConstructor &&
            (function.Symbol.ReturnsByRef || function.Symbol.ReturnsByRefReadonly))
        {
            if (function.Symbol.ReturnsByRefReadonly)
                output.Write("const ");
            output.Write(returnType);
            output.Write('*');
        }
        else
        {
            output.Write(returnType);
        }
        output.Write(' ');
        output.Write(function.EmittedName);
        EmitParameters(output, function);
    }

    private void EmitParameters(TextWriter output, CudaFunctionPlan function)
    {
        var parameters = function.Syntax.ParameterList;
        var parameterText = parameters.SyntaxTree is { } tree
            ? tree.GetText().ToString(parameters.Span)
            : parameters.ToString();
        var multiline = parameterText.Contains('\n') || parameterText.Contains('\r');
        output.Write('(');
        var wroteParameter = false;
        if (function.HasInstance)
        {
            if (multiline)
            {
                output.WriteLine();
                output.Write("    ");
            }
            if (function.IsReadOnlyInstance)
                output.Write("const ");
            output.Write(plan.FormatType(
                function.Symbol.ContainingType,
                false,
                function.Syntax.Identifier.GetLocation()));
            output.Write("* csharp2cuda_this");
            wroteParameter = true;
        }
        for (var index = 0; index < parameters.Parameters.Count; index++)
        {
            if (wroteParameter)
            {
                output.Write(',');
                if (!multiline)
                    output.Write(' ');
            }
            if (multiline)
            {
                output.WriteLine();
                output.Write("    ");
            }
            output.Write(plan.FormatParameterType(function, index));
            output.Write(' ');
            output.Write(plan.GetIdentifier(function.Symbol.Parameters[index]));
            wroteParameter = true;
        }
        foreach (var capture in function.Captures)
        {
            if (wroteParameter)
            {
                output.Write(',');
                if (!multiline)
                    output.Write(' ');
            }
            if (multiline)
            {
                output.WriteLine();
                output.Write("    ");
            }
            if (capture.ByReference && capture.IsReadOnly)
                output.Write("const ");
            output.Write(plan.FormatType(
                capture.Type,
                false,
                capture.Symbol.Locations.FirstOrDefault() ?? function.Syntax.GetLocation()));
            if (capture.ByReference)
                output.Write('*');
            output.Write(' ');
            output.Write(capture.EmittedName);
            wroteParameter = true;
        }
        output.Write(')');
    }

    private CudaBodyEmission TranslateBody(CudaFunctionPlan function)
    {
        if (function.Body is null)
            return new CudaBodyEmission(string.Empty, []);
        return new CudaCppBodyEmitter(
            options.NewLine,
            sourceMapper,
            options.EmitLineDirectives).Emit(function.Body);
    }

    private CudaModuleSection CreateMappedSection(string source, Location location)
    {
        var mapped = sourceMapper.Map(location);
        if (mapped is null)
            return CudaModuleSection.Raw(source);

        var generatedLine = 1;
        if (options.EmitLineDirectives)
        {
            source = $"#line {mapped.SourceLine.ToString(CultureInfo.InvariantCulture)} \"{CudaSourcePathMapper.EscapeDirectivePath(mapped.SourcePath)}\"{options.NewLine}{source}";
            generatedLine++;
        }

        return new CudaModuleSection(
            source,
            [new CudaSourceMapEntry(mapped.SourcePath, mapped.SourceLine, generatedLine)]);
    }

    private ImmutableArray<CudaSourceMapEntry> CombineSourceMaps(
        IReadOnlyList<CudaModuleSection> sections)
    {
        var result = ImmutableArray.CreateBuilder<CudaSourceMapEntry>();
        var sectionStartLine = 1;
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            foreach (var entry in section.SourceMap)
            {
                result.Add(entry with
                {
                    GeneratedLine = entry.GeneratedLine + sectionStartLine - 1
                });
            }
            if (index + 1 < sections.Count)
                sectionStartLine += CountNewLines(section.Source) + 2;
        }
        return result.ToImmutable();
    }

    private static int CountLinesBeforeNextText(string text) => CountNewLines(text);

    private static int CountNewLines(string text) => text.Count(static character => character == '\n');

    private StringWriter CreateWriter() => new() { NewLine = options.NewLine };

    private string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", options.NewLine, StringComparison.Ordinal);

    private const string VolatileMappedMemoryIntrinsics = """
        #ifndef CSHARP2CUDA_VOLATILE_MAPPED_MEMORY_0_1
        #define CSHARP2CUDA_VOLATILE_MAPPED_MEMORY_0_1
        static __device__ __forceinline__ int csharp2cuda_volatile_load_i32(
            const int* address)
        {
            return *((const volatile int*)address);
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_volatile_load_u64(
            const unsigned long long* address)
        {
            return *((const volatile unsigned long long*)address);
        }

        static __device__ __forceinline__ int csharp2cuda_volatile_load_i32_bytes(
            const unsigned char* address,
            unsigned long long byte_offset)
        {
            return *((const volatile int*)(address + byte_offset));
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_volatile_load_u64_bytes(
            const unsigned char* address,
            unsigned long long byte_offset)
        {
            return *((const volatile unsigned long long*)(address + byte_offset));
        }

        static __device__ __forceinline__ void csharp2cuda_volatile_store_i32(
            int* address,
            int value)
        {
            *((volatile int*)address) = value;
        }

        static __device__ __forceinline__ void csharp2cuda_volatile_store_u64(
            unsigned long long* address,
            unsigned long long value)
        {
            *((volatile unsigned long long*)address) = value;
        }

        static __device__ __forceinline__ void csharp2cuda_volatile_store_i32_bytes(
            unsigned char* address,
            unsigned long long byte_offset,
            int value)
        {
            *((volatile int*)(address + byte_offset)) = value;
        }

        static __device__ __forceinline__ void csharp2cuda_volatile_store_u64_bytes(
            unsigned char* address,
            unsigned long long byte_offset,
            unsigned long long value)
        {
            *((volatile unsigned long long*)(address + byte_offset)) = value;
        }
        #endif
        """;

    private const string ArrayViewTypes = """
        #ifndef CSHARP2CUDA_ARRAY_VIEWS_0_3
        #define CSHARP2CUDA_ARRAY_VIEWS_0_3
        template <typename T>
        struct csharp2cuda_readonly_array_view
        {
            const T* data;
            int length;
            bool is_null;

            __device__ csharp2cuda_readonly_array_view()
                : data(nullptr), length(0), is_null(false)
            {
            }

            __device__ csharp2cuda_readonly_array_view(const T* address, int count)
                : data(address), length(count), is_null(false)
            {
                if (count < 0 || (address == nullptr && count != 0))
                    asm volatile("trap;");
            }

            __device__ csharp2cuda_readonly_array_view(
                const T* address,
                int count,
                bool null_state)
                : data(address), length(count), is_null(null_state)
            {
                if (count < 0) asm volatile("trap;");
            }

            __device__ int get_length() const
            {
                if (is_null) asm volatile("trap;");
                return length;
            }

            __device__ bool is_empty() const
            {
                return get_length() == 0;
            }

            __device__ const T& operator[](int index) const
            {
                if (is_null || (unsigned int)index >= (unsigned int)length)
                    asm volatile("trap;");
                return data[index];
            }

            __device__ csharp2cuda_readonly_array_view<T> slice(int start) const
            {
                if (is_null || (unsigned int)start > (unsigned int)length)
                    asm volatile("trap;");
                return csharp2cuda_readonly_array_view<T>(
                    data == nullptr ? nullptr : data + start,
                    length - start);
            }

            __device__ csharp2cuda_readonly_array_view<T> slice(int start, int count) const
            {
                if (is_null ||
                    (unsigned int)start > (unsigned int)length ||
                    (unsigned int)count > (unsigned int)(length - start))
                    asm volatile("trap;");
                return csharp2cuda_readonly_array_view<T>(
                    data == nullptr ? nullptr : data + start,
                    count);
            }

            template <typename TDestination>
            __device__ void copy_to(TDestination destination) const
            {
                if (!try_copy_to(destination)) asm volatile("trap;");
            }

            template <typename TDestination>
            __device__ bool try_copy_to(TDestination destination) const
            {
                if (is_null || destination.is_null) asm volatile("trap;");
                if (destination.length < length)
                    return false;
                unsigned long long source_address = (unsigned long long)data;
                unsigned long long destination_address =
                    (unsigned long long)destination.data;
                unsigned long long byte_length =
                    (unsigned long long)length * (unsigned long long)sizeof(T);
                if (destination_address > source_address &&
                    destination_address < source_address + byte_length)
                {
                    for (int index = length; index-- > 0;)
                        destination.data[index] = data[index];
                }
                else
                {
                    for (int index = 0; index < length; index++)
                        destination.data[index] = data[index];
                }
                return true;
            }
        };

        template <typename T>
        struct csharp2cuda_array_view
        {
            T* data;
            int length;
            bool is_null;

            __device__ csharp2cuda_array_view()
                : data(nullptr), length(0), is_null(false)
            {
            }

            __device__ csharp2cuda_array_view(T* address, int count)
                : data(address), length(count), is_null(false)
            {
                if (count < 0 || (address == nullptr && count != 0))
                    asm volatile("trap;");
            }

            __device__ csharp2cuda_array_view(T* address, int count, bool null_state)
                : data(address), length(count), is_null(null_state)
            {
                if (count < 0) asm volatile("trap;");
            }

            __device__ int get_length() const
            {
                if (is_null) asm volatile("trap;");
                return length;
            }

            __device__ bool is_empty() const
            {
                return get_length() == 0;
            }

            __device__ T& operator[](int index) const
            {
                if (is_null || (unsigned int)index >= (unsigned int)length)
                    asm volatile("trap;");
                return data[index];
            }

            __device__ csharp2cuda_array_view<T> slice(int start) const
            {
                if (is_null || (unsigned int)start > (unsigned int)length)
                    asm volatile("trap;");
                return csharp2cuda_array_view<T>(
                    data == nullptr ? nullptr : data + start,
                    length - start);
            }

            __device__ csharp2cuda_array_view<T> slice(int start, int count) const
            {
                if (is_null ||
                    (unsigned int)start > (unsigned int)length ||
                    (unsigned int)count > (unsigned int)(length - start))
                    asm volatile("trap;");
                return csharp2cuda_array_view<T>(
                    data == nullptr ? nullptr : data + start,
                    count);
            }

            __device__ csharp2cuda_array_view<T> as_span() const
            {
                return is_null
                    ? csharp2cuda_array_view<T>(nullptr, 0, false)
                    : csharp2cuda_array_view<T>(data, length, false);
            }

            __device__ void clear() const
            {
                if (is_null) asm volatile("trap;");
                for (int index = 0; index < length; index++)
                    data[index] = T{};
            }

            __device__ void fill(T value) const
            {
                if (is_null) asm volatile("trap;");
                for (int index = 0; index < length; index++)
                    data[index] = value;
            }

            __device__ void copy_to(csharp2cuda_array_view<T> destination) const
            {
                if (!try_copy_to(destination)) asm volatile("trap;");
            }

            __device__ bool try_copy_to(csharp2cuda_array_view<T> destination) const
            {
                if (is_null || destination.is_null) asm volatile("trap;");
                if (destination.length < length)
                    return false;
                unsigned long long source_address = (unsigned long long)data;
                unsigned long long destination_address =
                    (unsigned long long)destination.data;
                unsigned long long byte_length =
                    (unsigned long long)length * (unsigned long long)sizeof(T);
                if (destination_address > source_address &&
                    destination_address < source_address + byte_length)
                {
                    for (int index = length; index-- > 0;)
                        destination.data[index] = data[index];
                }
                else
                {
                    for (int index = 0; index < length; index++)
                        destination.data[index] = data[index];
                }
                return true;
            }

            __device__ operator csharp2cuda_readonly_array_view<T>() const
            {
                return csharp2cuda_readonly_array_view<T>(data, length, is_null);
            }
        };

        template <typename TSourceView, typename T>
        static __device__ __forceinline__ csharp2cuda_array_view<T>
        csharp2cuda_copy_array(
            TSourceView source,
            T* destination,
            int capacity)
        {
            if (source.is_null || source.length < 0 || source.length > capacity)
                asm volatile("trap;");
            for (int index = 0; index < source.length; index++)
                destination[index] = source.data[index];
            return csharp2cuda_array_view<T>(destination, source.length, false);
        }
        #endif
        """;

    private const string TupleTypes = """
        #ifndef CSHARP2CUDA_TUPLE_TYPES_0_3_1
        #define CSHARP2CUDA_TUPLE_TYPES_0_3_1
        template <typename T1, typename T2>
        struct csharp2cuda_tuple2
        {
            T1 item1;
            T2 item2;
        };

        template <typename T1, typename T2, typename T3>
        struct csharp2cuda_tuple3
        {
            T1 item1;
            T2 item2;
            T3 item3;
        };

        template <typename T1, typename T2, typename T3, typename T4>
        struct csharp2cuda_tuple4
        {
            T1 item1;
            T2 item2;
            T3 item3;
            T4 item4;
        };

        template <typename T1, typename T2, typename T3, typename T4, typename T5>
        struct csharp2cuda_tuple5
        {
            T1 item1;
            T2 item2;
            T3 item3;
            T4 item4;
            T5 item5;
        };

        template <typename T1, typename T2, typename T3, typename T4, typename T5,
            typename T6>
        struct csharp2cuda_tuple6
        {
            T1 item1;
            T2 item2;
            T3 item3;
            T4 item4;
            T5 item5;
            T6 item6;
        };

        template <typename T1, typename T2, typename T3, typename T4, typename T5,
            typename T6, typename T7>
        struct csharp2cuda_tuple7
        {
            T1 item1;
            T2 item2;
            T3 item3;
            T4 item4;
            T5 item5;
            T6 item6;
            T7 item7;
        };
        #endif
        """;

    private const string GlobalTimerIntrinsic = """
        #ifndef CSHARP2CUDA_GLOBAL_TIMER_0_1
        #define CSHARP2CUDA_GLOBAL_TIMER_0_1
        static __device__ __forceinline__ unsigned long long csharp2cuda_global_timer()
        {
            unsigned long long value;
            asm volatile("mov.u64 %0, %%globaltimer;" : "=l"(value));
            return value;
        }
        #endif
        """;

    private const string IntegerSemantics = """
        #ifndef CSHARP2CUDA_INTEGER_SEMANTICS_0_1
        #define CSHARP2CUDA_INTEGER_SEMANTICS_0_1
        static_assert(sizeof(unsigned short) == 2, "C# char requires 16 bits");
        static_assert(sizeof(int) == 4, "CSharp2CUDA requires a 32-bit CUDA int.");
        static_assert(sizeof(long long) == 8, "CSharp2CUDA requires a 64-bit CUDA long long.");

        static __device__ __forceinline__ int csharp2cuda_index_from_end(
            int length,
            int value)
        {
            if (value < 0) __trap();
            return length - value;
        }

        static __device__ __forceinline__ int csharp2cuda_i32_from_bits(unsigned int bits)
        {
            return bits <= 0x7fffffffu ? (int)bits : -1 - (int)(~bits);
        }

        static __device__ __forceinline__ signed char csharp2cuda_i8_from_bits(
            unsigned int bits)
        {
            unsigned int value = bits & 0xffu;
            return value <= 0x7fu
                ? (signed char)value
                : (signed char)(-1 - (int)((~value) & 0xffu));
        }

        static __device__ __forceinline__ short csharp2cuda_i16_from_bits(
            unsigned int bits)
        {
            unsigned int value = bits & 0xffffu;
            return value <= 0x7fffu
                ? (short)value
                : (short)(-1 - (int)((~value) & 0xffffu));
        }

        static __device__ __forceinline__ long long csharp2cuda_i64_from_bits(unsigned long long bits)
        {
            return bits <= 0x7fffffffffffffffull ? (long long)bits : -1LL - (long long)(~bits);
        }

        static __device__ __forceinline__ int csharp2cuda_f64_to_i32(double value)
        {
            if (isnan(value)) return 0;
            if (value >= 2147483648.0) return 2147483647;
            if (value <= -2147483648.0) return (-2147483647 - 1);
            return (int)value;
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_f64_to_u32(double value)
        {
            if (isnan(value) || value <= 0.0) return 0u;
            if (value >= 4294967296.0) return 0xffffffffu;
            return (unsigned int)value;
        }

        static __device__ __forceinline__ long long csharp2cuda_f64_to_i64(double value)
        {
            if (isnan(value)) return 0LL;
            if (value >= 9223372036854775808.0) return 9223372036854775807LL;
            if (value <= -9223372036854775808.0) return (-9223372036854775807LL - 1LL);
            return (long long)value;
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_f64_to_u64(
            double value)
        {
            if (isnan(value) || value <= 0.0) return 0ull;
            if (value >= 18446744073709551616.0) return 0xffffffffffffffffull;
            return (unsigned long long)value;
        }

        static __device__ __forceinline__ signed char csharp2cuda_f64_to_i8(double value)
        {
            return csharp2cuda_i8_from_bits((unsigned int)csharp2cuda_f64_to_i32(value));
        }

        static __device__ __forceinline__ unsigned char csharp2cuda_f64_to_u8(double value)
        {
            return (unsigned char)csharp2cuda_f64_to_i32(value);
        }

        static __device__ __forceinline__ short csharp2cuda_f64_to_i16(double value)
        {
            return csharp2cuda_i16_from_bits((unsigned int)csharp2cuda_f64_to_i32(value));
        }

        static __device__ __forceinline__ unsigned short csharp2cuda_f64_to_u16(double value)
        {
            return (unsigned short)csharp2cuda_f64_to_i32(value);
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

        static __device__ __forceinline__ float csharp2cuda_f32_maximum(float left, float right)
        {
            if (left != right)
            {
                if (!isnan(left))
                    return right < left ? left : right;
                return left;
            }
            return signbit(right) ? left : right;
        }

        static __device__ __forceinline__ float csharp2cuda_f32_minimum(float left, float right)
        {
            if (left != right)
            {
                if (!isnan(left))
                    return left < right ? left : right;
                return left;
            }
            return signbit(left) ? left : right;
        }

        static __device__ __forceinline__ double csharp2cuda_f64_clamp(
            double value,
            double minimum,
            double maximum)
        {
            if (minimum > maximum)
            {
                __trap();
                return 0.0;
            }
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        static __device__ __forceinline__ float csharp2cuda_f32_clamp(
            float value,
            float minimum,
            float maximum)
        {
            if (minimum > maximum)
            {
                __trap();
                return 0.0f;
            }
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        static __device__ __forceinline__ int csharp2cuda_f64_sign(double value)
        {
            if (isnan(value))
            {
                __trap();
                return 0;
            }
            return value > 0.0 ? 1 : value < 0.0 ? -1 : 0;
        }

        static __device__ __forceinline__ int csharp2cuda_f32_sign(float value)
        {
            if (isnan(value))
            {
                __trap();
                return 0;
            }
            return value > 0.0f ? 1 : value < 0.0f ? -1 : 0;
        }

        static __device__ __forceinline__ signed char csharp2cuda_integral_abs(
            signed char value)
        {
            if (value == (signed char)-128)
            {
                __trap();
                return 0;
            }
            return value < 0 ? (signed char)(-(int)value) : value;
        }

        static __device__ __forceinline__ short csharp2cuda_integral_abs(short value)
        {
            if (value == (short)-32768)
            {
                __trap();
                return 0;
            }
            return value < 0 ? (short)(-(int)value) : value;
        }

        static __device__ __forceinline__ int csharp2cuda_integral_abs(int value)
        {
            if (value == (-2147483647 - 1))
            {
                __trap();
                return 0;
            }
            return value < 0 ? -value : value;
        }

        static __device__ __forceinline__ long long csharp2cuda_integral_abs(
            long long value)
        {
            if (value == (-9223372036854775807LL - 1LL))
            {
                __trap();
                return 0LL;
            }
            return value < 0LL ? -value : value;
        }

        template <typename T>
        static __device__ __forceinline__ int csharp2cuda_integral_sign(T value)
        {
            return value > (T)0 ? 1 : value < (T)0 ? -1 : 0;
        }

        template <typename T>
        static __device__ __forceinline__ T csharp2cuda_integral_minimum(T left, T right)
        {
            return left < right ? left : right;
        }

        template <typename T>
        static __device__ __forceinline__ T csharp2cuda_integral_maximum(T left, T right)
        {
            return left > right ? left : right;
        }

        template <typename T>
        static __device__ __forceinline__ T csharp2cuda_integral_clamp(
            T value,
            T minimum,
            T maximum)
        {
            if (minimum > maximum)
            {
                __trap();
                return (T)0;
            }
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        template <typename T>
        static __device__ __forceinline__ bool csharp2cuda_is_pow2(T value)
        {
            return value > (T)0 && (value & (value - (T)1)) == (T)0;
        }

        static __device__ __forceinline__ unsigned int
        csharp2cuda_u32_round_up_to_power_of_two(unsigned int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1u;
        }

        static __device__ __forceinline__ unsigned long long
        csharp2cuda_u64_round_up_to_power_of_two(unsigned long long value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1ull;
        }

        static __device__ __forceinline__ int csharp2cuda_u32_trailing_zero_count(
            unsigned int value)
        {
            return value == 0u ? 32 : __ffs(value) - 1;
        }

        static __device__ __forceinline__ int csharp2cuda_u64_trailing_zero_count(
            unsigned long long value)
        {
            return value == 0ull ? 64 : __ffsll(value) - 1;
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_rotate_left(
            unsigned int value,
            int count)
        {
            unsigned int shift = (unsigned int)count & 31u;
            return shift == 0u ? value : (value << shift) | (value >> (32u - shift));
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_rotate_left(
            unsigned long long value,
            int count)
        {
            unsigned int shift = (unsigned int)count & 63u;
            return shift == 0u ? value : (value << shift) | (value >> (64u - shift));
        }

        static __device__ __forceinline__ unsigned int csharp2cuda_u32_rotate_right(
            unsigned int value,
            int count)
        {
            unsigned int shift = (unsigned int)count & 31u;
            return shift == 0u ? value : (value >> shift) | (value << (32u - shift));
        }

        static __device__ __forceinline__ unsigned long long csharp2cuda_u64_rotate_right(
            unsigned long long value,
            int count)
        {
            unsigned int shift = (unsigned int)count & 63u;
            return shift == 0u ? value : (value >> shift) | (value << (64u - shift));
        }

        static __device__ __forceinline__ int csharp2cuda_u32_log2(unsigned int value)
        {
            return value == 0u ? 0 : 31 - __clz(value);
        }

        static __device__ __forceinline__ int csharp2cuda_u64_log2(unsigned long long value)
        {
            return value == 0ull ? 0 : 63 - __clzll(value);
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

        static __device__ __forceinline__ int csharp2cuda_i32_ushr(int value, int count)
        {
            unsigned int shift = (unsigned int)count & 31u;
            return csharp2cuda_i32_from_bits((unsigned int)value >> shift);
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

        static __device__ __forceinline__ long long csharp2cuda_i64_ushr(
            long long value,
            int count)
        {
            unsigned int shift = (unsigned int)count & 63u;
            return csharp2cuda_i64_from_bits((unsigned long long)value >> shift);
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
        static __device__ __forceinline__ signed char csharp2cuda_i8_pre_increment(signed char& target) { return target = csharp2cuda_i8_from_bits((unsigned int)(int)target + 1u); }
        static __device__ __forceinline__ signed char csharp2cuda_i8_post_increment(signed char& target) { signed char result = target; target = csharp2cuda_i8_from_bits((unsigned int)(int)target + 1u); return result; }
        static __device__ __forceinline__ signed char csharp2cuda_i8_pre_decrement(signed char& target) { return target = csharp2cuda_i8_from_bits((unsigned int)(int)target - 1u); }
        static __device__ __forceinline__ signed char csharp2cuda_i8_post_decrement(signed char& target) { signed char result = target; target = csharp2cuda_i8_from_bits((unsigned int)(int)target - 1u); return result; }
        static __device__ __forceinline__ short csharp2cuda_i16_pre_increment(short& target) { return target = csharp2cuda_i16_from_bits((unsigned int)(int)target + 1u); }
        static __device__ __forceinline__ short csharp2cuda_i16_post_increment(short& target) { short result = target; target = csharp2cuda_i16_from_bits((unsigned int)(int)target + 1u); return result; }
        static __device__ __forceinline__ short csharp2cuda_i16_pre_decrement(short& target) { return target = csharp2cuda_i16_from_bits((unsigned int)(int)target - 1u); }
        static __device__ __forceinline__ short csharp2cuda_i16_post_decrement(short& target) { short result = target; target = csharp2cuda_i16_from_bits((unsigned int)(int)target - 1u); return result; }
        #endif
        """;
}

internal sealed record CudaModuleEmission(
    string Source,
    ImmutableArray<CudaEntryPoint> EntryPoints,
    ImmutableArray<CudaSourceMapEntry> SourceMap);

internal sealed record CudaModuleSection(
    string Source,
    ImmutableArray<CudaSourceMapEntry> SourceMap)
{
    public static CudaModuleSection Raw(string source) => new(source, []);
}
