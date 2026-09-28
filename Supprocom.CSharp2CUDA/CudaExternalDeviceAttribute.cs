
namespace Supprocom.CSharp2CUDA;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CudaExternalDeviceAttribute : Attribute
{
    public string? Name { get; set; }
    public bool IsPure { get; set; }
}
