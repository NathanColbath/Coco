using System.Reflection;
using ComicStudio.Api.Features.Comics;
using ComicStudio.Api.NovelAi.Image;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// NovelAI takes only a few director references, so the page runner keeps the
/// four best in priority order: character sheets first, then the style anchor,
/// the environment plate and the previous page. That cap currently lives inline
/// inside ImageGenerationRunner.LoadPageReferencesAsync, behind a DbContext, so
/// it is not reachable from a unit test. This skipped test states the contract
/// an extracted pure helper should satisfy; enable it once the helper exists.
/// </summary>
public class ReferenceCappingTests
{
    [Fact(
        Skip = "The cap is inline in ImageGenerationRunner.LoadPageReferencesAsync and needs a DbContext. "
            + "Extract a pure helper (expected: static CapReferences(references, max)) and remove this skip."
    )]
    public void Keeps_the_four_highest_priority_references()
    {
        // Seven candidates arriving in priority order; a byte per reference
        // makes them identifiable after the cap.
        var candidates = Enumerable
            .Range(0, 7)
            .Select(index => new ReferenceImage([(byte)index], ReferenceKind.Style))
            .ToList();

        var method = typeof(ImageGenerationRunner).GetMethod(
            "CapReferences",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.True(
            method is not null,
            "ImageGenerationRunner.CapReferences should exist; the test found no such helper."
        );

        var kept = method!.GetParameters().Length == 2
            ? method.Invoke(null, [candidates, 4])
            : method.Invoke(null, [candidates]);

        var references = Assert
            .IsAssignableFrom<IEnumerable<ReferenceImage>>(kept)
            .ToList();

        Assert.Equal(4, references.Count);
        Assert.Equal([0, 1, 2, 3], references.Select(image => (int)image.Bytes[0]));
    }
}
