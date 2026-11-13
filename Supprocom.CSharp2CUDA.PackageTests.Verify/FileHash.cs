using System.Security.Cryptography;

namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class FileHash
{
    public static async Task<string> Sha256Async(string path)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
    }
}
