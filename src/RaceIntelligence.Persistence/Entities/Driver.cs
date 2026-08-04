namespace RaceIntelligence.Persistence.Entities;

/// <summary>A driver a session can be attributed to.</summary>
/// <remarks>
/// The schema deliberately does not put a uniqueness constraint on <see cref="DisplayName"/>:
/// driver identity from telemetry sources is just a reported name, and two different people can
/// share one. See <c>Repositories/DriverRepository.cs</c> for how resolve-or-create handles that.
/// </remarks>
public sealed class Driver
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>The driver's display/player name as reported by the simulator.</summary>
    public required string DisplayName { get; set; }

    /// <summary>UTC time this driver row was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Sessions attributed to this driver.</summary>
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
