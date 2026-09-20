# Portable C# profile

CSharp2CUDA 0.3.1 translates a deliberately bounded, value-oriented subset of
regular C#. Start with an ordinary static algorithm and call it from a small
`[CudaGlobal]` adapter. Only the adapter needs to know about thread indexes,
pointers, and the CUDA launch boundary.

Translation is fail-closed. Roslyn must first accept the C# program. If any
reachable operation has no semantics-preserving CUDA rule, CSharp2CUDA reports
a diagnostic and emits no partial CUDA source.

## Support matrix

| Area | Portable forms | Boundary |
| --- | --- | --- |
| Values | `bool`, integral and floating-point primitives, `char`, enums, pointers, and supported unmanaged structs | No `decimal`, native-sized integers, nullable values, strings, `object`, boxing, or ordinary managed classes |
| Methods | Statically reachable static and struct instance methods, closed generic methods, optional arguments, extension methods, and `in`/`ref`/`out` helper parameters | No virtual dispatch, runtime interface dispatch, arbitrary recursion, exceptions, async methods, iterators, or reflection |
| Arrays and views | One-dimensional array views, `Span<T>`, `ReadOnlySpan<T>`, indexing, slicing, ranges, fixed local collections, and bounded span operations | No multidimensional or jagged arrays, `Memory<T>`, runtime-sized local allocation, or observable managed array identity |
| Value types | Ordinary portable structs, closed portable generic structs, device-internal tuples of arity 2–7, and non-generic positional `readonly record struct` types | Tuples and positional records cannot cross a global or external CUDA ABI |
| Functional forms | Nonescaping local functions with portable captures and directly invoked lambdas | No general delegates, escaping closures, events, LINQ, or observable delegate identity |
| Control flow | Conditional and loop statements, constant and relational/logical patterns, `var` patterns, guards, switch statements, and switch expressions | No type, property, positional, recursive, or list patterns |
| References | `ref` and `ref readonly` locals, ref reassignment, constrained ref returns, `scoped`, and ref iteration over arrays or spans | A ref cannot outlive its storage or gain write access through a read-only root; ref fields are not supported |

## Ordinary methods and the kernel ABI

Only a CUDA root needs an attribute. A reachable ordinary method remains
callable on the CPU and becomes a `__device__` helper in the generated module.

```csharp
internal static class Statistics
{
    public static int ClampSample(int value, int maximum = 255) =>
        Math.Clamp(value, 0, maximum);
}

[TranspileToCUDA]
internal static unsafe class StatisticsGpu
{
    [CudaGlobal(Name = "clamp_samples")]
    private static void Kernel(int* input, int* output, int count)
    {
        int index = Cuda.BlockIdx.X * Cuda.BlockDim.X + Cuda.ThreadIdx.X;
        if (index < count)
            output[index] = Statistics.ClampSample(input[index]);
    }
}
```

`[CudaGlobal]` parameters use the stable launch ABI. Use supported primitive
values, pointers, or ABI-approved structures. Arrays, spans, `ref` parameters,
tuples, and positional record structs are device-internal; construct their
views or values after entering the kernel.

## Scalar framework mappings

The compiler recognizes supported framework members by exact Roslyn symbol,
not by text name. Alongside the existing math surface, 0.3.1 supports the
applicable portable integral overloads of `Math.Abs`, `Math.Min`, `Math.Max`,
`Math.Clamp`, and `Math.Sign`; `MathF.E`, `MathF.PI`, and `MathF.Tau`; and the
portable overloads of `BitOperations.IsPow2` and
`BitOperations.RoundUpToPowerOf2`.

```csharp
int magnitude = Math.Abs(value);
int bounded = Math.Clamp(magnitude, 1, 64);
bool powerOfTwo = BitOperations.IsPow2(bits);
uint capacity = BitOperations.RoundUpToPowerOf2(bits);
float circle = 2.0f * MathF.PI;
```

Signed minimum-value `Abs` and invalid `Clamp` bounds follow the device-trap
policy instead of invoking undefined C++ behavior. Unlisted overloads and
lookalike user APIs receive their normal reachability treatment.

## Arrays, spans, ranges, and fixed collections

Arrays are emitted as nullable pointer-and-length views. Writable input stays
writable; `ReadOnlySpan<T>` and other read-only paths never acquire a writable
alias.

```csharp
static int Update(int[] values, int replacement)
{
    Span<int> all = values.AsSpan();
    Span<int> middle = all[1..^1];
    middle.Fill(replacement);
    middle.CopyTo(all[..middle.Length]);
    all[^1] = middle.TryCopyTo(all[..middle.Length]) ? all[^2] : -1;
    all[0..1].Clear();
    return all[^1];
}
```

Supported range forms are `start..end`, `start..`, `..end`, and `..`, including
from-end (`^`) endpoints. `MemoryExtensions.AsSpan`, `Clear`, `Fill`, `CopyTo`,
and `TryCopyTo` are supported for applicable one-dimensional views. `CopyTo`
is overlap-safe; `TryCopyTo` leaves an undersized destination unchanged.
As in managed C#, a range on `T[]` creates an independent copy while a range
on a span remains a view. Array-range copies use bounded per-thread storage:
at most 1,024 elements, with a device trap if a runtime-sized range exceeds
that limit. Their backing storage cannot escape the current device function;
`CS2CUDA039` reports a return, external call, or other unprovable escape.

Fixed, nonescaping local storage can use a positive constant length, an array
initializer, or a target-typed collection expression:

```csharp
int[] zeroed = new int[3];
int[] values = [seed, 2, 3];
Span<int> writable = [seed, 4, 5];
ReadOnlySpan<int> readOnly = [seed, 6, 7];
int total = Sum(seed, 8, 9); // Sum(params int[] values)
```

Expanded `params T[]` and `params ReadOnlySpan<T>` calls use the same fixed
storage. An empty expanded call is a non-null empty view; an explicit `null`
passed to `params T[]` remains null. `CS2CUDA033` reports storage whose identity
or lifetime could escape—for example by returning it, storing it in a field,
comparing it by reference, boxing it, or passing it to an unknown call.
Each fixed declaration or expanded argument list is limited to 1,024 elements;
`CS2CUDA040` reports a larger declaration instead of silently creating
unbounded per-thread local storage. Empty collection expressions are represented
as non-null empty views without a zero-length CUDA array.

## Operators, patterns, tuples, and records

Portable source-defined operators and conversions on supported structs become
explicit device-helper calls. Unary and binary arithmetic, bitwise and shift
operators, equality and ordering, `++`/`--`, conversions, and compound
assignment are supported. Checked user-defined operators, lifted nullable
operators, and `operator true`/`false` are not.

Relational patterns, `and`, `or`, `not`, parentheses, `var` bindings, and
`when` guards work in `is`, switch statements, and switch expressions. Inputs
are evaluated once, arms stay ordered, and guards run only after a match.

Closed tuples with two through seven portable elements can be local values or
helper parameters and results. Named element access, nested deconstruction,
assignment deconstruction, and fieldwise `==`/`!=` are supported.

Non-generic positional `readonly record struct` declarations support
construction, positional properties, generated `Deconstruct`, fieldwise
equality, and explicitly written portable members:

```csharp
internal readonly record struct Vec2(int X, int Y)
{
    public static Vec2 operator +(Vec2 left, Vec2 right) =>
        new(left.X + right.X, left.Y + right.Y);
}

static int Calculate(int value)
{
    Vec2 sum = new Vec2(value, 2) + new Vec2(3, 4);
    (Vec2 Vector, int Extra) pair = (sum, 5);
    var (vector, extra) = pair;
    var (x, y) = vector;
    return x + y + extra + (vector == sum ? 1 : 0);
}
```

Tuple layouts and positional-record lowering are private implementation
details. `CS2CUDA036` rejects either at a global or external CUDA boundary.
Record classes, mutable or generic record structs, `with`, reachable
`ToString`, and reachable `GetHashCode` are outside the profile.

## Captures, direct lambdas, and references

A local function may capture portable locals and parameters when its entire
call graph remains statically known and nonescaping. Read-only captures become
hidden values; written captures become hidden references. Captures are read at
each invocation, not frozen at declaration time. A lambda is supported only
when directly invoked and no delegate identity is observable.

```csharp
static int Accumulate(Span<int> values, int seed)
{
    int total = seed;
    int Add(int value) => total += value;

    foreach (ref int value in values)
        value = Add(value);

    ref readonly int last = ref values[^1];
    return total + last;
}
```

Portable refs may be rooted in pointer dereferences, array or span elements,
fixed local storage, inline-array elements, ref parameters, or addressable
fields of supported structs. A ref return cannot refer to a local, temporary,
by-value parameter, or expired fixed storage. `CS2CUDA034` reports an escaping
closure; `CS2CUDA035` reports an invalid lifetime or mutability path.

## Trap policy

Managed exceptions are not implemented on the device. Where a supported C#
operation would throw, generated CUDA executes a device trap. This includes
null view access, invalid indexes or ranges, invalid `Math.Clamp` bounds,
signed minimum-value `Math.Abs`, integral divide-by-zero and invalid signed
division, checked conversion paths already covered by the profile, and the
fallback of a nonexhaustive switch expression.

## Still intentionally unsupported

CSharp2CUDA does not provide a managed device runtime. It rejects arbitrary
heap allocation, garbage collection, strings and ordinary reference types,
boxing, exceptions and disposal semantics, locks, dynamic dispatch, delegates
that escape, LINQ, reflection, async/await, iterators, general recursion,
multidimensional and jagged arrays, and framework APIs without an explicit
mapping.

CUDA-specific storage, intrinsics, and external linkage are documented in
[CUDA interop](cuda-interop.md).
