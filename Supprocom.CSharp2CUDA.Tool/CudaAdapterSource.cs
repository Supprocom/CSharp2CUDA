using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record CudaAdapterSource(
    string Source,
    string KernelName,
    string CudaOutputPath);
