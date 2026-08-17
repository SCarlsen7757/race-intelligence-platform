import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { TowerRow } from '../../shared/live/contracts';
import { TrackMap } from './TrackMap';

function row(overrides: Partial<TowerRow> = {}): TowerRow {
  return {
    driverKey: 'id:1',
    displayName: 'Driver 1',
    currentSectorMs: [],
    previousSectorMs: [],
    bestSectorMs: [],
    pitLaneState: -1,
    pitStopStatus: -1,
    finishStatus: 0,
    tier: 'Observed',
    ...overrides,
  };
}

const NONE: ReadonlySet<string> = new Set();

function renderMap(rows: TowerRow[], overrides: Partial<Parameters<typeof TrackMap>[0]> = {}) {
  return render(
    <TrackMap
      rows={rows}
      focusedDriverKeys={[]}
      expandedDriverKeys={NONE}
      onSelect={vi.fn()}
      {...overrides}
    />,
  );
}

describe('TrackMap', () => {
  it('draws a dot for every car with a reported lap position', () => {
    const { container } = renderMap([
      row({ driverKey: 'id:1', position: 1, trackPositionFraction: 0.1 }),
      row({ driverKey: 'id:2', position: 2, trackPositionFraction: 0.6 }),
    ]);

    expect(container.querySelectorAll('.track-map__car')).toHaveLength(2);
  });

  /**
   * The same rule the timing columns follow. A car whose lap fraction the simulator did not report
   * is not a car sitting on the start line, and drawing it there would invent a battle that is not
   * happening.
   */
  it('omits a car with no reported lap position rather than drawing it at the line', () => {
    const { container } = renderMap([
      row({ driverKey: 'id:1', position: 1, trackPositionFraction: 0.1 }),
      row({ driverKey: 'id:2', position: 2 }),
    ]);

    expect(container.querySelectorAll('.track-map__car')).toHaveLength(1);
  });

  /** Said rather than silently dropped: a field that looks a car short is a bug report waiting. */
  it('says how many cars it could not place', () => {
    renderMap([
      row({ driverKey: 'id:1', position: 1, trackPositionFraction: 0.1 }),
      row({ driverKey: 'id:2', position: 2 }),
    ]);

    expect(screen.getByText(/1 not shown/)).toBeDefined();
  });

  it('marks a car in the pit lane differently from one on track', () => {
    const { container } = renderMap([
      row({ driverKey: 'id:1', position: 1, trackPositionFraction: 0.1 }),
      row({ driverKey: 'id:2', position: 2, trackPositionFraction: 0.6, inPitLane: true }),
    ]);

    expect(container.querySelectorAll('.track-map__car--pit')).toHaveLength(1);
  });

  /** The viewer's own car — the only tier with telemetry behind it — has to stand out at a glance. */
  it('marks a car whose own machine is publishing', () => {
    const { container } = renderMap([
      row({ driverKey: 'id:1', position: 1, trackPositionFraction: 0.1, tier: 'Self' }),
      row({ driverKey: 'id:2', position: 2, trackPositionFraction: 0.6 }),
    ]);

    expect(container.querySelectorAll('.track-map__car--self')).toHaveLength(1);
  });

  it('selects the driver whose dot was clicked', () => {
    const onSelect = vi.fn();
    renderMap(
      [row({ driverKey: 'id:7', displayName: 'Rival', position: 3, trackPositionFraction: 0.4 })],
      { onSelect },
    );

    // fireEvent rather than .click(): the dot is an SVG group, and SVGElement has no click()
    // method in jsdom the way an HTMLElement does.
    fireEvent.click(screen.getByRole('button', { name: /Rival/ }));

    expect(onSelect).toHaveBeenCalledWith('id:7');
  });

  /**
   * Cars bunch on a circle far more than on a real outline — a two-second gap is less than a dot
   * wide — so two cars at almost the same lap fraction have to be drawn apart to stay countable.
   */
  it('separates two cars too close together to be drawn on the same radius', () => {
    const { container } = renderMap([
      row({ driverKey: 'id:1', position: 1, trackPositionFraction: 0.5 }),
      row({ driverKey: 'id:2', position: 2, trackPositionFraction: 0.501 }),
    ]);

    const dots = [...container.querySelectorAll('.track-map__dot')];
    const positions = dots.map((dot) => `${dot.getAttribute('cx')},${dot.getAttribute('cy')}`);

    expect(new Set(positions).size).toBe(2);
  });

  it('renders nothing before the first timing update', () => {
    const { container } = renderMap([]);

    expect(container.querySelector('.track-map')).toBeNull();
  });
});
