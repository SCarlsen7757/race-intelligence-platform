namespace RaceIntelligence.Persistence.RaceRoom.Entities;

/// <summary>
/// One row of <c>operating_windows</c>: the tyre and brake temperature band one corner ran in, on
/// one compound.
/// </summary>
/// <remarks>
/// <para>
/// <b>These used to be twenty-four columns on every telemetry row, and they never changed.</b>
/// Across 122,562 samples of one recorded session the six tyre and brake bounds had exactly one
/// distinct value each, against 119,146 for the readings they bound. At 58 Hz that is a constant
/// written several million times a session.
/// </para>
/// <para>
/// <b>The key is <c>(session_id, corner, compound)</c>, and the compound is load-bearing.</b> The
/// one thing that moves a window is fitting a different tyre. Keyed by session alone, a stop that
/// switched compound would leave every earlier stint described by the later tyre's band — wrong
/// exactly where a degradation question is asked. A null compound is its own key: it means the
/// simulator reported none, which is a different fact from any particular compound.
/// </para>
/// <para>
/// Written by the ingest endpoint with <c>ON CONFLICT DO NOTHING</c>, from rows the collector puts
/// on every telemetry batch. So "has the window changed" is answered by the key rather than tracked
/// by the connector, and a re-sent batch is as harmless here as it is for samples.
/// </para>
/// </remarks>
public sealed class OperatingWindowRow
{
    /// <summary>
    /// A surrogate key, because the natural one cannot be a primary key: <c>compound</c> is
    /// nullable and PostgreSQL treats every null in a key as distinct from every other. The natural
    /// key is a unique index with <c>NULLS NOT DISTINCT</c> instead — see the configuration.
    /// </summary>
    public long Id { get; init; }

    public Guid SessionId { get; init; }

    /// <summary>FL, FR, RL, RR as 0-3 — the platform's corner order, stored as the ordinal.</summary>
    public short Corner { get; init; }

    /// <summary>
    /// RaceRoom's <c>tire_subtype</c> for this corner's axle, raw and untranslated: <c>Primary</c>,
    /// <c>Alternate</c>, <c>Soft</c>, <c>Medium</c>, <c>Hard</c>. Null when none was reported.
    /// </summary>
    public int? Compound { get; init; }

    public float? TyreOptimalCelsius { get; init; }

    public float? TyreColdCelsius { get; init; }

    public float? TyreHotCelsius { get; init; }

    public float? BrakeOptimalCelsius { get; init; }

    public float? BrakeColdCelsius { get; init; }

    public float? BrakeHotCelsius { get; init; }
}
