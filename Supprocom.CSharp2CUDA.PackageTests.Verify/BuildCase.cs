namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal sealed record BuildCase(
    string Name,
    string Root,
    string IntermediateRoot,
    string Output,
    string Project,
    string[] Common);
