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

    private static (LiveObserver Observer, RecordingLiveOutbox Outbox) Create(TimeSpan? extrasInterval = null)
    {
        var outbox = new RecordingLiveOutbox();
        var options = Options.Create(new LiveOptions
        {
            BaseUrl = "https://home-server:5444/",
            ApiKey = "key",
            ExtrasInterval = extrasInterval ?? TimeSpan.FromSeconds(1),
        });

        return (new LiveObserver(outbox, options, new FakeTimeProvider(Now)), outbox);
    }

    [Fact]
    public void The_requested_extras_rate_is_the_configured_one()
    {
        var (observer, _) = Create(extrasInterval: TimeSpan.FromSeconds(2));

        observer.ExtrasInterval.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Extras_are_published_with_the_session_driver_identity()
    {
        var (observer, outbox) = Create();
        var session = SessionInfoFactory.Create();

        await observer.OnSessionStartedAsync(session, TestContext.Current.CancellationToken);
        observer.OnExtras(session.SessionId, """{"damage":{"engine":-1.0}}""");

        var published = outbox.PublishedExtras.ShouldHaveSingleItem();
        published.SessionId.ShouldBe(session.SessionId);
        published.SimDriverId.ShouldBe(session.SimDriverId);

        // Verbatim: the -1 a simulator writes for a channel it does not report must reach the
        // dashboard intact, or a damage panel says "undamaged" when the truth is "unknown".
        published.ExtrasJson.ShouldBe("""{"damage":{"engine":-1.0}}""");
    }

    /// <summary>
    /// Stamped on the publishing machine's own clock — the same clock every other frame this
    /// collector sends carries, which is what makes the hub's latency readout mean anything.
    /// </summary>
    [Fact]
    public async Task Extras_are_stamped_with_this_machines_capture_time()
    {
        var (observer, outbox) = Create();
        var session = SessionInfoFactory.Create();

        await observer.OnSessionStartedAsync(session, TestContext.Current.CancellationToken);
        observer.OnExtras(session.SessionId, "{}");

        outbox.PublishedExtras.ShouldHaveSingleItem().CapturedAtUtc.ShouldBe(Now);
    }

    /// <summary>
    /// Between sessions there is no driver identity to attach, and inventing the previous session's
    /// would put one driver's damage on another driver's row.
    /// </summary>
    [Fact]
    public async Task Extras_after_a_session_ends_carry_no_driver_identity()
    {
        var (observer, outbox) = Create();
        var session = SessionInfoFactory.Create();

        await observer.OnSessionStartedAsync(session, TestContext.Current.CancellationToken);
        await observer.OnSessionEndedAsync(session.SessionId, Now, TestContext.Current.CancellationToken);
        observer.OnExtras(session.SessionId, "{}");

        outbox.PublishedExtras.ShouldHaveSingleItem().SimDriverId.ShouldBeNull();
    }
}
