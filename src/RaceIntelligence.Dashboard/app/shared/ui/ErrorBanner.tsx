import { useLastError, useLive } from '../live/useLive';

/**
 * Shows the hub's answer when a viewer asks for something it cannot have.
 *
 * These exist because silence is ambiguous. A driver with no collector running looks identical in
 * the tower to one whose telemetry has not arrived yet, and a viewer who clicked a row deserves to
 * be told which it is rather than watching an empty panel and wondering.
 */
export function ErrorBanner() {
  const { store } = useLive();
  const error = useLastError();

  if (error === null) {
    return null;
  }

  return (
    <div
      className={`banner banner--${error.code === 'roomClosed' ? 'info' : 'warn'}`}
      role="status"
    >
      <span>{error.message}</span>
      <button type="button" className="link-button" onClick={() => store.clearError()}>
        Dismiss
      </button>
    </div>
  );
}
