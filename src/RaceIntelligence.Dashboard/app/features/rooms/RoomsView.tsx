import { useEffect } from 'react';
import { useConnected, useLive, useRooms } from '../../shared/live/useLive';
import { RoomList } from './RoomList';

/**
 * The landing route.
 *
 * Leaving the watched room here rather than on the way out of a session route is what makes the
 * back button work: a viewer who navigates back to the list stops receiving tower snapshots for a
 * session they are no longer looking at, without every session route having to remember to clean
 * up after itself.
 */
export function RoomsView() {
  const { connection } = useLive();
  const rooms = useRooms();
  const connected = useConnected();

  useEffect(() => {
    connection.watchRoom(null);
  }, [connection]);

  return <RoomList rooms={rooms} connected={connected} />;
}
