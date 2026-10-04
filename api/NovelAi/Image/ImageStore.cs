using System.Security.Cryptography;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;

namespace ComicStudio.Api.NovelAi.Image;

/// <summary>
/// Writes generated images to disk and records them in the database. Bytes live
/// outside SQLite so the database stays small and portable.
/// </summary>
public class ImageStore(IConfiguration configuration, AppDbContext db)
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public string RootPath { get; } = ResolveRoot(configuration);

    public async Task<ImageAsset> SaveAsync(
        byte[] bytes,
        string userId,
        ImageAssetKind kind,
        int width,
        int height,
        string? metadata = null,
        Guid? projectId = null,
        Guid? storyId = null,
        CancellationToken cancellationToken = default
    )
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var stamp = DateTimeOffset.UtcNow;
        var directory = Path.Combine(RootPath, userId, stamp.ToString("yyyy-MM"));
        Directory.CreateDirectory(directory);

        var fileName = $"{hash[..16]}.png";
        var fullPath = Path.Combine(directory, fileName);
        var relativePath = Path.GetRelativePath(RootPath, fullPath);

        // Identical bytes are the same asset; never rewrite them.
        if (!File.Exists(fullPath))
        {
            await WriteLock.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(fullPath))
                {
                    await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
                }
            }
            finally
            {
                WriteLock.Release();
            }
        }

        var asset = new ImageAsset
        {
            UserId = userId,
            Kind = kind,
            ProjectId = projectId,
            StoryId = storyId,
            RelativePath = relativePath,
            Sha256 = hash,
            Width = width,
            Height = height,
            Bytes = bytes.LongLength,
            MimeType = "image/png",
            SourceMetadataJson = metadata,
        };

        db.ImageAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public string ResolvePath(ImageAsset asset) => Path.Combine(RootPath, asset.RelativePath);

    public async Task<byte[]?> ReadAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.ImageAssets.FindAsync([assetId], cancellationToken);
        if (asset is null)
        {
            return null;
        }

        var path = ResolvePath(asset);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["Storage:ArtifactsPath"] ?? "App_Data/artifacts";
        return Path.GetFullPath(configured);
    }
}
