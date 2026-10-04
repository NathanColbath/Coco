using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ComicStudio.Api.Data.Configurations;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.ToTable("Characters");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Aliases).HasMaxLength(1000);

        builder.OwnsOne(c => c.Visual, owned => owned.ToJson());

        builder.HasIndex(c => c.StoryId);

        builder
            .HasOne(c => c.Story)
            .WithMany()
            .HasForeignKey(c => c.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(c => c.Sheets)
            .WithOne(s => s.Character!)
            .HasForeignKey(s => s.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CharacterSheetImageConfiguration : IEntityTypeConfiguration<CharacterSheetImage>
{
    public void Configure(EntityTypeBuilder<CharacterSheetImage> builder)
    {
        builder.ToTable("CharacterSheetImages");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ModelId).HasMaxLength(64);

        builder
            .HasOne(s => s.Image)
            .WithMany()
            .HasForeignKey(s => s.ImageAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ImageAssetConfiguration : IEntityTypeConfiguration<ImageAsset>
{
    public void Configure(EntityTypeBuilder<ImageAsset> builder)
    {
        builder.ToTable("ImageAssets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.RelativePath).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(a => a.MimeType).HasMaxLength(64).IsRequired();

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Sha256);
        builder.HasIndex(a => new { a.ProjectId, a.Kind });
    }
}

public class ComicProjectConfiguration : IEntityTypeConfiguration<ComicProject>
{
    public void Configure(EntityTypeBuilder<ComicProject> builder)
    {
        builder.ToTable("ComicProjects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Premise).HasMaxLength(4000);

        builder.OwnsOne(p => p.Style, owned => owned.ToJson());

        builder.HasIndex(p => p.UserId);

        builder
            .HasOne(p => p.Story)
            .WithMany()
            .HasForeignKey(p => p.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(p => p.Pages)
            .WithOne(page => page.Project!)
            .HasForeignKey(page => page.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(p => p.CurationRuns)
            .WithOne(run => run.Project!)
            .HasForeignKey(run => run.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ComicPageConfiguration : IEntityTypeConfiguration<ComicPage>
{
    public void Configure(EntityTypeBuilder<ComicPage> builder)
    {
        builder.ToTable("ComicPages");
        builder.HasKey(page => page.Id);
        builder.Property(page => page.LayoutTemplateId).HasMaxLength(64).IsRequired();
        builder.HasIndex(page => new { page.ProjectId, page.Index });

        builder
            .HasMany(page => page.Dialogue)
            .WithOne(line => line.Page!)
            .HasForeignKey(line => line.PageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DialogueLineConfiguration : IEntityTypeConfiguration<DialogueLine>
{
    public void Configure(EntityTypeBuilder<DialogueLine> builder)
    {
        builder.ToTable("DialogueLines");
        builder.HasKey(line => line.Id);

        builder
            .HasOne(line => line.Speaker)
            .WithMany()
            .HasForeignKey(line => line.SpeakerCharacterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PanelGenerationConfiguration : IEntityTypeConfiguration<PanelGeneration>
{
    public void Configure(EntityTypeBuilder<PanelGeneration> builder)
    {
        builder.ToTable("PanelGenerations");
        builder.HasKey(generation => generation.Id);
        builder.Property(generation => generation.ModelId).HasMaxLength(64).IsRequired();
        builder.Property(generation => generation.RequestJson).IsRequired();

        builder.HasIndex(generation => new { generation.Kind, generation.TargetId });

        builder
            .HasOne(generation => generation.Image)
            .WithMany()
            .HasForeignKey(generation => generation.ImageAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CurationRunConfiguration : IEntityTypeConfiguration<CurationRun>
{
    public void Configure(EntityTypeBuilder<CurationRun> builder)
    {
        builder.ToTable("CurationRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.ModelId).HasMaxLength(64).IsRequired();
        builder.HasIndex(run => run.ProjectId);
    }
}

public class CoverPlanConfiguration : IEntityTypeConfiguration<CoverPlan>
{
    public void Configure(EntityTypeBuilder<CoverPlan> builder)
    {
        builder.ToTable("CoverPlans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.TitleFont).HasMaxLength(64);
        builder.HasIndex(plan => plan.ProjectId).IsUnique();
    }
}

public class PageLayoutTemplateConfiguration : IEntityTypeConfiguration<PageLayoutTemplate>
{
    public void Configure(EntityTypeBuilder<PageLayoutTemplate> builder)
    {
        builder.ToTable("PageLayoutTemplates");
        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).HasMaxLength(64);
        builder.Property(template => template.Name).HasMaxLength(120).IsRequired();
        builder.Property(template => template.Description).HasMaxLength(400);
        builder.Property(template => template.SlotsJson).IsRequired();
        builder.Ignore(template => template.Slots);
    }
}
