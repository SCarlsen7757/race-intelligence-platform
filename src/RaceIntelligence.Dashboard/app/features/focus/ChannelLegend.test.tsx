import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ChannelLegend, type LegendChannel } from './ChannelLegend';

const CHANNELS: readonly LegendChannel[] = [
  { id: 'fl', label: 'FL', stroke: '#111' },
  { id: 'fr', label: 'FR', stroke: '#222' },
  { id: 'rl', label: 'RL', stroke: '#333' },
  { id: 'rr', label: 'RR', stroke: '#444' },
];

function toggle(label: string): HTMLButtonElement {
  return screen.getByRole<HTMLButtonElement>('button', { name: new RegExp(label) });
}

describe('ChannelLegend', () => {
  it('reports which channel was clicked', () => {
    const onToggle = vi.fn();
    render(<ChannelLegend channels={CHANNELS} hidden={[]} onToggle={onToggle} />);

    fireEvent.click(toggle('FL'));

    expect(onToggle).toHaveBeenCalledWith('fl');
  });

  /**
   * The state has to be readable without colour. A hidden channel is dimmed and struck through, and
   * `aria-pressed` is what says so to anyone not looking at the pixels.
   */
  it('marks a hidden channel as off', () => {
    render(<ChannelLegend channels={CHANNELS} hidden={['fr']} onToggle={vi.fn()} />);

    expect(toggle('FR').getAttribute('aria-pressed')).toBe('false');
    expect(toggle('FL').getAttribute('aria-pressed')).toBe('true');
  });

  /**
   * A chart drawing nothing is indistinguishable from a broken one, so the last channel still on
   * cannot be turned off. It costs nothing in practice: narrowing four channels to one never
   * reaches for the last, and swapping it out is turning the replacement on first.
   */
  it('will not let the last visible channel be turned off', () => {
    const onToggle = vi.fn();
    render(<ChannelLegend channels={CHANNELS} hidden={['fr', 'rl', 'rr']} onToggle={onToggle} />);

    const last = toggle('FL');
    expect(last.disabled).toBe(true);

    fireEvent.click(last);
    expect(onToggle).not.toHaveBeenCalled();
  });

  /** The hidden ones stay clickable, or there would be no way back to a full chart. */
  it('keeps the hidden channels clickable while one is locked on', () => {
    const onToggle = vi.fn();
    render(<ChannelLegend channels={CHANNELS} hidden={['fr', 'rl', 'rr']} onToggle={onToggle} />);

    fireEvent.click(toggle('RR'));

    expect(onToggle).toHaveBeenCalledWith('rr');
  });

  /**
   * The reading stays live for a hidden channel. The line going away is what was asked for; the
   * number is something the user may still want, and blanking it would make hiding a corner look
   * like losing it.
   */
  it('shows each channel its own current value', () => {
    render(
      <ChannelLegend
        channels={CHANNELS}
        hidden={['fl']}
        onToggle={vi.fn()}
        renderValue={(channel) => <span>{channel.id.toUpperCase()} value</span>}
        unit="kPa"
      />,
    );

    expect(toggle('FL value')).toBeTruthy();
    expect(screen.getByText('kPa')).toBeTruthy();
  });
});
