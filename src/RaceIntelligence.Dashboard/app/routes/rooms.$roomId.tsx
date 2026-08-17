import { createFileRoute } from '@tanstack/react-router';
import { SessionView } from '../features/tower/SessionView';

/** One session's timing tower, with the focus panel nested underneath it. */
export const Route = createFileRoute('/rooms/$roomId')({
  ssr: false,
  component: SessionView,
});
