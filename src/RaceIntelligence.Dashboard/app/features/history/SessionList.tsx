import { Link } from '@tanstack/react-router';
import { formatSessionType } from '../../shared/format/format';
import { useAge } from '../../shared/format/useAge';
import type { StoredSession } from '../../shared/history/contracts';

interface SessionListProps {
  sessions: readonly StoredSession[];
}

/**
 * A leaf so the shared one-second tick only repaints the age text, not the whole card around it.
 * Same reasoning as the room list's, and the same component shape.
 */
function Age({ atUtc }: { atUtc: string }) {
  return <span className="room__age">{useAge(atUtc)}</span>;
}

/**
 * Sessions that have already happened.
 *
 * Presentational, and the deliberate twin of `features/rooms/RoomList` — same markup vocabulary,
 * same `Link`-per-card shape, so a stored session and a live one read as the same kind of thing in
 * two tenses rather than as two designs.
 *
 * The one addition is the sample count, which decides whether a session is worth opening at all: a
 * session can hold laps and no telemetry, and a card that did not say so would offer a page with
 * nothing on it.
 */
export function SessionList({ sessions }: SessionListProps) {
  if (sessions.length === 0) {
    return (
      <div className="empty">
        <h2>No stored sessions</h2>
        <p>Sessions appear here once a collector has uploaded one.</p>
      </div>
    );
  }

  return (
    <ul className="rooms">
      {sessions.map((session) => (
        <li key={session.sessionId}>
          <Link
            className="room"
            to="/sessions/$sessionId"
            params={{ sessionId: session.sessionId }}
          >
            <div className="room__track">
              <span className="room__name">{session.trackName ?? 'Unknown track'}</span>
              <span className="room__layout">{session.layoutName ?? ''}</span>
            </div>

            <div className="room__meta">
              <span className="pill">{formatSessionType('raceroom', session.sessionType)}</span>
              {session.carName === undefined ? null : (
                <span className="pill pill--muted">{session.carName}</span>
              )}
              <span className="room__drivers">
                {session.lapCount} {session.lapCount === 1 ? 'lap' : 'laps'}
              </span>
            </div>

            <div className="room__publishers">
              {session.sampleCount === 0 ? (
                // Worth saying rather than leaving to be discovered by opening it. A session with
                // laps but no samples is a real thing — telemetry upload that never caught up —
                // and it charts nothing.
                <span className="room__idle">No telemetry stored</span>
              ) : (
                <span className="publisher">
                  {/* playerName is the name in use at the time; driverName tracks renames. */}
                  {session.playerName ?? session.driverName ?? 'Unknown driver'}
                </span>
              )}
            </div>

            <Age atUtc={session.startedAtUtc} />
          </Link>
        </li>
      ))}
    </ul>
  );
}
