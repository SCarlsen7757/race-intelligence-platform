using RaceIntelligence.Collector.Plugins.Live;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Live.Tests.Support;

/// <summary>
/// Fake <see cref="ILiveOutbox"/> that records every publish in call order, so a test can assert
/// both what was sent and the sequence it was sent in.
/// </summary>
/// <remarks>
/// Always reports <see cref="IsEnabled"/> as <see langword="true"/>: a disabled outbox is already
/// covered by the production <see cref="NullLiveOutbox"/>, and a fake that claimed to be off would
/// let callers skip the very work under test.
/// <para>
/// Every member is guarded by one lock. The collect loop publishes from its own thread while the
/// test asserts from the test thread, so an unsynchronised <see cref="List{T}"/> here would be a
/// data race — and the intermittent kind that passes locally and fails in CI.
/// </para>
/// </remarks>
internal sealed class RecordingLiveOutbox : ILiveOutbox
{
    private readonly Lock _gate = new();
    private readonly List<(SessionInfo Session, string RosterFingerprint, int RosterSize)> _startedSessions = [];
    private readonly List<SessionStandings> _publishedStandings = [];
    private readonly List<(TelemetrySample Sample, string? SimDriverId)> _publishedSelfFrames = [];
    private readonly List<(Guid SessionId, string ExtrasJson, DateTimeOffset CapturedAtUtc, string? SimDriverId)> _publishedExtras = [];
    private readonly List<(Guid SessionId, string? Reason)> _endedSessions = [];

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>Sessions announced via <see cref="PublishSessionStarted"/>, in order.</summary>
    public IReadOnlyList<(SessionInfo Session, string RosterFingerprint, int RosterSize)> StartedSessions
    {
        get
        {
            lock (_gate)
            {
                return [.. _startedSessions];
            }
        }
    }

    /// <summary>Standings snapshots published, in order.</summary>
    public IReadOnlyList<SessionStandings> PublishedStandings
    {
        get
        {
            lock (_gate)
            {
                return [.. _publishedStandings];
            }
        }
    }

    /// <summary>Local-car frames published, in order.</summary>
    public IReadOnlyList<(TelemetrySample Sample, string? SimDriverId)> PublishedSelfFrames
    {
        get
        {
            lock (_gate)
            {
                return [.. _publishedSelfFrames];
            }
        }
    }

    /// <summary>Extras documents published, in order.</summary>
    public IReadOnlyList<(Guid SessionId, string ExtrasJson, DateTimeOffset CapturedAtUtc, string? SimDriverId)> PublishedExtras
    {
        get
        {
            lock (_gate)
            {
                return [.. _publishedExtras];
            }
        }
    }

    /// <summary>Sessions announced as finished via <see cref="PublishSessionEnded"/>, in order.</summary>
    public IReadOnlyList<(Guid SessionId, string? Reason)> EndedSessions
    {
        get
        {
            lock (_gate)
            {
                return [.. _endedSessions];
            }
        }
    }

    /// <inheritdoc />
    public void PublishSessionStarted(SessionInfo session, string rosterFingerprint, int rosterSize)
    {
        lock (_gate)
        {
            _startedSessions.Add((session, rosterFingerprint, rosterSize));
        }
    }

    /// <inheritdoc />
    public void PublishStandings(SessionStandings standings)
    {
        lock (_gate)
        {
            _publishedStandings.Add(standings);
        }
    }

    /// <inheritdoc />
    public void PublishSelf(TelemetrySample sample, string? simDriverId)
    {
        lock (_gate)
        {
            _publishedSelfFrames.Add((sample, simDriverId));
        }
    }

    /// <inheritdoc />
    public void PublishExtras(Guid sessionId, string extrasJson, DateTimeOffset capturedAtUtc, string? simDriverId)
    {
        lock (_gate)
        {
            _publishedExtras.Add((sessionId, extrasJson, capturedAtUtc, simDriverId));
        }
    }

    /// <inheritdoc />
    public void PublishSessionEnded(Guid sessionId, string? reason)
    {
        lock (_gate)
        {
            _endedSessions.Add((sessionId, reason));
        }
    }
}
