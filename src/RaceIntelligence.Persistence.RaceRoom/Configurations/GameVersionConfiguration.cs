using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class GameVersionConfiguration : IEntityTypeConfiguration<GameVersion>
{
    public void Configure(EntityTypeBuilder<GameVersion> builder)
    {
        builder.ToTable("game_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(v => v.GameVersionText).HasColumnName("game_version");
        builder.Property(v => v.ApiVersionMajor).HasColumnName("api_version_major").IsRequired();
        builder.Property(v => v.ApiVersionMinor).HasColumnName("api_version_minor").IsRequired();
        builder.Property(v => v.ConnectorVersion).HasColumnName("connector_version").IsRequired();
        builder.Property(v => v.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();

        builder.HasIndex(v => new { v.GameVersionText, v.ApiVersionMajor, v.ApiVersionMinor, v.ConnectorVersion })
            .IsUnique();

        builder.HasMany(v => v.Sessions)
            .WithOne(s => s.GameVersion)
            .HasForeignKey(s => s.GameVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
