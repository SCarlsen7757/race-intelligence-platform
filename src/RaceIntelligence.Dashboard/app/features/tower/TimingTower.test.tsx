import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NOT_REPORTED } from '../../shared/format/format';
import type { TowerRow } from '../../shared/live/contracts';
import { TimingTower } from './TimingTower';

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

/** Cumulative splits, as they cross the wire — S3 is the whole lap. */
const cumulative = (s1: number, s2: number, s3: number) => [s1, s1 + s2, s1 + s2 + s3];

const NONE: ReadonlySet<string> = new Set();

function renderTower(rows: TowerRow[], overrides: Partial<Parameters<typeof TimingTower>[0]> = {}) {
  return render(
    <TimingTower
      rows={rows}
      focusedDriverKeys={[]}
      onFocus={vi.fn()}
      expandedDriverKeys={NONE}
      onToggleExpand={vi.fn()}
      {...overrides}
    />,
  );
}

describe('TimingTower', () => {
  it('renders a row per car, in position order', () => {
    renderTower([
      row({ driverKey: 'id:1', displayName: 'Leader', position: 1 }),
      row({ driverKey: 'id:2', displayName: 'Second', position: 2 }),
    ]);

    const names = screen.getAllByText(/Leader|Second/).map((node) => node.textContent);
    expect(names).toEqual(['▸Leader', '▸Second']);
  });

  /**
   * The only affordance telling a race engineer which rows have full telemetry behind them. A
   * driver not running a collector must not look like a broken button — but they must still
   * expand, because lap history comes from standings and the hub has that for the whole field.
   */
  it('offers telemetry only for a driver with their own collector, but expands either', () => {
    const onFocus = vi.fn();
    const onToggleExpand = vi.fn();
    renderTower(
      [
        row({ driverKey: 'id:1', displayName: 'Publishing', position: 1, tier: 'Self' }),
        row({ driverKey: 'id:2', displayName: 'Watched', position: 2, tier: 'Observed' }),
      ],
      { onFocus, onToggleExpand },
    );

    const telemetry = screen.getAllByRole('button', { name: /^Open telemetry/ });
    expect(telemetry).toHaveLength(1);
    expect(telemetry[0]!.getAttribute('aria-label')).toBe('Open telemetry for Publishing');

    telemetry[0]!.click();
    expect(onFocus).toHaveBeenCalledWith('id:1');

    screen.getByRole('button', { name: /Watched/ }).click();
    expect(onToggleExpand).toHaveBeenCalledWith('id:2');
  });

  it('marks the disclosure control so a screen reader knows the row is open', () => {
    renderTower([row({ driverKey: 'id:1', displayName: 'Leader', position: 1 })], {
      expandedDriverKeys: new Set(['id:1']),
      renderDetail: () => <p>Lap history</p>,
    });

    expect(screen.getByRole('button', { name: /Leader/ }).getAttribute('aria-expanded')).toBe(
      'true',
    );
    expect(screen.getByText('Lap history')).toBeDefined();
  });

  it('keeps several rows open at once', () => {
    renderTower(
      [
        row({ driverKey: 'id:1', displayName: 'Leader', position: 1 }),
        row({ driverKey: 'id:2', displayName: 'Second', position: 2 }),
      ],
      {
        expandedDriverKeys: new Set(['id:1', 'id:2']),
        renderDetail: (driverKey) => <p>{`Laps for ${driverKey}`}</p>,
      },
    );

    expect(screen.getByText('Laps for id:1')).toBeDefined();
    expect(screen.getByText('Laps for id:2')).toBeDefined();
  });

  /**
   * The wire carries cumulative splits because that is the form the connector normalises the
   * simulator's two conventions into. Showing them uncorrected would display S3 as the whole lap
   * time — a plausible-looking number that is simply wrong.
   */
  it('shows per-sector times, not the cumulative splits it receives', () => {
    renderTower([
      row({
        driverKey: 'id:1',
        position: 1,
        previousSectorMs: cumulative(30_000, 32_000, 40_000),
      }),
    ]);

    expect(screen.getByText('30.000')).toBeDefined();
    expect(screen.getByText('32.000')).toBeDefined();
    expect(screen.getByText('40.000')).toBeDefined();
  });

  it('marks the fastest sector in the session, not merely a personal best', () => {
    const { container } = renderTower([
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
    ]);

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
    const { container } = renderTower([row({ driverKey: 'id:1', position: 1 })]);

    const cells = [...container.querySelectorAll('td')].map((node) => node.textContent);
    expect(cells).toContain(NOT_REPORTED);
    expect(cells).not.toContain('0.000');
  });

  /**
   * The last-lap column shows `previousLapMs`, so it has to be struck from `previousLapValid` — the
   * validity of that same lap.
   */
  it('strikes through the completed lap the simulator marked invalid, and says so in words', () => {
    const { container } = renderTower([
      row({ driverKey: 'id:1', position: 1, previousLapMs: 95_000, previousLapValid: false }),
    ]);

    expect(container.querySelector('.time--invalid')?.textContent).toBe('1:35.000INV');
    expect(screen.getByText('INV')).toBeDefined();
  });

  /**
   * The bug this column had: `currentLapValid` describes the lap being driven right now, so reading
   * it here struck a clean last lap through the instant the driver put a wheel off on the next one —
   * while the lap that was actually binned went by unmarked.
   */
  it('does not strike a clean last lap because the lap in progress went invalid', () => {
    const { container } = renderTower([
      row({
        driverKey: 'id:1',
        position: 1,
        previousLapMs: 95_000,
        previousLapValid: true,
        currentLapValid: false,
      }),
    ]);

    expect(container.querySelector('.time--invalid')).toBeNull();
    expect(screen.queryByText('INV')).toBeNull();
  });

  /**
   * Unknown is not invalid. Every driver's first lap after the hub starts watching has no recorded
   * validity, and marking those as binned would condemn a whole grid on connect.
   */
  it('leaves a lap of unknown validity unmarked', () => {
    const { container } = renderTower([
      row({ driverKey: 'id:1', position: 1, previousLapMs: 95_000 }),
    ]);

    expect(container.querySelector('.time--invalid')).toBeNull();
    expect(screen.queryByText('INV')).toBeNull();
  });

  it('shows pit, penalty and finish state', () => {
    renderTower([
      row({
        driverKey: 'id:1',
        position: 1,
        inPitLane: true,
        penaltyCount: 2,
        finishStatus: 2,
      }),
    ]);

    expect(screen.getByText('PIT')).toBeDefined();
    expect(screen.getByText('PEN')).toBeDefined();
    expect(screen.getByText('DNF')).toBeDefined();
  });

  /**
   * `penaltyCount` counts penalty types, not penalties, so the pill never carries the number — two
   * pending kinds and three drive-throughs would both be lying if it did.
   */
  it('says PEN without a count, however many penalty types are pending', () => {
    const { container } = renderTower([row({ driverKey: 'id:1', position: 1, penaltyCount: 2 })]);

    const pill = container.querySelector('.pill--penalty');
    expect(pill).not.toBeNull();
    expect(pill!.textContent).toBe('PEN');
  });

  it('says nothing about penalties for a driver carrying none', () => {
    const { container } = renderTower([row({ driverKey: 'id:1', position: 1, penaltyCount: 0 })]);

    expect(container.querySelector('.pill--penalty')).toBeNull();
    expect(screen.queryByText('PEN')).toBeNull();
  });

  /**
   * Unreported is not zero anywhere else in the tower, and it is not zero here — but a simulator
   * that says nothing about penalties must not put a pill up on the strength of its silence.
   */
  it('says nothing about penalties when the simulator does not report them', () => {
    const { container } = renderTower([
      row({ driverKey: 'id:1', position: 1, penaltyCount: null }),
      row({ driverKey: 'id:2', position: 2 }),
    ]);

    expect(container.querySelector('.pill--penalty')).toBeNull();
    expect(screen.queryByText('PEN')).toBeNull();
  });

  it('grades the pit lane in a race', () => {
    renderTower(
      [
        row({ driverKey: 'id:1', position: 1, inPitLane: false, pitLaneState: 1 }),
        row({ driverKey: 'id:2', position: 2, inPitLane: true, pitLaneState: 3, pitStopStatus: 0 }),
        row({ driverKey: 'id:3', position: 3, inPitLane: true, pitLaneState: 4 }),
      ],
      { isRace: true },
    );

    expect(screen.getByText('PIT REQ')).toBeDefined();
    expect(screen.getByText('IN BOX')).toBeDefined();
    expect(screen.getByText('2T LEFT')).toBeDefined();
    expect(screen.getByText('PIT OUT')).toBeDefined();
  });

  /**
   * The rendering this whole column was rebuilt for. RaceRoom leaves `pit_stop_status` at 0 — "two
   * tyres unserved" — for a full field of cars lapping in practice, and the tower used to put PIT 2T
   * against every one of them. Nobody is pitting; there is nothing to say.
   */
  it('says nothing about pit stops in a session where nobody stops', () => {
    renderTower([
      row({ driverKey: 'id:1', position: 1, inPitLane: false, pitLaneState: 0, pitStopStatus: 0 }),
    ]);

    expect(screen.queryByText(/PIT|LEFT|SERVED/)).toBeNull();
  });

  /** A car in the garage during practice is still worth marking — just not with a stop's ladder. */
  it('still says a car is in the pit lane outside a race, and no more than that', () => {
    renderTower([
      row({ driverKey: 'id:1', position: 1, inPitLane: true, pitLaneState: 3, pitStopStatus: 1 }),
    ]);

    expect(screen.getByText('PIT')).toBeDefined();
    expect(screen.queryByText('IN BOX')).toBeNull();
    expect(screen.queryByText('4T LEFT')).toBeNull();
  });

  /**
   * The status column carries a `td` per row and a `th` above it, and nothing may lay it out as a
   * flex container: a flex `td` stops generating a table cell, and the anonymous cell the browser
   * substitutes does not line up with the column the header measured.
   */
  it('keeps the status pills inside a real table cell', () => {
    const { container } = renderTower([row({ driverKey: 'id:1', position: 1, inPitLane: true })]);

    const cell = container.querySelector('td.tower__state');
    expect(cell).not.toBeNull();
    expect(cell!.querySelector('.tower__pills')).not.toBeNull();
  });

  it('says it is waiting when no timing has arrived yet', () => {
    renderTower([]);

    expect(screen.getByText(/Waiting for the first timing update/)).toBeDefined();
  });

  it('marks the focused row', () => {
    const { container } = renderTower([row({ driverKey: 'id:1', position: 1, tier: 'Self' })], {
      focusedDriverKeys: ['id:1'],
    });

    expect(container.querySelector('.tower__row--focused')).not.toBeNull();
  });

  /** Two drivers can be compared, so both their rows have to read as open at once. */
  it('marks both rows of a comparison', () => {
    const { container } = renderTower(
      [
        row({ driverKey: 'id:1', position: 1, tier: 'Self' }),
        row({ driverKey: 'id:2', position: 2, tier: 'Self' }),
        row({ driverKey: 'id:3', position: 3, tier: 'Self' }),
      ],
      { focusedDriverKeys: ['id:1', 'id:3'] },
    );

    expect(container.querySelectorAll('.tower__row--focused')).toHaveLength(2);
    expect(
      screen.getAllByRole('button', { name: /Open telemetry/ }).map((b) => b.textContent),
    ).toEqual(['Shown', 'Show', 'Shown']);
  });

  /**
   * Clicking Show subscribes and then sits on a panel of em dashes until the first frame lands.
   * On a LAN that is invisible; through a tunnel it is long enough to click twice and wonder
   * whether the first one registered.
   */
  it('says a focused driver is still opening until their first frame arrives', () => {
    renderTower(
      [
        row({ driverKey: 'id:1', position: 1, tier: 'Self' }),
        row({ driverKey: 'id:2', position: 2, tier: 'Self' }),
      ],
      { focusedDriverKeys: ['id:1', 'id:2'], pendingDriverKeys: new Set(['id:2']) },
    );

    const buttons = screen.getAllByRole('button', { name: /Open telemetry/ });
    expect(buttons.map((b) => b.textContent)).toEqual(['Shown', 'Opening…']);

    // Still pressed while pending: the subscription is on, which is what the control toggles.
    // `aria-busy` is what says the data has not caught up.
    expect(buttons[1]!.getAttribute('aria-pressed')).toBe('true');
    expect(buttons[1]!.getAttribute('aria-busy')).toBe('true');
    expect(buttons[0]!.getAttribute('aria-busy')).toBe('false');
  });

  /** A driver nobody asked for cannot be waiting on a frame, whatever a stale set claims. */
  it('does not mark an unfocused driver as opening', () => {
    renderTower([row({ driverKey: 'id:1', position: 1, tier: 'Self' })], {
      focusedDriverKeys: [],
      pendingDriverKeys: new Set(['id:1']),
    });

    expect(screen.getByRole('button', { name: /Open telemetry/ }).textContent).toBe('Show');
  });

  it('shows a car with no reported position last, not first', () => {
    renderTower([
      row({ driverKey: 'id:1', displayName: 'Unplaced', position: null }),
      row({ driverKey: 'id:2', displayName: 'Leader', position: 1 }),
    ]);

    // The projector sorts server-side; the tower renders what it is given. This pins that the
    // component does not resort and undo that.
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]!).getByText('Unplaced')).toBeDefined();
  });
});
