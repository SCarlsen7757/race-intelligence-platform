using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Identity.Entities;

namespace RaceIntelligence.Identity.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("person");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(p => p.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();

        // Deliberately not unique. Two people may share a display name — that is precisely why this
        // registry exists rather than matching on names — and a unique index here would force
        // whoever is seeding it to invent distinguishing suffixes for real humans.
        builder.HasIndex(p => p.DisplayName);

        // Cascade, and it is the one place in this schema that does. An alias has no meaning without
        // the person it points at, so deleting a person that was asserted by mistake should take the
        // claims with it rather than leaving rows that block the sim ids being claimed again.
        builder.HasMany(p => p.Aliases)
            .WithOne(a => a.Person)
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
