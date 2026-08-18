import { Link, useParams } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import type { TowerRow } from '../../shared/live/contracts';
import { FocusPanel } from '../focus/FocusPanel';
import { MAX_FOCUSED_DRIVERS, toggleDriverKey } from '../focus/focusDriverKeys';
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
 * One session: the timing tower, the cars being compared, and the wall of widgets beside them.
 *
 * The room id comes from the path, and now nothing else does. A refresh or a pasted link lands
 * back on the same session; which cars are being watched is state belonging to this room, and the
 * arrangement of the wall is a document belonging to the simulator.
 *
 * **This is the only place the follow set is stated**, and it is derived rather than tracked: the
 * cars in the comparison are the cars followed, so there is no separate act of focusing that could
 * disagree with what is on screen.
 */
export function SessionView() {
  const { roomId } = useParams({ strict: false });
  const { connection } = useLive();
  const rooms = useRooms();
  const tower = useTower();
  const sessionState = useSessionState();
  const connected = useConnected();
  const focusReady = useFocusReady();

  // Which rows are open. A reading aid rather than a place, which is why it was never in the URL
  // even when the drivers were.
  const [expandedDriverKeys, setExpandedDriverKeys] = useState<ReadonlySet<string>>(
    () => new Set(),
  );

  /**
   * The cars in the comparison, in the order they were opened, and which of them is current.
   *
   * Per room, and deliberately not persisted with the wall: a driver key names one car in one
   * session, whereas a wall is opened against every session of that simulator. The wall stores
   * positions; this is what those positions point at, and it lives and dies with the room.
   *
   * The selection is always one of the compared cars, never a third. That is what keeps the follow
   * set inside the hub's cap without any arithmetic: a `'selected'` widget can only ever be showing
   * a car that is already being watched, so selecting never opens a stream.
   */
  const [comparedDriverKeys, setComparedDriverKeys] = useState<readonly string[]>([]);
  const [selectedDriverKey, setSelectedDriverKey] = useState<string | null>(null);

  // Reset during render rather than from an effect, which is React's own advice for state derived
  // from a prop: an effect would paint one frame of the previous session's cars against the new
  // session's tower before correcting itself.
  const [expansionRoomId, setExpansionRoomId] = useState(roomId);
  if (expansionRoomId !== roomId) {
    setExpansionRoomId(roomId);
    setExpandedDriverKeys(new Set());
    setComparedDriverKeys([]);
    setSelectedDriverKey(null);
  }

  useEffect(() => {
    if (roomId !== undefined) {
      connection.watchRoom(roomId);
    }
  }, [connection, roomId]);

  // Subscribed but not yet streaming. Derived rather than tracked, so it cannot drift from either
  // half: the comparison says who was asked for, and the store says who has answered.
  const pendingDriverKeys = useMemo(
    () => new Set(comparedDriverKeys.filter((key) => !focusReady.has(key))),
    [comparedDriverKeys, focusReady],
  );

  // Both subscriptions are stated here, in this order, and the order still matters even though the
  // nested route that made it subtle is gone. A driver key only means something inside a room, so
  // `watchRoom` necessarily clears the focus on both sides of the socket; a follow set stated
  // before it would be sent and then wiped.
  //
  // Stated as the whole set rather than one driver at a time, because `focusDrivers` diffs it. That
  // is what makes rearranging the wall free: nothing here calls `resetFocus`, so dropping one car
  // of a comparison sends a single `unfocusDriver` and leaves the other car's rings — and the stint
  // being read out of them — completely untouched.
  useEffect(() => {
    connection.focusDrivers(comparedDriverKeys);
  }, [connection, roomId, comparedDriverKeys]);

  const toggleExpand = useCallback((driverKey: string) => {
    setExpandedDriverKeys((current) => {
      const next = new Set(current);
      if (!next.delete(driverKey)) {
        next.add(driverKey);
      }

      return next;
    });
  }, []);

  /**
   * Adds the driver to the comparison, or removes them if they are already on screen.
   *
   * The selection follows, because opening a car is the clearest possible statement that it is the
   * one you want to look at — and every `'selected'` widget on the wall swings to it in the same
   * click. Closing the current car hands the selection to whoever is left rather than leaving those
   * widgets pointing at nobody.
   */
  const toggleFocus = useCallback(
    (clicked: string) => {
      const next = toggleDriverKey(comparedDriverKeys, clicked);

      // Computed from the current values rather than inside the setters' updaters: an updater must
      // be a pure function of the previous state, and React is free to run it twice.
      setComparedDriverKeys(next);
      setSelectedDriverKey(
        next.includes(clicked)
          ? clicked
          : selectedDriverKey !== null && next.includes(selectedDriverKey)
            ? selectedDriverKey
            : (next[0] ?? null),
      );
    },
    [comparedDriverKeys, selectedDriverKey],
  );

  /**
   * Picks which of the watched cars the `'selected'` widgets are about.
   *
   * Refuses a car that is not being watched, which is what confines the selection to the compared
   * set — and therefore what stops it from ever being a stream the hub has not been asked for.
   */
  const selectDriver = useCallback(
    (driverKey: string) => {
      if (comparedDriverKeys.includes(driverKey)) {
        setSelectedDriverKey(driverKey);
      }
    },
    [comparedDriverKeys],
  );

  const room = rooms.find((candidate) => candidate.roomId === roomId) ?? null;

  // The room vanishing out from under a viewer is routine — a session ends, the hub expires the
  // room thirty seconds later. The hub also clears the subscription and says so, so this only has
  // to stop rendering a tower that is no longer being updated.
  //
  // Memoised so the empty case is a stable reference: a fresh `[]` every render would make
  // everything derived from it recompute on every message the socket delivers, tower or not.
  const rows = useMemo(
    () => (tower !== null && tower.roomId === roomId ? tower.drivers : EMPTY_ROWS),
    [tower, roomId],
  );

  // Room-checked for the same reason the tower is. A session state that outlived a room switch would
  // put the previous race's pit window over this one's tower — and unlike a stale tower row, a banner
  // carries nothing on screen that would give the mistake away.
  const session = sessionState !== null && sessionState.roomId === roomId ? sessionState : null;
  const layoutLengthMeters = session?.layoutLengthMeters ?? null;

  // How a car is named, on a wall tile and in the comparison column. Falls back to the key with its
  // scheme stripped: a car can be opened before the tower has named it, and `id:4242` reads better
  // than an empty heading while that resolves.
  const displayName = useMemo(() => {
    const names = new Map(rows.map((row) => [row.driverKey, row.displayName]));
    return (key: string) => names.get(key) ?? key.replace(/^(id|slot|name):/, '');
  }, [rows]);

  // Only a driver whose own machine is publishing has full-rate channels at all, so they are the
  // only ones a comparison can be built from.
  const comparable = useMemo(() => rows.filter((row) => row.tier === 'Self').length, [rows]);

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
                focusedDriverKeys={comparedDriverKeys}
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
              focusedDriverKeys={comparedDriverKeys}
              expandedDriverKeys={expandedDriverKeys}
              // The same thing clicking the row's driver button does. Every car on the map has that
              // available — lap history comes from standings, so it works for the whole field — where
              // opening telemetry only works for the few running a collector.
              onSelect={toggleExpand}
            />
          </div>

          {comparedDriverKeys.length > 0 && (
            <FocusPanel
              driverKeys={comparedDriverKeys}
              selectedDriverKey={selectedDriverKey}
              onSelect={selectDriver}
              displayName={displayName}
              onClose={toggleFocus}
              // Said rather than shown as an empty second column. In a session where one person
              // runs a collector there is genuinely nobody to compare against, and a viewer looking
              // for the second "Show" button deserves to be told why there isn't one.
              note={
                comparedDriverKeys.length < MAX_FOCUSED_DRIVERS && comparable < 2
                  ? 'Nobody else in this session is running a collector, so there is no second car to compare against.'
                  : undefined
              }
            />
          )}
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
          comparedDriverKeys={comparedDriverKeys}
          selectedDriverKey={selectedDriverKey}
          displayName={displayName}
        />
      </div>
    </>
  );
}
