namespace RaceIntelligence.Identity.Entities;

/// <summary>
/// One simulator's name for a <see cref="Person"/>.
/// </summary>
/// <remarks>
/// <b>Asserted, never inferred.</b> A simulator's driver id is stable within that simulator and
/// shares a numeric namespace with every other simulator's, so ids collide across sims and mean
/// nothing to each other. Matching on display name is the precise failure the original
/// <c>(game_id, sim_driver_id)</c> design existed to avoid: people rename themselves, and two people
/// pick the same name. So a row gets here because a human said so — assisted at most by a prompt
/// asking whether two names are the same person, which a human answers.
/// <para>
/// A driver with no alias is not an error and not a gap to be filled automatically. They are simply
/// absent from cross-simulator analysis while remaining fully present in their own simulator's data.
/// </para>
/// <para>
/// Unique on <c>(<see cref="SimKey"/>, <see cref="SimDriverId"/>)</c>: one simulator identity
/// belongs to at most one person. The reverse is not constrained, because being the same human in
/// several simulators is the entire point.
/// </para>
/// </remarks>
public sealed class PersonSimAlias
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>The person this simulator identity belongs to.</summary>
    public Guid PersonId { get; set; }

    public Person? Person { get; set; }

    /// <summary>
    /// Which simulator issued the id — <c>raceroom</c>, <c>acc</c>, and so on.
    /// </summary>
    /// <remarks>
    /// The same game key the collector and the live wire already use, so a value here means the same
    /// thing it means everywhere else. Deliberately a plain string rather than an enum: a new
    /// simulator should cost a connector, and adding one must not require a migration here.
    /// </remarks>
    public required string SimKey { get; set; }

    /// <summary>
    /// The simulator's own stable identifier for this driver, exactly as that simulator reports it.
    /// </summary>
    /// <remarks>
    /// A string rather than a number because simulators disagree about the shape — RaceRoom's is
    /// numeric, others are not — and because it is an identifier rather than a quantity. Never
    /// parsed, never compared numerically.
    /// </remarks>
    public required string SimDriverId { get; set; }

    /// <summary>UTC time this alias was asserted.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
