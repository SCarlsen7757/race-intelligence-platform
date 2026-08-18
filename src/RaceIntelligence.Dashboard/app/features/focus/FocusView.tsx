import { useNavigate, useParams } from '@tanstack/react-router';
import { useMemo } from 'react';
import type { TowerRow } from '../../shared/live/contracts';
import { useTower } from '../../shared/live/useLive';
import { FocusPanel } from './FocusPanel';
import { formatDriverKeys, MAX_FOCUSED_DRIVERS, parseDriverKeys } from './focusDriverKeys';

const EMPTY_ROWS: TowerRow[] = [];

/**
 * The focused drivers, taken from the URL.
 *
 * A driver in the path is what makes "watch this car with me" a link rather than a set of
 * instructions, and a comparison of two is the same promise: the segment names both, so a refresh
 * or a pasted link rebuilds the same pair. It also means the browser's back button closes the
 * panel, which is what everyone tries first.
 *
 * The `focusDriver` subscriptions themselves are stated by `SessionView`, not here — see the
 * comment there for why the ordering against `watchRoom` matters.
 */
export function FocusView() {
  const { roomId, driverKey } = useParams({ strict: false });
  const tower = useTower();
  const navigate = useNavigate();

  const driverKeys = useMemo(() => parseDriverKeys(driverKey), [driverKey]);

  // Memoised so the empty case is a stable reference: recreating `[]` every render would make the
  // two lookups below recompute on every tower message, and every room-list message besides.
  const rows = useMemo(
    () => (tower !== null && tower.roomId === roomId ? tower.drivers : EMPTY_ROWS),
    [tower, roomId],
  );

  // Only a driver whose own machine is publishing has full-rate channels at all, so they are the
  // only ones a comparison can be built from.
  const comparable = useMemo(() => rows.filter((row) => row.tier === 'Self').length, [rows]);

  const displayName = useMemo(() => {
    const names = new Map(rows.map((row) => [row.driverKey, row.displayName]));

    // Falls back to the key with its scheme stripped: a link can name a driver the tower has not
    // sent yet, and `id:4242` reads better than an empty heading while that resolves.
    return (key: string) => names.get(key) ?? key.replace(/^(id|slot|name):/, '');
  }, [rows]);

  if (roomId === undefined || driverKeys.length === 0) {
    return null;
  }

  const close = (closed: string) => {
    const remaining = driverKeys.filter((key) => key !== closed);

    void (remaining.length === 0
      ? navigate({ to: '/rooms/$roomId', params: { roomId } })
      : navigate({
          to: '/rooms/$roomId/$driverKey',
          params: { roomId, driverKey: formatDriverKeys(remaining) },
        }));
  };

  return (
    <FocusPanel
      driverKeys={driverKeys}
      displayName={displayName}
      onClose={close}
      // Said rather than shown as an empty second column. In a session where one person runs a
      // collector there is genuinely nobody to compare against, and a viewer looking for the second
      // "Show" button deserves to be told why there isn't one.
      note={
        driverKeys.length < MAX_FOCUSED_DRIVERS && comparable < 2
          ? 'Nobody else in this session is running a collector, so there is no second car to compare against.'
          : undefined
      }
    />
  );
}
