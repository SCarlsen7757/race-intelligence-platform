using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RaceIntelligence.Collector.Plugins.Live;
using RaceIntelligence.Collector.Plugins.Live.Tests.Support;
using RaceIntelligence.Collector.TestSupport;
using RaceIntelligence.Live.Contracts.Publish;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Live.Tests;

/// <summary>
/// Covers the publishing loop's reconnect behaviour. The hub is on the other side of a home
/// internet connection, so losing it is routine rather than exceptional — what matters is that the
/// collector recovers on its own, keeps the hub able to place the frames it receives, and never
/// interferes with archiving on the way.
/// </summary>
public sealed class LivePublishServiceTests
{
    private static IOptions<LiveOptions> Options() => Microsoft.Extensions.Options.Options.Create(new LiveOptions
    {
        Enabled = true,
        ApiKey = "test",
        ReconnectDelay = TimeSpan.FromSeconds(1),
        MaxReconnectDelay = TimeSpan.FromSeconds(8),
    });

    private static LiveOutbox CreateOutbox() => new(NullLogger<LiveOutbox>.Instance);

    private static LivePublishService CreateService(
        LiveOutbox outbox,
        ILiveConnectionFactory factory,
        TimeProvider timeProvider) =>
        new(outbox, factory, Options(), timeProvider, NullLogger<LivePublishService>.Instance);

    [Fact]
    public async Task Publishes_what_the_outbox_holds()
    {
        var outbox = CreateOutbox();
        var connection = new RecordingLiveConnection();
        var factory = new StubConnectionFactory(() => connection);
        var service = CreateService(outbox, factory, TimeProvider.System);

        outbox.PublishSessionStarted(SessionInfoFactory.Create(), "fingerprint", 2);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await connection.WaitForSendsAsync(1);
        await service.StopAsync(TestContext.Current.CancellationToken);

        connection.Sent.ShouldHaveSingleItem().ShouldBeOfType<LiveSessionFrame>();
    }

    /// <summary>
    /// The regression this guards: a hub learns which session a client is publishing from the
    /// announcement carried on that socket. A reconnect mid-race that did not re-announce would
    /// deliver standings for a session the new connection has never been told about, and the room
    /// would never appear.
    /// </summary>
    [Fact]
    public async Task Re_announces_the_current_session_on_every_new_connection()
    {
        var outbox = CreateOutbox();
        var first = new RecordingLiveConnection { FailAfterSends = 1 };
        var second = new RecordingLiveConnection();

        var connections = new Queue<RecordingLiveConnection>([first, second]);
        var factory = new StubConnectionFactory(connections.Dequeue);

        var time = new FakeTimeProvider();
        var service = CreateService(outbox, factory, time);

        var session = SessionInfoFactory.Create();
        outbox.PublishSessionStarted(session, "fingerprint", 2);

        await service.StartAsync(TestContext.Current.CancellationToken);

        // The first connection takes the announcement.
        await first.WaitForSendsAsync(1);
        first.Sent[0].ShouldBeOfType<LiveSessionFrame>();

        // Publishing again is what makes the loop attempt the send that fails — without it the
        // pump simply parks waiting for work and the socket is never exercised.
        outbox.PublishStandings(new Core.Sessions.SessionStandings
        {
            SessionId = session.SessionId,
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
            Drivers = [],
        });

        // Advance past the backoff so the loop reconnects.
        await AdvanceUntilAsync(time, () => second.Sent.Count >= 1);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // The second connection was told about the session without anyone re-publishing it — the
        // announcement outlived the connection that originally carried it.
        second.Sent.ShouldNotBeEmpty();
        second.Sent[0].ShouldBeOfType<LiveSessionFrame>().SessionId.ShouldBe(session.SessionId);
    }

    [Fact]
    public async Task Keeps_reconnecting_after_a_failure_to_connect()
    {
        var outbox = CreateOutbox();
        var connection = new RecordingLiveConnection();
        int attempts = 0;

        var factory = new StubConnectionFactory(() =>
        {
            // Fail the first two attempts outright, as an unreachable hub would.
            if (Interlocked.Increment(ref attempts) <= 2)
            {
                throw new IOException("the hub is unreachable.");
            }

            return connection;
        });

        var time = new FakeTimeProvider();
        var service = CreateService(outbox, factory, time);

        outbox.PublishSessionStarted(SessionInfoFactory.Create(), "fingerprint", 2);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await AdvanceUntilAsync(time, () => connection.Sent.Count >= 1);
        await service.StopAsync(TestContext.Current.CancellationToken);

        attempts.ShouldBeGreaterThanOrEqualTo(3);
        connection.Sent[0].ShouldBeOfType<LiveSessionFrame>();
    }

    /// <summary>
    /// Backoff is capped so a hub that was down for an hour is picked back up within the ceiling of
    /// returning, rather than after a delay that has grown past the length of the race.
    /// </summary>
    [Fact]
    public async Task The_reconnect_backoff_does_not_grow_without_limit()
    {
        var outbox = CreateOutbox();
        var factory = new StubConnectionFactory(() => throw new IOException("down."));
        var time = new FakeTimeProvider();
        var service = CreateService(outbox, factory, time);

        await service.StartAsync(TestContext.Current.CancellationToken);

        // Far more doublings than it takes to reach the 8 second ceiling from 1 second.
        for (int i = 0; i < 20; i++)
        {
            time.Advance(TimeSpan.FromSeconds(8));
            await Task.Yield();
        }

        int attemptsAtCeiling = factory.Attempts;
        time.Advance(TimeSpan.FromSeconds(8));
        await AdvanceUntilAsync(time, () => factory.Attempts > attemptsAtCeiling, step: TimeSpan.FromSeconds(8));

        await service.StopAsync(TestContext.Current.CancellationToken);

        // Still trying, at a bounded rate, rather than having backed off into next week.
        factory.Attempts.ShouldBeGreaterThan(attemptsAtCeiling);
    }

    /// <summary>
    /// Pumps the fake clock until <paramref name="condition"/> holds, so a test never depends on
    /// real elapsed time. Fails rather than hanging if the loop never gets there.
    /// </summary>
    private static async Task AdvanceUntilAsync(
        FakeTimeProvider time,
        Func<bool> condition,
        TimeSpan? step = null,
        int maxIterations = 200)
    {
        for (int i = 0; i < maxIterations; i++)
        {
            if (condition())
            {
                return;
            }

            time.Advance(step ?? TimeSpan.FromSeconds(1));

            // Let the service's continuation run before checking again — Advance only releases the
            // timer, it does not run the task that was waiting on it.
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue("the publish loop never reached the expected state.");
    }

    /// <summary>Hands out pre-built connections, or throws, so a test can script a flaky hub.</summary>
    private sealed class StubConnectionFactory(Func<ILiveConnection> connect) : ILiveConnectionFactory
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public Task<ILiveConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(connect());
        }
    }

    /// <summary>Records what was sent, and can fail partway through to model a dropped socket.</summary>
    private sealed class RecordingLiveConnection : ILiveConnection
    {
        private readonly List<LivePublisherMessage> _sent = [];
        private readonly Lock _gate = new();

        /// <summary>Fail every send after this many have succeeded. Null never fails.</summary>
        public int? FailAfterSends { get; init; }

        public IReadOnlyList<LivePublisherMessage> Sent
        {
            get
            {
                lock (_gate)
                {
                    return [.. _sent];
                }
            }
        }

        public ValueTask SendAsync(LivePublisherMessage message, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (FailAfterSends is { } limit && _sent.Count >= limit)
                {
                    throw new IOException("the connection dropped.");
                }

                _sent.Add(message);
            }

            return ValueTask.CompletedTask;
        }

        public async Task WaitForSendsAsync(int count)
        {
            for (int i = 0; i < 200 && Sent.Count < count; i++)
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            Sent.Count.ShouldBeGreaterThanOrEqualTo(count, "the publish loop did not send what was expected.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
