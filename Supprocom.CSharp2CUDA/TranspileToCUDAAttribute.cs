
namespace Supprocom.CSharp2CUDA;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TranspileToCUDAAttribute : Attribute
{
    public TranspileToCUDAAttribute()
    {
    }

    public TranspileToCUDAAttribute(string outputPath)
    {
        OutputPath = outputPath;
    }

    public string? OutputPath { get; }
}
