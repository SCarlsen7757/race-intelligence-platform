import { describe, expect, it } from 'vitest';
import {
  formatAge,
  formatGap,
  formatGear,
  formatLapTime,
  formatSector,
  formatSessionType,
  formatSpeed,
  NOT_REPORTED,
} from './format';

describe('formatLapTime', () => {
  it('shows minutes when the lap is over a minute', () => {
    expect(formatLapTime(102_456)).toBe('1:42.456');
  });

  it('omits minutes under a minute', () => {
    expect(formatLapTime(42_100)).toBe('42.100');
  });

  it('pads the seconds so times line up in a column', () => {
    expect(formatLapTime(63_000)).toBe('1:03.000');
  });
});

/**
 * The sentinel discipline the whole platform runs on, at the point where a person acts on it. A
 * time the simulator did not report must never render as a number — "0.000" is not a smaller
 * version of "unknown", it is a different and confident claim.
 */
describe('missing values', () => {
  it.each([
    ['formatLapTime', formatLapTime],
    ['formatSector', formatSector],
    ['formatGap', formatGap],
    ['formatSpeed', formatSpeed],
  ])('%s renders null and undefined as not-reported', (_name, format) => {
    expect(format(null)).toBe(NOT_REPORTED);
    expect(format(undefined)).toBe(NOT_REPORTED);
  });

  it('renders a negative sentinel that slipped through as not-reported, not as a negative time', () => {
    expect(formatLapTime(-1)).toBe(NOT_REPORTED);
    expect(formatSector(-1)).toBe(NOT_REPORTED);
  });

  it('still renders a genuine zero gap, which is a real reading', () => {
    expect(formatGap(0)).toBe('+0.000');
  });
});

describe('formatGap', () => {
  it('signs the gap so the direction is unambiguous', () => {
    expect(formatGap(1234)).toBe('+1.234');
    expect(formatGap(-1234)).toBe('-1.234');
  });
});

describe('formatGear', () => {
  it.each([
    [-1, 'R'],
    [0, 'N'],
    [1, '1'],
    [7, '7'],
  ])('renders gear %i as %s', (gear, expected) => {
    expect(formatGear(gear)).toBe(expected);
  });
});

describe('formatSpeed', () => {
  it('converts metres per second to km/h', () => {
    expect(formatSpeed(50)).toBe('180');
  });
});

/**
 * Mirrors the canonical SessionType enum, not RaceRoom's own numbering — the connector has already
 * interpreted it. Getting this wrong labels every race as qualifying, which is the kind of error
 * nobody questions because the screen looks fine.
 */
describe('formatSessionType', () => {
  it.each([
    [1, 'Practice'],
    [2, 'Qualifying'],
    [3, 'Race'],
    [4, 'Warmup'],
  ])('maps canonical session type %i to %s', (value, expected) => {
    expect(formatSessionType(value)).toBe(expected);
  });

  it('falls back for an unknown value rather than guessing', () => {
    expect(formatSessionType(0)).toBe('Session');
    expect(formatSessionType(99)).toBe('Session');
  });
});

describe('formatAge', () => {
  const now = Date.parse('2026-08-16T12:00:00Z');

  it.each([
    ['2026-08-16T11:59:58Z', 'now'],
    ['2026-08-16T11:59:30Z', '30s ago'],
    ['2026-08-16T11:55:00Z', '5m ago'],
    ['2026-08-16T10:00:00Z', '2h ago'],
  ])('renders %s as %s', (timestamp, expected) => {
    expect(formatAge(timestamp, now)).toBe(expected);
  });

  it('does not render a clock skew as a negative age', () => {
    expect(formatAge('2026-08-16T12:00:05Z', now)).toBe('now');
  });
});
