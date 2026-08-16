import type { LiveRoomSummary } from '../live/contracts';
import { formatAge, formatSessionType } from '../live/format';

interface RoomListProps {
  rooms: LiveRoomSummary[];
  connected: boolean;
  onWatch: (roomId: string) => void;
}

/** The landing view: every session currently being published, and who is publishing it. */
export function RoomList({ rooms, connected, onWatch }: RoomListProps) {
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
          <button type="button" className="room" onClick={() => onWatch(room.roomId)}>
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
          </button>
        </li>
      ))}
    </ul>
  );
}
