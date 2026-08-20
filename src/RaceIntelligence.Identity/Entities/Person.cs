namespace RaceIntelligence.Identity.Entities;

/// <summary>
/// One human being, independent of any simulator.
/// </summary>
/// <remarks>
/// The only entity in this platform that is not scoped to a simulator, and the reason this database
/// exists at all. Once storage is one database per simulator, nothing else in the system can say
/// that RaceRoom driver <c>4242</c> and iRacing customer <c>881109</c> are the same person — the two
/// rows live in databases that cannot be joined, by construction.
/// <para>
/// <see cref="DisplayName"/> is a label for a human reading the registry, not an identity. Identity
/// is the row, and the aliases hanging off it are what tie it to each simulator. Two people may
/// legitimately share a display name here, which is exactly why matching on it is refused — see
/// <see cref="PersonSimAlias"/>.
/// </para>
/// </remarks>
public sealed class Person
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>What to call this person in the registry. A label; never an identity.</summary>
    public required string DisplayName { get; set; }

    /// <summary>UTC time this person was first asserted.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Every simulator this person is known in. May be empty — see <see cref="PersonSimAlias"/>.</summary>
    public ICollection<PersonSimAlias> Aliases { get; set; } = new List<PersonSimAlias>();
}
