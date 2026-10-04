using System.Text.Json;
using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ComicStudio.Api.Data.Configurations;

public class AiModelConfiguration : IEntityTypeConfiguration<AiModel>
{
    public void Configure(EntityTypeBuilder<AiModel> builder)
    {
        builder.ToTable("AiModels");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasMaxLength(64);
        builder.Property(m => m.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(m => m.ProviderModelId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Family).HasMaxLength(64);

        builder.OwnsOne(m => m.Capabilities, owned => owned.ToJson());

        var tierOverrides = new ValueConverter<Dictionary<string, int>?, string?>(
            value => value == null ? null : JsonSerializer.Serialize(value, JsonOptions),
            value =>
                value == null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, int>>(value, JsonOptions)
        );

        builder
            .Property(m => m.TierOverrides)
            .HasConversion(tierOverrides)
            .HasColumnName("TierOverrides");
    }

    private static readonly JsonSerializerOptions JsonOptions = new();
}
