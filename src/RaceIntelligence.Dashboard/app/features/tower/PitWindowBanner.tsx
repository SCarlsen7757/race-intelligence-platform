import type { PitWindowState, PitWindowUnit } from '../../shared/live/contracts';

interface PitWindowBannerProps {
  /** Null when the session has no mandatory window, or the hub has not answered yet. */
  window: PitWindowState | null;
}

/** How a bound reads: `lap 12`, `minute 25`, or nothing at all when the unit is unknown. */
function formatBound(value: number | null | undefined, unit: PitWindowUnit): string | null {
  if (value == null) {
    return null;
  }

  // No unit means the number cannot be labelled, and a bare "12" beside a pit window is worse than
  // silence — a strategist reads it as whichever of lap or minute they were already thinking in.
  switch (unit) {
    case 'Laps':
      return `lap ${value}`;
    case 'Minutes':
      return `minute ${value}`;
    default:
      return null;
  }
}

/** The bounds phrase for a window: "lap 12 to lap 20", or half of it, or nothing. */
function formatRange(window: PitWindowState): string | null {
  const start = formatBound(window.start, window.unit);
  const end = formatBound(window.end, window.unit);

  if (start !== null && end !== null) {
    return `${start} to ${end}`;
  }

  return start ?? end;
}

/**
 * Whether the session's mandatory pit stop can be taken, across the top of the room.
 *
 * **Absent unless there is something to say.** A session with no mandatory window — which is every
 * practice and qualifying session, and most races — renders nothing at all rather than a grey "no
 * pit window" bar that would occupy the same space on every screen forever.
 *
 * The three states are distinguished by their **words** first and their colour second. A banner
 * whose only difference between "you may pit now" and "you may not" is a hue is one that fails
 * exactly the person reading it in a hurry on a washed-out screen.
 *
 * No countdown, deliberately. "Closes in 2 laps" needs the driver's own lap count against the
 * window's bound, and for a timed session it needs the session clock, which the viewer wire does not
 * carry at all. A wrong countdown on a mandatory stop is considerably worse than an absolute figure.
 */
export function PitWindowBanner({ window }: PitWindowBannerProps) {
  if (window === null) {
    return null;
  }

  // Both mean "there is no window to show", and the difference between them — the simulator says
  // nothing, versus the session has none — is not a difference a race engineer can act on.
  if (window.status === 'Unavailable' || window.status === 'Disabled') {
    return null;
  }

  const range = formatRange(window);

  // `Stopped` is the local car sitting in its box during the window, and `Completed` is that car
  // having served. Both describe the publishing machine rather than the session, which is why they
  // read as statements about the stop rather than about the window.
  const { modifier, label } =
    window.status === 'Open'
      ? { modifier: 'open', label: 'Pit window OPEN' }
      : window.status === 'Stopped'
        ? { modifier: 'open', label: 'Pit window OPEN — serving stop' }
        : window.status === 'Completed'
          ? { modifier: 'done', label: 'Mandatory stop SERVED' }
          : { modifier: 'closed', label: 'Pit window CLOSED' };

  return (
    // `status` rather than `alert`: the banner changes rarely and is not an interruption, so it is
    // announced politely when it does rather than cutting across whatever is being read.
    <p className={`pit-window pit-window--${modifier}`} role="status">
      <span className="pit-window__label">{label}</span>
      {range !== null && <span className="pit-window__range">{range}</span>}
    </p>
  );
}
