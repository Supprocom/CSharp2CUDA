using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record CudaScaffoldPlan(
    string OutputDirectory,
    ImmutableDictionary<string, string> Files,
    CudaScaffoldManifest Manifest);
