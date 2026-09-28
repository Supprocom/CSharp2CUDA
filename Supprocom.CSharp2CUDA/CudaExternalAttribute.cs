
namespace Supprocom.CSharp2CUDA;

[AttributeUsage(
    AttributeTargets.Struct | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CudaExternalAttribute : Attribute
{
    public bool IsPure { get; set; }
}
