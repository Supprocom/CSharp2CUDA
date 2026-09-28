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
