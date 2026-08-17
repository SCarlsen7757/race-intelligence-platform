using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Drives the real state machine and asserts when <see cref="ExtrasUpdated"/> is and is not emitted.
/// </summary>
/// <remarks>
/// The rate is the whole point of the channel. The document is written for every sample anyway — the
/// archive path stores it per row — so what this gate buys is not cheaper production but a consumer
/// that parses JSON once a second instead of sixty times.
/// </remarks>
public class RaceRoomTelemetrySourceExtrasTests
{
    private static RaceRoomConnectorOptions Options(TimeSpan extrasInterval) => new()
    {
        PollInterval = TimeSpan.FromMilliseconds(2),
        ReconnectDelay = TimeSpan.FromMilliseconds(5),
        StaleFrameTimeout = TimeSpan.FromMinutes(5),
        MaxSuspendDuration = TimeSpan.FromMinutes(5),
        SessionStartDebounce = TimeSpan.Zero,
        StandingsInterval = Timeout.InfiniteTimeSpan,
        ExtrasInterval = extrasInterval,
    };

    private static byte[] OnTrackFrame(int ticks) =>
        new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .WithTicks(ticks)
            .Configure((ref R3ESharedRaw r) => r.VehicleInfo.UserId = 4242)
            .BuildBytes();

    /// <summary>Collects events until <paramref name="sampleCount"/> samples have gone by.</summary>
    private static async Task<List<TelemetryEvent>> CollectAsync(
        RaceRoomConnectorOptions options,
        int sampleCount)
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().BuildBytes());
        await using var source = new RaceRoomTelemetrySource(options, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        var collected = new List<TelemetryEvent>();
        int seen = 0;
        int ticks = 100;
        bool onTrack = false;

        while (await enumerator.MoveNextAsync())
        {
            collected.Add(enumerator.Current);

            if (!onTrack && enumerator.Current is ConnectionStateChanged)
            {
                onTrack = true;
                view.SetFrame(OnTrackFrame(ticks));
                continue;
            }

            if (enumerator.Current is TelemetrySampleReceived && ++seen >= sampleCount)
            {
                return collected;
            }

            // The tick counter has to keep moving or the stale-frame check declares the game gone.
            view.SetFrame(OnTrackFrame(++ticks));
        }

        throw new ShouldAssertException($"the event stream ended before {sampleCount} samples were produced.");
    }

    [Fact]
    public async Task Extras_carry_the_same_document_the_sample_does()
    {
        var collected = await CollectAsync(Options(TimeSpan.Zero), sampleCount: 3);

        var extras = collected.OfType<ExtrasUpdated>().First();
        var sample = collected.OfType<TelemetrySampleReceived>().First().Sample;

        extras.SessionId.ShouldBe(sample.SessionId);
        extras.ExtrasJson.ShouldBe(sample.Extras);

        // RaceRoom's -1 arrives intact. Translating it here would be the platform's worst kind of
        // mistake: a damage panel reading "undamaged" when the truth is "not reported".
        extras.ExtrasJson.ShouldContain("damage");
    }

    /// <summary>
    /// A 1 Hz interval against a 2 ms poll. The first sample of a session is always due — a
    /// dashboard opening mid-session must not wait out an interval for its first reading — and
    /// nothing after it is, within the fraction of a second these samples span.
    /// </summary>
    [Fact]
    public async Task Extras_are_published_far_less_often_than_samples()
    {
        var collected = await CollectAsync(Options(TimeSpan.FromSeconds(1)), sampleCount: 30);

        collected.OfType<TelemetrySampleReceived>().Count().ShouldBeGreaterThanOrEqualTo(30);
        collected.OfType<ExtrasUpdated>().Count().ShouldBe(1);
    }

    /// <summary>
    /// What a collector running no extras consumer is configured with. Any negative interval
    /// disables the channel, not only <see cref="Timeout.InfiniteTimeSpan"/> — the same reasoning as
    /// standings, where testing for that exact value would let <c>TimeSpan.FromSeconds(-1)</c> fall
    /// through to a comparison that is never true and publish on every single poll.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(-10_000_000L)]
    public async Task A_negative_extras_interval_disables_the_channel(long ticks)
    {
        var collected = await CollectAsync(Options(TimeSpan.FromTicks(ticks)), sampleCount: 30);

        collected.OfType<ExtrasUpdated>().ShouldBeEmpty();
    }
}
