import { Link } from '@tanstack/react-router';
import { formatAge, formatSessionType } from '../../shared/format/format';
import type { LiveRoomSummary } from '../../shared/live/contracts';

interface RoomListProps {
  rooms: LiveRoomSummary[];
  connected: boolean;
}

/**
 * The landing view: every session currently being published, and who is publishing it.
 *
 * Each session is a `Link`, not a button with a click handler, because the room is part of the URL
 * now — so it can be opened in a new tab, bookmarked, and sent to whoever is asking what the tyres
 * are doing.
 */
export function RoomList({ rooms, connected }: RoomListProps) {
  if (rooms.length === 0) {
    return (
      <div className="empty">
        <h2>No live sessions</h2>
        <p>
          {connected
            ? 'Start a collector with live publishing enabled and its session will appear here.'
            : 'Waiting for the hub…'}
        </p>
      </div>
    );
  }

  return (
    <ul className="rooms">
      {rooms.map((room) => (
        <li key={room.roomId}>
          <Link className="room" to="/rooms/$roomId" params={{ roomId: room.roomId }}>
            <div className="room__track">
              <span className="room__name">{room.trackName}</span>
              <span className="room__layout">{room.layoutName}</span>
            </div>

            <div className="room__meta">
              <span className="pill">{formatSessionType(room.gameKey, room.sessionType)}</span>
              <span className="pill pill--muted">{room.gameKey}</span>
              <span className="room__drivers">
                {room.driverCount} {room.driverCount === 1 ? 'car' : 'cars'}
              </span>
            </div>

            <div className="room__publishers">
              {room.publishers.length === 0 ? (
                // A room with no publishers is one whose collectors have all dropped. It is kept
                // briefly so a reconnect rejoins it rather than starting a new session, so saying
                // so is more useful than showing an empty space.
                <span className="room__idle">No collector connected — expiring shortly</span>
              ) : (
                room.publishers.map((publisher) => (
                  <span key={publisher.clientId} className="publisher">
                    <span className="publisher__dot" aria-hidden="true" />
                    {publisher.driverName ?? publisher.clientName}
                  </span>
                ))
              )}
            </div>

            <span className="room__age">{formatAge(room.lastUpdatedAtUtc)}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
