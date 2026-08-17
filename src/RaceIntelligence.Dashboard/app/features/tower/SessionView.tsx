import { Link, Outlet, useNavigate, useParams } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { formatDriverKeys, parseDriverKeys, toggleDriverKey } from '../focus/focusDriverKeys';
import { LapHistoryPanel } from '../laps/LapHistoryPanel';
import { formatSessionType, isRaceSession } from '../../shared/format/format';
import { useLive, useRooms, useSessionState, useTower } from '../../shared/live/useLive';
import { PitWindowBanner } from './PitWindowBanner';
import { TimingTower } from './TimingTower';
import { TrackMap } from './TrackMap';

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
  const sessionState = useSessionState();
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

  // The drivers the URL asks for. A comma-separated segment, so a comparison is as linkable as a
  // single car — see `focusDriverKeys.ts`.
  const focusedDriverKeys = useMemo(() => parseDriverKeys(driverKey), [driverKey]);

  // Both subscriptions are stated here, in this order, rather than the focus one living in
  // `FocusView`. React runs a child's effects before its parent's, so a focus stated from the
  // nested route would be sent first and then wiped by the `watchRoom` above — a driver key only
  // means something inside a room, so watching one necessarily clears the focus. Opening
  // /rooms/x/id:42 directly is exactly the case that gets this wrong, and it is the case the whole
  // URL rewrite exists to serve.
  //
  // Stated as the whole set rather than one driver at a time: the connection diffs it, so dropping
  // one half of a comparison leaves the other half's stream untouched.
  useEffect(() => {
    connection.focusDrivers(focusedDriverKeys);
  }, [connection, roomId, focusedDriverKeys]);

  const toggleExpand = useCallback((driverKey: string) => {
    setExpandedDriverKeys((current) => {
      const next = new Set(current);
      if (!next.delete(driverKey)) {
        next.add(driverKey);
      }

      return next;
    });
  }, []);

  // Adds the driver to the comparison, or removes them if they are already on screen. The URL is
  // the only place this lives, which is what makes a two-car comparison survive a refresh.
  const toggleFocus = useCallback(
    (clicked: string) => {
      if (roomId === undefined) {
        return;
      }

      const next = toggleDriverKey(focusedDriverKeys, clicked);

      void (next.length === 0
        ? navigate({ to: '/rooms/$roomId', params: { roomId } })
        : navigate({
            to: '/rooms/$roomId/$driverKey',
            params: { roomId, driverKey: formatDriverKeys(next) },
          }));
    },
    [navigate, roomId, focusedDriverKeys],
  );

  const room = rooms.find((candidate) => candidate.roomId === roomId) ?? null;

  // The room vanishing out from under a viewer is routine — a session ends, the hub expires the
  // room thirty seconds later. The hub also clears the subscription and says so, so this only has
  // to stop rendering a tower that is no longer being updated.
  const rows = tower !== null && tower.roomId === roomId ? tower.drivers : [];

  // Room-checked for the same reason the tower is. A session state that outlived a room switch would
  // put the previous race's pit window over this one's tower — and unlike a stale tower row, a banner
  // carries nothing on screen that would give the mistake away.
  const session = sessionState !== null && sessionState.roomId === roomId ? sessionState : null;
  const layoutLengthMeters = session?.layoutLengthMeters ?? null;

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

      {/*
        Above the tower rather than inside it: the window applies to every row at once, and a
        strategist looking for it should not have to find it among thirty cars. Renders nothing
        at all when the session has no mandatory stop.
      */}
      <PitWindowBanner window={session?.pitWindow ?? null} />

      <div className="session">
        {/*
          Tower and map side by side, wrapping to a stack when the window cannot hold both. The map
          reads the same snapshot the tower does — no new subscription, no new wire field — so the
          two can never disagree about where a car is.
        */}
        <div className="session__timing">
          <div className="session__tower">
            <TimingTower
              rows={rows}
              focusedDriverKeys={focusedDriverKeys}
              onFocus={toggleFocus}
              expandedDriverKeys={expandedDriverKeys}
              onToggleExpand={toggleExpand}
              // No room yet means no session type yet, and an unknown session is not a race. The
              // tower then withholds pit state for the first message or two rather than guessing.
              isRace={room !== null && isRaceSession(room.gameKey, room.sessionType)}
              renderDetail={(key, sessionBests) => (
                <LapHistoryPanel
                  driverKey={key}
                  sessionBests={sessionBests}
                  layoutLengthMeters={layoutLengthMeters}
                />
              )}
            />
          </div>

          <TrackMap
            rows={rows}
            focusedDriverKeys={focusedDriverKeys}
            expandedDriverKeys={expandedDriverKeys}
            // The same thing clicking the row's driver button does. Every car on the map has that
            // available — lap history comes from standings, so it works for the whole field — where
            // opening telemetry only works for the few running a collector.
            onSelect={toggleExpand}
          />
        </div>

        <Outlet />
      </div>
    </>
  );
}
