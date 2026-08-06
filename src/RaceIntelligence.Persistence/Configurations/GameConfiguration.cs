using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Configurations;

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("games");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(g => g.Key).HasColumnName("key").IsRequired();
        builder.Property(g => g.Name).HasColumnName("name").IsRequired();
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(g => g.Key).IsUnique();

        builder.HasMany(g => g.Versions)
            .WithOne(v => v.Game)
            .HasForeignKey(v => v.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(g => g.Tracks)
            .WithOne(t => t.Game)
            .HasForeignKey(t => t.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(g => g.Cars)
            .WithOne(c => c.Game)
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
