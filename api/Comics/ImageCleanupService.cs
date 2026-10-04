using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Features.Comics;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Comics;

/// <summary>Kind labels used on <see cref="DeleteImpactDto"/> cascades.</summary>
public static class DeletionCascadeKinds
{
    public const string Characters = "Characters";

    public const string Blocks = "Blocks";

    public const string Lorebook = "Lorebook";

    public const string Comic = "Comic";
}

/// <summary>
/// Reports and performs what a delete does to stored images. Database rows
/// always go first; a file is only unlinked once no remaining row points at
/// it, so content-addressed files shared with another row survive. Character
/// sheets and environment plates belong to their story entity rather than to
/// the comic that happened to generate them, so a comic delete leaves them
/// alone.
/// </summary>
public class ImageCleanupService(AppDbContext db, IConfiguration configuration)
{
    public string RootPath { get; } = ResolveRoot(configuration);

    public async Task<DeletionPreviewDto> PreviewComicAsync(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        var pages = await db
            .ComicPages.AsNoTracking()
            .CountAsync(page => page.ProjectId == projectId, cancellationToken);

        var assets = db
            .ImageAssets.AsNoTracking()
            .Where(asset =>
                asset.ProjectId == projectId
                && asset.Kind != ImageAssetKind.CharacterSheet
                && !db.EnvironmentPlates.Any(plate => plate.ImageAssetId == asset.Id)
            );

        var images = await assets.CountAsync(cancellationToken);

        var bytes =
            (await assets.SumAsync(asset => (long?)asset.Bytes, cancellationToken)) ?? 0L;

        var hasCover =
            await db
                .CoverPlans.AsNoTracking()
                .AnyAsync(plan => plan.ProjectId == projectId, cancellationToken)
            || await assets.AnyAsync(
                asset => asset.Kind == ImageAssetKind.Cover,
                cancellationToken
            );

        var curationRuns = await db
            .CurationRuns.AsNoTracking()
            .CountAsync(run => run.ProjectId == projectId, cancellationToken);

        return new DeletionPreviewDto(pages, images, bytes, hasCover, curationRuns, []);
    }

    public async Task<DeletionPreviewDto> PreviewStoryAsync(
        Guid storyId,
        CancellationToken cancellationToken
    )
    {
        var projects = await db
            .ComicProjects.AsNoTracking()
            .Where(project => project.StoryId == storyId)
            .OrderBy(project => project.Title)
            .Select(project => new { project.Id, project.Title })
            .ToListAsync(cancellationToken);

        var projectIds = projects.Select(project => project.Id).ToList();

        var pageCounts = await db
            .ComicPages.AsNoTracking()
            .Where(page => projectIds.Contains(page.ProjectId))
            .GroupBy(page => page.ProjectId)
            .Select(group => new { ProjectId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ProjectId, item => item.Count, cancellationToken);

        var curationRuns = await db
            .CurationRuns.AsNoTracking()
            .CountAsync(run => projectIds.Contains(run.ProjectId), cancellationToken);

        var assets = await db
            .ImageAssets.AsNoTracking()
            .Where(asset =>
                asset.StoryId == storyId
                || (asset.ProjectId != null && projectIds.Contains(asset.ProjectId.Value))
            )
            .Select(asset => new
            {
                asset.Id,
                asset.ProjectId,
                asset.Kind,
                asset.Bytes,
            })
            .ToListAsync(cancellationToken);

        var plateAssetIds = (
            await db
                .EnvironmentPlates.AsNoTracking()
                .Where(plate => plate.Environment!.StoryId == storyId)
                .Select(plate => plate.ImageAssetId)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

        var characters = await db
            .Characters.AsNoTracking()
            .CountAsync(character => character.StoryId == storyId, cancellationToken);

        var blocks = await db
            .StoryBlocks.AsNoTracking()
            .CountAsync(block => block.StoryId == storyId, cancellationToken);

        var lorebook = await db
            .LorebookEntries.AsNoTracking()
            .CountAsync(entry => entry.StoryId == storyId, cancellationToken);

        var cascades = new List<DeleteImpactDto>();

        // DeleteImpactDto has no dedicated count field, so for cascades that
        // are not comics, Pages carries the number of entities being destroyed.
        if (characters > 0)
        {
            var sheets = assets
                .Where(asset => asset.Kind == ImageAssetKind.CharacterSheet)
                .ToList();

            cascades.Add(
                new DeleteImpactDto(
                    DeletionCascadeKinds.Characters,
                    CountLabel(characters, "character", "characters"),
                    characters,
                    sheets.Count,
                    sheets.Sum(asset => asset.Bytes)
                )
            );
        }

        if (blocks > 0)
        {
            cascades.Add(
                new DeleteImpactDto(
                    DeletionCascadeKinds.Blocks,
                    CountLabel(blocks, "story block", "story blocks"),
                    blocks,
                    0,
                    0
                )
            );
        }

        if (lorebook > 0)
        {
            cascades.Add(
                new DeleteImpactDto(
                    DeletionCascadeKinds.Lorebook,
                    CountLabel(lorebook, "lorebook entry", "lorebook entries"),
                    lorebook,
                    0,
                    0
                )
            );
        }

        foreach (var project in projects)
        {
            var comicAssets = assets
                .Where(asset =>
                    asset.ProjectId == project.Id
                    && asset.Kind != ImageAssetKind.CharacterSheet
                    && !plateAssetIds.Contains(asset.Id)
                )
                .ToList();

            cascades.Add(
                new DeleteImpactDto(
                    DeletionCascadeKinds.Comic,
                    project.Title,
                    pageCounts.GetValueOrDefault(project.Id),
                    comicAssets.Count,
                    comicAssets.Sum(asset => asset.Bytes)
                )
            );
        }

        var hasCover =
            assets.Any(asset => asset.Kind == ImageAssetKind.Cover)
            || await db
                .CoverPlans.AsNoTracking()
                .AnyAsync(plan => projectIds.Contains(plan.ProjectId), cancellationToken);

        return new DeletionPreviewDto(
            pageCounts.Values.Sum(),
            assets.Count,
            assets.Sum(asset => asset.Bytes),
            hasCover,
            curationRuns,
            cascades
        );
    }

    /// <summary>
    /// Removes a comic's own image rows and unlinks any files nothing else
    /// references. Sheets and plates are left for their character or place to
    /// release, because they are shared by every comic adapted from the story.
    /// </summary>
    public async Task DeleteComicImagesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var assets = await db
            .ImageAssets.Where(asset =>
                asset.ProjectId == projectId
                && asset.Kind != ImageAssetKind.CharacterSheet
                && !db.EnvironmentPlates.Any(plate => plate.ImageAssetId == asset.Id)
            )
            .ToListAsync(cancellationToken);

        await RemoveAssetsAsync(assets, cancellationToken);
    }

    /// <summary>Removes every image row belonging to a story and its comics.</summary>
    public async Task DeleteStoryImagesAsync(Guid storyId, CancellationToken cancellationToken)
    {
        var projectIds = await db
            .ComicProjects.AsNoTracking()
            .Where(project => project.StoryId == storyId)
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);

        var assets = await db
            .ImageAssets.Where(asset =>
                asset.StoryId == storyId
                || (asset.ProjectId != null && projectIds.Contains(asset.ProjectId.Value))
            )
            .ToListAsync(cancellationToken);

        await RemoveAssetsAsync(assets, cancellationToken);
    }

    private async Task RemoveAssetsAsync(
        IReadOnlyList<ImageAsset> assets,
        CancellationToken cancellationToken
    )
    {
        if (assets.Count == 0)
        {
            return;
        }

        var assetIds = assets.Select(asset => asset.Id).ToList();

        // Restrict relationships must let go first. Sheets and plates go with
        // their character or place; generation audits survive without the bytes.
        var sheets = await db
            .CharacterSheetImages.Where(sheet => assetIds.Contains(sheet.ImageAssetId))
            .ToListAsync(cancellationToken);
        db.CharacterSheetImages.RemoveRange(sheets);

        var plates = await db
            .EnvironmentPlates.Where(plate => assetIds.Contains(plate.ImageAssetId))
            .ToListAsync(cancellationToken);
        db.EnvironmentPlates.RemoveRange(plates);

        var generations = await db
            .PanelGenerations.Where(generation =>
                generation.ImageAssetId != null && assetIds.Contains(generation.ImageAssetId.Value)
            )
            .ToListAsync(cancellationToken);
        foreach (var generation in generations)
        {
            generation.ImageAssetId = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        var paths = assets
            .Select(asset => asset.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        db.ImageAssets.RemoveRange(assets);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var path in paths)
        {
            var referenced = await db.ImageAssets.AnyAsync(
                asset => asset.RelativePath == path,
                cancellationToken
            );

            if (!referenced)
            {
                TryDeleteFile(path);
            }
        }
    }

    private void TryDeleteFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(RootPath);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

            // A tampered path must never reach outside the artifacts root.
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException)
        {
            // Missing or locked files are not worth failing a delete over.
        }
        catch (UnauthorizedAccessException)
        {
            // The rows are gone either way; leave the file for manual cleanup.
        }
        catch (ArgumentException)
        {
            // A malformed stored path can never be a file we created.
        }
    }

    private static string CountLabel(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["Storage:ArtifactsPath"] ?? "App_Data/artifacts";
        return Path.GetFullPath(configured);
    }
}
