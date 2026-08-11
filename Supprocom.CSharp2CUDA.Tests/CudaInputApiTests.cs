using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Tests.FileInputs;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaInputApiTests
{
    [Fact]
    public void TranspileFile_ReadsACompileCheckedCSharpFile()
    {
        Assert.Equal(6, ManualKernel.Double(3));
        var path = GetInputPath("ManualKernel.cs");

        var result = CudaTranspiler.TranspileFile(path);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int Double(int value)", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("TranspileToCUDA", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileFiles_CombinesSelectedCompileCheckedFiles()
    {
        var paths = new[]
        {
            GetInputPath("ManualHelper.cs"),
            GetInputPath("ManualMultiFileKernel.cs")
        };

        var result = CudaTranspiler.TranspileFiles(paths);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("__device__ int AddOne(int value);", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "values[index] = AddOne(values[index]);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_UsesTheManuallySelectedCompilationWithoutAMarker()
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
    public void Transpile_UsesManualSelectionInsteadOfClassMarkerSelection()
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
    public void Transpile_RejectsAnUnsupportedSelectedTopLevelDeclaration()
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA005");
    }

    [Fact]
    public void PublicApi_DoesNotAcceptRawSourceText()
    {
        var rawSourceOverload = typeof(CudaTranspiler).GetMethods()
            .Where(method => method.Name == nameof(CudaTranspiler.Transpile))
            .SingleOrDefault(method => method.GetParameters().FirstOrDefault()?.ParameterType ==
                typeof(string));

        Assert.Null(rawSourceOverload);
        Assert.Null(typeof(CudaTranspiler).Assembly.GetType(
            "Supprocom.CSharp2CUDA.CudaTranslationUnitAttribute"));
        Assert.Null(typeof(CudaTranspilationOptions).GetProperty(
            "TranspileAttributedClassesOnly"));
        Assert.Null(typeof(CudaTranspilationResult).GetProperty(
            "RequestedOutputPath"));
    }

    [Fact]
    public void TranspileFile_RejectsANonCSharpPath()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CudaTranspiler.TranspileFile(GetInputPath("ManualKernel.cs") + ".txt"));

        Assert.Contains(".cs file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileFile_RejectsAMissingFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "FileInputs", "Missing.cs");

        Assert.Throws<FileNotFoundException>(() => CudaTranspiler.TranspileFile(path));
    }

    [Fact]
    public void TranspileFiles_RejectsDuplicatePaths()
    {
        var path = GetInputPath("ManualKernel.cs");

        Assert.Throws<ArgumentException>(() => CudaTranspiler.TranspileFiles([path, path]));
    }

    [Fact]
    public void TranspileFile_ReturnsEmptySourceForACompilerError()
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
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS0103");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClassMarker_SelectsOnlyTheMarkedClassAndReturnsItsPath()
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
    public void ClassMarker_UsesTheDefaultPathForAnEmptyArgument()
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
    public void ClassMarker_RejectsANullOutputPath()
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA021");
    }

    [Theory]
    [InlineData("../escape.cu")]
    [InlineData("cuda/not-cuda.txt")]
    [InlineData("C:/escape.cu")]
    [InlineData("cuda//kernel.cu")]
    [InlineData("cuda/CON.cu")]
    [InlineData("cuda/COM¹.cu")]
    [InlineData("cuda/ kernel.cu")]
    public void ClassMarker_RejectsAnInvalidOutputPath(string path)
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA021");
    }

    [Fact]
    public void ClassMarker_NormalizesPortableDirectorySeparators()
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
    public void ClassMarker_AcceptsEquivalentPortableOutputPaths()
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
    public void ClassMarker_RejectsConflictingOutputPaths()
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA022");
    }

    [Fact]
    public void ClassMarker_RequiresTheExactRoslynSymbol()
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
    public void ClassMarker_RejectsAMarkedRecord()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            public sealed record SelectedKernel(int Value);
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA002");
    }

    [Fact]
    public void ClassMarker_RejectsANestedMarkedClass()
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS2CUDA002");
    }

    private static string GetInputPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "FileInputs", name);

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}
