using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Supprocom.CSharp2CUDA.Build;

internal static class CudaAssemblyInspector
{
    private const string MarkerNamespace = "Supprocom.CSharp2CUDA";
    private const string MarkerName = "TranspileToCUDAAttribute";

    public static bool HasProductClassMarker(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException("The managed compiler output has no metadata.");

        var reader = peReader.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (IsMarkerConstructor(reader, attribute.Constructor))
                    return true;
            }
        }

        return false;
    }

    private static bool IsMarkerConstructor(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
            return false;

        var member = reader.GetMemberReference((MemberReferenceHandle)constructor);
        if (member.Parent.Kind != HandleKind.TypeReference)
            return false;

        var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
        return reader.StringComparer.Equals(type.Namespace, MarkerNamespace) &&
            reader.StringComparer.Equals(type.Name, MarkerName) &&
            IsProductAssembly(reader, type.ResolutionScope);
    }

    private static bool IsProductAssembly(MetadataReader reader, EntityHandle scope)
    {
        if (scope.Kind != HandleKind.AssemblyReference)
            return false;

        var reference = reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
        var product = typeof(TranspileToCUDAAttribute).Assembly.GetName();
        var productName = product.Name ?? throw new InvalidOperationException(
            "The product assembly name is unavailable.");
        return reader.StringComparer.Equals(reference.Name, productName) &&
            reference.Version == product.Version &&
            string.Equals(
                GetCulture(reader, reference.Culture),
                product.CultureName ?? string.Empty,
                StringComparison.Ordinal) &&
            reader.GetBlobBytes(reference.PublicKeyOrToken).AsSpan().SequenceEqual(
                product.GetPublicKeyToken() ?? []);
    }

    private static string GetCulture(MetadataReader reader, StringHandle handle) =>
        handle.IsNil ? string.Empty : reader.GetString(handle);
}
