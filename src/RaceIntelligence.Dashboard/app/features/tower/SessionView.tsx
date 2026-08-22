import { Link, useParams } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import type { TowerRow } from '../../shared/live/contracts';
import { LapFeedPanel } from '../laps/LapFeedPanel';
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
const NO_DRIVERS: readonly string[] = [];

/**
 * A leaf, so the one-second tick that keeps this honest does not re-render twenty tower rows.
 *
 * The same shape the room list uses for its session ages, and for the same reason — see `useAge`.
 */
function LastUpdated({ atUtc }: { atUtc: string }) {
  return <span className="tower__stamp-age">{useAge(atUtc)}</span>;
}

/**
 * One session, in one of two states.
 *
 * **Nothing selected**: the timing tower is the whole interface and fills the screen. That is not a
 * degraded version of the page — it is what someone wants on joining a session, before they have
 * decided which car to look at, and for a strategist watching a whole field it may be the only
 * state they ever use.
 *
 * **A car selected**: the tower moves to the left column and the pit wall appears beside it. The
 * wall's widgets are all about that car; selecting a different one swings every tile at once.
 *
 * The room id comes from the path, and nothing else does. Which car is selected is state belonging
 * to this room; the arrangement of the wall is a document belonging to the simulator.
 *
 * **This is the only place the follow set is stated**, and it is derived rather than tracked: the
 * selected car is the followed car, so there is no separate act of focusing that could disagree
 * with what is on screen.
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
   * The car being looked at, or none.
   *
   * One car. Several at once, overlaid on a chart and told apart by colour, is the eventual shape
   * and the widgets are built to take it — but there is one collector publishing today, and a
   * comparison of one car against nobody is a feature nothing can exercise.
   *
   * Per room, and deliberately never persisted with the wall: a driver key names one car in one
   * session and nobody in the next, whereas a wall is opened against every session of that
   * simulator. Keeping it here is what lets the saved document name no car at all.
   *
   * One selection also means the follow set is at most one stream, which is comfortably inside the
   * hub's cap on how many cars a viewer may follow at full rate — see `LiveViewer.MaxFocusDrivers`
   * for why that cap exists and why the client should never construct a request that trips it.
   */
  const [selectedDriverKey, setSelectedDriverKey] = useState<string | null>(null);

  // Which view the left column shows: the tower and track map, or the room-wide lap feed. A UI
  // mode rather than data, but reset alongside the rest below anyway — a feed left open across a
  // room switch would read as this room's laps for a moment before the new subscriptions land.
  const [leftView, setLeftView] = useState<'timing' | 'feed'>('timing');

  // Reset during render rather than from an effect, which is React's own advice for state derived
  // from a prop: an effect would paint one frame of the previous session's car against the new
  // session's tower before correcting itself.
  const [expansionRoomId, setExpansionRoomId] = useState(roomId);
  if (expansionRoomId !== roomId) {
    setExpansionRoomId(roomId);
    setExpandedDriverKeys(new Set());
    setSelectedDriverKey(null);
    setLeftView('timing');
  }

  useEffect(() => {
    if (roomId !== undefined) {
      connection.watchRoom(roomId);
    }
  }, [connection, roomId]);

  // The follow set, derived. A list of none or one, which is the whole model.
  const followedDriverKeys = useMemo(
    () => (selectedDriverKey === null ? NO_DRIVERS : [selectedDriverKey]),
    [selectedDriverKey],
  );

  // Subscribed but not yet streaming. Derived rather than tracked, so it cannot drift from either
  // half: the selection says who was asked for, and the store says who has answered.
  const pendingDriverKeys = useMemo(
    () => new Set(followedDriverKeys.filter((key) => !focusReady.has(key))),
    [followedDriverKeys, focusReady],
  );

  // Both subscriptions are stated here, in this order, and the order still matters even though the
  // nested route that made it subtle is gone. A driver key only means something inside a room, so
  // `watchRoom` necessarily clears the focus on both sides of the socket; a follow set stated
  // before it would be sent and then wiped.
  //
  // Stated as the whole set rather than one driver at a time, because `focusDrivers` diffs it.
  // Nothing here calls `resetFocus`, so rearranging the wall — which does not touch this at all —
  // and switching cars both leave every ring the store holds exactly where it was.
  useEffect(() => {
    connection.focusDrivers(followedDriverKeys);
  }, [connection, roomId, followedDriverKeys]);

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
   * Opens a car's telemetry, or closes it if it is the one already open.
   *
   * Selecting a second car replaces the first rather than joining it. That is the single-car model
   * stated in one line, and it is why there is no cap arithmetic anywhere in this file.
   */
  const toggleFocus = useCallback((clicked: string) => {
    setSelectedDriverKey((current) => (current === clicked ? null : clicked));
  }, []);

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

  // How a car is named on the wall's heading. Falls back to the key with its scheme stripped: a car
  // can be opened before the tower has named it, and `id:4242` reads better than an empty heading
  // while that resolves.
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
        Two states, one class apart. With no car open the tower has the whole page and the grid is a
        single column; open one and the layout splits, on a wide enough monitor. The modifier is
        what carries that, because the split is not a function of the viewport alone — a 4K screen
        showing nobody's telemetry should still be a full-width tower rather than a tower squeezed
        into 560 pixels beside an empty half.
      */}
      <div className={`session ${selectedDriverKey === null ? '' : 'session--split'}`}>
        {/*
          The tower takes the height and the map is pinned under it — two rows, not the wrapping row
          this used to be. The map reads the same snapshot the tower does, so the two can never
          disagree about where a car is, and keeping it always visible is the point of pinning it:
          it used to travel down the page with the bottom of a thirty-car field.
        */}
        <div className="session__left">
          {/*
            The tower/track-map pair and the room-wide lap feed are two views of the same left
            column, not two panels stacked — see #70. Both read the room rather than one car, which
            is exactly why neither ever joined the wall on the right: a widget there is scoped to
            `selectedDriverKey`, and these aren't.
          */}
          <div className="session__left-tabs" role="tablist" aria-label="Left column view">
            <button
              type="button"
              role="tab"
              aria-selected={leftView === 'timing'}
              className={`session__left-tab ${
                leftView === 'timing' ? 'session__left-tab--active' : ''
              }`}
              onClick={() => setLeftView('timing')}
            >
              Timing
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={leftView === 'feed'}
              className={`session__left-tab ${
                leftView === 'feed' ? 'session__left-tab--active' : ''
              }`}
              onClick={() => setLeftView('feed')}
            >
              Lap feed
            </button>
          </div>

          {leftView === 'timing' ? (
            <>
              <div className={`session__tower ${connected ? '' : 'session__tower--stale'}`}>
                {/*
                  Where the numbers are, not in the corner. The header's connection light is the only
                  thing on screen today that tells a frozen tower from a tower where nobody is
                  improving, and it is twelve pixels of muted text a metre from what is being read.
                  This says the same thing in the place a gap is being read off, and keeps counting
                  while the socket is down — which is exactly when it matters and exactly when no new
                  snapshot will arrive to refresh it.

                  Outside the scroll box below, so it cannot scroll away from the tower it describes.
                */}
                {tower !== null && tower.roomId === roomId && (
                  <p className="tower__stamp">
                    {connected ? 'Updated' : 'Not updating — last snapshot'}{' '}
                    <LastUpdated atUtc={tower.capturedAtUtc} />
                  </p>
                )}

                <div className="session__tower-scroll">
                  <TimingTower
                    rows={rows}
                    focusedDriverKeys={followedDriverKeys}
                    onFocus={toggleFocus}
                    pendingDriverKeys={pendingDriverKeys}
                    expandedDriverKeys={expandedDriverKeys}
                    onToggleExpand={toggleExpand}
                    // No room yet means no session type yet, and an unknown session is not a race.
                    // The tower then withholds pit state for the first message or two rather than
                    // guessing.
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
              </div>

              <TrackMap
                rows={rows}
                focusedDriverKeys={followedDriverKeys}
                expandedDriverKeys={expandedDriverKeys}
                // The same thing clicking the row's driver button does. Every car on the map has
                // that available — lap history comes from standings, so it works for the whole
                // field — where opening telemetry only works for the few running a collector.
                onSelect={toggleExpand}
              />
            </>
          ) : (
            <LapFeedPanel rows={rows} />
          )}
        </div>

        {/*
          The wall takes the rest of the glass, and only exists once there is a car for it to be
          about. Mounted rather than emptied, because a wall with no driver is not a wall of empty
          tiles — it is a page that should be showing the tower, which is exactly what the state
          above it does.

          Given the room's whole capability set, flattened across publishers: with two collectors
          feeding one session a widget is offerable if any of them can produce what it needs.
        */}
        {selectedDriverKey !== null && (
          <PitWall
            gameKey={room?.gameKey ?? ''}
            capabilities={room?.publishers.flatMap((publisher) => publisher.capabilities) ?? []}
            driverKey={selectedDriverKey}
            displayName={displayName}
          />
        )}
      </div>
    </>
  );
}
