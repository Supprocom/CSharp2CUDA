
namespace Supprocom.CSharp2CUDA;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CudaGlobalAttribute : Attribute
{
    public string? Name { get; set; }
    public bool ExternC { get; set; } = true;
}
