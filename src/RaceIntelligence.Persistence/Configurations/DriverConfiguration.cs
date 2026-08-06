using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Configurations;

/// <summary>Maps <see cref="Driver"/> to the <c>drivers</c> table.</summary>
public sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.GameId).HasColumnName("game_id").IsRequired();
        builder.Property(d => d.SimDriverId).HasColumnName("sim_driver_id");
        builder.Property(d => d.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();

        // Postgres treats NULLs as distinct in a unique index, so this constrains only the rows
        // that actually carry a sim driver id — name-only drivers (sim_driver_id IS NULL) are not
        // affected by it, and are covered by the partial index below instead.
        builder.HasIndex(d => new { d.GameId, d.SimDriverId }).IsUnique();

        // The name-fallback path. Uniqueness here is enforced over exactly the rows matching this
        // filter, whatever any query looks like. What the matching predicate in DriverRepository
        // buys is that the planner can actually use this index to satisfy that lookup — keep the
        // two in step, or the fallback path silently degrades to a sequential scan.
        builder.HasIndex(d => new { d.GameId, d.DisplayName })
            .IsUnique()
            .HasFilter("sim_driver_id IS NULL");

        builder.HasOne(d => d.Game)
            .WithMany()
            .HasForeignKey(d => d.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Sessions)
            .WithOne(s => s.Driver)
            .HasForeignKey(s => s.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
