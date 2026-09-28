
namespace Supprocom.CSharp2CUDA.Tool;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Every tool failure requires an explicit stable CS2CUDA diagnostic code; code-free constructors violate that reporting contract.")]
internal sealed class ToolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
