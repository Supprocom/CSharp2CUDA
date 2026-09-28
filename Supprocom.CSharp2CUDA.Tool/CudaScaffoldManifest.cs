using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record CudaScaffoldManifest(
    int SchemaVersion,
    string SourceProject,
    string Method,
    string Mapping,
    string ProjectFile,
    string AdapterFile,
    string CudaOutputPath,
    ImmutableArray<string> SourceFiles,
    ImmutableArray<CudaManifestFile> GeneratedFiles);
