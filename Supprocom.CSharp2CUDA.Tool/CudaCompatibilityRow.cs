using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record CudaCompatibilityRow(
    CudaAdapterMapping Mapping,
    bool Supported,
    string Status,
    string Detail);
