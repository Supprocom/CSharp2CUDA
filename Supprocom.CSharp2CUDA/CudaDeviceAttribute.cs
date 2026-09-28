
namespace Supprocom.CSharp2CUDA;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CudaDeviceAttribute : Attribute
{
    public string? Name { get; set; }
}
