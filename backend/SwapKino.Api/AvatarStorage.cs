using Minio;
using Minio.DataModel.Args;

namespace SwapKino.Api;

public sealed class AvatarStorage(IMinioClient minio, IConfiguration config, ILogger<AvatarStorage> log)
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif"
    };
    private string Bucket => config["MINIO_BUCKET"] ?? "swapkino-uploads";
    private string PublicBase => (config["MINIO_PUBLIC_URL"] ?? "http://localhost:9000").TrimEnd('/');

    public async Task<string> SaveAsync(Guid userId, IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > MaxBytes) throw new InvalidOperationException("Аватар должен быть размером от 1 байта до 5 МБ");
        if (!ContentTypes.TryGetValue(file.ContentType, out var extension)) throw new InvalidOperationException("Поддерживаются JPG, PNG, WEBP и GIF");
        await EnsureBucketAsync(ct);
        var objectName = $"avatars/{userId:N}/{Guid.NewGuid():N}{extension}";
        await using var stream = file.OpenReadStream();
        await minio.PutObjectAsync(new PutObjectArgs().WithBucket(Bucket).WithObject(objectName).WithStreamData(stream).WithObjectSize(file.Length).WithContentType(file.ContentType), ct);
        return $"{PublicBase}/{Bucket}/{Uri.EscapeDataString(objectName).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}";
    }

    public async Task DeleteAsync(string? url, CancellationToken ct)
    {
        var prefix = $"{PublicBase}/{Bucket}/";
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var objectName = Uri.UnescapeDataString(url[prefix.Length..]);
        try { await minio.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(Bucket).WithObject(objectName), ct); }
        catch (Exception ex) { log.LogWarning(ex, "Could not remove old avatar object {ObjectName}", objectName); }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (!await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(Bucket), ct))
        {
            await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(Bucket), ct);
            var policy = $$"""
            {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"AWS":["*"]},"Action":["s3:GetObject"],"Resource":["arn:aws:s3:::{{Bucket}}/avatars/*"]}]}
            """;
            await minio.SetPolicyAsync(new SetPolicyArgs().WithBucket(Bucket).WithPolicy(policy), ct);
        }
    }
}
