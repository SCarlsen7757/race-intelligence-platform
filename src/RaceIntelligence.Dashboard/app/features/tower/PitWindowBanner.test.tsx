import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { PitWindowState } from '../../shared/live/contracts';
import { PitWindowBanner } from './PitWindowBanner';

function window(overrides: Partial<PitWindowState> = {}): PitWindowState {
  return { status: 'Open', start: 12, end: 20, unit: 'Laps', ...overrides };
}

describe('PitWindowBanner', () => {
  it('announces an open window with its bounds', () => {
    const { container } = render(<PitWindowBanner window={window()} />);

    expect(screen.getByText(/Pit window OPEN/)).toBeDefined();
    expect(screen.getByText('lap 12 to lap 20')).toBeDefined();
    expect(container.querySelector('.pit-window--open')).not.toBeNull();
  });

  /**
   * The states have to be told apart by their words, not only by a hue. A banner whose sole
   * difference between "you may pit now" and "you may not" is a colour fails the person reading it
   * in a hurry on a washed-out screen.
   */
  it('distinguishes closed from open in words', () => {
    render(<PitWindowBanner window={window({ status: 'Closed' })} />);

    expect(screen.getByText(/Pit window CLOSED/)).toBeDefined();
    expect(screen.queryByText(/Pit window OPEN/)).toBeNull();
  });

  it('says when the mandatory stop has been served', () => {
    render(<PitWindowBanner window={window({ status: 'Completed' })} />);

    expect(screen.getByText(/Mandatory stop SERVED/)).toBeDefined();
  });

  /**
   * Every practice and qualifying session, and most races, have no mandatory stop. A permanent grey
   * "no pit window" bar on all of them is noise, so there is nothing to render at all.
   */
  it.each(['Unavailable', 'Disabled'] as const)(
    'renders nothing when the window is %s',
    (status) => {
      const { container } = render(<PitWindowBanner window={window({ status })} />);

      expect(container.innerHTML).toBe('');
    },
  );

  it('renders nothing before the hub has answered', () => {
    const { container } = render(<PitWindowBanner window={null} />);

    expect(container.innerHTML).toBe('');
  });

  /**
   * The same integer is lap 25 in a lap race and the 25-minute mark in a timed one. Labelling it is
   * the whole reason the unit crosses the wire.
   */
  it('labels the bounds in the session own unit', () => {
    render(<PitWindowBanner window={window({ unit: 'Minutes', start: 25, end: 40 })} />);

    expect(screen.getByText('minute 25 to minute 40')).toBeDefined();
  });

  /**
   * A bound with no unit cannot be labelled, and a bare number beside a pit window reads as
   * whichever of lap or minute the reader was already thinking in.
   */
  it('shows no bounds it cannot label, and none the simulator withheld', () => {
    const { rerender } = render(<PitWindowBanner window={window({ unit: 'Unknown' })} />);
    expect(screen.queryByText(/12/)).toBeNull();
    expect(screen.getByText(/Pit window OPEN/)).toBeDefined();

    rerender(<PitWindowBanner window={window({ start: null, end: null })} />);
    expect(screen.queryByText(/lap/)).toBeNull();
  });

  /** Half a window is still worth stating: "closes at lap 20" is actionable on its own. */
  it('shows one bound when only one is reported', () => {
    render(<PitWindowBanner window={window({ start: null, end: 20 })} />);

    expect(screen.getByText('lap 20')).toBeDefined();
  });
});
