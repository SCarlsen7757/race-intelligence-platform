namespace RaceIntelligence.Collector.Abstractions;

/// <summary>
/// Consumes the simulator-specific channels that have no place in the canonical model, as the raw
/// JSON the connector wrote them to.
/// </summary>
/// <remarks>
/// <para>
/// This is the "flexible metadata" half of the platform's data model: a simulator exposing a field
/// nobody anticipated should cost a connector, not a migration. Car damage, push-to-pass, tyre
/// subtypes and the rest arrive here shaped the way the sim shapes them, and a consumer that wants
/// one is expected to know which simulator it is reading.
/// </para>
/// <para>
/// <b>Deliberately its own channel, at its own rate.</b> These values change slowly and matter
/// slowly — damage a second late is still actionable — while the payload is a JSON document rather
/// than a handful of floats. Riding it on the sample channel would mean serialising and parsing it
/// sixty times a second to watch a number that moves once a lap, spending on the low-value channel
/// exactly the budget the high-rate one needs.
/// </para>
/// <para>
/// <b>Sentinels are not translated.</b> Extras are written raw, so a simulator's "not available"
/// encoding arrives intact — RaceRoom uses <c>-1</c>, which is emphatically not zero. A consumer
/// that renders <c>-1</c> damage as "undamaged" reports the opposite of the truth.
/// </para>
/// <para>Runs on the collect loop and must return immediately.</para>
/// </remarks>
public interface IExtrasObserver
{
    /// <summary>
    /// Simulator-specific channels for the local car.
    /// </summary>
    /// <param name="sessionId">The session the values were captured in.</param>
    /// <param name="extrasJson">The connector's raw JSON document. Never null; may be empty.</param>
    void OnExtras(Guid sessionId, string extrasJson);
}
