import { useEffect, useState } from 'react';
import { formatLapTime, formatSessionType } from '../../shared/format/format';
import type { StoredLap, StoredSample, StoredSession } from '../../shared/history/contracts';
import { fetchLapTelemetry, ReadApiError } from '../../shared/history/client';
import { StoredLapChart } from './StoredLapChart';

interface StoredSessionViewProps {
  session: StoredSession;
  laps: readonly StoredLap[];
  /** Which laps actually have telemetry — not the same list as `laps`. */
  sampledLapNumbers: readonly number[];
}

/**
 * One stored session: what it was, which laps it holds, and a chart of the lap you pick.
 *
 * The session and its lap list come from the route's loader, because they are what the page *is*
 * and it should not render before they arrive. A lap's telemetry does not: it is tens of thousands
 * of samples fetched in response to a click, and putting it in the loader would make choosing a lap
 * a navigation and the first paint wait for a chart nobody had asked for yet.
 */
/**
 * Which lap to open on.
 *
 * The fastest lap that has both a recorded time and telemetry — not the first one, which is almost
 * always the out-lap and the least interesting thing in the session. Real data makes the case: in a
 * practice session of nineteen sampled laps, lap 1 was 164 seconds of leaving the garage, only
 * eight laps had times at all, and the rest were resets and an in-lap.
 *
 * Falls back to the first sampled lap when nothing is timed — a session of nothing but out-laps
 * still has to open on something.
 */
function defaultLap(
  laps: readonly StoredLap[],
  sampledLapNumbers: readonly number[],
): number | null {
  const sampled = new Set(sampledLapNumbers);

  const fastest = laps
    .filter((lap) => lap.lapTimeMs !== undefined && sampled.has(lap.lapNumber))
    .reduce<StoredLap | null>(
      (best, lap) => (best === null || lap.lapTimeMs! < best.lapTimeMs! ? lap : best),
      null,
    );

  return fastest?.lapNumber ?? sampledLapNumbers[0] ?? null;
}

export function StoredSessionView({ session, laps, sampledLapNumbers }: StoredSessionViewProps) {
  const [selected, setSelected] = useState<number | null>(() =>
    defaultLap(laps, sampledLapNumbers),
  );

  /**
   * The lap that has finished loading, tagged with which lap it is.
   *
   * One piece of state carrying its own identity, rather than a `samples` and a separate `loading`
   * flag reset at the top of the effect. Resetting synchronously there is a cascading render, and
   * more importantly it is a second source of truth: "which lap is on screen" would be answerable
   * two ways and they would disagree for a frame. Here, loading is simply `loaded.lapNumber !==
   * selected` — a derivation, so it cannot drift.
   */
  const [loaded, setLoaded] = useState<{
    lapNumber: number;
    samples: readonly StoredSample[];
  } | null>(null);
  const [error, setError] = useState<{ lapNumber: number; message: string } | null>(null);

  useEffect(() => {
    if (selected === null) {
      return;
    }

    // Aborted on change so a fast series of clicks cannot land an earlier lap's samples after a
    // later one's and chart the wrong lap.
    const controller = new AbortController();

    fetchLapTelemetry(session.sessionId, selected, controller.signal)
      .then((lap) => setLoaded({ lapNumber: selected, samples: lap.samples }))
      .catch((cause: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        // The read API's problem detail is written to be shown — a lap over the sample cap explains
        // itself far better than "request failed" would.
        setError({
          lapNumber: selected,
          message: cause instanceof ReadApiError ? cause.message : 'Could not load that lap.',
        });
      });

    return () => controller.abort();
  }, [session.sessionId, selected]);

  const samples = loaded !== null && loaded.lapNumber === selected ? loaded.samples : null;
  const lapError = error !== null && error.lapNumber === selected ? error.message : null;

  const lapsByNumber = new Map(laps.map((lap) => [lap.lapNumber, lap]));

  return (
    <div className="stored-session">
      <header className="session__header">
        <h2>
          {session.trackName ?? 'Unknown track'}
          {session.layoutName === undefined ? '' : ` — ${session.layoutName}`}
        </h2>
        <div className="room__meta">
          <span className="pill">{formatSessionType('raceroom', session.sessionType)}</span>
          {session.carName === undefined ? null : (
            <span className="pill pill--muted">{session.carName}</span>
          )}
          <span className="room__drivers">
            {session.playerName ?? session.driverName ?? 'Unknown driver'}
          </span>
        </div>
      </header>

      {sampledLapNumbers.length === 0 ? (
        <div className="empty">
          <h2>No telemetry stored</h2>
          <p>
            This session recorded {session.lapCount} laps but no samples, so there is nothing to
            chart.
          </p>
        </div>
      ) : (
        <>
          <ul className="lap-picker">
            {sampledLapNumbers.map((lapNumber) => {
              const lap = lapsByNumber.get(lapNumber);
              return (
                <li key={lapNumber}>
                  <button
                    type="button"
                    className={`lap-picker__lap${lapNumber === selected ? ' lap-picker__lap--selected' : ''}`}
                    aria-pressed={lapNumber === selected}
                    onClick={() => setSelected(lapNumber)}
                  >
                    <span className="lap-picker__number">Lap {lapNumber}</span>
                    {/* A lap with samples need not have a lap row — see the read API's note on why
                        these two lists diverge — so the time is absent rather than zero. */}
                    <span className="lap-picker__time">
                      {lap === undefined ? '—' : formatLapTime(lap.lapTimeMs)}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>

          {lapError !== null ? (
            <div className="empty">
              <h2>Could not load lap {selected}</h2>
              <p>{lapError}</p>
            </div>
          ) : samples === null ? (
            <div className="empty">
              <p>Loading lap {selected}…</p>
            </div>
          ) : (
            <StoredLapChart samples={samples} />
          )}
        </>
      )}
    </div>
  );
}
