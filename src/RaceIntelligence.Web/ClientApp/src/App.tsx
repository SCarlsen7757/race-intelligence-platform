import { useCallback, useState } from 'react';
import { ErrorBanner } from './components/ErrorBanner';
import { FocusPanel } from './components/FocusPanel';
import { RoomList } from './components/RoomList';
import { TimingTower } from './components/TimingTower';
import { formatSessionType } from './live/format';
import { useConnected, useLive, useRooms, useTower } from './live/useLive';

export function App() {
  const { connection } = useLive();
  const rooms = useRooms();
  const tower = useTower();
  const connected = useConnected();

  const [roomId, setRoomId] = useState<string | null>(null);
  const [focusedDriverKey, setFocusedDriverKey] = useState<string | null>(null);

  const watchRoom = useCallback(
    (id: string | null) => {
      setRoomId(id);
      setFocusedDriverKey(null);
      connection.watchRoom(id);
    },
    [connection],
  );

  const focusDriver = useCallback(
    (driverKey: string | null) => {
      setFocusedDriverKey(driverKey);
      connection.focusDriver(driverKey);
    },
    [connection],
  );

  const room = rooms.find((candidate) => candidate.roomId === roomId) ?? null;

  // The room vanishing out from under a viewer is routine — a session ends, the hub expires the
  // room thirty seconds later. The hub also clears the subscription and says so, so this only has
  // to stop rendering a tower that is no longer being updated.
  const showTower = room !== null && tower !== null && tower.roomId === room.roomId;

  return (
    <div className="app">
      <header className="app__header">
        <h1 className="app__title">Race Intelligence</h1>

        {room !== null && (
          <nav className="app__breadcrumb">
            <button type="button" className="link-button" onClick={() => watchRoom(null)}>
              ← All sessions
            </button>
            <span className="app__session">
              {room.trackName} · {room.layoutName} · {formatSessionType(room.gameKey, room.sessionType)}
            </span>
          </nav>
        )}

        <span className={`status status--${connected ? 'live' : 'offline'}`}>
          {connected ? 'Connected' : 'Reconnecting…'}
        </span>
      </header>

      <ErrorBanner />

      <main className="app__main">
        {room === null ? (
          <RoomList rooms={rooms} onWatch={watchRoom} connected={connected} />
        ) : (
          <div className="session">
            <TimingTower
              rows={showTower ? tower.drivers : []}
              focusedDriverKey={focusedDriverKey}
              onFocus={focusDriver}
            />
            {focusedDriverKey !== null && (
              <FocusPanel
                driverKey={focusedDriverKey}
                capabilities={room.publishers.flatMap((publisher) => publisher.capabilities)}
                gameKey={room.gameKey}
                onClose={() => focusDriver(null)}
              />
            )}
          </div>
        )}
      </main>
    </div>
  );
}
