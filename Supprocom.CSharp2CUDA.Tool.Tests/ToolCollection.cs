using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ToolCollection
{
    public const string Name = "CSharp2CUDA tool";
}
