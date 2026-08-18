import { Link, Outlet, useNavigate, useParams } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import type { TowerRow } from '../../shared/live/contracts';
import { formatDriverKeys, parseDriverKeys, toggleDriverKey } from '../focus/focusDriverKeys';
import { LapHistoryPanel } from '../laps/LapHistoryPanel';
import { formatSessionType, isRaceSession } from '../../shared/format/format';
import { useAge } from '../../shared/format/useAge';
import {
  useConnected,
  useFocusReady,
  useLive,
  useRooms,
  useSessionState,
  useTower,
} from '../../shared/live/useLive';
import { PitWall } from '../wall/PitWall';
import { PitWindowBanner } from './PitWindowBanner';
import { TimingTower } from './TimingTower';
import { TrackMap } from './TrackMap';

const EMPTY_ROWS: TowerRow[] = [];

/**
 * A leaf, so the one-second tick that keeps this honest does not re-render twenty tower rows.
 *
 * The same shape the room list uses for its session ages, and for the same reason — see `useAge`.
 */
function LastUpdated({ atUtc }: { atUtc: string }) {
  return <span className="tower__stamp-age">{useAge(atUtc)}</span>;
}

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
  const connected = useConnected();
  const focusReady = useFocusReady();
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

  // Subscribed but not yet streaming. Derived rather than tracked, so it cannot drift from either
  // half: the URL says who was asked for, and the store says who has answered.
  const pendingDriverKeys = useMemo(
    () => new Set(focusedDriverKeys.filter((key) => !focusReady.has(key))),
    [focusedDriverKeys, focusReady],
  );

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
  //
  // Memoised so the empty case is a stable reference, the same shape `FocusView` uses: a fresh `[]`
  // every render would make everything derived from it recompute on every message the socket
  // delivers, tower or not.
  const rows = useMemo(
    () => (tower !== null && tower.roomId === roomId ? tower.drivers : EMPTY_ROWS),
    [tower, roomId],
  );

  // Room-checked for the same reason the tower is. A session state that outlived a room switch would
  // put the previous race's pit window over this one's tower — and unlike a stale tower row, a banner
  // carries nothing on screen that would give the mistake away.
  const session = sessionState !== null && sessionState.roomId === roomId ? sessionState : null;
  const layoutLengthMeters = session?.layoutLengthMeters ?? null;

  // How a car is named on a wall tile. The same fallback `FocusView` uses: a link can name a driver
  // the tower has not sent yet, and `id:4242` reads better than an empty heading while that
  // resolves.
  const displayName = useMemo(() => {
    const names = new Map(rows.map((row) => [row.driverKey, row.displayName]));
    return (key: string) => names.get(key) ?? key.replace(/^(id|slot|name):/, '');
  }, [rows]);

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

      {/*
        Two regions: timing and the driver comparison on the left, the pit wall on the right. One
        column until there is enough glass for both — see `.session` for where that line is drawn
        and why it is the only breakpoint here.
      */}
      <div className="session">
        <div className="session__left">
          {/*
            Tower and map side by side, wrapping to a stack when the window cannot hold both. The map
            reads the same snapshot the tower does — no new subscription, no new wire field — so the
            two can never disagree about where a car is.
          */}
          <div className="session__timing">
            <div className={`session__tower ${connected ? '' : 'session__tower--stale'}`}>
              {/*
              Where the numbers are, not in the corner. The header's connection light is the only
              thing on screen today that tells a frozen tower from a tower where nobody is
              improving, and it is twelve pixels of muted text a metre from what is being read.
              This says the same thing in the place a gap is being read off, and keeps counting
              while the socket is down — which is exactly when it matters and exactly when no new
              snapshot will arrive to refresh it.
            */}
              {tower !== null && tower.roomId === roomId && (
                <p className="tower__stamp">
                  {connected ? 'Updated' : 'Not updating — last snapshot'}{' '}
                  <LastUpdated atUtc={tower.capturedAtUtc} />
                </p>
              )}

              <TimingTower
                rows={rows}
                focusedDriverKeys={focusedDriverKeys}
                onFocus={toggleFocus}
                pendingDriverKeys={pendingDriverKeys}
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

        {/*
          The wall takes the rest of the glass. Timing and the comparison column are what a race
          engineer must always be able to see, so they hold the left; everything the engineer chose
          to have in front of them goes right, and on a wide monitor that is most of the screen.

          Given the room's whole capability set, flattened across publishers: with two collectors
          feeding one session a widget is offerable if any of them can produce what it needs.
        */}
        <PitWall
          gameKey={room?.gameKey ?? ''}
          capabilities={room?.publishers.flatMap((publisher) => publisher.capabilities) ?? []}
          driverKeys={focusedDriverKeys}
          displayName={displayName}
        />
      </div>
    </>
  );
}
