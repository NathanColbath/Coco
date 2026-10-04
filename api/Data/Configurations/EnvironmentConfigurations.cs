using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Environment = ComicStudio.Api.Domain.Environment;

namespace ComicStudio.Api.Data.Configurations;

public class EnvironmentConfiguration : IEntityTypeConfiguration<Environment>
{
    public void Configure(EntityTypeBuilder<Environment> builder)
    {
        builder.ToTable("Environments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Aliases).HasMaxLength(1000);
        builder.Property(e => e.VisualTags).HasMaxLength(2000);
        builder.Property(e => e.Lighting).HasMaxLength(1000);
        builder.Property(e => e.PaletteNotes).HasMaxLength(1000);
        builder.Property(e => e.Features).HasMaxLength(2000);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.StoryId);

        builder
            .HasOne(e => e.Story)
            .WithMany()
            .HasForeignKey(e => e.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(e => e.Plates)
            .WithOne(p => p.Environment!)
            .HasForeignKey(p => p.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EnvironmentPlateConfiguration : IEntityTypeConfiguration<EnvironmentPlate>
{
    public void Configure(EntityTypeBuilder<EnvironmentPlate> builder)
    {
        builder.ToTable("EnvironmentPlates");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ModelId).HasMaxLength(64);

        builder
            .HasOne(p => p.Image)
            .WithMany()
            .HasForeignKey(p => p.ImageAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SceneConfiguration : IEntityTypeConfiguration<Scene>
{
    public void Configure(EntityTypeBuilder<Scene> builder)
    {
        builder.ToTable("Scenes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Summary).HasMaxLength(2000);
        builder.Property(s => s.TimeOfDay).HasMaxLength(120);
        builder.Property(s => s.Mood).HasMaxLength(200);
        builder.Property(s => s.Purpose).HasMaxLength(1000);
        builder.Property(s => s.Enters).HasMaxLength(2000);
        builder.Property(s => s.Leaves).HasMaxLength(2000);

        builder.HasIndex(s => new { s.ProjectId, s.Index });

        builder
            .HasOne<ComicProject>()
            .WithMany(p => p.Scenes)
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
