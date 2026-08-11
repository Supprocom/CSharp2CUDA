namespace CSharp2CUDA;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CudaTranslationUnitAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CudaDeviceAttribute : Attribute
{
    public string? Name { get; set; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CudaGlobalAttribute : Attribute
{
    public string? Name { get; set; }
    public bool ExternC { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class CudaReadOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class CudaConstantAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Struct | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CudaExternalAttribute : Attribute
{
    public bool IsPure { get; set; }
}
