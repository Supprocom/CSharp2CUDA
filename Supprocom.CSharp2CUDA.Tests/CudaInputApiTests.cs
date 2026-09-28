using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Tests.FileInputs;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaInputApiTests
{
    [Fact]
    public void TranspileFileReadsACompileCheckedCSharpFile()
    {
        Assert.Equal(6, ManualKernel.Double(3));
        var path = GetInputPath("ManualKernel.cs");

        var result = CudaTranspiler.TranspileFile(path);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int Double(int value)", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("TranspileToCUDA", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileFilesCombinesSelectedCompileCheckedFiles()
    {
        var paths = new[]
        {
            GetInputPath("ManualHelper.cs"),
            GetInputPath("ManualMultiFileKernel.cs")
        };

        var result = CudaTranspiler.TranspileFiles(paths);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int AddOne(int value);", result.Source, StringComparison.Ordinal);
        Assert.Contains("AddOne(", result.Source, StringComparison.Ordinal);
        Assert.Contains("(values)[", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileUsesTheManuallySelectedCompilationWithoutAMarker()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public static class CompilationKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;
        var compilation = CudaTestCompiler.CreateCompilation(source);

        var result = CudaTranspiler.Transpile(compilation);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int Identity(int value)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileUsesManualSelectionInsteadOfClassMarkerSelection()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA("../ignored.cu")]
            public static class CompilationKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;
        var compilation = CudaTestCompiler.CreateCompilation(source);

        var result = CudaTranspiler.Transpile(compilation);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int Identity(int value)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileRejectsAnUnsupportedSelectedTopLevelDeclaration()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public sealed record Result(int Value);

            public static class CompilationKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;
        var compilation = CudaTestCompiler.CreateCompilation(source);

        var result = CudaTranspiler.Transpile(compilation);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS2CUDA005", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicApiDoesNotAcceptRawSourceText()
    {
#pragma warning disable HLQ005 // Exactly one element is a validation invariant; First would silently accept duplicates.
        var rawSourceOverload = typeof(CudaTranspiler).GetMethods()
            .Where(method => string.Equals(method.Name, nameof(CudaTranspiler.Transpile), StringComparison.Ordinal))
            .SingleOrDefault(method => method.GetParameters().FirstOrDefault()?.ParameterType ==
                typeof(string));
#pragma warning restore HLQ005

        Assert.Null(rawSourceOverload);
        Assert.Null(typeof(CudaTranspiler).Assembly.GetType(
            "Supprocom.CSharp2CUDA.CudaTranslationUnitAttribute"));
        Assert.Null(typeof(CudaTranspilationOptions).GetProperty(
            "TranspileAttributedClassesOnly"));
        Assert.Null(typeof(CudaTranspilationResult).GetProperty(
            "RequestedOutputPath"));
    }

    [Fact]
    public void TranspileFileRejectsANonCSharpPath()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CudaTranspiler.TranspileFile(GetInputPath("ManualKernel.cs") + ".txt"));

        Assert.Contains(".cs file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileFileRejectsAMissingFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "FileInputs", "Missing.cs");

        Assert.Throws<FileNotFoundException>(() => CudaTranspiler.TranspileFile(path));
    }

    [Fact]
    public void TranspileFilesRejectsDuplicatePaths()
    {
        var path = GetInputPath("ManualKernel.cs");

        Assert.Throws<ArgumentException>(() => CudaTranspiler.TranspileFiles([path, path]));
    }

    [Fact]
    public void TranspileFileReturnsEmptySourceForACompilerError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(
                path,
                "public static class Broken { public static int Value() => missing; }");

            var result = CudaTranspiler.TranspileFile(path);

            Assert.False(result.Succeeded);
            Assert.Empty(result.Source);
            Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS0103", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClassMarkerSelectsOnlyTheMarkedClassAndReturnsItsPath()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public static class OrdinaryClass
            {
                public static int Value = 1;
            }

            [TranspileToCUDA("cuda/Selected.cu")]
            public static class SelectedKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal("cuda/Selected.cu", result.RequestedOutputPath);
        Assert.Contains("Identity", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("OrdinaryClass", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassMarkerUsesTheDefaultPathForAnEmptyArgument()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA("")]
            public static class SelectedKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Null(result.RequestedOutputPath);
    }

    [Fact]
    public void ClassMarkerRejectsANullOutputPath()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA(null)]
            public static class SelectedKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS2CUDA021", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../escape.cu")]
    [InlineData("cuda/not-cuda.txt")]
    [InlineData("C:/escape.cu")]
    [InlineData("cuda//kernel.cu")]
    [InlineData("cuda/CON.cu")]
    [InlineData("cuda/COM¹.cu")]
    [InlineData("cuda/ kernel.cu")]
    public void ClassMarkerRejectsAnInvalidOutputPath(string path)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA("{{path}}")]
            public static class SelectedKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS2CUDA021", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassMarkerNormalizesPortableDirectorySeparators()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA(@"cuda\Selected.cu")]
            public static class SelectedKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal("cuda/Selected.cu", result.RequestedOutputPath);
    }

    [Fact]
    public void ClassMarkerAcceptsEquivalentPortableOutputPaths()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA("cuda/Selected.cu")]
            public static class FirstKernel
            {
                [CudaDevice]
                public static int First(int value)
                {
                    return value;
                }
            }

            [TranspileToCUDA(@"cuda\selected.cu")]
            public static class SecondKernel
            {
                [CudaDevice]
                public static int Second(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Equal("cuda/Selected.cu", result.RequestedOutputPath);
        Assert.Contains("First", result.Source, StringComparison.Ordinal);
        Assert.Contains("Second", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassMarkerRejectsConflictingOutputPaths()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA("cuda/One.cu")]
            public static class FirstKernel
            {
                [CudaDevice]
                public static int First(int value)
                {
                    return value;
                }
            }

            [TranspileToCUDA("cuda/Two.cu")]
            public static class SecondKernel
            {
                [CudaDevice]
                public static int Second(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS2CUDA022", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassMarkerRequiresTheExactRoslynSymbol()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            public sealed class TranspileToCUDAAttribute : Attribute
            {
            }

            [TranspileToCUDA]
            public static class LookalikeKernel
            {
                [CudaDevice]
                public static int Identity(int value)
                {
                    return value;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Empty(result.Source);
    }

    [Fact]
    public void ClassMarkerRejectsAMarkedRecord()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            public sealed record SelectedKernel(int Value);
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS2CUDA002", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassMarkerRejectsANestedMarkedClass()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public static class Container
            {
                [TranspileToCUDA]
                public static class SelectedKernel
                {
                    [CudaDevice]
                    public static int Identity(int value)
                    {
                        return value;
                    }
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => string.Equals(diagnostic.Id, "CS2CUDA002", StringComparison.Ordinal));
    }

    private static string GetInputPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "FileInputs", name);

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}
