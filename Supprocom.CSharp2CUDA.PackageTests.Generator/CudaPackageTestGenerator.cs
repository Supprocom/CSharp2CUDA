using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Supprocom.CSharp2CUDA.PackageTests.Generator;

#pragma warning disable RS1041 // The package-test generator runs only in this .NET 10 build host.
[Generator]
public sealed class CudaPackageTestGenerator : IIncrementalGenerator
{
#pragma warning restore RS1041
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var assemblyName = context.CompilationProvider.Select(
            static (compilation, _) => compilation.AssemblyName);
        context.RegisterSourceOutput(assemblyName, static (production, name) =>
        {
            var source = name switch
            {
                "GeneratedAttributed" => AttributedSource,
                "GeneratedProject" => ProjectSource,
                _ => null
            };
            if (source is not null)
            {
                production.AddSource(
                    "GeneratedCudaKernel.g.cs",
                    SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private const string AttributedSource = """
        namespace Supprocom.CSharp2CUDA.PackageTests.GeneratedAttributed;

        [global::Supprocom.CSharp2CUDA.TranspileToCUDA("cuda/GeneratedAttributed.cu")]
        public static class GeneratedKernel
        {
            [global::Supprocom.CSharp2CUDA.CudaDevice]
            public static int Increment(int value)
            {
                return value + 1;
            }
        }
        """;

    private const string ProjectSource = """
        namespace Supprocom.CSharp2CUDA.PackageTests.GeneratedProject;

        public static class GeneratedKernel
        {
            [global::Supprocom.CSharp2CUDA.CudaDevice]
            public static int Increment(int value)
            {
                return value + 1;
            }
        }
        """;
}
