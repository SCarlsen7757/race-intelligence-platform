import { useEffect } from 'react';
import type { SessionBests } from '../../shared/format/sectors';
import { useLapHistory, useLive } from '../../shared/live/useLive';
import { LapHistory } from './LapHistory';

interface LapHistoryPanelProps {
  driverKey: string;
  sessionBests: SessionBests;
}

/**
 * The half of the lap-history view that talks to the hub.
 *
 * Subscribing on mount and unsubscribing on unmount is what ties the subscription to the expanded
 * row: collapsing the row unmounts this, which tells the hub to stop building a snapshot per lap
 * for something nobody is looking at. Several of these can be mounted at once — the hub conflates
 * per driver — which is what makes comparing two stints side by side possible.
 */
export function LapHistoryPanel({ driverKey, sessionBests }: LapHistoryPanelProps) {
  const { connection } = useLive();
  const history = useLapHistory(driverKey);

  useEffect(() => {
    connection.subscribeLapHistory(driverKey);
    return () => connection.unsubscribeLapHistory(driverKey);
  }, [connection, driverKey]);

  return (
    <LapHistory
      laps={history?.laps ?? null}
      truncated={history?.truncated ?? false}
      sessionBests={sessionBests}
    />
  );
}
