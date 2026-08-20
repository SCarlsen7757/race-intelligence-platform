using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Live.Contracts.Publish;

namespace RaceIntelligence.Live.Contracts.Mapping;

/// <summary>
/// Converts canonical standings and telemetry into the live publishing wire format, so the
/// collector (writing it) and the hub (reading it) share exactly one conversion.
/// </summary>
/// <remarks>
/// Straight field-for-field copies with no defaulting or coercion, matching
/// <c>TelemetrySampleContractMapper</c>: a <see langword="null"/> must stay
/// <see langword="null"/> in both directions. The reason is weaker here than on the archive path —
/// a live frame is discarded within milliseconds rather than stored forever — but a timing tower
/// that renders "unavailable" as a confident <c>0.0s</c> gap is actively misleading to the person
/// making a pit call from it.
/// </remarks>
public static class LiveStandingsContractMapper
{
    /// <summary>Converts one canonical timing-tower row into its wire form.</summary>
    public static LiveDriverDto ToDto(DriverStanding standing) => new()
    {
        SimDriverId = standing.SimDriverId,
        SlotId = standing.SlotId,
        DisplayName = standing.DisplayName,
        CarNumber = standing.CarNumber,
        SimCarId = standing.SimCarId,
        SimCarClassId = standing.SimCarClassId,
        SimManufacturerId = standing.SimManufacturerId,
        Position = standing.Position,
        PositionInClass = standing.PositionInClass,
        CompletedLaps = standing.CompletedLaps,
        TrackPositionFraction = standing.TrackPositionFraction,
        Sector = standing.Sector,
        Speed = standing.Speed,
        CurrentLapTime = standing.CurrentLapTime,
        PreviousLapTime = standing.PreviousLapTime,
        BestLapTime = standing.BestLapTime,
        CurrentLapValid = standing.CurrentLapValid,
        CurrentSectorTimes = standing.CurrentSectorTimes,
        PreviousSectorTimes = standing.PreviousSectorTimes,
        BestSectorTimes = standing.BestSectorTimes,
        GapToCarAhead = standing.GapToCarAhead,
        GapToCarBehind = standing.GapToCarBehind,
        InPitLane = standing.InPitLane,
        PitLaneState = (int)standing.PitLaneState,
        PitStopStatus = (int)standing.PitStopStatus,
        PitStopCount = standing.PitStopCount,
        FinishStatus = (int)standing.FinishStatus,
        PenaltyCount = standing.PenaltyCount,
    };

    /// <summary>Converts a wire timing-tower row back into its canonical form.</summary>
    /// <remarks>
    /// The two status enums are carried as plain <see cref="int"/> on the wire and validated here
    /// rather than cast blindly: the value comes from another machine's build of the client, which
    /// may know a status this one does not. An unrecognised code becomes <c>Unavailable</c>, which
    /// the dashboard already renders as "no information", instead of an out-of-range enum value
    /// that every downstream <c>switch</c> would fall through.
    /// </remarks>
    public static DriverStanding ToCore(LiveDriverDto dto) => new()
    {
        SimDriverId = dto.SimDriverId,
        SlotId = dto.SlotId,
        DisplayName = dto.DisplayName,
        CarNumber = dto.CarNumber,
        SimCarId = dto.SimCarId,
        SimCarClassId = dto.SimCarClassId,
        SimManufacturerId = dto.SimManufacturerId,
        Position = dto.Position,
        PositionInClass = dto.PositionInClass,
        CompletedLaps = dto.CompletedLaps,
        TrackPositionFraction = dto.TrackPositionFraction,
        Sector = dto.Sector,
        Speed = dto.Speed,
        CurrentLapTime = dto.CurrentLapTime,
        PreviousLapTime = dto.PreviousLapTime,
        BestLapTime = dto.BestLapTime,
        CurrentLapValid = dto.CurrentLapValid,
        // A message that omitted the key leaves these null; the Core type promises a list. See the
        // remarks on LiveDriverDto.CurrentSectorTimes for why the wire is allowed to say nothing.
        CurrentSectorTimes = dto.CurrentSectorTimes ?? [],
        PreviousSectorTimes = dto.PreviousSectorTimes ?? [],
        BestSectorTimes = dto.BestSectorTimes ?? [],
        GapToCarAhead = dto.GapToCarAhead,
        GapToCarBehind = dto.GapToCarBehind,
        InPitLane = dto.InPitLane,
        PitLaneState = Enum.IsDefined((PitLaneState)dto.PitLaneState)
            ? (PitLaneState)dto.PitLaneState
            : Core.Sessions.PitLaneState.Unavailable,
        PitStopStatus = Enum.IsDefined((PitStopStatus)dto.PitStopStatus)
            ? (PitStopStatus)dto.PitStopStatus
            : Core.Sessions.PitStopStatus.Unavailable,
        PitStopCount = dto.PitStopCount,
        FinishStatus = Enum.IsDefined((DriverFinishStatus)dto.FinishStatus)
            ? (DriverFinishStatus)dto.FinishStatus
            : DriverFinishStatus.Unavailable,
        PenaltyCount = dto.PenaltyCount,
    };

    /// <summary>Converts a canonical standings snapshot into the frame a collector publishes.</summary>
    public static LiveStandingsFrame ToFrame(SessionStandings standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        var drivers = new LiveDriverDto[standings.Drivers.Count];
        for (int i = 0; i < drivers.Length; i++)
        {
            drivers[i] = ToDto(standings.Drivers[i]);
        }

        return new LiveStandingsFrame(
            standings.SessionId,
            standings.CapturedAtUtc,
            standings.SimulationTime,
            standings.LocalSimDriverId,
            drivers,
            standings.PitWindow is { } window ? ToDto(window) : null,
            standings.RaceLength is { } length ? ToDto(length) : null);
    }

    /// <summary>Converts a canonical race length into its wire form.</summary>
    public static LiveRaceLengthDto ToDto(RaceLength length)
    {
        ArgumentNullException.ThrowIfNull(length);

        return new LiveRaceLengthDto(length.Laps, length.DurationSeconds, (int)length.Unit);
    }

    /// <summary>Converts a wire race length back into its canonical form.</summary>
    /// <remarks>
    /// The unit is validated rather than cast, for the reason <see cref="ToCore(LivePitWindowDto)"/>
    /// gives. An unrecognised unit becomes <see cref="RaceLengthUnit.Unknown"/>, which
    /// <see cref="RaceLength.Exists"/> already reports as no usable length — so a build that has not
    /// heard of some future session format declines to compute rather than dividing fuel by a figure
    /// it cannot label.
    /// </remarks>
    public static RaceLength ToCore(LiveRaceLengthDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new RaceLength
        {
            Laps = dto.Laps,
            DurationSeconds = dto.DurationSeconds,
            Unit = Enum.IsDefined((RaceLengthUnit)dto.Unit)
                ? (RaceLengthUnit)dto.Unit
                : RaceLengthUnit.Unknown,
        };
    }

    /// <summary>Converts a canonical pit window into its wire form.</summary>
    public static LivePitWindowDto ToDto(PitWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new LivePitWindowDto((int)window.Status, window.Start, window.End, (int)window.Unit);
    }

    /// <summary>Converts a wire pit window back into its canonical form.</summary>
    /// <remarks>
    /// Both codes are validated rather than cast, exactly as <see cref="ToCore(LiveDriverDto)"/>
    /// validates the per-car pit enums. An unrecognised status becomes
    /// <see cref="PitWindowStatus.Unavailable"/>, which every consumer already renders as "nothing
    /// known" — the one safe reading of a code this build has never heard of.
    /// </remarks>
    public static PitWindow ToCore(LivePitWindowDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new PitWindow
        {
            Status = Enum.IsDefined((PitWindowStatus)dto.Status)
                ? (PitWindowStatus)dto.Status
                : PitWindowStatus.Unavailable,
            Start = dto.Start,
            End = dto.End,
            Unit = Enum.IsDefined((PitWindowUnit)dto.Unit) ? (PitWindowUnit)dto.Unit : PitWindowUnit.Unknown,
        };
    }

    /// <summary>Converts a published standings frame back into its canonical form.</summary>
    public static SessionStandings ToCore(LiveStandingsFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var drivers = new DriverStanding[frame.Drivers.Count];
        for (int i = 0; i < drivers.Length; i++)
        {
            drivers[i] = ToCore(frame.Drivers[i]);
        }

        return new SessionStandings
        {
            SessionId = frame.SessionId,
            CapturedAtUtc = frame.CapturedAtUtc,
            SimulationTime = frame.SimulationTime,
            LocalSimDriverId = frame.LocalSimDriverId,
            Drivers = drivers,
            PitWindow = frame.PitWindow is { } window ? ToCore(window) : null,
            RaceLength = frame.RaceLength is { } length ? ToCore(length) : null,
        };
    }

    /// <summary>
    /// Builds the rich local-car frame from a canonical telemetry sample.
    /// </summary>
    /// <param name="sample">The sample to publish.</param>
    /// <param name="simDriverId">
    /// The local driver's simulator identity, carried alongside because a
    /// <see cref="TelemetrySample"/> does not know whose car it describes — the session does.
    /// </param>
    /// <remarks>
    /// A deliberate subset of the sample, not all of it. Wheel speed, suspension travel and the
    /// per-wheel inner/middle/outer temperature breakdown are dropped: none of them is readable on
    /// a live dashboard, and carrying them would roughly double a frame sent at the full poll rate.
    /// The archive path still stores every one of them.
    /// </remarks>
    public static LiveSelfFrame ToSelfFrame(TelemetrySample sample, string? simDriverId)
    {
        ArgumentNullException.ThrowIfNull(sample);

        return new LiveSelfFrame(
            sample.SessionId,
            simDriverId,
            sample.SequenceNumber,
            sample.Timestamp,
            sample.SimulationTime,
            sample.LapNumber,
            sample.Sector,
            sample.TrackPositionFraction,
            sample.Speed,
            sample.Throttle,
            sample.Brake,
            sample.Steering,
            sample.Gear,
            sample.EngineRpm,
            sample.FuelLeft,
            new LiveWheelValues(
                sample.BrakePressure.FrontLeft,
                sample.BrakePressure.FrontRight,
                sample.BrakePressure.RearLeft,
                sample.BrakePressure.RearRight),
            sample.Clutch,
            sample.AbsSetting,
            sample.AbsActive,
            sample.TractionControlSetting,
            sample.TractionControlActive,
            sample.BrakeBias);
    }

    /// <summary>
    /// Builds the stint frame — the tyre channels, at their own slower rate.
    /// </summary>
    /// <remarks>
    /// Takes the same <see cref="TelemetrySample"/> the self frame does, and the publisher decides
    /// how often to call it. Nothing about the sample says which of its channels are fast and which
    /// are slow; that judgement lives in the two mapping functions and in the interval the outbox
    /// applies, which is the only place it can be changed once rather than in every consumer.
    /// </remarks>
    public static LiveStintFrame ToStintFrame(TelemetrySample sample, string? simDriverId)
    {
        ArgumentNullException.ThrowIfNull(sample);

        return new LiveStintFrame(
            sample.SessionId,
            simDriverId,
            sample.Timestamp,
            new LiveWheelValues(
                sample.TyrePressure.FrontLeft,
                sample.TyrePressure.FrontRight,
                sample.TyrePressure.RearLeft,
                sample.TyrePressure.RearRight),
            new LiveWheelValues(
                sample.TyreWear.FrontLeft,
                sample.TyreWear.FrontRight,
                sample.TyreWear.RearLeft,
                sample.TyreWear.RearRight),
            new LiveTyreTemperatures(
                ToTreadTemperatures(sample.TyreTemperature.FrontLeft),
                ToTreadTemperatures(sample.TyreTemperature.FrontRight),
                ToTreadTemperatures(sample.TyreTemperature.RearLeft),
                ToTreadTemperatures(sample.TyreTemperature.RearRight)));
    }

    /// <summary>
    /// Carries one tyre's readings onto the wire whole.
    /// </summary>
    /// <remarks>
    /// This used to select <c>Middle</c> and drop the other five, on the reasoning that
    /// inner/middle/outer is a setup signal read across a stint rather than something watched in
    /// real time. The reasoning was sound about the tread and wrong about the consequence: the
    /// archive kept all six, so the analysis the argument pointed at was possible — but only after
    /// the session, and only for the person holding the database. A race engineer watching a stint
    /// could not see the shoulder running away, and could not see the window the simulator was
    /// perfectly willing to name. Five floats per tyre is a cheap price for both.
    /// </remarks>
    private static LiveTreadTemperatures ToTreadTemperatures(TyreTemperature temperature) => new(
        temperature.Inner,
        temperature.Middle,
        temperature.Outer,
        temperature.Optimal,
        temperature.Cold,
        temperature.Hot);
}
