using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Identity.Entities;

namespace RaceIntelligence.Identity.Configurations;

public sealed class PersonSimAliasConfiguration : IEntityTypeConfiguration<PersonSimAlias>
{
    public void Configure(EntityTypeBuilder<PersonSimAlias> builder)
    {
        builder.ToTable("person_sim_alias");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.PersonId).HasColumnName("person_id").IsRequired();
        builder.Property(a => a.SimKey).HasColumnName("sim_key").IsRequired();
        builder.Property(a => a.SimDriverId).HasColumnName("sim_driver_id").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        // The constraint the whole registry turns on: one simulator identity belongs to at most one
        // person. Enforced by the database rather than by the endpoint, because the endpoint is not
        // the only thing that will ever write here — a translator, a seeding script and a human with
        // psql all have to meet the same rule.
        //
        // Note what is *not* constrained: a person may hold any number of aliases, and may hold
        // several within one simulator. Two accounts in the same sim being the same human is
        // ordinary, and refusing it would make the registry unable to describe a real case.
        builder.HasIndex(a => new { a.SimKey, a.SimDriverId }).IsUnique();

        // The lookup a translator does per row: given a person, which sims are they in.
        builder.HasIndex(a => a.PersonId);
    }
}
