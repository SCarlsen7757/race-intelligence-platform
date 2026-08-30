import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { RaceRoomSample } from '../../shared/live/contracts';
import { LiveStore, type SlowSnapshot } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { slowFrame } from '../../testing/slowFrame';
import {
  IncidentsPanel,
  incidentsPanelIsEmpty,
  toIncidentCount,
  toIncidentLimit,
} from './IncidentsPanel';

const DRIVER = 'id:2';

/**
 * Mounts the panel over a store holding one slow frame.
 *
 * Fed directly rather than through a socket: the panel reads the low-rate slow channel and nothing
 * else, so a real connection would only add a network to the test.
 */
function renderIncidents(sample: RaceRoomSample) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply(slowFrame(DRIVER, sample));

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <IncidentsPanel store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('toIncidentCount', () => {
  /**
   * An absent count is the simulator saying it has no reading, not the driver having none. The `-1`
   * it used to arrive as is translated by the connector now; the guard stays because a defensive
   * check that costs nothing is worth keeping on a number a panel renders.
   */
  it('reads an unreported count as no reading', () => {
    expect(toIncidentCount(undefined)).toBeNull();
    expect(toIncidentCount(-1)).toBeNull();
    expect(toIncidentCount(Number.NaN)).toBeNull();
  });

  /** A clean sheet is a real answer, and the one most worth being sure about. */
  it('keeps zero as a real count', () => {
    expect(toIncidentCount(0)).toBe(0);
    expect(toIncidentCount(4)).toBe(4);
  });
});

describe('toIncidentLimit', () => {
  /**
   * Zero is rejected where the count keeps it: a limit of nothing cannot be rendered (`4 / 0`) and
   * cannot be divided into, so there is no reading it usefully means.
   */
  it('treats an absent limit and a zero limit alike', () => {
    expect(toIncidentLimit(-1)).toBeNull();
    expect(toIncidentLimit(0)).toBeNull();
    expect(toIncidentLimit(undefined)).toBeNull();
    expect(toIncidentLimit(Number.NaN)).toBeNull();
  });

  it('keeps a reported limit', () => {
    expect(toIncidentLimit(10)).toBe(10);
  });
});

describe('IncidentsPanel', () => {
  it('uses the panel rendering rule when deciding whether its frame is empty', () => {
    const snapshot = (sample: RaceRoomSample): SlowSnapshot => ({
      message: slowFrame(DRIVER, sample),
    });

    expect(incidentsPanelIsEmpty(snapshot({}))).toBe(true);
    expect(incidentsPanelIsEmpty(snapshot({ incidentPoints: 0 }))).toBe(false);
  });

  it('shows the count against the limit when the server reports one', () => {
    const { container } = renderIncidents({ incidentPoints: 4, maxIncidentPoints: 10 });

    expect(container.querySelector('.incidents__count')?.textContent).toBe('4');
    expect(container.querySelector('.incidents__limit')?.textContent).toBe('/ 10');
  });

  it('renders a clean sheet as zero rather than as nothing reported', () => {
    const { container } = renderIncidents({ incidentPoints: 0, maxIncidentPoints: 10 });

    expect(container.querySelector('.incidents__count')?.textContent).toBe('0');
  });

  /**
   * Offline, or on a server that disqualifies nobody. The count still means something on its own;
   * `4 / -1` and `4 / 0` would both be inventions.
   */
  it('shows the count alone when no limit is reported', () => {
    const { container } = renderIncidents({ incidentPoints: 4 });

    expect(container.querySelector('.incidents__count')?.textContent).toBe('4');
    expect(container.querySelector('.incidents__limit')).toBeNull();
    expect(container.textContent).not.toContain('/');
  });

  /**
   * With no count there is nothing honest to draw, and `0` would be the specific lie that the
   * driver is clean.
   */
  it('shows nothing at all when neither value is reported', () => {
    const { container } = renderIncidents({});

    expect(container.querySelector('.incidents')).toBeNull();
    expect(container.textContent).toBe('');
  });

  it('marks a count close to the limit', () => {
    const { container } = renderIncidents({ incidentPoints: 8, maxIncidentPoints: 10 });

    expect(container.querySelector('.incidents--critical')).not.toBeNull();
  });

  /** Without a limit there is nothing to be close to, so no count may raise the warning. */
  it('never warns when no limit is reported, however high the count', () => {
    const { container } = renderIncidents({ incidentPoints: 99 });

    expect(container.querySelector('.incidents--critical')).toBeNull();
  });

  /**
   * Two drivers can be compared, so a panel reads its own driver's frame rather than whichever one
   * arrived last.
   */
  it('reads the slow channels of its own driver, not of the other car on screen', () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER, 'id:9']);

    for (const [driverKey, incidentPoints] of [
      [DRIVER, 4],
      ['id:9', 7],
    ] as const) {
      store.apply(slowFrame(driverKey, { incidentPoints, maxIncidentPoints: 10 }));
    }

    const { container } = render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <IncidentsPanel store={store} driverKey={DRIVER} />
      </LiveContext.Provider>,
    );

    expect(container.querySelector('.incidents__count')?.textContent).toBe('4');
  });
});
