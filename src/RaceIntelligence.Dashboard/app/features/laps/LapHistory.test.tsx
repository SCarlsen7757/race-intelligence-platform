import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SessionBests } from '../../shared/format/sectors';
import type { LapRecord } from '../../shared/live/contracts';
import { LapHistory } from './LapHistory';

/** Cumulative splits, as they cross the wire — S3 is the whole lap. */
const cumulative = (s1: number, s2: number, s3: number) => [s1, s1 + s2, s1 + s2 + s3];

function lap(overrides: Partial<LapRecord> = {}): LapRecord {
  return {
    lapNumber: 1,
    lapTimeMs: 102_000,
    sectorMs: cumulative(30_000, 32_000, 40_000),
    valid: true,
    ...overrides,
  };
}

const noBests: SessionBests = { lapMs: null, sectorMs: [null, null, null] };

describe('LapHistory', () => {
  it('lists every completed lap with its number, time and per-sector splits', () => {
    render(
      <LapHistory
        laps={[lap({ lapNumber: 1 }), lap({ lapNumber: 2, lapTimeMs: 101_000 })]}
        truncated={false}
        sessionBests={noBests}
      />,
    );

    expect(screen.getAllByRole('row')).toHaveLength(3);
    expect(screen.getByText('1:42.000')).toBeDefined();
    expect(screen.getByText('1:41.000')).toBeDefined();

    // Per-sector, not the cumulative splits that crossed the wire.
    expect(screen.getAllByText('32.000')).toHaveLength(2);
  });

  /**
   * The same refusal the tower makes: a missing S2 leaves S3 undeterminable, and a fabricated
   * number there would be indistinguishable from a real one.
   */
  it('renders nothing for a sector it cannot derive', () => {
    render(
      <LapHistory
        laps={[lap({ sectorMs: [30_000, null, 102_000] })]}
        truncated={false}
        sessionBests={noBests}
      />,
    );

    expect(screen.getByText('30.000')).toBeDefined();
    expect(screen.queryByText('72.000')).toBeNull();
  });

  it('marks a personal best green and a session best purple', () => {
    const { container } = render(
      <LapHistory
        laps={[
          lap({ lapNumber: 1, lapTimeMs: 102_000 }),
          lap({ lapNumber: 2, lapTimeMs: 100_000 }),
        ]}
        truncated={false}
        sessionBests={{ lapMs: 100_000, sectorMs: [null, null, null] }}
      />,
    );

    expect(container.querySelector('.time--session-best')?.textContent).toBe('1:40.000');
    expect(
      [...container.querySelectorAll('.time--personal-best')].map((node) => node.textContent),
    ).not.toContain('1:42.000');
  });

  /**
   * A lap the simulator refused to count must not set a personal best, or the table paints a
   * green sector for a time that officially never happened.
   */
  it('does not let an invalid lap set a personal best', () => {
    const { container } = render(
      <LapHistory
        laps={[
          lap({ lapNumber: 1, lapTimeMs: 90_000, valid: false }),
          lap({ lapNumber: 2, lapTimeMs: 102_000 }),
        ]}
        truncated={false}
        sessionBests={noBests}
      />,
    );

    const invalid = container.querySelectorAll('.time--invalid');
    expect([...invalid].map((node) => node.textContent)).toContain('1:30.000');
    expect(container.querySelector('.time--personal-best')?.textContent).toBe('1:42.000');
  });

  it('says so when the hub has dropped the earliest laps', () => {
    render(<LapHistory laps={[lap()]} truncated sessionBests={noBests} />);

    expect(screen.getByText(/Earliest laps dropped/)).toBeDefined();
  });

  it('distinguishes a driver with no laps yet from a history that has not arrived', () => {
    const { rerender } = render(
      <LapHistory laps={null} truncated={false} sessionBests={noBests} />,
    );
    expect(screen.getByText(/Loading laps/)).toBeDefined();

    rerender(<LapHistory laps={[]} truncated={false} sessionBests={noBests} />);
    expect(screen.getByText(/No completed laps yet/)).toBeDefined();
  });
});
