import { useEffect, useRef } from 'react';
import { formatLapTime, formatSector } from '../../shared/format/format';
import { SECTOR_COUNT, toPerSector } from '../../shared/format/sectors';
import type { TowerRow } from '../../shared/live/contracts';
import { useLapFeed, useLive } from '../../shared/live/useLive';

interface LapFeedPanelProps {
  rows: TowerRow[];
}

/**
 * The room-wide lap feed: every driver's completed laps, in the order each was noticed.
 *
 * Subscribes lap history for every driver currently in the room, not just the one or two being
 * compared — the price of a feed that covers the whole field rather than a driver at a time.
 * Independent of any tower row's own expand-to-inspect subscription: `LiveConnection` refcounts
 * `subscribeLapHistory`/`unsubscribeLapHistory` precisely so this panel and a row's own detail view
 * can both want the same driver without one silencing the other.
 */
export function LapFeedPanel({ rows }: LapFeedPanelProps) {
  const { connection } = useLive();
  const feed = useLapFeed();
  const subscribed = useRef<Set<string>>(new Set());

  useEffect(() => {
    const current = new Set(rows.map((row) => row.driverKey));

    for (const driverKey of current) {
      if (!subscribed.current.has(driverKey)) {
        connection.subscribeLapHistory(driverKey);
      }
    }

    for (const driverKey of subscribed.current) {
      if (!current.has(driverKey)) {
        connection.unsubscribeLapHistory(driverKey);
      }
    }

    subscribed.current = current;
  }, [connection, rows]);

  // Unsubscribes everything still held on unmount — closing the feed tab or leaving the room.
  useEffect(
    () => () => {
      for (const driverKey of subscribed.current) {
        connection.unsubscribeLapHistory(driverKey);
      }
      subscribed.current = new Set();
    },
    [connection],
  );

  if (feed.length === 0) {
    return <p className="laps__note">No laps completed yet.</p>;
  }

  return (
    <div className="lap-feed">
      <table className="lap-feed__table">
        <thead>
          <tr>
            <th>Driver</th>
            <th>Lap</th>
            <th>Time</th>
            <th>S1</th>
            <th>S2</th>
            <th>S3</th>
          </tr>
        </thead>
        <tbody>
          {feed.map((entry) => {
            const perSector = toPerSector(entry.sectorMs);
            const isInvalid = entry.valid === false;

            return (
              <tr key={entry.id} className={isInvalid ? 'laps__row--invalid' : ''}>
                <td className="lap-feed__driver">{entry.displayName}</td>
                <td className="laps__number">{entry.lapNumber}</td>
                <td className={`time ${isInvalid ? 'time--invalid' : ''}`}>
                  {formatLapTime(entry.lapTimeMs)}
                </td>
                {Array.from({ length: SECTOR_COUNT }, (_, i) => (
                  <td key={i} className={`time ${isInvalid ? 'time--invalid' : ''}`}>
                    {formatSector(perSector[i] ?? null)}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
