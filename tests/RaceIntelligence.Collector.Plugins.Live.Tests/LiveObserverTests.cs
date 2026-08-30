using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RaceIntelligence.Collector.Plugins.Live;
using RaceIntelligence.Collector.Plugins.Live.Tests.Support;
using RaceIntelligence.Collector.TestSupport;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Live.Tests;

/// <summary>
/// The observer's own job: carrying the session state the live wire needs but the canonical model
/// does not. A telemetry sample, and an extras document, describe a car — only the session knows
/// whose car it is.
/// </summary>
public sealed class LiveObserverTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-17T10:00:00Z", null);

    private static (LiveObserver Observer, RecordingLiveOutbox Outbox) Create(TimeSpan? slowChannelInterval = null)
    {
        var outbox = new RecordingLiveOutbox();
        var options = Options.Create(new LiveOptions
        {
            BaseUrl = "https://home-server:5444/",
            ApiKey = "key",
            SlowChannelInterval = slowChannelInterval ?? TimeSpan.FromSeconds(1),
        });

        return (new LiveObserver(outbox, options, new FakeTimeProvider(Now)), outbox);
    }

    [Fact]
    public void The_requested_slow_channel_rate_is_the_configured_one()
    {
        var (observer, _) = Create(slowChannelInterval: TimeSpan.FromSeconds(2));

        observer.SlowChannelInterval.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Slow_channels_are_published_with_the_session_driver_identity()
    {
        var (observer, outbox) = Create();
        var session = SessionInfoFactory.Create();
        var sample = TelemetrySampleFactory.Create(session.SessionId) with { DamageEngine = null };

        await observer.OnSessionStartedAsync(session, TestContext.Current.CancellationToken);
        observer.OnSlowChannels(sample, OperatingWindowFactory.Create());

        var published = outbox.PublishedSlowFrames.ShouldHaveSingleItem();
        published.Sample.SessionId.ShouldBe(session.SessionId);
        published.SimDriverId.ShouldBe(session.SimDriverId);

        // A channel the simulator did not report arrives as null, not as the -1 it used to. A damage
        // panel can no longer say "undamaged" when the truth is "unknown", because there is no
        // number there to misread.
        published.Sample.DamageEngine.ShouldBeNull();
        published.OperatingWindows.Count.ShouldBe(4);
    }

    /// <summary>
    /// Stamped on the publishing machine's own clock — the same clock every other frame this
    /// collector sends carries, which is what makes the hub's latency readout mean anything.
    /// </summary>
    [Fact]
    public async Task Slow_channels_are_stamped_with_this_machines_capture_time()
    {
        var (observer, outbox) = Create();
        var session = SessionInfoFactory.Create();

        await observer.OnSessionStartedAsync(session, TestContext.Current.CancellationToken);
        observer.OnSlowChannels(TelemetrySampleFactory.Create(session.SessionId), OperatingWindowFactory.Create());

        outbox.PublishedSlowFrames.ShouldHaveSingleItem().CapturedAtUtc.ShouldBe(Now);
    }

    /// <summary>
    /// Between sessions there is no driver identity to attach, and inventing the previous session's
    /// would put one driver's damage on another driver's row.
    /// </summary>
    [Fact]
    public async Task Slow_channels_after_a_session_ends_carry_no_driver_identity()
    {
        var (observer, outbox) = Create();
        var session = SessionInfoFactory.Create();

        await observer.OnSessionStartedAsync(session, TestContext.Current.CancellationToken);
        await observer.OnSessionEndedAsync(session.SessionId, Now, TestContext.Current.CancellationToken);
        observer.OnSlowChannels(TelemetrySampleFactory.Create(session.SessionId), OperatingWindowFactory.Create());

        outbox.PublishedSlowFrames.ShouldHaveSingleItem().SimDriverId.ShouldBeNull();
    }
}
