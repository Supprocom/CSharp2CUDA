using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

if (args.Length != 5)
{
    throw new ArgumentException(
        "Specify the package directory, repository directory, commit, branch, and version.");
}

var packageDirectory = Path.GetFullPath(args[0]);
var repositoryDirectory = Path.GetFullPath(args[1]);
var expectedCommit = args[2];
var expectedBranch = args[3];
var expectedVersion = args[4];
Require(
    expectedCommit.Length == 40 && expectedCommit.All(Uri.IsHexDigit),
    "The expected repository commit is invalid.");
Require(
    !string.IsNullOrWhiteSpace(expectedBranch),
    "The expected repository branch is invalid.");
Require(
    Version.TryParse(expectedVersion, out var parsedVersion) &&
    parsedVersion.ToString(3) == expectedVersion,
    "The expected package version is invalid.");

var nupkgPath = Path.Combine(
    packageDirectory,
    $"Supprocom.CSharp2CUDA.{expectedVersion}.nupkg");
var snupkgPath = Path.Combine(
    packageDirectory,
    $"Supprocom.CSharp2CUDA.{expectedVersion}.snupkg");
var toolNupkgPath = Path.Combine(
    packageDirectory,
    $"Supprocom.CSharp2CUDA.Tool.{expectedVersion}.nupkg");
var toolSnupkgPath = Path.Combine(
    packageDirectory,
    $"Supprocom.CSharp2CUDA.Tool.{expectedVersion}.snupkg");
Require(File.Exists(nupkgPath), "The nupkg is missing.");
Require(File.Exists(snupkgPath), "The snupkg is missing.");
Require(File.Exists(toolNupkgPath), "The tool nupkg is missing.");
Require(File.Exists(toolSnupkgPath), "The tool snupkg is missing.");

using var nupkg = ZipFile.OpenRead(nupkgPath);
using var snupkg = ZipFile.OpenRead(snupkgPath);
using var toolNupkg = ZipFile.OpenRead(toolNupkgPath);
using var toolSnupkg = ZipFile.OpenRead(toolSnupkgPath);
ValidateEntries(nupkg, "nupkg");
ValidateEntries(snupkg, "snupkg");
ValidateEntries(toolNupkg, "tool nupkg");
ValidateEntries(toolSnupkg, "tool snupkg");
Require(nupkg.Entries.Count == 21, "The nupkg entry count is incorrect.");
Require(snupkg.Entries.Count == 7, "The snupkg entry count is incorrect.");
Require(toolNupkg.Entries.Count == 127, "The tool nupkg entry count is incorrect.");
Require(toolSnupkg.Entries.Count == 8, "The tool snupkg entry count is incorrect.");

RequireEntry(nupkg, "build/Supprocom.CSharp2CUDA.targets");
RequireEntry(nupkg, "build/compiler/Supprocom.CSharp2CUDA.Compiler.dll");
RequireEntry(nupkg, "build/compiler/Supprocom.CSharp2CUDA.Compiler.pdb");
RequireEntry(nupkg, "build/compiler/Supprocom.CSharp2CUDA.dll");
RequireEntry(nupkg, "build/task/Supprocom.CSharp2CUDA.Build.dll");
RequireEntry(nupkg, "build/task/Supprocom.CSharp2CUDA.Build.pdb");
RequireEntry(nupkg, "build/task/Supprocom.CSharp2CUDA.dll");
RequireEntry(nupkg, "build/task/Microsoft.CodeAnalysis.dll");
RequireEntry(nupkg, "build/task/Microsoft.CodeAnalysis.CSharp.dll");
RequireEntry(nupkg, "lib/net10.0/Supprocom.CSharp2CUDA.dll");
RequireEntry(snupkg, "build/compiler/Supprocom.CSharp2CUDA.Compiler.pdb");
RequireEntry(snupkg, "build/task/Supprocom.CSharp2CUDA.Build.pdb");
RequireEntry(snupkg, "lib/net10.0/Supprocom.CSharp2CUDA.pdb");
Require(
    nupkg.Entries.All(entry =>
        !entry.FullName.StartsWith("buildTransitive/", StringComparison.Ordinal)),
    "The package contains a transitive build target.");

var nuspecEntry = nupkg.Entries.Single(entry => entry.FullName.EndsWith(".nuspec"));
var nuspec = XDocument.Parse(
    Encoding.UTF8.GetString(ReadEntry(nuspecEntry)).TrimStart('\uFEFF'));
var metadata = nuspec.Root!.Elements().Single().Elements().ToDictionary(
    element => element.Name.LocalName,
    element => element);
Require(metadata["id"].Value == "Supprocom.CSharp2CUDA", "The package ID is incorrect.");
Require(metadata["version"].Value == expectedVersion, "The package version is incorrect.");
Require(
    metadata["license"].Value == "AGPL-3.0-only" &&
    metadata["license"].Attribute("type")?.Value == "expression",
    "The package license is incorrect.");
var repository = metadata["repository"];
Require(
    repository.Attribute("url")?.Value == "https://github.com/Supprocom/CSharp2CUDA",
    "The repository URL is incorrect.");
Require(
    repository.Attribute("branch")?.Value == expectedBranch,
    "The repository branch is incorrect.");
Require(
    repository.Attribute("commit")?.Value == expectedCommit,
    "The repository commit is incorrect.");
var dependency = metadata["dependencies"].Descendants()
    .Single(element => element.Name.LocalName == "dependency");
Require(
    dependency.Attribute("id")?.Value == "Microsoft.CodeAnalysis.CSharp" &&
    dependency.Attribute("version")?.Value == "[5.6.0]",
    "The Roslyn dependency is incorrect.");

CompareEntry(nupkg, "README.md", Path.Combine(repositoryDirectory, "README.md"));
CompareEntry(nupkg, "LICENSE.md", Path.Combine(repositoryDirectory, "LICENSE.md"));
CompareEntry(
    nupkg,
    "THIRD-PARTY-NOTICES.md",
    Path.Combine(repositoryDirectory, "THIRD-PARTY-NOTICES.md"));
CompareEntry(
    nupkg,
    "docs/getting-started.md",
    Path.Combine(repositoryDirectory, "docs", "getting-started.md"));
CompareEntry(
    nupkg,
    "docs/portable-csharp.md",
    Path.Combine(repositoryDirectory, "docs", "portable-csharp.md"));
CompareEntry(
    nupkg,
    "docs/cuda-interop.md",
    Path.Combine(repositoryDirectory, "docs", "cuda-interop.md"));
CompareEntry(
    nupkg,
    "docs/tool.md",
    Path.Combine(repositoryDirectory, "docs", "tool.md"));

var libraryBytes = ReadEntry(RequireEntry(
    nupkg,
    "lib/net10.0/Supprocom.CSharp2CUDA.dll"));
var taskCoreBytes = ReadEntry(RequireEntry(
    nupkg,
    "build/task/Supprocom.CSharp2CUDA.dll"));
var compilerCoreBytes = ReadEntry(RequireEntry(
    nupkg,
    "build/compiler/Supprocom.CSharp2CUDA.dll"));
Require(
    libraryBytes.AsSpan().SequenceEqual(taskCoreBytes),
    "The build task has a different core assembly.");
Require(
    libraryBytes.AsSpan().SequenceEqual(compilerCoreBytes),
    "The compiler has a different core assembly.");

var taskPdbBytes = ReadEntry(RequireEntry(
    nupkg,
    "build/task/Supprocom.CSharp2CUDA.Build.pdb"));
var symbolTaskPdbBytes = ReadEntry(RequireEntry(
    snupkg,
    "build/task/Supprocom.CSharp2CUDA.Build.pdb"));
Require(
    taskPdbBytes.AsSpan().SequenceEqual(symbolTaskPdbBytes),
    "The task symbol files differ.");
var compilerPdbBytes = ReadEntry(RequireEntry(
    nupkg,
    "build/compiler/Supprocom.CSharp2CUDA.Compiler.pdb"));
var symbolCompilerPdbBytes = ReadEntry(RequireEntry(
    snupkg,
    "build/compiler/Supprocom.CSharp2CUDA.Compiler.pdb"));
Require(
    compilerPdbBytes.AsSpan().SequenceEqual(symbolCompilerPdbBytes),
    "The compiler symbol files differ.");
var symbolCorePdbBytes = ReadEntry(RequireEntry(
    snupkg,
    "lib/net10.0/Supprocom.CSharp2CUDA.pdb"));
ValidatePdb(
    symbolCorePdbBytes,
    "/Supprocom.CSharp2CUDA/CudaTranspiler.cs",
    expectedCommit);
ValidatePdb(
    symbolTaskPdbBytes,
    "/Supprocom.CSharp2CUDA.Build/CudaOutputTask.cs",
    expectedCommit);
ValidatePdb(
    symbolCompilerPdbBytes,
    "/Supprocom.CSharp2CUDA.Compiler/CudaTranspilationAnalyzer.cs",
    expectedCommit);

RequireEntry(toolNupkg, "tools/net10.0/any/DotnetToolSettings.xml");
var toolDllBytes = ReadEntry(RequireEntry(
    toolNupkg,
    "tools/net10.0/any/Supprocom.CSharp2CUDA.Tool.dll"));
var toolCoreBytes = ReadEntry(RequireEntry(
    toolNupkg,
    "tools/net10.0/any/Supprocom.CSharp2CUDA.dll"));
Require(
    toolCoreBytes.AsSpan().SequenceEqual(libraryBytes),
    "The tool package has a different core assembly.");
var toolPdbBytes = ReadEntry(RequireEntry(
    toolNupkg,
    "tools/net10.0/any/Supprocom.CSharp2CUDA.Tool.pdb"));
var symbolToolPdbBytes = ReadEntry(RequireEntry(
    toolSnupkg,
    "tools/net10.0/any/Supprocom.CSharp2CUDA.Tool.pdb"));
Require(
    toolPdbBytes.AsSpan().SequenceEqual(symbolToolPdbBytes),
    "The tool symbol files differ.");
var toolCorePdbBytes = ReadEntry(RequireEntry(
    toolNupkg,
    "tools/net10.0/any/Supprocom.CSharp2CUDA.pdb"));
var symbolToolCorePdbBytes = ReadEntry(RequireEntry(
    toolSnupkg,
    "tools/net10.0/any/Supprocom.CSharp2CUDA.pdb"));
Require(
    toolCorePdbBytes.AsSpan().SequenceEqual(symbolToolCorePdbBytes),
    "The tool core symbol files differ.");
Require(
    toolCorePdbBytes.AsSpan().SequenceEqual(symbolCorePdbBytes),
    "The core package and tool package symbols differ.");
ValidatePdb(
    symbolToolPdbBytes,
    "/Supprocom.CSharp2CUDA.Tool/ToolApplication.cs",
    expectedCommit);
ValidatePdb(
    symbolToolCorePdbBytes,
    "/Supprocom.CSharp2CUDA/CudaTranspiler.cs",
    expectedCommit);

var toolNuspecEntry = toolNupkg.Entries.Single(entry =>
    entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
var toolNuspec = XDocument.Parse(
    Encoding.UTF8.GetString(ReadEntry(toolNuspecEntry)).TrimStart('\uFEFF'));
var toolMetadata = toolNuspec.Root!.Elements().Single().Elements().ToDictionary(
    element => element.Name.LocalName,
    element => element);
Require(
    toolMetadata["id"].Value == "Supprocom.CSharp2CUDA.Tool",
    "The tool package ID is incorrect.");
Require(
    toolMetadata["version"].Value == expectedVersion,
    "The tool package version is incorrect.");
Require(
    toolMetadata["license"].Value == "AGPL-3.0-only" &&
    toolMetadata["license"].Attribute("type")?.Value == "expression",
    "The tool package license is incorrect.");
var toolRepository = toolMetadata["repository"];
Require(
    toolRepository.Attribute("url")?.Value ==
        "https://github.com/Supprocom/CSharp2CUDA" &&
    toolRepository.Attribute("branch")?.Value == expectedBranch &&
    toolRepository.Attribute("commit")?.Value == expectedCommit,
    "The tool repository provenance is incorrect.");
Require(
    toolMetadata["packageTypes"].Descendants().Single()
        .Attribute("name")?.Value == "DotnetTool",
    "The tool package type is incorrect.");
CompareEntry(toolNupkg, "README.md", Path.Combine(repositoryDirectory, "README.md"));
CompareEntry(toolNupkg, "LICENSE.md", Path.Combine(repositoryDirectory, "LICENSE.md"));
CompareEntry(
    toolNupkg,
    "THIRD-PARTY-NOTICES.md",
    Path.Combine(repositoryDirectory, "THIRD-PARTY-NOTICES.md"));
CompareEntry(
    toolNupkg,
    "docs/getting-started.md",
    Path.Combine(repositoryDirectory, "docs", "getting-started.md"));
CompareEntry(
    toolNupkg,
    "docs/portable-csharp.md",
    Path.Combine(repositoryDirectory, "docs", "portable-csharp.md"));
CompareEntry(
    toolNupkg,
    "docs/cuda-interop.md",
    Path.Combine(repositoryDirectory, "docs", "cuda-interop.md"));
CompareEntry(
    toolNupkg,
    "docs/tool.md",
    Path.Combine(repositoryDirectory, "docs", "tool.md"));

Console.WriteLine($"NupkgSha256={HashFile(nupkgPath)}");
Console.WriteLine($"SnupkgSha256={HashFile(snupkgPath)}");
Console.WriteLine($"CoreDllSha256={HashBytes(libraryBytes)}");
Console.WriteLine($"ToolNupkgSha256={HashFile(toolNupkgPath)}");
Console.WriteLine($"ToolSnupkgSha256={HashFile(toolSnupkgPath)}");
Console.WriteLine($"ToolDllSha256={HashBytes(toolDllBytes)}");
Console.WriteLine($"NupkgEntryCount={nupkg.Entries.Count}");
Console.WriteLine($"SnupkgEntryCount={snupkg.Entries.Count}");
Console.WriteLine($"ToolNupkgEntryCount={toolNupkg.Entries.Count}");
Console.WriteLine($"ToolSnupkgEntryCount={toolSnupkg.Entries.Count}");
Console.WriteLine("ArchiveSafety=passed");
Console.WriteLine("PackedDocumentation=matched");
Console.WriteLine("SourceLink=matched");

static void ValidateEntries(ZipArchive archive, string name)
{
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var entry in archive.Entries)
    {
        Require(names.Add(entry.FullName), $"The {name} contains a duplicate path.");
        Require(!Path.IsPathRooted(entry.FullName), $"The {name} contains a rooted path.");
        Require(!entry.FullName.Contains('\\'), $"The {name} contains a backslash path.");
        Require(
            !entry.FullName.Split('/').Any(segment => segment == ".."),
            $"The {name} contains a parent path.");
    }
}

static ZipArchiveEntry RequireEntry(ZipArchive archive, string name) =>
    archive.GetEntry(name) ??
    throw new InvalidOperationException($"Package entry '{name}' is missing.");

static void CompareEntry(ZipArchive archive, string entryName, string sourcePath)
{
    Require(
        ReadEntry(RequireEntry(archive, entryName)).AsSpan()
            .SequenceEqual(File.ReadAllBytes(sourcePath)),
        $"Package entry '{entryName}' differs from its source file.");
}

static void ValidatePdb(byte[] bytes, string sourceSuffix, string expectedCommit)
{
    Require(
        Encoding.ASCII.GetString(bytes, 0, 4) == "BSJB",
        "A portable PDB header is invalid.");
    using var stream = new MemoryStream(bytes, writable: false);
    using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
    var reader = provider.GetMetadataReader();
    var documents = reader.Documents
        .Select(handle => reader.GetString(reader.GetDocument(handle).Name))
        .ToArray();
    Require(
        documents.Any(document => document.EndsWith(sourceSuffix, StringComparison.Ordinal)),
        $"The PDB does not contain '{sourceSuffix}'.");

    var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    string? sourceLink = null;
    foreach (var handle in reader.GetCustomDebugInformation(MetadataTokens.EntityHandle(1)))
    {
        var information = reader.GetCustomDebugInformation(handle);
        if (reader.GetGuid(information.Kind) == sourceLinkKind)
            sourceLink = Encoding.UTF8.GetString(reader.GetBlobBytes(information.Value));
    }

    if (sourceLink is null)
        throw new InvalidOperationException("Source Link is missing.");
    Require(
        sourceLink.Contains(expectedCommit, StringComparison.Ordinal),
        "Source Link has a different repository commit.");
}

static byte[] ReadEntry(ZipArchiveEntry entry)
{
    using var stream = entry.Open();
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return memory.ToArray();
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
