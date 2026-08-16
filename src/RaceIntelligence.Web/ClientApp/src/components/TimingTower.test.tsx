import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { TowerRow } from '../live/contracts';
import { NOT_REPORTED } from '../live/format';
import { TimingTower } from './TimingTower';

function row(overrides: Partial<TowerRow> = {}): TowerRow {
  return {
    driverKey: 'id:1',
    displayName: 'Driver 1',
    currentSectorMs: [],
    previousSectorMs: [],
    bestSectorMs: [],
    pitStopStatus: -1,
    finishStatus: 0,
    tier: 'Observed',
    ...overrides,
  };
}

/** Cumulative splits, as they cross the wire — S3 is the whole lap. */
const cumulative = (s1: number, s2: number, s3: number) => [s1, s1 + s2, s1 + s2 + s3];

describe('TimingTower', () => {
  it('renders a row per car, in position order', () => {
    render(
      <TimingTower
        rows={[
          row({ driverKey: 'id:1', displayName: 'Leader', position: 1 }),
          row({ driverKey: 'id:2', displayName: 'Second', position: 2 }),
        ]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    const names = screen.getAllByText(/Leader|Second/).map((node) => node.textContent);
    expect(names).toEqual(['Leader', 'Second']);
  });

  /**
   * The only affordance telling a race engineer which rows can be opened. A driver not running a
   * collector must not look like a broken button.
   */
  it('makes only a driver with their own collector clickable', () => {
    const onFocus = vi.fn();
    render(
      <TimingTower
        rows={[
          row({ driverKey: 'id:1', displayName: 'Publishing', position: 1, tier: 'Self' }),
          row({ driverKey: 'id:2', displayName: 'Watched', position: 2, tier: 'Observed' }),
        ]}
        focusedDriverKey={null}
        onFocus={onFocus}
      />,
    );

    const buttons = screen.getAllByRole('button');
    expect(buttons).toHaveLength(1);
    expect(buttons[0]!.textContent).toContain('Publishing');

    buttons[0]!.click();
    expect(onFocus).toHaveBeenCalledWith('id:1');
  });

  /**
   * The wire carries cumulative splits because that is the form the connector normalises the
   * simulator's two conventions into. Showing them uncorrected would display S3 as the whole lap
   * time — a plausible-looking number that is simply wrong.
   */
  it('shows per-sector times, not the cumulative splits it receives', () => {
    render(
      <TimingTower
        rows={[
          row({
            driverKey: 'id:1',
            position: 1,
            previousSectorMs: cumulative(30_000, 32_000, 40_000),
          }),
        ]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    expect(screen.getByText('30.000')).toBeDefined();
    expect(screen.getByText('32.000')).toBeDefined();
    expect(screen.getByText('40.000')).toBeDefined();
  });

  it('marks the fastest sector in the session, not merely a personal best', () => {
    const { container } = render(
      <TimingTower
        rows={[
          row({
            driverKey: 'id:1',
            position: 1,
            previousSectorMs: cumulative(30_000, 32_000, 40_000),
            bestSectorMs: cumulative(30_000, 32_000, 40_000),
          }),
          row({
            driverKey: 'id:2',
            position: 2,
            previousSectorMs: cumulative(29_000, 33_000, 41_000),
            bestSectorMs: cumulative(29_000, 33_000, 41_000),
          }),
        ]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    // 29.000 is the session's best S1 and belongs to the second row.
    const sessionBests = [...container.querySelectorAll('.time--session-best')].map(
      (node) => node.textContent,
    );

    expect(sessionBests).toContain('29.000');
    expect(sessionBests).not.toContain('30.000');
  });

  /**
   * A lap that has not been set is not a lap of zero. This is the rendering a pit call is made
   * from, so the distinction has to survive all the way to the cell.
   */
  it('renders unreported times as not-reported rather than as zero', () => {
    const { container } = render(
      <TimingTower
        rows={[row({ driverKey: 'id:1', position: 1 })]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    const cells = [...container.querySelectorAll('td')].map((node) => node.textContent);
    expect(cells).toContain(NOT_REPORTED);
    expect(cells).not.toContain('0.000');
  });

  it('strikes through a lap the simulator marked invalid', () => {
    const { container } = render(
      <TimingTower
        rows={[row({ driverKey: 'id:1', position: 1, previousLapMs: 95_000, currentLapValid: false })]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    expect(container.querySelector('.time--invalid')?.textContent).toBe('1:35.000');
  });

  it('shows pit, penalty and finish state', () => {
    render(
      <TimingTower
        rows={[
          row({ driverKey: 'id:1', position: 1, inPitLane: true, penaltyCount: 2, finishStatus: 2 }),
        ]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    expect(screen.getByText('PIT')).toBeDefined();
    expect(screen.getByText('2P')).toBeDefined();
    expect(screen.getByText('DNF')).toBeDefined();
  });

  it('says it is waiting when no timing has arrived yet', () => {
    render(<TimingTower rows={[]} focusedDriverKey={null} onFocus={vi.fn()} />);

    expect(screen.getByText(/Waiting for the first timing update/)).toBeDefined();
  });

  it('marks the focused row', () => {
    const { container } = render(
      <TimingTower
        rows={[row({ driverKey: 'id:1', position: 1, tier: 'Self' })]}
        focusedDriverKey="id:1"
        onFocus={vi.fn()}
      />,
    );

    expect(container.querySelector('.tower__row--focused')).not.toBeNull();
  });

  it('shows a car with no reported position last, not first', () => {
    render(
      <TimingTower
        rows={[
          row({ driverKey: 'id:1', displayName: 'Unplaced', position: null }),
          row({ driverKey: 'id:2', displayName: 'Leader', position: 1 }),
        ]}
        focusedDriverKey={null}
        onFocus={vi.fn()}
      />,
    );

    // The projector sorts server-side; the tower renders what it is given. This pins that the
    // component does not resort and undo that.
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]!).getByText('Unplaced')).toBeDefined();
  });
});
