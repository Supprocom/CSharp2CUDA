using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Emission;

internal sealed class CudaSourcePathMapper(string? sourceRoot)
{
    private readonly string? normalizedSourceRoot = sourceRoot is null
        ? null
        : Path.GetFullPath(sourceRoot);

    public CudaMappedLocation? Map(Location location)
    {
        if (location == Location.None || !location.IsInSource)
            return null;

        var span = location.GetMappedLineSpan();
        if (!span.IsValid || string.IsNullOrWhiteSpace(span.Path))
            span = location.GetLineSpan();
        if (!span.IsValid || string.IsNullOrWhiteSpace(span.Path))
            return null;

        var path = NormalizePath(span.Path);
        if (path.Length == 0)
            return null;

        return new CudaMappedLocation(
            path,
            span.StartLinePosition.Line + 1);
    }

    public static string EscapeDirectivePath(string path)
    {
        var result = new System.Text.StringBuilder(path.Length);
        foreach (var character in path)
        {
            switch (character)
            {
                case '\\':
                    result.Append("\\\\");
                    break;
                case '"':
                    result.Append("\\\"");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                default:
                    result.Append(char.IsControl(character) ? '_' : character);
                    break;
            }
        }
        return result.ToString();
    }

    private string NormalizePath(string path)
    {
        try
        {
            var absolutePath = Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : normalizedSourceRoot is null
                    ? path
                    : Path.GetFullPath(Path.Combine(normalizedSourceRoot, path));
            path = normalizedSourceRoot is null
                ? absolutePath
                : Path.GetRelativePath(normalizedSourceRoot, absolutePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            path = path.Trim();
        }

        var normalized = path.Replace('\\', '/');
        var result = new System.Text.StringBuilder(normalized.Length);
        foreach (var character in normalized)
            result.Append(char.IsControl(character) ? '_' : character);
        return result.ToString();
    }
}

internal sealed record CudaMappedLocation(
    string SourcePath,
    int SourceLine);
