import { useNavigate, useParams } from '@tanstack/react-router';
import { useRooms } from '../../shared/live/useLive';
import { FocusPanel } from './FocusPanel';

/**
 * The focused driver, taken from the URL.
 *
 * A driver in the path is what makes "watch this car with me" a link rather than a set of
 * instructions. It also means the browser's back button closes the panel, which is what everyone
 * tries first.
 *
 * The `focusDriver` subscription itself is stated by `SessionView`, not here — see the comment
 * there for why the ordering against `watchRoom` matters.
 */
export function FocusView() {
  const { roomId, driverKey } = useParams({ strict: false });
  const rooms = useRooms();
  const navigate = useNavigate();

  if (roomId === undefined || driverKey === undefined) {
    return null;
  }

  const room = rooms.find((candidate) => candidate.roomId === roomId) ?? null;

  return (
    <FocusPanel
      driverKey={driverKey}
      gameKey={room?.gameKey ?? ''}
      // Flattened from every publisher in the room: with two collectors feeding one session, a
      // panel is showable if any of them can produce what it needs.
      capabilities={room?.publishers.flatMap((publisher) => publisher.capabilities) ?? []}
      onClose={() => void navigate({ to: '/rooms/$roomId', params: { roomId } })}
    />
  );
}
