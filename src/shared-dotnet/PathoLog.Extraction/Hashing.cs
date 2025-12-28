using System.Security.Cryptography;

namespace PathoLog.Extraction;

public static class Hashing
{
    public static string ComputeSha256(Stream content)
    {
        if (!content.CanSeek)
        {
            throw new InvalidOperationException("Stream must be seekable to compute hash.");
        }

        var originalPosition = content.Position;
        content.Position = 0;
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(content);
        content.Position = originalPosition;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
