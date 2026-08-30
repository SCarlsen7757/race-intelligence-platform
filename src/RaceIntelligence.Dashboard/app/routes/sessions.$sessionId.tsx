import { createFileRoute } from '@tanstack/react-router';
import { fetchLaps, fetchSampledLapNumbers, fetchSession } from '../shared/history/client';
import { StoredSessionView } from '../features/history/StoredSessionView';

/**
 * One stored session.
 *
 * The loader fetches what the page *is* — the session, its laps, and which laps can be charted —
 * and deliberately not any telemetry. A lap is tens of thousands of samples; loading one here would
 * make the first paint wait for a chart the reader has not chosen yet, and make choosing a
 * different lap a navigation.
 *
 * The three run together rather than in sequence: none depends on another's result, and awaiting
 * them one at a time would spend three round trips to answer one question.
 */
export const Route = createFileRoute('/sessions/$sessionId')({
  ssr: false,
  loader: async ({ params }) => {
    const [session, laps, sampledLapNumbers] = await Promise.all([
      fetchSession(params.sessionId),
      fetchLaps(params.sessionId),
      fetchSampledLapNumbers(params.sessionId),
    ]);

    return { session, laps, sampledLapNumbers };
  },
  component: StoredSessionRoute,
});

function StoredSessionRoute() {
  const { session, laps, sampledLapNumbers } = Route.useLoaderData();

  return <StoredSessionView session={session} laps={laps} sampledLapNumbers={sampledLapNumbers} />;
}
