using Xunit;
using Xunit.Abstractions;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaLibraryVersionTests(ITestOutputHelper output)
{
    [CudaFact]
    public void CudaLibrariesReportLoadedVersions()
    {
        output.WriteLine(CudaTestRuntime.DescribeLibraries());

        Assert.True(CudaTestRuntime.TryGetVersions(out var driver, out var major, out var minor));
        Assert.True(driver > 0);
        Assert.True(major > 0);
        Assert.True(minor >= 0);
    }
}
