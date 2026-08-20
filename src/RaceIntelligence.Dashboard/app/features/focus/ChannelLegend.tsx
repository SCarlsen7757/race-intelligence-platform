import type { ReactNode } from 'react';

/** One entry in the legend: what it is called, what colour it is drawn in, and what it is. */
export interface LegendChannel {
  id: string;
  label: string;
  stroke: string;
}

interface ChannelLegendProps {
  channels: readonly LegendChannel[];
  /** Channel ids currently turned off. */
  hidden: readonly string[];
  onToggle: (channelId: string) => void;
  /** The channel's current reading, shown beside its name. Absent on a chart with no readout. */
  renderValue?: (channel: LegendChannel, index: number) => ReactNode;
  /** What the readings are in, shown once at the end rather than on each. */
  unit?: string;
}

/**
 * The chart's channels, as a row of switches that is also its legend.
 *
 * **The legend was already there, so the controls cost no furniture.** A tyre chart has always
 * carried a swatch, a name and a current value per wheel; making each of those a button adds a
 * hit target and a state, and nothing else. A separate row of checkboxes would have been a second
 * list of the same four things — on a wall of a dozen tiles, that is a dozen rows of chrome
 * competing with the data they sit under, which is the opposite of what a dense dashboard wants.
 *
 * It also puts the control where the question is asked. Somebody deciding they only care about the
 * front left is looking at the four numbers when they decide it.
 *
 * **The last visible channel cannot be turned off.** A chart drawing nothing is indistinguishable
 * from a broken one, and the legend sits under the plot where an empty canvas is what draws the eye
 * first — so the state is refused rather than explained. It costs nothing in practice: narrowing
 * four channels to one never reaches for the last, and swapping the last one out is turning the
 * replacement on and the old one off, in either order.
 */
export function ChannelLegend({
  channels,
  hidden,
  onToggle,
  renderValue,
  unit,
}: ChannelLegendProps) {
  const hiddenIds = new Set(hidden);
  const lastVisible = channels.filter((channel) => !hiddenIds.has(channel.id)).length === 1;

  return (
    <div className="wheel-chart__values">
      {channels.map((channel, index) => {
        const off = hiddenIds.has(channel.id);
        // The only channel still drawn, which is the one that has nowhere to go.
        const locked = !off && lastVisible;

        return (
          <button
            key={channel.id}
            type="button"
            className={`wheel-chart__value channel-toggle${off ? ' channel-toggle--off' : ''}`}
            aria-pressed={!off}
            disabled={locked}
            title={
              locked
                ? 'The last channel on a chart stays on'
                : `${off ? 'Show' : 'Hide'} ${channel.label}`
            }
            onClick={() => onToggle(channel.id)}
          >
            <span
              className="wheel-chart__key"
              // Inline because the colour comes from the same module the canvas line does, so the
              // swatch and the line cannot be restyled apart. See `traceColours.ts`.
              style={{ background: channel.stroke }}
            />
            <span className="wheel-chart__wheel">{channel.label}</span>
            {renderValue?.(channel, index)}
          </button>
        );
      })}
      {unit !== undefined && <span className="wheel-chart__unit">{unit}</span>}
    </div>
  );
}
