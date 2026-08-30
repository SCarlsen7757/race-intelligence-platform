using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>
/// Consumes the channels that change slowly — damage, push-to-pass, tyre compounds, flags, the pit
/// window — at their own rate rather than at the collect loop's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately its own channel, at its own rate.</b> These values change slowly and matter
/// slowly — damage a second late is still actionable — while there are a great many of them.
/// Sending them sixty times a second would spend on the low-value channels exactly the budget the
/// high-rate ones need. So the same <see cref="RaceRoomTelemetrySample"/> the archive stores every
/// frame is handed here about once a second, and a consumer reads the slow fields off it.
/// </para>
/// <para>
/// <b>This used to be a raw JSON document</b> (<c>IExtrasObserver</c>), and the note that came with
/// it — "sentinels are not translated, <c>-1</c> is emphatically not zero" — is now unnecessary
/// rather than merely unmentioned. Every channel is typed and nullable, the connector has already
/// turned RaceRoom's <c>-1</c> into <see langword="null"/>, and a consumer can no longer render
/// "not available" as zero by accident because there is no <c>-1</c> left to render.
/// </para>
/// <para>Runs on the collect loop and must return immediately.</para>
/// </remarks>
public interface ISlowChannelObserver
{
    /// <summary>How often this observer wants a sample. See <see cref="IStandingsObserver.StandingsInterval"/>.</summary>
    TimeSpan SlowChannelInterval { get; }

    /// <summary>
    /// A sample carrying the local car's slow-moving channels, at this observer's interval.
    /// </summary>
    /// <param name="sample">
    /// A full sample — the same shape the archive stores. Only the slow channels are of interest
    /// here, but sending a subset would mean a second type that has to be kept in step with this
    /// one, which is the whole problem this design removed.
    /// </param>
    /// <param name="operatingWindows">
    /// The tyre and brake temperature bands in force, one per corner. Constant for a compound, and
    /// therefore not on the sample — a widget drawing a tyre against its band needs both.
    /// </param>
    void OnSlowChannels(RaceRoomTelemetrySample sample, IReadOnlyList<OperatingWindow> operatingWindows);
}
