import { createFileRoute } from '@tanstack/react-router';
import { RoomsView } from '../features/rooms/RoomsView';

/**
 * Every session the hub currently knows about.
 *
 * `ssr: false`, like every route here. The room list is whatever the socket says it is a
 * millisecond from now; rendering it on the server would ship a snapshot that is already stale by
 * the time it arrives, and pay a render to do it.
 */
export const Route = createFileRoute('/')({
  ssr: false,
  component: RoomsView,
});
