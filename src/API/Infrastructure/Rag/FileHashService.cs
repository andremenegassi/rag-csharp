using System.Security.Cryptography;

namespace IA.API.Infrastructure.Rag;

public sealed class FileHashService
{
    public async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        stream.Position = 0;

        return Convert.ToHexString(hash);
    }
}
