import { describe, expect, it } from 'vitest';
import {
  formatAge,
  formatGap,
  formatGear,
  formatLapTime,
  formatPitLaneState,
  formatPitStopStatus,
  isRaceSession,
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
 * The value is RaceRoom's own raw code, not the canonical SessionType numbering — the connector
 * passes session_type through uninterpreted. Reading it as canonical shifts every label by one and
 * reports a race as qualifying, which looks entirely normal on screen.
 */
describe('formatSessionType', () => {
  it.each([
    [0, 'Practice'],
    [1, 'Qualifying'],
    [2, 'Race'],
    [3, 'Warmup'],
  ])("maps RaceRoom's raw session type %i to %s", (value, expected) => {
    expect(formatSessionType('raceroom', value)).toBe(expected);
  });

  it("renders RaceRoom's -1 sentinel as unnamed rather than as a session type", () => {
    expect(formatSessionType('raceroom', -1)).toBe('Session');
  });

  it('does not guess for a simulator whose numbering it does not know', () => {
    expect(formatSessionType('some-other-sim', 2)).toBe('Session');
  });

  it('falls back for an unknown value rather than guessing', () => {
    expect(formatSessionType('raceroom', 99)).toBe('Session');
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

describe('formatPitLaneState', () => {
  it.each([
    [1, 'PIT REQ'],
    [2, 'PIT IN'],
    [3, 'IN BOX'],
    [4, 'PIT OUT'],
    [5, 'PIT'],
  ])('names rung %i as %s', (state, expected) => {
    expect(formatPitLaneState(state)).toBe(expected);
  });

  /**
   * A tower marks the exceptions. A car on track has nothing to report, and neither does one whose
   * simulator declines to say — rendering either would put a pill on every row in the field.
   */
  it.each([-1, 0, 99])('says nothing for %i', (state) => {
    expect(formatPitLaneState(state)).toBe('');
  });

  /** Ungraded is a weaker claim than entering, and must not be dressed up as one. */
  it('does not describe an unknown stage as a direction of travel', () => {
    expect(formatPitLaneState(5)).not.toBe(formatPitLaneState(2));
  });
});

describe('formatPitStopStatus', () => {
  it.each([
    [0, '2T LEFT'],
    [1, '4T LEFT'],
    [2, 'SERVED'],
  ])('names %i as %s', (status, expected) => {
    expect(formatPitStopStatus(status)).toBe(expected);
  });

  it('says nothing for a status the simulator does not report', () => {
    expect(formatPitStopStatus(-1)).toBe('');
  });
});

describe('isRaceSession', () => {
  it("recognises RaceRoom's race", () => {
    expect(isRaceSession('raceroom', 2)).toBe(true);
  });

  it.each([0, 1, 3, -1])('does not mistake RaceRoom session type %i for a race', (sessionType) => {
    expect(isRaceSession('raceroom', sessionType)).toBe(false);
  });

  /**
   * The same trap `formatSessionType` documents: 2 is a race in RaceRoom's own numbering and
   * something else in the canonical one, so a simulator this does not know gets no guess.
   */
  it("does not read another simulator's numbering as RaceRoom's", () => {
    expect(isRaceSession('some-other-sim', 2)).toBe(false);
  });
});
