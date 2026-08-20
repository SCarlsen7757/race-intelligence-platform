namespace RaceIntelligence.Persistence.Entities;

/// <summary>A driver a session can be attributed to.</summary>
/// <remarks>
/// Identity is <see cref="SimDriverId"/> whenever the simulator supplies one — the same "sim's own
/// id" convention <see cref="Car"/> uses. It used to be scoped by game, because two simulators can
/// hand out the same numeric id to different people; that scoping is gone because the *database* is
/// the simulator now. Recognising one human across simulators is a different question with a
/// different answer — the identity registry, ADR 0002 — and deliberately not this row.
/// <see cref="DisplayName"/> is <i>not</i> an identity; it is a mutable label tracking the
/// most recently observed name for this driver, rewritten when the person renames themselves. The
/// name actually reported at the time of a given session is preserved on
/// <see cref="Session.PlayerName"/> instead.
/// <para>
/// Sims that expose no driver id at all fall back to name-based resolution: rows with a null
/// <see cref="SimDriverId"/> are unique on <see cref="DisplayName"/>. Such a driver cannot survive
/// a rename — there is nothing stable
/// to tie the old and new names together — and two real people sharing one name in that sim would
/// collapse into a single row. See <c>Repositories/DriverRepository.cs</c> for both paths.
/// </para>
/// </remarks>
public sealed class Driver
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The simulator's own stable identifier for this driver (e.g. a RaceRoom account id), if the
    /// sim exposes one. Unique within its game. <see langword="null"/> for sims that report only a
    /// name, in which case <see cref="DisplayName"/> carries identity within the game instead.
    /// </summary>
    public string? SimDriverId { get; set; }

    /// <summary>
    /// The driver's most recently observed display/player name. A mutable label, not an identity —
    /// it is updated in place when a driver resolved by <see cref="SimDriverId"/> is seen under a
    /// new name.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>UTC time this driver row was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Sessions attributed to this driver.</summary>
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
