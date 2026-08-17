import { Link, Outlet, useNavigate, useParams } from '@tanstack/react-router';
import { useCallback, useEffect, useState } from 'react';
import { LapHistoryPanel } from '../laps/LapHistoryPanel';
import { formatSessionType } from '../../shared/format/format';
import { useLive, useRooms, useTower } from '../../shared/live/useLive';
import { TimingTower } from './TimingTower';

/**
 * One session: the timing tower, and whatever focus panel the URL asks for.
 *
 * The room id comes from the path, which is the whole point of the rewrite — a refresh, a
 * bookmark, or a link pasted into a chat all land back on the same session instead of on an empty
 * list.
 */
export function SessionView() {
  const { roomId, driverKey } = useParams({ strict: false });
  const { connection } = useLive();
  const rooms = useRooms();
  const tower = useTower();
  const navigate = useNavigate();

  // Which rows are open. Kept here rather than in the URL: it is a reading aid, not a place — a
  // link that reopened someone else's four expanded rows would be worse than one that did not.
  const [expandedDriverKeys, setExpandedDriverKeys] = useState<ReadonlySet<string>>(
    () => new Set(),
  );

  // Reset during render rather than from an effect, which is React's own advice for state derived
  // from a prop: an effect would paint one frame of the previous session's expanded rows against
  // the new session's tower before correcting itself.
  const [expansionRoomId, setExpansionRoomId] = useState(roomId);
  if (expansionRoomId !== roomId) {
    setExpansionRoomId(roomId);
    setExpandedDriverKeys(new Set());
  }

  useEffect(() => {
    if (roomId !== undefined) {
      connection.watchRoom(roomId);
    }
  }, [connection, roomId]);

  // Both subscriptions are stated here, in this order, rather than the focus one living in
  // `FocusView`. React runs a child's effects before its parent's, so a focus stated from the
  // nested route would be sent first and then wiped by the `watchRoom` above — a driver key only
  // means something inside a room, so watching one necessarily clears the focus. Opening
  // /rooms/x/id:42 directly is exactly the case that gets this wrong, and it is the case the whole
  // URL rewrite exists to serve.
  useEffect(() => {
    connection.focusDriver(driverKey ?? null);
  }, [connection, roomId, driverKey]);

  const toggleExpand = useCallback((driverKey: string) => {
    setExpandedDriverKeys((current) => {
      const next = new Set(current);
      if (!next.delete(driverKey)) {
        next.add(driverKey);
      }

      return next;
    });
  }, []);

  const focusDriver = useCallback(
    (driverKey: string) => {
      if (roomId !== undefined) {
        void navigate({ to: '/rooms/$roomId/$driverKey', params: { roomId, driverKey } });
      }
    },
    [navigate, roomId],
  );

  const room = rooms.find((candidate) => candidate.roomId === roomId) ?? null;

  // The room vanishing out from under a viewer is routine — a session ends, the hub expires the
  // room thirty seconds later. The hub also clears the subscription and says so, so this only has
  // to stop rendering a tower that is no longer being updated.
  const showTower = tower !== null && tower.roomId === roomId;

  return (
    <>
      <nav className="app__breadcrumb">
        <Link className="link-button" to="/">
          ← All sessions
        </Link>
        {room !== null && (
          <span className="app__session">
            {room.trackName} · {room.layoutName} ·{' '}
            {formatSessionType(room.gameKey, room.sessionType)}
          </span>
        )}
      </nav>

      <div className="session">
        <TimingTower
          rows={showTower ? tower.drivers : []}
          focusedDriverKey={driverKey ?? null}
          onFocus={focusDriver}
          expandedDriverKeys={expandedDriverKeys}
          onToggleExpand={toggleExpand}
          renderDetail={(key, sessionBests) => (
            <LapHistoryPanel driverKey={key} sessionBests={sessionBests} />
          )}
        />

        <Outlet />
      </div>
    </>
  );
}
