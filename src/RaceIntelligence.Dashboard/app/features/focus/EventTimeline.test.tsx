import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { RaceRoomExtras } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { EventTimeline } from './EventTimeline';

const DRIVER = 'id:3';

/**
 * Feeds a sequence of extras documents a second apart, as the collector would.
 *
 * The whole point of the store's event recording is that it compares each document against the last
 * one, so a test that fed a single document could never see a transition at all.
 */
function drive(documents: RaceRoomExtras[]) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);

  documents.forEach((document, index) => {
    store.apply({
      type: 'extrasFrame',
      roomId: 'room-1',
      driverKey: DRIVER,
      capturedAtUtc: new Date(Date.UTC(2026, 7, 19, 12, 0, index)).toISOString(),
      extras: JSON.stringify(document),
    });
  });

  return store;
}

function renderTimeline(documents: RaceRoomExtras[]) {
  const store = drive(documents);

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <EventTimeline store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('the event timeline', () => {
  it('says so before anything has happened', () => {
    const view = renderTimeline([{ flags: { yellow: 0 } }]);

    expect(view.getByText(/No flags, activations or incidents yet/)).toBeTruthy();
  });

  /**
   * A viewer opening the dashboard under a yellow has not just watched it come out. The first
   * document establishes what is already true and announces none of it.
   */
  it('does not announce what was already true when the viewer joined', () => {
    const view = renderTimeline([{ flags: { yellow: 1 } }, { flags: { yellow: 1 } }]);

    expect(view.queryByText('Yellow flag')).toBeNull();
  });

  it('records a flag when it comes out, once rather than once a second', () => {
    const view = renderTimeline([
      { flags: { yellow: 0 } },
      { flags: { yellow: 1 } },
      { flags: { yellow: 1 } },
      { flags: { yellow: 1 } },
    ]);

    expect(view.getAllByText('Yellow flag')).toHaveLength(1);
  });

  /** A second yellow raised while the first still stands is a second event: the count went up. */
  it('records a second flag raised while the first still stands', () => {
    const view = renderTimeline([
      { flags: { yellow: 0 } },
      { flags: { yellow: 1 } },
      { flags: { yellow: 2 } },
    ]);

    expect(view.getAllByText('Yellow flag')).toHaveLength(2);
  });

  /**
   * `amountLeft` counts activations *remaining* and falls as they are spent, so it would never
   * register on an increase test. Engagement is what the store watches instead.
   */
  it('records each push-to-pass activation as it is engaged', () => {
    const view = renderTimeline([
      { pushToPass: { engaged: 0, amountLeft: 3 } },
      { pushToPass: { engaged: 1, amountLeft: 2 } },
      { pushToPass: { engaged: 0, amountLeft: 2 } },
      { pushToPass: { engaged: 1, amountLeft: 1 } },
    ]);

    expect(view.getAllByText('Push to pass')).toHaveLength(2);
  });

  /** `-1` is the simulator's "not available", and an unreported channel is not an event. */
  it('does not read the not-available sentinel as something happening', () => {
    const view = renderTimeline([
      { incidentPoints: -1, drs: { engaged: -1 } },
      { incidentPoints: -1, drs: { engaged: -1 } },
    ]);

    expect(view.getByText(/No flags, activations or incidents yet/)).toBeTruthy();
  });

  it('orders the newest first, with a clock relative to the first event seen', () => {
    const view = renderTimeline([
      { flags: { yellow: 0 }, incidentPoints: 0 },
      { flags: { yellow: 1 }, incidentPoints: 0 },
      { flags: { yellow: 1 }, incidentPoints: 2 },
    ]);

    const labels = [...view.container.querySelectorAll('.event-timeline__label')].map(
      (node) => node.textContent,
    );

    expect(labels).toEqual(['Incident points', 'Yellow flag']);

    const times = [...view.container.querySelectorAll('.event-timeline__at')].map(
      (node) => node.textContent,
    );

    expect(times).toEqual(['0:01', '0:00']);
  });
});
