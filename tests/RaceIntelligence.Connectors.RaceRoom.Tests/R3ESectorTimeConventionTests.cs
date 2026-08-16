using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers the runtime determination of how RaceRoom fills its <c>sector_time_*</c> triples. r3e.h
/// documents the unit and the N/A sentinel and nothing else, and the two possible readings differ
/// by roughly a minute on a lap time while both looking entirely reasonable — so the connector
/// settles it from the block rather than assuming.
/// </summary>
public sealed class R3ESectorTimeConventionTests
{
    /// <summary>A 92.7 s lap with splits at 30.1 / 60.4 / 92.7 — cumulative from the lap start.</summary>
    private static R3ESharedRaw CumulativeFrame() =>
        Frame(lapTime: 92.7f, 30.1f, 60.4f, 92.7f);

    /// <summary>The same lap expressed as per-sector durations: 30.1 + 30.3 + 32.3 = 92.7.</summary>
    private static R3ESharedRaw PerSectorFrame() =>
        Frame(lapTime: 92.7f, 30.1f, 30.3f, 32.3f);

    private static R3ESharedRaw Frame(float lapTime, float first, float second, float third) =>
        new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .Configure((ref R3ESharedRaw raw) =>
            {
                raw.LapTimePreviousSelf = lapTime;
                raw.SectorTimesPreviousSelf[0] = first;
                raw.SectorTimesPreviousSelf[1] = second;
                raw.SectorTimesPreviousSelf[2] = third;
            })
            .Build();

    [Fact]
    public void A_lap_whose_final_split_is_the_lap_time_proves_cumulative()
    {
        var raw = CumulativeFrame();

        R3ESectorTimeConventionDetector.Detect(in raw, R3ESectorTimeConvention.PerSector)
            .ShouldBe(R3ESectorTimeConvention.Cumulative);
    }

    [Fact]
    public void A_lap_whose_splits_sum_to_the_lap_time_proves_per_sector()
    {
        var raw = PerSectorFrame();

        R3ESectorTimeConventionDetector.Detect(in raw, R3ESectorTimeConvention.Cumulative)
            .ShouldBe(R3ESectorTimeConvention.PerSector);
    }

    /// <summary>
    /// Before the first completed lap there is nothing to measure against. The detector must hold
    /// its previous answer rather than reporting a default, or every session would spend its
    /// opening laps flip-flopping.
    /// </summary>
    [Theory]
    [InlineData(-1f, 30.1f, 60.4f, 92.7f)] // no previous lap time yet
    [InlineData(92.7f, -1f, 60.4f, 92.7f)] // sector 1 unreported
    [InlineData(92.7f, 30.1f, -1f, 92.7f)] // sector 2 unreported
    [InlineData(92.7f, 30.1f, 60.4f, -1f)] // sector 3 unreported
    [InlineData(0f, 0f, 0f, 0f)] // nothing reported at all
    public void An_incomplete_lap_leaves_the_current_answer_alone(float lapTime, float first, float second, float third)
    {
        var raw = Frame(lapTime, first, second, third);

        R3ESectorTimeConventionDetector.Detect(in raw, R3ESectorTimeConvention.PerSector)
            .ShouldBe(R3ESectorTimeConvention.PerSector);
        R3ESectorTimeConventionDetector.Detect(in raw, R3ESectorTimeConvention.Cumulative)
            .ShouldBe(R3ESectorTimeConvention.Cumulative);
    }

    /// <summary>
    /// A frame caught mid-update can hold splits from one lap and a total from another. Neither
    /// hypothesis fits, and changing the answer on one odd sample would be worse than keeping it.
    /// </summary>
    [Fact]
    public void A_frame_matching_neither_reading_leaves_the_current_answer_alone()
    {
        var raw = Frame(lapTime: 92.7f, 10f, 20f, 30f);

        R3ESectorTimeConventionDetector.Detect(in raw, R3ESectorTimeConvention.Cumulative)
            .ShouldBe(R3ESectorTimeConvention.Cumulative);
    }

    /// <summary>
    /// The game rounds sector and lap times independently, so the two never agree to the last bit.
    /// The tolerance has to absorb that while staying far inside the ~60 s gap between the two
    /// hypotheses.
    /// </summary>
    [Fact]
    public void Rounding_between_the_lap_time_and_its_splits_does_not_defeat_detection()
    {
        var raw = Frame(lapTime: 92.700f, 30.1f, 60.4f, 92.712f);

        R3ESectorTimeConventionDetector.Detect(in raw, R3ESectorTimeConvention.PerSector)
            .ShouldBe(R3ESectorTimeConvention.Cumulative);
    }

    [Fact]
    public void Per_sector_splits_are_normalised_to_cumulative_for_the_canonical_model()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithPreviousLap(sector1: 30.1f, sector2: 30.3f, sector3: 32.3f)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.PerSector);

        // DriverStanding documents its splits as cumulative, so the mapper converts rather than
        // passing a second convention downstream.
        standing.PreviousSectorTimes[0]!.Value.TotalSeconds.ShouldBe(30.1, tolerance: 1e-4);
        standing.PreviousSectorTimes[1]!.Value.TotalSeconds.ShouldBe(60.4, tolerance: 1e-4);
        standing.PreviousSectorTimes[2]!.Value.TotalSeconds.ShouldBe(92.7, tolerance: 1e-4);

        // And the lap total comes out right under either reading, which is the whole point.
        standing.PreviousLapTime!.Value.TotalSeconds.ShouldBe(92.7, tolerance: 1e-4);
    }

    /// <summary>
    /// A running sum cannot skip a hole: with sector 1 unreported, no later cumulative split can be
    /// reconstructed, and inventing one would understate the lap by a full sector.
    /// </summary>
    [Fact]
    public void A_gap_in_per_sector_splits_stops_the_running_total_rather_than_skipping_it()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithPreviousLap(sector1: -1f, sector2: 30.3f, sector3: 32.3f)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.PerSector);

        standing.PreviousSectorTimes.ShouldAllBe(t => t == null);
        standing.PreviousLapTime.ShouldBeNull();
    }

    [Fact]
    public void Cumulative_splits_pass_through_untouched()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithPreviousLap(sector1: 30.1f, sector2: 60.4f, sector3: 92.7f)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative);

        standing.PreviousSectorTimes[1]!.Value.TotalSeconds.ShouldBe(60.4, tolerance: 1e-4);
        standing.PreviousLapTime!.Value.TotalSeconds.ShouldBe(92.7, tolerance: 1e-4);
    }
}
