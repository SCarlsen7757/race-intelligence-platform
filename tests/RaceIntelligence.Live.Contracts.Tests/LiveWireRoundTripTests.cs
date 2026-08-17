using MessagePack;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts.Mapping;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Live.Contracts.Tests;

/// <summary>
/// Proves the publishing wire format survives the trip from a collector to the hub. The collector
/// and the hub are separate processes on separate machines, so nothing else in the system would
/// catch a field that encodes but does not decode — it would simply arrive as a default value and
/// be rendered as a plausible, wrong number.
/// </summary>
public sealed class LiveWireRoundTripTests
{
    private static T RoundTrip<T>(T message)
        where T : LivePublisherMessage
    {
        byte[] bytes = MessagePackSerializer.Serialize<LivePublisherMessage>(message, LiveMessagePackOptions.Default);
        return MessagePackSerializer
            .Deserialize<LivePublisherMessage>(bytes, LiveMessagePackOptions.Default)
            .ShouldBeOfType<T>();
    }

    [Fact]
    public void Hello_survives_the_round_trip()
    {
        var hello = new LiveHello(
            LiveSchemaVersion.Current,
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ClientName: "Mark's rig",
            ClientVersion: "0.1.0",
            GameKey: "raceroom",
            Capabilities: 0b1010_1010_1010_1010UL);

        RoundTrip(hello).ShouldBe(hello);
    }

    [Fact]
    public void SessionFrame_survives_the_round_trip()
    {
        var frame = new LiveSessionFrame(
            SessionId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            GameKey: "raceroom",
            TrackName: "Spa-Francorchamps",
            LayoutName: "Grand Prix",
            LayoutLengthMeters: 7004f,
            SessionType: 3,
            SessionIteration: 2,
            StartedAtUtc: new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            PlayerName: "Mark",
            LocalSimDriverId: "4242",
            RosterFingerprint: "0123456789abcdef0123456789abcdef",
            RosterSize: 20,
            LocalSlotId: 7);

        RoundTrip(frame).ShouldBe(frame);
    }

    [Fact]
    public void Goodbye_survives_the_round_trip()
    {
        var goodbye = new LiveGoodbye(Guid.NewGuid(), "session ended");

        RoundTrip(goodbye).ShouldBe(goodbye);
    }

    /// <summary>
    /// The extras document crosses as an opaque string, so it must arrive byte for byte — including
    /// the <c>-1</c> a simulator writes for a channel it does not report. Anything that "helpfully"
    /// normalised that in transit would turn "not reported" into "undamaged".
    /// </summary>
    [Fact]
    public void ExtrasFrame_survives_the_round_trip_with_its_sentinels_intact()
    {
        var frame = new LiveExtrasFrame(
            SessionId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            SimDriverId: "4242",
            CapturedAtUtc: new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            ExtrasJson: """{"damage":{"engine":0.5,"transmission":-1.0,"aerodynamics":1.0,"suspension":-1.0}}""");

        RoundTrip(frame).ShouldBe(frame);
    }

    /// <summary>
    /// The union is what makes a single socket carry five message shapes, and its keys are
    /// permanent — reusing one would decode an old client's frames as the wrong type instead of
    /// failing. This pins that each type still arrives as itself.
    /// </summary>
    [Fact]
    public void Every_publisher_message_decodes_back_to_its_own_type()
    {
        LivePublisherMessage[] messages =
        [
            new LiveHello(LiveSchemaVersion.Current, Guid.NewGuid(), "rig", "0.1.0", "raceroom", 0),
            new LiveSessionFrame(Guid.NewGuid(), "raceroom", "Spa", "GP", null, 3, 1, DateTimeOffset.UnixEpoch, null, null, "", 0, null),
            LiveStandingsContractMapper.ToFrame(LiveDtoFactory.FullyPopulatedStandings()),
            LiveStandingsContractMapper.ToSelfFrame(LiveDtoFactory.FullyPopulatedSample(), "4242"),
            new LiveGoodbye(Guid.NewGuid(), null),
            new LiveExtrasFrame(Guid.NewGuid(), "4242", DateTimeOffset.UnixEpoch, "{}"),
        ];

        var cancellationToken = TestContext.Current.CancellationToken;

        foreach (var message in messages)
        {
            byte[] bytes = MessagePackSerializer.Serialize(message, LiveMessagePackOptions.Default, cancellationToken);
            var decoded = MessagePackSerializer.Deserialize<LivePublisherMessage>(bytes, LiveMessagePackOptions.Default, cancellationToken);

            decoded.GetType().ShouldBe(message.GetType());
        }
    }

    [Fact]
    public void A_fully_populated_standings_snapshot_survives_the_round_trip_through_Core()
    {
        var original = LiveDtoFactory.FullyPopulatedStandings();

        var frame = RoundTrip(LiveStandingsContractMapper.ToFrame(original));
        var restored = LiveStandingsContractMapper.ToCore(frame);

        restored.SessionId.ShouldBe(original.SessionId);
        restored.CapturedAtUtc.ShouldBe(original.CapturedAtUtc);
        restored.SimulationTime.ShouldBe(original.SimulationTime);
        restored.LocalSimDriverId.ShouldBe(original.LocalSimDriverId);
        restored.Drivers.Count.ShouldBe(original.Drivers.Count);

        for (int i = 0; i < restored.Drivers.Count; i++)
        {
            // Records compare structurally, but IReadOnlyList members compare by reference, so the
            // sector arrays are asserted separately below rather than relying on record equality.
            var expected = original.Drivers[i];
            var actual = restored.Drivers[i];

            actual.ShouldBe(expected with
            {
                CurrentSectorTimes = actual.CurrentSectorTimes,
                PreviousSectorTimes = actual.PreviousSectorTimes,
                BestSectorTimes = actual.BestSectorTimes,
            });

            actual.CurrentSectorTimes.ShouldBe(expected.CurrentSectorTimes);
            actual.PreviousSectorTimes.ShouldBe(expected.PreviousSectorTimes);
            actual.BestSectorTimes.ShouldBe(expected.BestSectorTimes);
        }
    }

    /// <summary>
    /// The half a populated round trip cannot prove: that an absent reading stays absent. A timing
    /// tower that renders "no gap reported" as a confident 0.0s is worse than one that renders
    /// nothing, because a race engineer will act on it.
    /// </summary>
    [Fact]
    public void Absent_readings_stay_absent_rather_than_becoming_zero()
    {
        var empty = LiveDtoFactory.EmptyStanding();

        var restored = LiveStandingsContractMapper.ToCore(
            RoundTrip(new LiveStandingsFrame(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                SimulationTime: null,
                LocalSimDriverId: null,
                [LiveStandingsContractMapper.ToDto(empty)])));

        var driver = restored.Drivers.ShouldHaveSingleItem();

        driver.SimDriverId.ShouldBeNull();
        driver.SlotId.ShouldBeNull();
        driver.Position.ShouldBeNull();
        driver.Speed.ShouldBeNull();
        driver.BestLapTime.ShouldBeNull();
        driver.GapToCarAhead.ShouldBeNull();
        driver.GapToCarBehind.ShouldBeNull();
        driver.InPitLane.ShouldBeNull();
        driver.CurrentLapValid.ShouldBeNull();
        driver.PitStopStatus.ShouldBe(PitStopStatus.Unavailable);
        driver.FinishStatus.ShouldBe(DriverFinishStatus.Unavailable);
        restored.SimulationTime.ShouldBeNull();
    }

    /// <summary>
    /// The status enums cross the wire as plain ints, and the sending client may be a build that
    /// knows a code this one does not. An unrecognised value must land on Unavailable rather than
    /// becoming an out-of-range enum every downstream switch falls through.
    /// </summary>
    [Fact]
    public void An_unrecognised_status_code_decodes_as_unavailable()
    {
        var dto = LiveStandingsContractMapper.ToDto(LiveDtoFactory.FullyPopulatedStanding()) with
        {
            PitStopStatus = 99,
            FinishStatus = 98,
        };

        var restored = LiveStandingsContractMapper.ToCore(dto);

        restored.PitStopStatus.ShouldBe(PitStopStatus.Unavailable);
        restored.FinishStatus.ShouldBe(DriverFinishStatus.Unavailable);
    }

    [Fact]
    public void The_self_frame_carries_every_channel_a_focus_panel_renders()
    {
        var sample = LiveDtoFactory.FullyPopulatedSample();

        var frame = RoundTrip(LiveStandingsContractMapper.ToSelfFrame(sample, "4242"));

        frame.SessionId.ShouldBe(sample.SessionId);
        frame.SimDriverId.ShouldBe("4242");
        frame.SequenceNumber.ShouldBe(sample.SequenceNumber);
        frame.CapturedAtUtc.ShouldBe(sample.Timestamp);
        frame.SimulationTime.ShouldBe(sample.SimulationTime);
        frame.LapNumber.ShouldBe(sample.LapNumber);
        frame.Sector.ShouldBe(sample.Sector);
        frame.TrackPositionFraction.ShouldBe(sample.TrackPositionFraction);
        frame.Speed.ShouldBe(sample.Speed);
        frame.Throttle.ShouldBe(sample.Throttle);
        frame.Brake.ShouldBe(sample.Brake);
        frame.Steering.ShouldBe(sample.Steering);
        frame.Gear.ShouldBe(sample.Gear);
        frame.EngineRpm.ShouldBe(sample.EngineRpm);
        frame.FuelLeft.ShouldBe(sample.FuelLeft);

        frame.TyrePressure.ShouldBe(new LiveWheelValues(180f, 181f, 182f, 183f));

        // The unreported rear-right wheel stays null rather than reading as a brand-new tyre.
        frame.TyreWear.ShouldBe(new LiveWheelValues(0.1f, 0.2f, 0.3f, null));

        // Tyre temperature is reduced to the middle-of-tread reading for the live path.
        frame.TyreTemperature.ShouldBe(new LiveWheelValues(81f, 84f, 87f, 90f));
    }
}
