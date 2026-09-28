
namespace Supprocom.CSharp2CUDA;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class CudaInlineArrayAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}
