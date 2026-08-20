using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Core.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class TrackLayoutConfiguration : IEntityTypeConfiguration<TrackLayout>
{
    public void Configure(EntityTypeBuilder<TrackLayout> builder)
    {
        builder.ToTable("track_layouts");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.TrackId).HasColumnName("track_id").IsRequired();
        builder.Property(l => l.Name).HasColumnName("name").IsRequired();
        builder.Property(l => l.LengthMeters).HasColumnName("length_meters").HasColumnType("double precision").IsRequired();

        builder.HasIndex(l => new { l.TrackId, l.Name }).IsUnique();

        builder.HasMany(l => l.Sessions)
            .WithOne(s => s.TrackLayout)
            .HasForeignKey(s => s.TrackLayoutId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
