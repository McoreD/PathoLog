using System.Security.Cryptography;

namespace PathoLog.Persistence;

public sealed class LocalFileStore : ILocalFileStore
{
    private readonly string _rootDirectory;

    public LocalFileStore(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string sha256Hash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sha256Hash))
        {
            throw new ArgumentException("SHA256 hash is required", nameof(sha256Hash));
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        var relativePath = GetRelativePath(sha256Hash, sanitizedFileName);
        var fullPath = Path.Combine(_rootDirectory, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var output = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_rootDirectory, storedPath);
        Stream stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public static string ComputeSha256(Stream content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetRelativePath(string sha256Hash, string fileName)
    {
        var prefixA = sha256Hash.Substring(0, 2);
        var prefixB = sha256Hash.Substring(2, 2);
        return Path.Combine(prefixA, prefixB, $"{sha256Hash}_{fileName}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Where(ch => !invalid.Contains(ch)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "file.pdf" : cleaned;
    }
}
