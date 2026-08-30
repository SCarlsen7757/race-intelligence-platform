using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Connectors.RaceRoom;

/// <summary>
/// <see cref="ITelemetrySource"/> implementation for RaceRoom Racing Experience, reading its
/// <c>"$R3E"</c> named shared memory block.
/// </summary>
/// <remarks>
/// <para>
/// All fallible I/O (opening the shared memory block, reading a snapshot) is centralized in the
/// synchronous <see cref="ProcessTick"/> method and its helpers, which never throw — every known
/// failure mode is converted into a <see cref="ConnectionStateChanged"/> transition instead. This
/// keeps <see cref="ReadAllAsync"/> itself free of any <c>try</c>/<c>catch</c> around
/// <see langword="yield return"/>, which C# disallows (a <c>try</c> block containing a
/// <c>yield return</c> may have a <c>finally</c>, but never a <c>catch</c>).
/// </para>
/// <para>
/// See the state machine documented on <see cref="ProcessTick"/>'s helpers: opening the shared
/// memory and validating its version takes a connection from Disconnected/WaitingForSimulator to
/// Connected; a recognized session type, held steady for
/// <see cref="RaceRoomConnectorOptions.SessionStartDebounce"/>, takes it from Connected to
/// InSession; and a genuine session boundary — the session type becoming unavailable, the phase
/// moving past checkered, the (track, layout, session type, session iteration) tuple changing, or
/// the simulation tick counter regressing (an in-game session restart, which otherwise looks
/// identical) — ends the current session.
/// </para>
/// <para>
/// <b>An in-session menu suspends the session; it does not end it.</b> RaceRoom sets
/// <c>game_in_menus</c> for its in-session (ESC) menu, not only for the main menu, so treating it
/// as a session boundary ended and restarted the session every time the driver paused — one
/// qualifying session became six database rows, and any lap completed while the menu was open was
/// dropped, because the restart re-based the lap counter. Menus and <c>game_paused</c> therefore
/// move the source to SessionSuspended, which keeps the session id, sequence numbering and lap
/// count intact and publishes no samples until the driver returns. Laps completed during the
/// suspension are emitted on resume by the same catch-up path that covers a skipped poll.
/// </para>
/// <para>
/// While suspended, nothing else in the frame is trusted: a menu frame's session type and phase
/// describe the menu rather than the session behind it, so end conditions are re-evaluated only
/// once the driver is back on track. The only rule that applies while suspended is
/// <see cref="RaceRoomConnectorOptions.MaxSuspendDuration"/>.
/// </para>
/// <para>
/// <b>Game exit is detected by a frozen tick counter, not by the mapping disappearing.</b> The
/// <c>$R3E</c> section stays mapped and readable for as long as this process holds a handle to it,
/// so a RaceRoom that has exited (or crashed, or hung) is indistinguishable from one that simply
/// stopped writing: every poll keeps returning the last frame it wrote, forever. While in a
/// session, this source therefore treats <c>game_simulation_ticks</c> not advancing for
/// <see cref="RaceRoomConnectorOptions.StaleFrameTimeout"/> as the game being gone — it ends the
/// session and drops back to Disconnected so the reconnect loop can pick the game back up when it
/// returns. Note that a *frozen* counter cannot be caught by the tick-regression check, which only
/// fires on a counter that moves backwards.
/// </para>
/// </remarks>
public sealed class RaceRoomTelemetrySource : ITelemetrySource
{
    private static readonly string[] GameProcessNames = ["RRRE64", "RRRE"];

    private readonly RaceRoomConnectorOptions _options;
    private readonly Func<ISharedMemoryView> _viewFactory;
    private readonly RunState _run = new();
    private ConnectionState _state = ConnectionState.Disconnected;
    private GameVersionIdentity? _version;
    private bool _disposed;

    /// <summary>Creates a source that reads the real RaceRoom shared memory block.</summary>
    /// <param name="options">Polling/reconnect tuning. Defaults to 60 Hz polling with a 2 second reconnect delay.</param>
    public RaceRoomTelemetrySource(RaceRoomConnectorOptions? options = null)
        : this(options ?? new RaceRoomConnectorOptions(), MappedFileSharedMemoryView.Open)
    {
    }

    /// <summary>Test seam: supply a fake shared-memory source instead of opening the real one.</summary>
    internal RaceRoomTelemetrySource(RaceRoomConnectorOptions options, Func<ISharedMemoryView> viewFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(viewFactory);

        _options = options;
        _viewFactory = viewFactory;
    }

    /// <inheritdoc />
    public GameVersionIdentity? Version => _version;

    /// <summary>
    /// What this connector can produce, independent of any instance.
    /// </summary>
    /// <remarks>
    /// Static because the collector has to declare it to the live hub when opening a publishing
    /// connection, which happens before any telemetry source has been constructed — and because
    /// the answer cannot vary between instances anyway.
    /// </remarks>
    public static SimCapabilities DeclaredCapabilities { get; } =
        SimCapabilities.TyreWear |
        SimCapabilities.TyrePressure |
        SimCapabilities.TyreTemperature |
        SimCapabilities.BrakeTemperature |
        SimCapabilities.FuelFlow |
        SimCapabilities.PushToPass |
        SimCapabilities.Drs |
        SimCapabilities.Damage |
        SimCapabilities.PitWindow |
        SimCapabilities.SuspensionTelemetry |
        // Root-level in the shared block, so local car only — which is why it is declared here and
        // not alongside the opponent capabilities below.
        SimCapabilities.IncidentPoints |
        // RaceRoom's driver array covers every car in the session, but only at scoring granularity
        // — hence no opponent tyre/fuel/input capability to declare alongside these three.
        SimCapabilities.OpponentStandings |
        SimCapabilities.OpponentSectorTimes |
        SimCapabilities.OpponentPitState;

    /// <inheritdoc />
    public SimCapabilities Capabilities => DeclaredCapabilities;

    /// <inheritdoc />
    public ConnectionState State => _state;

    /// <inheritdoc />
    public async IAsyncEnumerable<TelemetryEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timer = new PeriodicTimer(_options.PollInterval);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var telemetryEvent in ProcessTick())
                {
                    yield return telemetryEvent;
                }

                bool waitingToConnect = _state is ConnectionState.Disconnected
                    or ConnectionState.WaitingForSimulator
                    or ConnectionState.Faulted;
                if (waitingToConnect)
                {
                    try
                    {
                        await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }
                }
                else if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield break;
                }
            }
        }
        finally
        {
            _run.View?.Dispose();
            _run.View = null;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _run.View?.Dispose();
        _run.View = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Runs exactly one poll tick's worth of the state machine and returns every event it
    /// produced, in order. Never throws — every fallible operation inside is caught and converted
    /// into a state transition.
    /// </summary>
    private List<TelemetryEvent> ProcessTick()
    {
        var events = new List<TelemetryEvent>(4);
        var now = DateTimeOffset.UtcNow;

        switch (_state)
        {
            case ConnectionState.Disconnected:
            case ConnectionState.WaitingForSimulator:
            // Faulted retries too. It is reached only from the catch-all in TryConnect, which means
            // "something we did not anticipate", not "something that cannot get better" — and the
            // things that land there in practice (a transient permissions or mapping error while
            // the game is starting) clear on their own. Leaving it terminal cost a whole race for a
            // fault that a two-second retry would have cleared, and left the loop spinning at the
            // full poll rate producing nothing. SetState is idempotent, so a fault that genuinely
            // persists re-enters this branch silently rather than logging every retry.
            case ConnectionState.Faulted:
                TryConnect(events, now);
                break;

            case ConnectionState.Connected:
            case ConnectionState.InSession:
            case ConnectionState.SessionSuspended:
                ProcessConnectedTick(events, now);
                break;

            default:
                break;
        }

        return events;
    }

    /// <summary>Disconnected/WaitingForSimulator: attempt to open and version-check the shared memory block.</summary>
    private void TryConnect(List<TelemetryEvent> events, DateTimeOffset now)
    {
        ISharedMemoryView? view = null;
        try
        {
            view = _viewFactory();

            if (!R3EVersionGate.TryValidate(view, out int major, out int minor, out string? failureReason))
            {
                SetState(ConnectionState.WaitingForSimulator, events, now, failureReason);
                view.Dispose();
                return;
            }

            _run.View = view;
            _version = BuildVersionIdentity(major, minor);
            SetState(ConnectionState.Connected, events, now, reason: null);
        }
        catch (FileNotFoundException)
        {
            // The documented, expected way OpenExisting reports "RaceRoom is not running yet".
            view?.Dispose();
            SetState(ConnectionState.WaitingForSimulator, events, now, "RaceRoom is not currently running (shared memory \"$R3E\" was not found).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            view?.Dispose();
            SetState(ConnectionState.WaitingForSimulator, events, now, $"failed to open the RaceRoom shared memory block: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Something genuinely unexpected — do not let it escape ProcessTick/ReadAllAsync.
            view?.Dispose();
            SetState(ConnectionState.Faulted, events, now, $"unexpected error connecting to RaceRoom: {ex.Message}");
        }
    }

    /// <summary>Connected/InSession: read one snapshot and advance the state machine from it.</summary>
    private void ProcessConnectedTick(List<TelemetryEvent> events, DateTimeOffset now)
    {
        var view = _run.View;
        R3ESharedRaw raw = default;
        var outcome = view is null || !view.IsValid
            ? ReadOutcome.Failed
            : TryReadRaw(view, out raw);

        if (outcome == ReadOutcome.Torn)
        {
            // The game rewrote the block underneath us; this snapshot mixes two frames and must not
            // be published. Skipping the tick is free — the next poll is 1/60th of a second away —
            // whereas a torn frame would be stored permanently.
            return;
        }

        if (outcome == ReadOutcome.Failed)
        {
            EndCurrentSessionIfAny(events, now);
            _run.View?.Dispose();
            _run.View = null;
            SetState(ConnectionState.Disconnected, events, now, "lost connection to the RaceRoom shared memory block.");
            return;
        }

        // The driver is not driving: an in-session (ESC) menu, an outright pause, or a replay.
        //
        // Menus and pauses freeze the published frame, so neither its telemetry nor its session
        // fields describe anything current. A replay is worse than stale — the block publishes the
        // replayed car's inputs and lap times as if they were happening now, which is
        // indistinguishable from live driving to everything downstream. Collecting it would file
        // replay laps in the archive as real ones and put a race engineer in front of a timing
        // tower showing a race that already finished.
        bool suspended = raw.GameInMenus != 0 || raw.GamePaused != 0 || raw.GameInReplay != 0;
        bool sessionAvailable = raw.SessionType != (int)R3ESessionType.Unavailable;
        bool phasePastCheckered = raw.SessionPhase > (int)R3ESessionPhase.Checkered;
        bool onTrackSessionFrame = !suspended && sessionAvailable && !phasePastCheckered;

        var newKey = new SessionKey(
            R3ETelemetryMapper.DecodeUtf8Name(raw.TrackName),
            R3ETelemetryMapper.DecodeUtf8Name(raw.LayoutName),
            raw.SessionType,
            raw.SessionIteration);

        if (_state == ConnectionState.Connected)
        {
            // No session yet. Waiting for the key to settle discards the sub-second flicker the
            // game publishes while loading the next session.
            if (onTrackSessionFrame)
            {
                StartSessionOnceKeyIsStable(events, now, in raw, newKey);
            }
            else
            {
                _run.ClearPendingStart();
            }

            return;
        }

        // InSession or SessionSuspended: a session is live and must survive this tick unless a
        // genuine boundary says otherwise.
        if (suspended)
        {
            // The one boundary worth trusting from a menu frame: it names a different session that
            // is unambiguously real. That is the game loading the next session while the loading
            // screen still counts as "in menus", and it ends the previous session promptly instead
            // of leaving it open until the suspend timeout.
            //
            // A session type of "unavailable" is deliberately NOT treated the same way. It is what
            // the main menu reports, but it cannot be distinguished from a mid-session menu that
            // simply stops publishing a session type, and guessing wrong there reinstates the bug
            // this state exists to fix. Waiting costs a late ended_at on a session the driver
            // abandoned; guessing costs every paused session in the database.
            bool movedToAnotherSession = sessionAvailable && _run.CurrentKey is { } suspendedKey && suspendedKey != newKey;

            if (movedToAnotherSession)
            {
                EndCurrentSessionIfAny(events, now);
                SetState(ConnectionState.Connected, events, now, reason: null);
                return;
            }

            if (_state == ConnectionState.InSession)
            {
                _run.SuspendedAtUtc = now;
                SetState(ConnectionState.SessionSuspended, events, now, "the driver is in a menu or the game is paused; the session is suspended, not ended.");
            }
            else if (now - _run.SuspendedAtUtc >= _options.MaxSuspendDuration)
            {
                EndCurrentSessionIfAny(events, now);
                SetState(ConnectionState.Connected, events, now, $"the session stayed suspended for longer than {_options.MaxSuspendDuration}; presuming it was abandoned.");
            }

            // Deliberately no other checks. A menu frame reports the menu's session type and phase,
            // not the suspended session's, so evaluating boundaries against it is what caused a
            // menu visit to end the session in the first place.
            return;
        }

        // Back on track — the frame can be trusted again, so re-evaluate the real end conditions.
        bool keyChanged = _run.CurrentKey is { } currentKey && currentKey != newKey;

        // A restart from the in-session menu resumes with the tick counter re-based, which is the
        // only thing distinguishing it from an ordinary resume. Checked on the resuming frame too,
        // for exactly that reason.
        bool ticksRegressed = !keyChanged && raw.Player.GameSimulationTicks < _run.LastTicks;

        if (!sessionAvailable || phasePastCheckered || keyChanged || ticksRegressed)
        {
            EndCurrentSessionIfAny(events, now);
            SetState(ConnectionState.Connected, events, now, reason: null);

            // The same frame may already describe the next session. Starting it here rather than on
            // a later tick is what makes a restart read as SessionEnded immediately followed by
            // SessionStarted, once the key has settled.
            if (onTrackSessionFrame)
            {
                StartSessionOnceKeyIsStable(events, now, in raw, newKey);
            }

            return;
        }

        if (_state == ConnectionState.SessionSuspended)
        {
            // Resume the same session. The tick clock restarts from here: the frozen span while
            // suspended is not evidence that the game has stopped writing.
            _run.LastTicks = raw.Player.GameSimulationTicks;
            _run.LastTicksChangedAtUtc = now;
            _run.SuspendedAtUtc = default;
            SetState(ConnectionState.InSession, events, now, reason: null);
        }

        // InSession: a tick counter that has stopped advancing for longer than the configured
        // timeout means RaceRoom is no longer writing to the block. Checked before publishing a
        // sample because a dead game keeps serving the same frame indefinitely, and that frame is
        // (by definition) still on-track, still the same session key, and has ticks that are equal
        // rather than lower — so nothing else here would ever fire.
        if (raw.Player.GameSimulationTicks != _run.LastTicks)
        {
            _run.LastTicksChangedAtUtc = now;
        }
        else if (now - _run.LastTicksChangedAtUtc >= _options.StaleFrameTimeout)
        {
            EndCurrentSessionIfAny(events, now);
            _run.View?.Dispose();
            _run.View = null;
            SetState(
                ConnectionState.Disconnected,
                events,
                now,
                $"RaceRoom's simulation tick counter has not advanced for {_options.StaleFrameTimeout}; presuming the game exited.");
            return;
        }

        // Also emits a LapCompleted per lap finished since the last published sample — which, after
        // a suspension, is every lap the driver completed before opening the menu on the out-lap as
        // well as any the game credited while it was open.
        EmitSample(events, now, in raw);
    }

    /// <summary>
    /// Connected -&gt; InSession, once <paramref name="key"/> has been reported unchanged for
    /// <see cref="RaceRoomConnectorOptions.SessionStartDebounce"/>.
    /// </summary>
    private void StartSessionOnceKeyIsStable(List<TelemetryEvent> events, DateTimeOffset now, in R3ESharedRaw raw, SessionKey key)
    {
        if (_run.PendingKey is not { } pending || pending != key)
        {
            _run.PendingKey = key;
            _run.PendingKeySinceUtc = now;
        }

        if (now - _run.PendingKeySinceUtc < _options.SessionStartDebounce)
        {
            return;
        }

        _run.ClearPendingStart();
        StartSession(events, now, in raw);
    }

    /// <summary>Connected -&gt; InSession: begin tracking a new session and emit its first sample.</summary>
    private void StartSession(List<TelemetryEvent> events, DateTimeOffset now, in R3ESharedRaw raw)
    {
        _run.SessionId = Guid.NewGuid();
        _run.CurrentKey = new SessionKey(
            R3ETelemetryMapper.DecodeUtf8Name(raw.TrackName),
            R3ETelemetryMapper.DecodeUtf8Name(raw.LayoutName),
            raw.SessionType,
            raw.SessionIteration);
        _run.LastCompletedLaps = raw.CompletedLaps;
        _run.LastTicks = raw.Player.GameSimulationTicks;
        _run.LastTicksChangedAtUtc = now;
        _run.SequenceNumber = 0;

        SetState(ConnectionState.InSession, events, now, reason: null);

        var gameVersion = _version
            ?? throw new InvalidOperationException("A session cannot start before a game version has been established.");
        var sessionInfo = R3ETelemetryMapper.ToSessionInfo(in raw, _run.SessionId, gameVersion, Capabilities, now);
        events.Add(new SessionStarted { OccurredAtUtc = now, Session = sessionInfo });

        EmitSample(events, now, in raw);
    }

    /// <summary>Maps and emits one telemetry sample, plus a LapCompleted if completed_laps advanced.</summary>
    private void EmitSample(List<TelemetryEvent> events, DateTimeOffset now, in R3ESharedRaw raw)
    {
        var sample = R3ETelemetryMapper.ToSample(in raw, _run.SessionId, _run.SequenceNumber++, now);
        events.Add(new TelemetrySampleReceived { OccurredAtUtc = now, Sample = sample });

        if (raw.CompletedLaps > _run.LastCompletedLaps)
        {
            // One LapCompleted per lap actually completed, not one per observation. A skipped poll
            // (GC pause, a stalled frame, the process descheduled) can advance completed_laps by
            // more than one, and emitting a single event would silently delete the laps in between.
            // Only the newest of them is described by this snapshot's lap-scoped fields — the rest
            // are reported with their timings unknown rather than with the newest lap's numbers.
            int firstNewLap = _run.LastCompletedLaps + 1;
            int newestLap = raw.CompletedLaps;
            _run.LastCompletedLaps = newestLap;

            for (int lapNumber = firstNewLap; lapNumber <= newestLap; lapNumber++)
            {
                var lap = R3ETelemetryMapper.ToLapInfo(in raw, _run.SessionId, lapNumber, snapshotDescribesThisLap: lapNumber == newestLap);
                events.Add(new LapCompleted { OccurredAtUtc = now, Lap = lap });
            }
        }

        _run.LastTicks = raw.Player.GameSimulationTicks;

        EmitSlowChannelsIfDue(events, now, sample, in raw);
        EmitStandingsIfDue(events, now, in raw);
    }

    /// <summary>
    /// Re-publishes the local car's slow-moving channels, no more often than
    /// <see cref="RaceRoomConnectorOptions.SlowChannelInterval"/>.
    /// </summary>
    /// <remarks>
    /// Republishes the sample that was just mapped rather than reading the block a second time. Every
    /// channel is on it already — the archive path stores all of them per row — so this channel is
    /// free to produce, and the rate limit is purely about how often anything downstream has to look
    /// at values that move once a lap.
    /// </remarks>
    private void EmitSlowChannelsIfDue(List<TelemetryEvent> events, DateTimeOffset now, RaceRoomTelemetrySample sample, in R3ESharedRaw raw)
    {
        // Any negative interval disables the channel, not only Timeout.InfiniteTimeSpan — see the
        // same reasoning spelled out on EmitStandingsIfDue.
        if (_options.SlowChannelInterval < TimeSpan.Zero)
        {
            return;
        }

        if (now - _run.LastSlowChannelsAtUtc < _options.SlowChannelInterval)
        {
            return;
        }

        _run.LastSlowChannelsAtUtc = now;
        events.Add(new SlowChannelsUpdated
        {
            OccurredAtUtc = now,
            Sample = sample,
            OperatingWindows = R3ETelemetryMapper.ToOperatingWindows(in raw),
        });
    }

    /// <summary>
    /// Reads the driver array and emits <see cref="StandingsUpdated"/>, no more often than
    /// <see cref="RaceRoomConnectorOptions.StandingsInterval"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from <see cref="EmitSample"/> rather than from the tick loop directly, so that
    /// standings inherit exactly the conditions under which a sample is publishable: a live
    /// session, an untorn frame, and not suspended. A menu frame's driver array describes the
    /// moment the driver pressed escape, and republishing it would show a race still unfolding
    /// while nothing is moving.
    /// </para>
    /// <para>
    /// A torn or failed array read is not a connection failure: the player snapshot it accompanies
    /// was read cleanly and has already been published. The tick is skipped and the due-time is
    /// left untouched, so the next poll retries immediately rather than waiting out another
    /// interval.
    /// </para>
    /// </remarks>
    private void EmitStandingsIfDue(List<TelemetryEvent> events, DateTimeOffset now, in R3ESharedRaw raw)
    {
        // Any negative interval disables standings, not only Timeout.InfiniteTimeSpan (-1 tick).
        // Testing for that exact value would let TimeSpan.FromSeconds(-1) fall through to the
        // comparison below, where "elapsed < a negative" is never true — silently turning an
        // obvious attempt to switch the feature off into reading the field on every single poll.
        if (_options.StandingsInterval < TimeSpan.Zero)
        {
            return;
        }

        // No "never read yet" special case: LastStandingsAtUtc defaults to year 1, so the elapsed
        // span is ~739,000 days and the first tick of a session is always due.
        if (now - _run.LastStandingsAtUtc < _options.StandingsInterval)
        {
            return;
        }

        var view = _run.View;
        if (view is null || !view.IsValid)
        {
            return;
        }

        if (R3EDriverDataReader.TryRead(view, in raw, out var drivers) != DriverArrayReadOutcome.Ok)
        {
            return;
        }

        _run.LastStandingsAtUtc = now;

        if (drivers.Length == 0)
        {
            // A session with no field reported yet — nothing to say, and an empty tower would read
            // as "everyone retired" rather than "not known yet".
            return;
        }

        // Settled from the local car, whose root block carries both a lap time and that lap's
        // splits, then applied to every car in the field. Checked on each snapshot rather than once
        // because the first frames of a session cannot answer it — nobody has completed a lap yet.
        _run.SectorTimeConvention = R3ESectorTimeConventionDetector.Detect(in raw, _run.SectorTimeConvention);

        var standings = R3ETelemetryMapper.ToSessionStandings(
            drivers, in raw, _run.SessionId, now, _run.SectorTimeConvention, _run.PitLane);
        events.Add(new StandingsUpdated { OccurredAtUtc = now, Standings = standings });
    }

    /// <summary>
    /// InSession or SessionSuspended, if either -&gt; emits SessionEnded and clears session state.
    /// </summary>
    /// <remarks>
    /// A suspended session is still a live session that has to be closed: the game exiting, or the
    /// suspension outstaying <see cref="RaceRoomConnectorOptions.MaxSuspendDuration"/>, must not
    /// leave it dangling with no <see cref="SessionEnded"/> ever emitted.
    /// </remarks>
    private void EndCurrentSessionIfAny(List<TelemetryEvent> events, DateTimeOffset now)
    {
        if (_state is ConnectionState.InSession or ConnectionState.SessionSuspended)
        {
            events.Add(new SessionEnded { OccurredAtUtc = now, SessionId = _run.SessionId });
            _run.ClearSession();
        }
    }

    /// <summary>Records a state transition and emits <see cref="ConnectionStateChanged"/> — a no-op if the state is unchanged.</summary>
    private void SetState(ConnectionState newState, List<TelemetryEvent> events, DateTimeOffset now, string? reason)
    {
        if (_state == newState)
        {
            return;
        }

        _state = newState;
        events.Add(new ConnectionStateChanged { OccurredAtUtc = now, State = newState, Reason = reason });
    }

    /// <summary>What one attempt to read a snapshot produced.</summary>
    private enum ReadOutcome
    {
        /// <summary>A whole, self-consistent frame.</summary>
        Ok,

        /// <summary>The game wrote to the block while we were reading it; the snapshot mixes two frames.</summary>
        Torn,

        /// <summary>The view could not be read at all.</summary>
        Failed,
    }

    /// <summary>
    /// Byte offset of <c>player.game_simulation_ticks</c> within the block — the field re-read to
    /// detect a torn frame.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because <see cref="R3EDriverDataReader"/> makes the same
    /// torn-frame check against the same field, and the two must never drift apart.
    /// </remarks>
    internal static readonly long SimulationTicksOffset =
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.Player)).ToInt64()
        + Marshal.OffsetOf<R3EPlayerData>(nameof(R3EPlayerData.GameSimulationTicks)).ToInt64();

    /// <summary>
    /// Reads one raw snapshot. Never throws — every failure is reported as a
    /// <see cref="ReadOutcome"/> instead.
    /// </summary>
    /// <remarks>
    /// RaceRoom publishes the block with no sequence lock, version counter, or any other
    /// synchronization: it simply overwrites the memory in place while we read it, so a ~2 KB read
    /// can straddle two frames and produce a snapshot that never existed (e.g. this frame's speed
    /// beside last frame's gear). Re-reading <c>game_simulation_ticks</c> after the copy and
    /// comparing it against the copy's own value catches exactly that — the counter advances every
    /// physics tick, so a write that landed during our read almost always moved it.
    /// </remarks>
    private static ReadOutcome TryReadRaw(ISharedMemoryView view, out R3ESharedRaw raw)
    {
        try
        {
            if (view.Length < Unsafe.SizeOf<R3ESharedRaw>())
            {
                raw = default;
                return ReadOutcome.Failed;
            }

            raw = view.Read<R3ESharedRaw>();
            int ticksAfterRead = view.Read<int>(SimulationTicksOffset);
            return ticksAfterRead == raw.Player.GameSimulationTicks ? ReadOutcome.Ok : ReadOutcome.Torn;
        }
        catch (ObjectDisposedException)
        {
            raw = default;
            return ReadOutcome.Failed;
        }
    }

    private GameVersionIdentity BuildVersionIdentity(int apiMajor, int apiMinor) => new()
    {
        Game = WellKnownGames.RaceRoom,
        GameVersion = TryGetInstalledGameVersion(),
        ApiVersionMajor = apiMajor,
        ApiVersionMinor = apiMinor,
        ConnectorVersion = ConnectorVersion,
    };

    private static readonly string ConnectorVersion = ResolveConnectorVersion();

    private static string ResolveConnectorVersion()
    {
        var assembly = typeof(RaceRoomTelemetrySource).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    /// <summary>
    /// Best-effort lookup of the running game's own file version via the RRRE.exe/RRRE64.exe
    /// process. Must never be fatal to establishing a connection — any failure here (game not
    /// found, access denied, ...) simply yields <see langword="null"/>.
    /// </summary>
    private static string? TryGetInstalledGameVersion()
    {
        try
        {
            foreach (var processName in GameProcessNames)
            {
                Process[] processes = Process.GetProcessesByName(processName);
                try
                {
                    if (processes.Length == 0)
                    {
                        continue;
                    }

                    return processes[0].MainModule?.FileVersionInfo.FileVersion;
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class RunState
    {
        public ISharedMemoryView? View;
        public Guid SessionId;
        public SessionKey? CurrentKey;
        public int LastCompletedLaps;
        public int LastTicks;

        /// <summary>When <see cref="LastTicks"/> last changed — the clock behind the stale-frame (game exited) check.</summary>
        public DateTimeOffset LastTicksChangedAtUtc;

        public long SequenceNumber;

        /// <summary>When the session was suspended — the clock behind the max-suspend check.</summary>
        public DateTimeOffset SuspendedAtUtc;

        /// <summary>
        /// The key of a session that has been observed but not yet started, and when it was first
        /// seen — the debounce that keeps a half-loaded session out of the database.
        /// </summary>
        public SessionKey? PendingKey;

        public DateTimeOffset PendingKeySinceUtc;

        /// <summary>
        /// When the driver array was last read — the clock behind
        /// <see cref="RaceRoomConnectorOptions.StandingsInterval"/>. Never set means "not yet this
        /// session", which reads as due.
        /// </summary>
        public DateTimeOffset LastStandingsAtUtc;

        /// <summary>
        /// When the extras document was last published — the clock behind
        /// <see cref="RaceRoomConnectorOptions.SlowChannelInterval"/>. Never set reads as due, so the
        /// first sample of a session always carries one.
        /// </summary>
        public DateTimeOffset LastSlowChannelsAtUtc;

        /// <summary>
        /// How this game fills its sector triples. Held per session and re-established each time,
        /// since a session boundary is also where a game update could take effect.
        /// </summary>
        public R3ESectorTimeConvention SectorTimeConvention;

        /// <summary>
        /// What earlier frames showed about each car's pit lane visit. Held for the run rather than
        /// rebuilt per snapshot — its entire value is that it remembers, and cleared at a session
        /// boundary because slot numbers are reused across sessions.
        /// </summary>
        public readonly R3EPitLaneTracker PitLane = new();

        public void ClearSession()
        {
            PitLane.Clear();
            SessionId = default;
            CurrentKey = null;
            LastCompletedLaps = 0;
            LastTicks = 0;
            LastTicksChangedAtUtc = default;
            SequenceNumber = 0;
            SuspendedAtUtc = default;
            LastStandingsAtUtc = default;
            LastSlowChannelsAtUtc = default;
            SectorTimeConvention = R3ESectorTimeConvention.Cumulative;
        }

        public void ClearPendingStart()
        {
            PendingKey = null;
            PendingKeySinceUtc = default;
        }
    }

    /// <summary>
    /// The tuple that identifies one of the simulator's sessions. Deliberately does not include
    /// <see cref="RunState.LastTicks"/> — tick regression is checked separately so that an in-game
    /// "restart session" (identical tuple, ticks reset) is still detected.
    /// </summary>
    /// <remarks>
    /// <paramref name="RawSessionIteration"/> is what makes the tuple sufficient on its own. Two
    /// qualifying sessions at the same track differ in nothing else, so without it the connector
    /// could only tell them apart by watching for a trip through the menus — which is precisely the
    /// coupling that made an ordinary pause look like a new session.
    /// </remarks>
    private readonly record struct SessionKey(string TrackName, string LayoutName, int RawSessionType, int RawSessionIteration);
}
