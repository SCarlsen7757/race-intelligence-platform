import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { IncidentsPanel, toIncidentCount, toIncidentLimit } from './IncidentsPanel';

const DRIVER = 'id:2';

/**
 * Mounts the panel over a store holding one extras document.
 *
 * Fed directly rather than through a socket: the panel reads the low-rate extras channel and
 * nothing else, so a real connection would only add a network to the test.
 */
function renderIncidents(extras: Record<string, number>) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply({
    type: 'extrasFrame',
    roomId: 'room',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-16T12:00:00Z',
    extras: JSON.stringify(extras),
  });

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <IncidentsPanel store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('toIncidentCount', () => {
  /** `-1` is the simulator saying it has no reading, not the driver having none. */
  it('reads the simulator sentinel as no reading', () => {
    expect(toIncidentCount(-1)).toBeNull();
    expect(toIncidentCount(undefined)).toBeNull();
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
  it('treats both the sentinel and a zero limit as no limit', () => {
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
    const { container } = renderIncidents({ incidentPoints: 4, maxIncidentPoints: -1 });

    expect(container.querySelector('.incidents__count')?.textContent).toBe('4');
    expect(container.querySelector('.incidents__limit')).toBeNull();
    expect(container.textContent).not.toContain('-1');
    expect(container.textContent).not.toContain('/');
  });

  /**
   * With no count there is nothing honest to draw, and `0` would be the specific lie that the
   * driver is clean.
   */
  it('shows nothing at all when neither value is reported', () => {
    const { container } = renderIncidents({ incidentPoints: -1, maxIncidentPoints: -1 });

    expect(container.querySelector('.incidents')).toBeNull();
    expect(container.textContent).toBe('');
  });

  /** A malformed extras document is the same as no document: nothing reported. */
  it('shows nothing when the extras document will not parse', () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER]);
    store.apply({
      type: 'extrasFrame',
      roomId: 'room',
      driverKey: DRIVER,
      capturedAtUtc: '2026-08-16T12:00:00Z',
      extras: '{ not json',
    });

    const { container } = render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <IncidentsPanel store={store} driverKey={DRIVER} />
      </LiveContext.Provider>,
    );

    expect(container.textContent).toBe('');
  });

  it('marks a count close to the limit', () => {
    const { container } = renderIncidents({ incidentPoints: 8, maxIncidentPoints: 10 });

    expect(container.querySelector('.incidents--critical')).not.toBeNull();
  });

  /** Without a limit there is nothing to be close to, so no count may raise the warning. */
  it('never warns when no limit is reported, however high the count', () => {
    const { container } = renderIncidents({ incidentPoints: 99, maxIncidentPoints: -1 });

    expect(container.querySelector('.incidents--critical')).toBeNull();
  });

  /**
   * Two drivers can be compared, so a panel reads its own driver's extras rather than whichever
   * document arrived last.
   */
  it('reads the extras of its own driver, not of the other car on screen', () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER, 'id:9']);

    for (const [driverKey, incidentPoints] of [
      [DRIVER, 4],
      ['id:9', 7],
    ] as const) {
      store.apply({
        type: 'extrasFrame',
        roomId: 'room',
        driverKey,
        capturedAtUtc: '2026-08-16T12:00:00Z',
        extras: JSON.stringify({ incidentPoints, maxIncidentPoints: 10 }),
      });
    }

    const { container } = render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <IncidentsPanel store={store} driverKey={DRIVER} />
      </LiveContext.Provider>,
    );

    expect(container.querySelector('.incidents__count')?.textContent).toBe('4');
  });
});
