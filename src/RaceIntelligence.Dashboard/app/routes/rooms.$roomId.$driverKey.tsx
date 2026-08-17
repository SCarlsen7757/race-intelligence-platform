import { createFileRoute } from '@tanstack/react-router';
import { FocusView } from '../features/focus/FocusView';

/** One driver's full-rate telemetry, nested inside their session so the tower stays on screen. */
export const Route = createFileRoute('/rooms/$roomId/$driverKey')({
  ssr: false,
  component: FocusView,
});
