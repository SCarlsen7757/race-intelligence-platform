using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.ToTable("tracks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.Name).HasColumnName("name").IsRequired();

        builder.HasIndex(t => t.Name).IsUnique();

        builder.HasMany(t => t.Layouts)
            .WithOne(l => l.Track)
            .HasForeignKey(l => l.TrackId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
