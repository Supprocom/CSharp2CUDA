namespace Supprocom.CSharp2CUDA;

internal static class CudaOutputPath
{
    private static readonly HashSet<string> ReservedFileNames = new(
        [
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "CLOCK$",
            "CONIN$",
            "CONOUT$",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "COM¹",
            "COM²",
            "COM³",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
            "LPT¹",
            "LPT²",
            "LPT³"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalize(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            return false;

        var segments = path.Split(['/', '\\']);
        if (segments.Any(IsInvalidSegment) ||
            !string.Equals(
                Path.GetExtension(segments[^1]),
                ".cu",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }

    public static string ToPlatformPath(string normalizedPath) =>
        Path.Combine(normalizedPath.Split('/'));

    private static bool IsInvalidSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment) ||
            segment is "." or ".." ||
            !string.Equals(segment, segment.Trim(), StringComparison.Ordinal) ||
            segment.EndsWith('.') ||
            segment.Any(character =>
                char.IsControl(character) ||
                character is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
        {
            return true;
        }

        var baseName = segment.Split('.')[0].TrimEnd(' ', '.');
        return ReservedFileNames.Contains(baseName);
    }
}
