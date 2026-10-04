using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ComicStudio.Api.Data.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("Stories");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Synopsis).HasMaxLength(4000);
        builder.Property(s => s.Genre).HasMaxLength(120);
        builder.Property(s => s.Tags).HasMaxLength(500);
        builder.Property(s => s.TextModelId).HasMaxLength(64);

        builder.OwnsOne(s => s.Context, owned => owned.ToJson());
        builder.OwnsOne(s => s.Sampler, owned => owned.ToJson());

        builder.HasIndex(s => s.UserId);

        builder
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // CursorBlockId is intentionally a plain scalar: a real FK here would
        // create a second path from Story to StoryBlock and SQLite would need
        // one of them to be non-cascading. The cursor is app-managed.
        builder.Property(s => s.CursorBlockId);

        builder
            .HasMany(s => s.Blocks)
            .WithOne(b => b.Story!)
            .HasForeignKey(b => b.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(s => s.LorebookEntries)
            .WithOne(e => e.Story!)
            .HasForeignKey(e => e.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(s => s.Generations)
            .WithOne(g => g.Story!)
            .HasForeignKey(g => g.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StoryBlockConfiguration : IEntityTypeConfiguration<StoryBlock>
{
    public void Configure(EntityTypeBuilder<StoryBlock> builder)
    {
        builder.ToTable("StoryBlocks");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Text).IsRequired();

        builder
            .HasOne(b => b.Parent)
            .WithMany()
            .HasForeignKey(b => b.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.StoryId);
        builder.HasIndex(b => b.ParentId);
    }
}

public class LorebookEntryConfiguration : IEntityTypeConfiguration<LorebookEntry>
{
    public void Configure(EntityTypeBuilder<LorebookEntry> builder)
    {
        builder.ToTable("LorebookEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.KeysRaw).HasMaxLength(2000);
        builder.Property(e => e.Content).IsRequired();
        builder.Property(e => e.Category).HasMaxLength(80);
        builder.Property(e => e.Prefix).HasMaxLength(200);
        builder.Property(e => e.Suffix).HasMaxLength(200);

        builder.HasIndex(e => e.StoryId);
    }
}

public class StoryGenerationConfiguration : IEntityTypeConfiguration<StoryGeneration>
{
    public void Configure(EntityTypeBuilder<StoryGeneration> builder)
    {
        builder.ToTable("StoryGenerations");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.ModelId).HasMaxLength(64).IsRequired();
        builder.Property(g => g.Transport).HasMaxLength(32).IsRequired();
        builder.Property(g => g.ParametersJson).IsRequired();

        builder.HasIndex(g => g.StoryId);
        builder.HasIndex(g => new { g.StoryId, g.CreatedAt });
    }
}
