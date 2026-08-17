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

/** Defaults to no layout length, so a test opts in to average speeds rather than out of them. */
function renderLaps(props: Partial<Parameters<typeof LapHistory>[0]> = {}) {
  return render(
    <LapHistory
      laps={[lap()]}
      truncated={false}
      sessionBests={noBests}
      layoutLengthMeters={null}
      {...props}
    />,
  );
}

describe('LapHistory', () => {
  it('lists every completed lap with its number, time and per-sector splits', () => {
    renderLaps({ laps: [lap({ lapNumber: 1 }), lap({ lapNumber: 2, lapTimeMs: 101_000 })] });

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
    renderLaps({ laps: [lap({ sectorMs: [30_000, null, 102_000] })] });

    expect(screen.getByText('30.000')).toBeDefined();
    expect(screen.queryByText('72.000')).toBeNull();
  });

  it('marks a personal best green and a session best purple', () => {
    const { container } = renderLaps({
      laps: [lap({ lapNumber: 1, lapTimeMs: 102_000 }), lap({ lapNumber: 2, lapTimeMs: 100_000 })],
      sessionBests: { lapMs: 100_000, sectorMs: [null, null, null] },
    });

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
    const { container } = renderLaps({
      laps: [
        lap({ lapNumber: 1, lapTimeMs: 90_000, valid: false }),
        lap({ lapNumber: 2, lapTimeMs: 102_000 }),
      ],
    });

    const invalid = container.querySelectorAll('.time--invalid');
    expect([...invalid].map((node) => node.textContent)).toContain('1:30.000');
    expect(container.querySelector('.time--personal-best')?.textContent).toBe('1:42.000');
  });

  it('says so when the hub has dropped the earliest laps', () => {
    renderLaps({ truncated: true });

    expect(screen.getByText(/Earliest laps dropped/)).toBeDefined();
  });

  it('distinguishes a driver with no laps yet from a history that has not arrived', () => {
    const { rerender } = renderLaps({ laps: null });
    expect(screen.getByText(/Loading laps/)).toBeDefined();

    rerender(
      <LapHistory laps={[]} truncated={false} sessionBests={noBests} layoutLengthMeters={null} />,
    );
    expect(screen.getByText(/No completed laps yet/)).toBeDefined();
  });

  /**
   * A time alone does not say what shape a lap was, and it is not comparable between layouts. The
   * average is the figure that puts two circuits on one scale.
   */
  it('shows an average speed per lap when the layout length is known', () => {
    // 4000 m in 100 s is 40 m/s, which is 144 km/h.
    renderLaps({ laps: [lap({ lapTimeMs: 100_000 })], layoutLengthMeters: 4000 });

    expect(screen.getByText('144')).toBeDefined();
  });

  /**
   * The sentinel discipline the whole platform runs on. A speed reads as authoritative in a way a
   * blank time does not, so an unknown one has to render as nothing rather than as zero.
   */
  it('renders no average speed without a layout length, and none for an untimed lap', () => {
    const { container, rerender } = renderLaps({
      laps: [lap({ lapTimeMs: 100_000 })],
      layoutLengthMeters: null,
    });

    // Third cell of the body row: lap number, time, average.
    const averageOf = () => container.querySelector('tbody tr')?.children[2]?.textContent;
    expect(averageOf()).toBe('—');

    rerender(
      <LapHistory
        laps={[lap({ lapTimeMs: null })]}
        truncated={false}
        sessionBests={noBests}
        layoutLengthMeters={4000}
      />,
    );
    expect(averageOf()).toBe('—');

    // RaceRoom reports a non-positive length when it has none, and dividing by it would produce an
    // infinity that renders as a very confident number.
    rerender(
      <LapHistory
        laps={[lap({ lapTimeMs: 100_000 })]}
        truncated={false}
        sessionBests={noBests}
        layoutLengthMeters={0}
      />,
    );
    expect(averageOf()).toBe('—');
  });

  /**
   * Colour and a strikethrough are the only signals this table gave before, and neither survives a
   * colour-blind reader or a washed-out screen.
   */
  it('flags an invalid lap in words as well as in colour, and counts them', () => {
    renderLaps({
      laps: [
        lap({ lapNumber: 1, valid: false }),
        lap({ lapNumber: 2, valid: true }),
        lap({ lapNumber: 3, valid: false }),
      ],
    });

    expect(screen.getAllByText('INV')).toHaveLength(2);
    expect(screen.getByText(/2 of 3 laps invalidated/)).toBeDefined();
  });

  /**
   * Unknown validity is not invalidity. A simulator that never reports the flag must not have every
   * lap of a stint marked as binned.
   */
  it('leaves a lap of unknown validity unmarked and uncounted', () => {
    const { container } = renderLaps({ laps: [lap({ valid: null }), lap({ lapNumber: 2 })] });

    expect(screen.queryByText('INV')).toBeNull();
    expect(screen.queryByText(/invalidated/)).toBeNull();
    expect(container.querySelector('.laps__row--invalid')).toBeNull();
  });
});
