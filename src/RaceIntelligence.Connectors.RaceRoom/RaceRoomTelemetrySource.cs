using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;

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
/// Connected; leaving the game's menus with a recognized session type takes it from Connected to
/// InSession; and a "session boundary" — the game returning to menus, the session phase moving
/// past checkered, the (track, layout, session type) tuple changing, or the simulation tick
/// counter regressing (an in-game session restart, which otherwise looks identical) — ends the
/// current session.
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

    /// <inheritdoc />
    public SimCapabilities Capabilities { get; } =
        SimCapabilities.TyreWear |
        SimCapabilities.TyrePressure |
        SimCapabilities.TyreTemperature |
        SimCapabilities.BrakeTemperature |
        SimCapabilities.FuelFlow |
        SimCapabilities.PushToPass |
        SimCapabilities.Drs |
        SimCapabilities.Damage |
        SimCapabilities.PitWindow |
        SimCapabilities.SuspensionTelemetry;

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

                bool waitingToConnect = _state is ConnectionState.Disconnected or ConnectionState.WaitingForSimulator;
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
                TryConnect(events, now);
                break;

            case ConnectionState.Connected:
            case ConnectionState.InSession:
                ProcessConnectedTick(events, now);
                break;

            case ConnectionState.Faulted:
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

        bool inMenus = raw.GameInMenus != 0;
        bool sessionAvailable = raw.SessionType != (int)R3ESessionType.Unavailable;
        bool phasePastCheckered = raw.SessionPhase > (int)R3ESessionPhase.Checkered;

        if (_state == ConnectionState.Connected)
        {
            if (!inMenus && sessionAvailable)
            {
                StartSession(events, now, in raw);
            }

            return;
        }

        // InSession: a tick counter that has stopped advancing for longer than the configured
        // timeout means RaceRoom is no longer writing to the block. Checked before every other
        // boundary rule because a dead game keeps serving the same frame indefinitely, and that
        // frame is (by definition) still on-track, still the same session key, and has ticks that
        // are equal rather than lower — so nothing else here would ever fire.
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

        // InSession: detect the session boundary described on the type's <remarks/>.
        var newKey = new SessionKey(
            R3ETelemetryMapper.DecodeUtf8Name(raw.TrackName),
            R3ETelemetryMapper.DecodeUtf8Name(raw.LayoutName),
            raw.SessionType);

        bool keyChanged = _run.CurrentKey is { } currentKey && currentKey != newKey;
        bool ticksRegressed = !keyChanged && raw.Player.GameSimulationTicks < _run.LastTicks;
        bool sessionBoundary = inMenus || phasePastCheckered || keyChanged || ticksRegressed;

        if (sessionBoundary)
        {
            events.Add(new SessionEnded { OccurredAtUtc = now, SessionId = _run.SessionId });
            _run.ClearSession();
            SetState(ConnectionState.Connected, events, now, reason: null);

            // Same frame may already describe a new (or restarted) session — start it immediately
            // instead of losing a tick, which is what makes an in-session restart look like
            // SessionEnded immediately followed by SessionStarted rather than a multi-tick gap.
            if (!inMenus && sessionAvailable && !phasePastCheckered)
            {
                StartSession(events, now, in raw);
            }

            return;
        }

        EmitSample(events, now, in raw);
    }

    /// <summary>Connected -&gt; InSession: begin tracking a new session and emit its first sample.</summary>
    private void StartSession(List<TelemetryEvent> events, DateTimeOffset now, in R3ESharedRaw raw)
    {
        _run.SessionId = Guid.NewGuid();
        _run.CurrentKey = new SessionKey(
            R3ETelemetryMapper.DecodeUtf8Name(raw.TrackName),
            R3ETelemetryMapper.DecodeUtf8Name(raw.LayoutName),
            raw.SessionType);
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
    }

    /// <summary>InSession, if any -&gt; emits SessionEnded and clears session state.</summary>
    private void EndCurrentSessionIfAny(List<TelemetryEvent> events, DateTimeOffset now)
    {
        if (_state == ConnectionState.InSession)
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
    private static readonly long SimulationTicksOffset =
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

        public void ClearSession()
        {
            SessionId = default;
            CurrentKey = null;
            LastCompletedLaps = 0;
            LastTicks = 0;
            LastTicksChangedAtUtc = default;
            SequenceNumber = 0;
        }
    }

    /// <summary>
    /// The (track, layout, session type) tuple that identifies a session boundary. Deliberately
    /// does not include <see cref="RunState.LastTicks"/> — tick regression is checked separately
    /// so that an in-game "restart session" (identical tuple, ticks reset) is still detected.
    /// </summary>
    private readonly record struct SessionKey(string TrackName, string LayoutName, int RawSessionType);
}
