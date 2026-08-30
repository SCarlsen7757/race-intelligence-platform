import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { RaceRoomSample } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { slowFrame } from '../../testing/slowFrame';
import { EventTimeline } from './EventTimeline';

const DRIVER = 'id:3';

/**
 * Feeds a sequence of slow frames a second apart, as the collector would.
 *
 * The whole point of the store's event recording is that it compares each frame against the last
 * one, so a test that fed a single frame could never see a transition at all.
 */
function drive(samples: RaceRoomSample[]) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);

  samples.forEach((sample, index) => {
    store.apply(
      slowFrame(DRIVER, sample, {
        roomId: 'room-1',
        capturedAtUtc: new Date(Date.UTC(2026, 7, 19, 12, 0, index)).toISOString(),
      }),
    );
  });

  return store;
}

function renderTimeline(samples: RaceRoomSample[]) {
  const store = drive(samples);

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <EventTimeline store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('the event timeline', () => {
  it('says so before anything has happened', () => {
    const view = renderTimeline([{ flagYellow: 0 }]);

    expect(view.getByText(/No flags, activations or incidents yet/)).toBeTruthy();
  });

  /**
   * A viewer opening the dashboard under a yellow has not just watched it come out. The first frame
   * establishes what is already true and announces none of it.
   */
  it('does not announce what was already true when the viewer joined', () => {
    const view = renderTimeline([{ flagYellow: 1 }, { flagYellow: 1 }]);

    expect(view.queryByText('Yellow flag')).toBeNull();
  });

  it('records a flag when it comes out, once rather than once a second', () => {
    const view = renderTimeline([
      { flagYellow: 0 },
      { flagYellow: 1 },
      { flagYellow: 1 },
      { flagYellow: 1 },
    ]);

    expect(view.getAllByText('Yellow flag')).toHaveLength(1);
  });

  /** A second yellow raised while the first still stands is a second event: the count went up. */
  it('records a second flag raised while the first still stands', () => {
    const view = renderTimeline([{ flagYellow: 0 }, { flagYellow: 1 }, { flagYellow: 2 }]);

    expect(view.getAllByText('Yellow flag')).toHaveLength(2);
  });

  /**
   * `pushToPassAmountLeft` counts activations *remaining* and falls as they are spent, so it would
   * never register on an increase test. Engagement is what the store watches instead.
   */
  it('records each push-to-pass activation as it is engaged', () => {
    const view = renderTimeline([
      { pushToPassEngaged: 0, pushToPassAmountLeft: 3 },
      { pushToPassEngaged: 1, pushToPassAmountLeft: 2 },
      { pushToPassEngaged: 0, pushToPassAmountLeft: 2 },
      { pushToPassEngaged: 1, pushToPassAmountLeft: 1 },
    ]);

    expect(view.getAllByText('Push to pass')).toHaveLength(2);
  });

  /**
   * An unreported channel is not an event. It used to arrive as the simulator's `-1` and this test
   * guarded against reading that as a value; the channel is simply absent now, and absence must be
   * as silent as the sentinel was made to be.
   */
  it('does not read an unreported channel as something happening', () => {
    const view = renderTimeline([{}, {}]);

    expect(view.getByText(/No flags, activations or incidents yet/)).toBeTruthy();
  });

  it('orders the newest first, with a clock relative to the first event seen', () => {
    const view = renderTimeline([
      { flagYellow: 0, incidentPoints: 0 },
      { flagYellow: 1, incidentPoints: 0 },
      { flagYellow: 1, incidentPoints: 2 },
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
